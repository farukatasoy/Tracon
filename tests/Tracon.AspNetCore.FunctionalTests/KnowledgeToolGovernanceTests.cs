using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>search_knowledge</c> runs through the tool chain like every other tool: the authorization
/// handler decides, the argument validator and the timeout apply, and the query's token count reaches the run record.
/// </summary>
/// <remarks>
/// <para>
/// Before this change the knowledge tool was added to the agent as a bare function. The chain was composed for the
/// registry, the MCP tools and the workspace tools, and this tool was none of those, so a handler written to close a
/// collection for a user never saw it. The embedding call of the tool sends the query to the tenant's embedding
/// provider, so a denial that comes late is a query already sent.
/// </para>
/// <para>
/// Each scenario crosses the DI boundary (the chain is built from the host's registrations), the HTTP boundary (the tenant
/// comes from the request) and the stream (the call id the usage is attached to lives in the tool-call loop).
/// </para>
/// </remarks>
public sealed class KnowledgeToolGovernanceTests
{
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string ProviderName = "knowledge-governance-model";
    private const string AgentName = "knowledge-agent";

    [Fact]
    public async Task A_denying_handler_stops_the_search_before_the_query_is_embedded_and_sees_the_effect_and_the_tenant()
    {
        var embeddings = new CountingEmbeddings();
        var handler = new RecordingHandler(ToolAuthorizationResult.Deny("The knowledge base is closed for you."));
        await using var host = await StartAsync(embeddings, new CountingKnowledgeStore(), services => services.AddSingleton<IToolAuthorizationHandler>(handler));

        var stream = await RunAsync(host, tenant: "tenant-b");

        embeddings.Calls.ShouldBe(0, "the query reached the embedding provider after the handler denied the call");
        handler.Seen.ShouldHaveSingleItem().ShouldBe(("search_knowledge", ToolEffect.External, "tenant-b"));
        stream.ShouldContain("The knowledge base is closed for you.");

        var call = (await CallsAsync(host, tenant: "tenant-b")).ShouldHaveSingleItem();

        call.ToolName.ShouldBe("search_knowledge");
        call.AuthorizationDenied.ShouldBeTrue();
        call.Usage.ShouldBeNull("a denied call embedded nothing");
    }

    [Fact]
    public async Task An_allowing_handler_lets_the_search_run_and_still_sees_the_call()
    {
        // The control for the denial above: the same host and turn, and the call goes through.
        var embeddings = new CountingEmbeddings();
        var store = new CountingKnowledgeStore();
        var handler = new RecordingHandler(ToolAuthorizationResult.Allow());
        await using var host = await StartAsync(embeddings, store, services => services.AddSingleton<IToolAuthorizationHandler>(handler));

        await RunAsync(host, tenant: null);

        embeddings.Calls.ShouldBe(1);
        store.Searches.ShouldBe(1);
        handler.Seen.ShouldHaveSingleItem().Item1.ShouldBe("search_knowledge");
    }

    [Fact]
    public async Task A_validator_that_rejects_the_arguments_stops_the_search_and_is_given_the_tool_by_name()
    {
        var embeddings = new CountingEmbeddings();
        var validator = new RejectingValidator("The query is not allowed.");
        await using var host = await StartAsync(embeddings, new CountingKnowledgeStore(), services => services.AddSingleton<IToolArgumentsValidator>(validator));

        var stream = await RunAsync(host, tenant: null);

        embeddings.Calls.ShouldBe(0, "a rejected call embedded its query");
        validator.Seen.ShouldHaveSingleItem().ShouldBe(("search_knowledge", ToolEffect.External));
        stream.ShouldContain("The query is not allowed.");

        var call = (await CallsAsync(host)).ShouldHaveSingleItem();

        call.Succeeded.ShouldBeFalse();
        call.Error.ShouldBe("The query is not allowed.");
    }

