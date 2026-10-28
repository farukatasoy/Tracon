using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The knowledge base passes the tenant's provider checks before a tenant's text
/// reaches the embedding provider: the egress policy first, then the tenant's own
/// credential.
/// </summary>
/// <remarks>
/// <para>
/// F-416: the <c>search_knowledge</c> query and the document upload called the
/// registered embedding generator and read neither. The generator's own provider
/// name cannot be trusted for the policy: measured, the OpenAI and the Azure OpenAI
/// generators both report <c>openai</c>. The provider is named by
/// <c>Tracon:Knowledge:EmbeddingProvider</c>, and a tenant with a policy is refused
/// when no name is known at all.
/// </para>
/// <para>
/// Both public paths are asserted through the real host — the HTTP endpoints and the
/// agent tool — because the gate is wired into two different places.
/// </para>
/// </remarks>
public sealed class KnowledgeTenantPolicyTests
{
    private const string EmbeddingProvider = "azure-openai";
    private static readonly Uri SearchUri = new("/tracon/api/knowledge/docs/search", UriKind.Relative);
    private static readonly Uri UploadUri = new("/tracon/api/knowledge/docs/documents", UriKind.Relative);

    [Fact]
    public async Task Search_refuses_an_embedding_provider_the_tenant_policy_forbids()
    {
        var embeddings = new CountingEmbeddingGenerator();
        await using var host = await StartAsync(embeddings, EmbeddingProvider);

        await SavePolicyAsync(host, "openai");

        using var response = await host.Client.PostAsJsonAsync(SearchUri, new { query = "refund policy" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).ShouldContain($"'{EmbeddingProvider}'");
        embeddings.Calls.ShouldBe(0, "the tenant's query must not reach a provider its policy forbids");
    }

    [Fact]
    public async Task Upload_refuses_an_embedding_provider_the_tenant_policy_forbids()
    {
        var embeddings = new CountingEmbeddingGenerator();
        await using var host = await StartAsync(embeddings, EmbeddingProvider);

        await SavePolicyAsync(host, "openai");

        using var response = await host.Client.PostAsJsonAsync(UploadUri, new { sourceId = "handbook", text = "Refunds take five days." });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        embeddings.Calls.ShouldBe(0, "the tenant's document must not reach a provider its policy forbids");
    }

    [Fact]
    public async Task Search_runs_when_the_policy_names_the_embedding_provider()
    {
        var embeddings = new CountingEmbeddingGenerator();
        await using var host = await StartAsync(embeddings, EmbeddingProvider);

        await SavePolicyAsync(host, "openai", EmbeddingProvider);

        using var response = await host.Client.PostAsJsonAsync(SearchUri, new { query = "refund policy" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        embeddings.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_tenant_with_a_policy_is_refused_when_the_embedding_provider_is_not_named()
    {
        // No configured name, and the generator reports none: the policy cannot be
        // judged, so a tenant that has one is refused rather than let through.
        var embeddings = new CountingEmbeddingGenerator();
        await using var host = await StartAsync(embeddings, embeddingProvider: null);

        await SavePolicyAsync(host, "openai", EmbeddingProvider);

        using var response = await host.Client.PostAsJsonAsync(SearchUri, new { query = "refund policy" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).ShouldContain("Tracon:Knowledge:EmbeddingProvider");
        embeddings.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_tenant_without_a_policy_searches_whether_or_not_the_provider_is_named()
    {
        var embeddings = new CountingEmbeddingGenerator();
        await using var host = await StartAsync(embeddings, embeddingProvider: null);

        using var response = await host.Client.PostAsJsonAsync(SearchUri, new { query = "refund policy" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        embeddings.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task With_RequireEgressPolicy_a_tenant_without_a_policy_is_refused_the_embedding_provider()
    {
        // F-254: the embedding access check is one of the callers that must read the switch.
        var embeddings = new CountingEmbeddingGenerator();
        await using var host = await StartAsync(
            embeddings,
            EmbeddingProvider,
            static services => services.Configure<TraconTenantProviderOptions>(static options => options.RequireEgressPolicy = true));

        using var refused = await host.Client.PostAsJsonAsync(SearchUri, new { query = "refund policy" });

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadAsStringAsync()).ShouldContain("has no egress policy");
        embeddings.Calls.ShouldBe(0, "the tenant's query must not reach a provider the tenant has no policy for");

        await SavePolicyAsync(host, EmbeddingProvider);

        using var searched = await host.Client.PostAsJsonAsync(SearchUri, new { query = "refund policy" });

        searched.StatusCode.ShouldBe(HttpStatusCode.OK, await searched.Content.ReadAsStringAsync());
        embeddings.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task An_unnamed_embedding_provider_is_refused_without_a_policy_when_one_is_required()
    {
        // The provider-not-named branch judged "no policy" as allowed. With a policy required there is
        // nothing the call can be judged against, so it is refused and the message says how to name it.
        var embeddings = new CountingEmbeddingGenerator();
        await using var host = await StartAsync(
            embeddings,
            embeddingProvider: null,
            static services => services.Configure<TraconTenantProviderOptions>(static options => options.RequireEgressPolicy = true));

        using var refused = await host.Client.PostAsJsonAsync(SearchUri, new { query = "refund policy" });

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());

        var body = await refused.Content.ReadAsStringAsync();
        body.ShouldContain("Tracon:Knowledge:EmbeddingProvider");
        body.ShouldContain("Tracon:TenantProviders:RequireEgressPolicy");
        embeddings.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Search_refuses_a_tenant_with_its_own_key_for_the_embedding_provider()
    {
        // The generator was built with the setup key and cannot use the tenant's.
        const string KeyName = "Tracon:ProviderKeys:TestEmbeddings";
        var embeddings = new CountingEmbeddingGenerator();
        await using var host = await StartAsync(
            embeddings,
            EmbeddingProvider,
            services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection([new(KeyName, "tenant-embedding-key")]).Build()));

        await host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = "default",
                ProviderName = EmbeddingProvider,
                ApiKeyConfigurationName = KeyName,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        using var response = await host.Client.PostAsJsonAsync(SearchUri, new { query = "refund policy" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        embeddings.Calls.ShouldBe(0);
    }

    /// <summary>
    /// A binding that names a key the tenant may not read is refused with <c>403</c>
    /// before anything is embedded.
    /// </summary>
    [Fact]
    public async Task Search_refuses_a_binding_whose_key_the_tenant_may_not_read()
    {
        const string ForeignKey = "Other:Keys:TestEmbeddings";
        var embeddings = new CountingEmbeddingGenerator();
        await using var host = await StartAsync(
            embeddings,
            EmbeddingProvider,
            services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection([new(ForeignKey, "foreign-embedding-key")]).Build()));

        await host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = "default",
                ProviderName = EmbeddingProvider,
                ApiKeyConfigurationName = ForeignKey,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        using var response = await host.Client.PostAsJsonAsync(SearchUri, new { query = "refund policy" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        embeddings.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_agent_tool_refuses_an_embedding_provider_the_tenant_policy_forbids()
    {
        var embeddings = new CountingEmbeddingGenerator();
        var model = new FakeModelProvider("knowledge-model")
            .CallsTool("search_knowledge", new { query = "refund policy" })
            .EchoesLastToolResult();
        await using var host = await StartAsync(
            embeddings,
            EmbeddingProvider,
            configureTracon: builder => builder
                .AddModelProvider(model)
                .AddAgent(new AgentDefinition
                {
                    Name = "knowledge-agent",
                    Instructions = "Answer from the knowledge base.",
                    Model = new ModelBinding { Provider = "knowledge-model", Model = "knowledge-1" },
                    Memory = new MemorySettings { EnableVectorSearch = true, VectorCollection = "docs" },
                }));

        // The agent's own model provider is allowed; only the embedding provider is not.
        await SavePolicyAsync(host, "knowledge-model");

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/knowledge-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "what is the refund policy?" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        var runs = host.Services.GetRequiredService<IRunStore>();
        var run = (await runs.QueryRunsAsync(new RunQuery { AgentName = "knowledge-agent" }, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();
        var invocation = (await runs.ListToolInvocationsAsync(run.Id, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        invocation.ToolName.ShouldBe("search_knowledge");
        invocation.Succeeded.ShouldBeFalse();
        invocation.Error.ShouldNotBeNull().ShouldContain($"'{EmbeddingProvider}'");
        embeddings.Calls.ShouldBe(0);

        // Phase 246 (F-688): the refusal is a sentence Tracon wrote for the model, so it reaches the model as the result of
        // the call (the explained-failure layer of the tool chain), not as the framework's "Error: Function failed.".
        var told = string.Join(
            "\n",
            model.Requests[^1].Messages.SelectMany(static message => message.Contents).OfType<FunctionResultContent>()
                .Select(static result => result.Result?.ToString()));

        told.ShouldContain($"'{EmbeddingProvider}'");
        told.ShouldNotContain("Function failed");
    }

    private static async Task SavePolicyAsync(TraconTestHost host, params string[] allowed)
        => await host.Services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync("default", allowed, TestContext.Current.CancellationToken);

    private static Task<TraconTestHost> StartAsync(
        CountingEmbeddingGenerator embeddings,
        string? embeddingProvider,
        Action<IServiceCollection>? configureServices = null,
        Action<ITraconBuilder>? configureTracon = null)
        => TraconTestHost.StartAsync(
            configureTracon: configureTracon,
            configureServices: services =>
            {
                services.Configure<TraconKnowledgeOptions>(options =>
                {
                    options.Dimensions = 3;
                    options.EmbeddingProvider = embeddingProvider;
                });
                services.AddSingleton<IVectorSearchStore>(new EmptyVectorSearchStore());
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddings);
                configureServices?.Invoke(services);
            });

    /// <summary>An embedding generator that counts its calls and reports no provider name.</summary>
    private sealed class CountingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                values.Select(static _ => new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f }))));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>A vector store that holds nothing: the tests are about the call before it.</summary>
    private sealed class EmptyVectorSearchStore : IVectorSearchStore
    {
        public int Dimensions => 3;

        public ValueTask UpsertAsync(
            string tenantId, string collection, string sourceId, IReadOnlyList<VectorChunk> chunks, CancellationToken cancellationToken = default)
            => default;

        public ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken = default)
            => new([]);

        public ValueTask<int> DeleteSourceAsync(string tenantId, string collection, string sourceId, CancellationToken cancellationToken = default)
            => new(0);

        public ValueTask<IReadOnlyList<string>> ListSourcesAsync(string tenantId, string collection, CancellationToken cancellationToken = default)
            => new([]);
    }
}
