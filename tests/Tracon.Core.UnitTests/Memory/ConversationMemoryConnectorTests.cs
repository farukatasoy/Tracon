using CommunityToolkit.VectorData.InMemory;
using InMemoryVectorStore = CommunityToolkit.VectorData.InMemory.InMemoryVectorStore;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Memory;

/// <summary>
/// Conversation memory against a REAL vector store connector (the maintained in-memory one), not the
/// test double: the scope filters the framework builds and the paging and deletes of the eraser are
/// translated by the connector, which is where a scope can leak or a delete can miss.
/// </summary>
/// <remarks>
/// What this cannot prove is a database connector's own translation (PgVector, SQL Server): that is the
/// connector's contract. The sample host run against PostgreSQL (phase 233, manual cases) covers one.
/// </remarks>
// MAAI001: the context provider contexts are marked "evaluation purposes only" - see K-020.
#pragma warning disable MAAI001
public sealed class ConversationMemoryConnectorTests : IDisposable
{
    private const string Collection = "connector_memory";

    private readonly InMemoryVectorStore _store = new(new InMemoryVectorStoreOptions { EmbeddingGenerator = new BagOfWordsEmbeddings() });
    private readonly AIAgent _agent = new ChatClientAgent(new FakeChatClient());

    public void Dispose()
    {
        TraconRunContext.SetCurrent(null);
        _store.Dispose();
    }

    [Fact]
    public async Task A_user_is_remembered_across_sessions_and_nobody_else_is()
    {
        var provider = Provider("agent-a", "tenant-a");
        await RememberAsync(provider, "tenant-a", "agent-a", "ada", "session-1", "My favourite colour is teal.");
        await RememberAsync(provider, "tenant-a", "agent-a", "grace", "session-1", "My favourite colour is plum.");

        (await RecallAsync(provider, "tenant-a", "agent-a", "ada", "session-2", "What is my favourite colour?")).ShouldContain("teal");
        (await RecallAsync(provider, "tenant-a", "agent-a", "ada", "session-2", "What is my favourite colour?")).ShouldNotContain("plum");
        (await RecallAsync(provider, "tenant-a", "agent-a", "grace", "session-2", "What is my favourite colour?")).ShouldNotContain("teal");
    }

    [Fact]
    public async Task Another_agent_and_another_tenant_find_nothing()
    {
        var writer = Provider("agent-a", "tenant-a");
        await RememberAsync(writer, "tenant-a", "agent-a", "ada", "session-1", "My favourite colour is teal.");

        (await RecallAsync(Provider("agent-b", "tenant-a"), "tenant-a", "agent-b", "ada", "session-2", "What is my favourite colour?")).ShouldNotContain("teal");
        (await RecallAsync(Provider("agent-a", "tenant-b"), "tenant-b", "agent-a", "ada", "session-2", "What is my favourite colour?")).ShouldNotContain("teal");
    }

    [Fact]
    public async Task The_eraser_counts_pages_exports_and_deletes_one_session_of_one_tenant()
    {
        var provider = Provider("agent-a", "tenant-a");

        for (var index = 0; index < 230; index++)
        {
            await RememberAsync(provider, "tenant-a", "agent-a", "ada", "session-1", $"Message number {index} about teal.");
        }

        await RememberAsync(provider, "tenant-a", "agent-a", "ada", "session-2", "Another session stays.");
        await RememberAsync(Provider("agent-a", "tenant-b"), "tenant-b", "agent-a", "ada", "session-1", "Same session id in another tenant stays.");

        var eraser = new ConversationMemoryEraser(Services());

        var count = await eraser.CountAsync("tenant-a", ["session-1"], CancellationToken.None);
        count.ShouldBeGreaterThanOrEqualTo(230);

        using var buffer = new MemoryStream();

        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            await eraser.WriteExportAsync("tenant-a", ["session-1"], writer, CancellationToken.None);
            writer.WriteEndObject();
        }

        using (var document = System.Text.Json.JsonDocument.Parse(buffer.ToArray()))
        {
            document.RootElement.GetProperty("conversation_memory").GetArrayLength().ShouldBe(count);
        }

        var deleted = await eraser.EraseAsync("tenant-a", ["session-1"], CancellationToken.None);

        deleted.ShouldBe(count);
        (await eraser.CountAsync("tenant-a", ["session-1"], CancellationToken.None)).ShouldBe(0);
        (await eraser.CountAsync("tenant-a", ["session-2"], CancellationToken.None)).ShouldBeGreaterThan(0);
        (await eraser.CountAsync("tenant-b", ["session-1"], CancellationToken.None)).ShouldBeGreaterThan(0);
    }

    private ServiceProvider Services()
        => new ServiceCollection()
            .AddSingleton<VectorStore>(_store)
            .AddOptions<TraconConversationMemoryOptions>().Configure(static options =>
            {
                options.CollectionName = Collection;
                options.VectorDimensions = BagOfWordsEmbeddings.Dimensions;
            }).Services
            .BuildServiceProvider();

    private ConversationMemoryScopeProvider Provider(string agent, string tenant)
        => new(
            _store,
            new TraconConversationMemoryOptions { CollectionName = Collection, VectorDimensions = BagOfWordsEmbeddings.Dimensions },
            tenant,
            agent,
            ConversationMemorySearch.BeforeEachTurn,
            maxResults: 5,
            loggerFactory: null);

    private static AgentRunScope Run(string tenant, string agent, string user, string session)
        => new() { RunId = Guid.NewGuid(), RootRunId = Guid.NewGuid(), AgentName = agent, TenantId = tenant, UserId = user, SessionId = session };

    private async Task RememberAsync(ConversationMemoryScopeProvider provider, string tenant, string agent, string user, string session, string said)
    {
        TraconRunContext.SetCurrent(Run(tenant, agent, user, session));

        await provider.InvokedAsync(new AIContextProvider.InvokedContext(_agent, null, [new ChatMessage(ChatRole.User, said)], []));
    }

    private async Task<string> RecallAsync(ConversationMemoryScopeProvider provider, string tenant, string agent, string user, string session, string asked)
    {
        TraconRunContext.SetCurrent(Run(tenant, agent, user, session));

        var context = await provider.InvokingAsync(
            new AIContextProvider.InvokingContext(_agent, null, new AIContext { Messages = [new ChatMessage(ChatRole.User, asked)] }));

        return string.Join("\n", (context.Messages ?? []).Select(static message => message.Text));
    }

    /// <summary>Turns a text into a vector of word-hash counts, so texts that share words are close.</summary>
    private sealed class BagOfWordsEmbeddings : IEmbeddingGenerator<string, Embedding<float>>
    {
        public const int Dimensions = 64;

        public EmbeddingGeneratorMetadata Metadata { get; } = new("bag-of-words");

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GeneratedEmbeddings<Embedding<float>>();

            foreach (var value in values)
            {
                var vector = new float[Dimensions];

                foreach (var word in value.Split([' ', '.', ',', '?', '!'], StringSplitOptions.RemoveEmptyEntries))
                {
                    vector[(word.ToUpperInvariant().Aggregate(17, static (hash, character) => (hash * 31) + character) & int.MaxValue) % Dimensions] += 1f;
                }

                result.Add(new Embedding<float>(vector));
            }

            return Task.FromResult(result);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