    [Fact]
    public async Task A_vector_store_that_hangs_ends_at_the_tool_timeout_and_the_run_goes_on()
    {
        var embeddings = new CountingEmbeddings();
        await using var host = await StartAsync(
            embeddings,
            new HangingKnowledgeStore(),
            configureTracon: static builder => builder.Configure(static options => options.Tools.DefaultTimeout = TimeSpan.FromMilliseconds(200)));

        var started = DateTimeOffset.UtcNow;
        var stream = await RunAsync(host, tenant: null);

        (DateTimeOffset.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(30));
        stream.ShouldContain("Tool 'search_knowledge' did not complete within 200ms.");

        var call = (await CallsAsync(host)).ShouldHaveSingleItem();

        call.TimedOut.ShouldBeTrue();
        call.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task The_query_embedding_is_reported_as_tokens_with_no_cost_and_the_run_totals_do_not_move()
    {
        var embeddings = new CountingEmbeddings { InputTokens = 7 };
        await using var host = await StartAsync(embeddings, new CountingKnowledgeStore());

        await RunAsync(host, tenant: null);

        var usage = (await CallsAsync(host)).ShouldHaveSingleItem().Usage.ShouldNotBeNull();

        usage.Unit.ShouldBe(ToolUsageUnits.Tokens);
        usage.Quantity.ShouldBe(7m);
        usage.Cost.ShouldBeNull("the embedding has no price identity yet; a cost here would reach the budget");
        usage.IsEstimated.ShouldBeFalse();

        var run = await RunRecordAsync(host);

        // Documented in TraconToolUsage: an item with no cost is listed and counted, and no amount is added.
        var nonTokenCost = run.NonTokenCost.ShouldNotBeNull();

        nonTokenCost.Amount.ShouldBeNull("a report with no cost adds nothing to the run's non-token total");
        nonTokenCost.UnsummedItemCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_hits_reach_the_model_as_text_through_the_chain()
    {
        // The chain canonicalizes a result to the text a provider sends. The search used to hand the model the raw
        // serialized list; the content of a hit must still be there.
        var store = new CountingKnowledgeStore { Hits = [Refunds] };
        await using var host = await StartAsync(new CountingEmbeddings(), store);

        var stream = await RunAsync(host, tenant: null);

        stream.ShouldContain("Refunds take five days.");
        (await CallsAsync(host)).ShouldHaveSingleItem().Result.ShouldNotBeNull().ShouldContain("Refunds take five days.");
    }

    [Fact]
    public async Task A_replay_plays_the_recorded_search_back_and_does_not_embed_the_query_again()
    {
        // The replay wraps every tool of the compiled agent with a player that never runs the body. The knowledge search is
        // wrapped before that, so the player must wrap the chain once: not skip it, not wrap it twice.
        var embeddings = new CountingEmbeddings();
        var store = new CountingKnowledgeStore { Hits = [Refunds] };
        var model = new FakeModelProvider(ProviderName)
            .CallsTool("search_knowledge", new { query = "refund policy" })
            .RespondsWith("Live answer.")
            .CallsTool("search_knowledge", new { query = "refund policy" })
            .EchoesLastToolResult("Replayed: ");
        await using var host = await StartAsync(embeddings, store, model: model);

        // A replay recompiles the stored definition; a code agent has none, so the agent that is replayed is a stored one.
        await host.Services.GetRequiredService<IAgentDefinitionStore>().SaveAsync(
            new AgentDefinition
            {
                Name = "stored-knowledge-agent",
                Instructions = "Answer from the knowledge base.",
                Model = new ModelBinding { Provider = ProviderName, Model = "knowledge-1" },
                Memory = new MemorySettings { EnableVectorSearch = true, VectorCollection = "docs" },
            },
            TestContext.Current.CancellationToken);

        using (var first = await host.Client.PostAsJsonAsync(
                   new Uri("/tracon/api/agents/stored-knowledge-agent/run", UriKind.Relative),
                   new AgentRunRequest { Message = "what is the refund policy?" }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
            (await first.Content.ReadAsStringAsync()).ShouldContain("Refunds take five days.");
        }

        var live = (await host.Services.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { AgentName = "stored-knowledge-agent" }, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        embeddings.Calls.ShouldBe(1);
        store.Searches.ShouldBe(1);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/runs/{live.Id}/replay", UriKind.Relative), new { toolMode = "ReplayTools" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var output = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("output").GetString();

        output.ShouldNotBeNull().ShouldContain("Replayed: ");
        output.ShouldContain("Refunds take five days.");
        embeddings.Calls.ShouldBe(1, "the replay embedded the query again");
        store.Searches.ShouldBe(1, "the replay read the store again");
    }

    [Fact]
    public async Task A_provider_that_reports_no_usage_leaves_the_call_without_a_usage_line()
    {
        var embeddings = new CountingEmbeddings { InputTokens = null };
        await using var host = await StartAsync(embeddings, new CountingKnowledgeStore());

        await RunAsync(host, tenant: null);

        (await CallsAsync(host)).ShouldHaveSingleItem().Usage.ShouldBeNull();
    }

    private static async Task<string> RunAsync(TraconTestHost host, string? tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "what is the refund policy?" }),
        };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        return body;
    }

    // The run store reads under the ambient tenant, and the run of a header-named tenant is stored under that tenant.
    private static async Task<RunRecord> RunRecordAsync(TraconTestHost host, string tenant = "default")
    {
        using (AmbientTenantScope.Begin(tenant))
        {
            return (await host.Services.GetRequiredService<IRunStore>()
                    .QueryRunsAsync(new RunQuery { AgentName = AgentName }, TestContext.Current.CancellationToken))
                .ShouldHaveSingleItem();
        }
    }

    private static async Task<IReadOnlyList<ToolInvocationRecord>> CallsAsync(TraconTestHost host, string tenant = "default")
    {
        var run = await RunRecordAsync(host, tenant);

        using (AmbientTenantScope.Begin(tenant))
        {
            return await host.Services.GetRequiredService<IRunStore>().ListToolInvocationsAsync(run.Id, TestContext.Current.CancellationToken);
        }
    }

    private static readonly VectorSearchHit Refunds = new()
    {
        SourceId = "handbook",
        ChunkIndex = 0,
        Content = "Refunds take five days.",
        Distance = 0.1,
    };

    private static Task<TraconTestHost> StartAsync(
        CountingEmbeddings embeddings,
        IVectorSearchStore store,
        Action<IServiceCollection>? configureServices = null,
        Action<ITraconBuilder>? configureTracon = null,
        FakeModelProvider? model = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder
                    .UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    })
                    .AddModelProvider(model ?? new FakeModelProvider(ProviderName)
                        .CallsTool("search_knowledge", new { query = "refund policy" })
                        .EchoesLastToolResult())
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Answer from the knowledge base.",
                        Model = new ModelBinding { Provider = ProviderName, Model = "knowledge-1" },
                        Memory = new MemorySettings { EnableVectorSearch = true, VectorCollection = "docs" },
                        Origin = AgentDefinitionOrigin.Code,
                    });
                configureTracon?.Invoke(builder);
            },
            configureServices: services =>
            {
                services.Configure<TraconKnowledgeOptions>(static options => options.Dimensions = 3);
                services.AddSingleton(store);
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddings);
                configureServices?.Invoke(services);
            });

    private sealed class RecordingHandler(ToolAuthorizationResult answer) : IToolAuthorizationHandler
    {
        private readonly List<(string Tool, ToolEffect Effect, string Tenant)> _seen = [];

        public IReadOnlyList<(string Tool, ToolEffect Effect, string Tenant)> Seen
        {
            get
            {
                lock (_seen)
                {
                    return [.. _seen];
                }
            }
        }

        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            lock (_seen)
            {
                _seen.Add((request.ToolName, request.Effect, request.TenantId));
            }

            return new(answer);
        }
    }

    private sealed class RejectingValidator(string reason) : IToolArgumentsValidator
    {
        private readonly List<(string Tool, ToolEffect Effect)> _seen = [];

        public IReadOnlyList<(string Tool, ToolEffect Effect)> Seen
        {
            get
            {
                lock (_seen)
                {
                    return [.. _seen];
                }
            }
        }

        public ValueTask<ToolArgumentsValidationResult> ValidateAsync(
            ToolDescriptor tool, AIFunctionArguments arguments, CancellationToken cancellationToken = default)
        {
            lock (_seen)
            {
                _seen.Add((tool.Name, tool.Effect));
            }

            return new(ToolArgumentsValidationResult.Invalid(reason));
        }
    }
}
