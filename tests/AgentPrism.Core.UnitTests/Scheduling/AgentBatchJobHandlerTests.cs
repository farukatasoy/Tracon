using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Scheduling;

public sealed class AgentBatchJobHandlerTests
{
    [Fact]
    public void Kind_AgentBatch_dir()
    {
        new AgentBatchJobHandler(new SingleAgentCatalog(new ScriptedAgent()), NullLogger<AgentBatchJobHandler>.Instance)
            .Kind.ShouldBe(JobKind.AgentBatch);
    }

    [Fact]
    public async Task Agent_bulunamazsa_istisna_firlatir()
    {
        var handler = new AgentBatchJobHandler(new SingleAgentCatalog(null), NullLogger<AgentBatchJobHandler>.Instance);

        await Should.ThrowAsync<AgentPrismException>(
            () => handler.ExecuteAsync(BuildContext([Item(0, "girdi")])).AsTask());
    }

    [Fact]
    public async Task Ogeler_sirayla_islenir_ve_sonuclar_raporlanir()
    {
        var agent = new ScriptedAgent(fail: "patlat");
        var handler = new AgentBatchJobHandler(new SingleAgentCatalog(agent), NullLogger<AgentBatchJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        var context = BuildContext(
            [Item(0, "birinci"), Item(1, "patlat"), Item(2, "ucuncu")],
            reported);

        await handler.ExecuteAsync(context);

        agent.Inputs.ShouldBe(["birinci", "patlat", "ucuncu"]);
        reported.Count.ShouldBe(3);
        reported[0].Status.ShouldBe(JobItemStatus.Completed);
        reported[0].RunId.ShouldNotBeNull();
        reported[1].Status.ShouldBe(JobItemStatus.Failed);
        reported[1].Error.ShouldNotBeNullOrWhiteSpace();
        reported[2].Status.ShouldBe(JobItemStatus.Completed);
    }

    [Fact]
    public async Task Daha_once_islenmis_ogeler_yeniden_calistirilmaz()
    {
        // Kira suresi dolup is yeniden alindiginda yalnizca Pending ogeler islenir.
        var agent = new ScriptedAgent();
        var handler = new AgentBatchJobHandler(new SingleAgentCatalog(agent), NullLogger<AgentBatchJobHandler>.Instance);

        var context = BuildContext(
        [
            Item(0, "eski", JobItemStatus.Completed),
            Item(1, "yeni"),
        ]);

        await handler.ExecuteAsync(context);

        agent.Inputs.ShouldBe(["yeni"]);
    }

    [Fact]
    public async Task Iptal_edilen_is_ogeler_arasinda_durur()
    {
        var agent = new ScriptedAgent();
        var handler = new AgentBatchJobHandler(new SingleAgentCatalog(agent), NullLogger<AgentBatchJobHandler>.Instance);

        var context = BuildContext(
            [Item(0, "birinci"), Item(1, "ikinci")],
            cancelAfter: 1);

        await handler.ExecuteAsync(context);

        agent.Inputs.ShouldBe(["birinci"]);
    }

    private static JobItemRecord Item(int seq, string input, JobItemStatus status = JobItemStatus.Pending)
        => new() { Id = Guid.NewGuid(), JobId = Guid.NewGuid(), Seq = seq, Input = input, Status = status };

    private static JobContext BuildContext(
        IReadOnlyList<JobItemRecord> items,
        List<JobItemResult>? reported = null,
        int? cancelAfter = null)
    {
        reported ??= [];
        var calls = 0;

        return new JobContext
        {
            Job = new JobRecord
            {
                Id = Guid.NewGuid(),
                TenantId = "kiraci",
                Kind = JobKind.AgentBatch,
                TargetName = "ozetleyici",
                Status = JobStatus.Running,
                ScheduledFor = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            Items = items,
            ReportItemAsync = (result, _) =>
            {
                reported.Add(result);
                return default;
            },
            IsCancelledAsync = _ =>
            {
                calls++;
                return new ValueTask<bool>(cancelAfter is { } limit && calls > limit);
            },
        };
    }

    private sealed class SingleAgentCatalog(AIAgent? agent) : IAgentCatalog
    {
        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)[]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, CancellationToken cancellationToken = default)
            => new(agent);
    }

    /// <summary>Girdisi <paramref name="fail"/> ile eslesirse hata firlatan en kucuk sahte agent.</summary>
    private sealed class ScriptedAgent(string? fail = null) : AIAgent
    {
        public List<string> Inputs { get; } = [];

        public override string Name => "sahte-agent";

        public override string Description => "test icin";

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var last = messages.LastOrDefault()?.Text ?? string.Empty;
            Inputs.Add(last);

            if (fail is not null && string.Equals(last, fail, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"'{last}' girdisi kasitli olarak basarisiz oldu.");
            }

            return Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, $"tamam:{last}")));
        }

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);

            foreach (var message in response.Messages)
            {
                yield return new AgentResponseUpdate(message.Role, message.Contents);
            }
        }

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => new(new ScriptedSession());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(new ScriptedSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(EmptyState);

        private static JsonElement EmptyState { get; } = JsonDocument.Parse("{}").RootElement.Clone();

        private sealed class ScriptedSession : AgentSession;
    }
}
