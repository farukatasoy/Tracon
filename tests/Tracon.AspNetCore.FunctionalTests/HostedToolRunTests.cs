using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using TraconTestHost = Tracon.AspNetCore.FunctionalTests.Infrastructure.TraconTestHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A hosted tool a host registered in code, through a real DI-built host: the tool reaches the model
/// call, its calls become tool events under the registered name, its cost enters the run's non-token total,
/// the tenant's quota, the <c>run.completed</c> webhook and the tree's budget exactly once, and a call of a tool
/// nobody registered (the harness's web search) is billed all the same and recorded as before.
/// </summary>
/// <remarks>
/// <para>
/// The provider's response names a kind of call, not the tool; the name comes from the options of the model call
/// (<c>HostedToolIdentity</c>), through the cost ring, to the recorder. Each hop crosses a boundary a unit test
/// does not: the compiled agent that built the tools, the pipeline that carries the options, the ambient run
/// scope, the recorder that reads the ledger, and the stores that read the result. Every scenario below runs both
/// the buffered and the streaming path, because a streamed call arrives before the response ends.
/// </para>
/// <para>
/// The scripted provider cannot know what a real adapter returns; the shapes it returns here were measured on the
/// wire in <c>HostedToolWireTests</c>.
/// </para>
/// </remarks>
public sealed class HostedToolRunTests
{
    private const string AgentName = "hosted-agent";
    private const string ProviderName = "hosted-model";

    private static readonly decimal WebSearchPrice = 0.01m;
    private static readonly decimal CodeInterpreterPrice = 0.03m;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_registered_tools_calls_are_tool_events_under_its_registered_name_and_carry_their_cost(bool streaming)
    {
        var provider = new ContentModelProvider(Calls(), ProviderName);
        var publisher = new RecordingWebhookPublisher();

        await using var host = await StartAsync(provider, ["search", "python"], publisher: publisher);

        await RunAsync(host, streaming);

        var run = await OutputTruncationTests.SingleRunAsync(host);
        var events = await OutputTruncationTests.EventsAsync(host, run.Id);

        run.Status.ShouldBe(RunStatus.Completed);

        events.Where(static e => e.Type is RunEventType.ToolInvoking or RunEventType.ToolInvoked)
            .Select(static e => (e.Type, e.ToolName, e.ToolCallId))
            .ShouldBe(
            [
                (RunEventType.ToolInvoking, "search", "ws_1"),
                (RunEventType.ToolInvoked, "search", "ws_1"),
                (RunEventType.ToolInvoking, "python", "ci_1"),
                (RunEventType.ToolInvoked, "python", "ci_1"),
            ]);

        // The registered calls are no content events any more: the result of a call is part of the call.
        events.ShouldNotContain(static e => string.Equals(e.CustomType, RunEventCustomTypes.Content, StringComparison.Ordinal));
        events.ShouldNotContain(static e => e.Type == RunEventType.ToolFailed);
        events.Select(static e => e.Payload).Concat(events.Select(static e => e.Text))
            .ShouldNotContain(static text => text != null && text.Contains("private query", StringComparison.Ordinal));

        var invocations = await host.Services.GetRequiredService<IRunStore>().ListToolInvocationsAsync(run.Id);

        invocations.Select(static call => (call.ToolName, call.ToolCallId, call.Succeeded)).ShouldBe(
            [("search", "ws_1", true), ("python", "ci_1", true)],
            ignoreOrder: true);

        var search = invocations.Single(static call => string.Equals(call.ToolName, "search", StringComparison.Ordinal)).Usage.ShouldNotBeNull();
        search.Unit.ShouldBe(ToolUsageUnits.Calls);
        search.Quantity.ShouldBe(1m);
        search.Cost.ShouldBe(WebSearchPrice);
        search.IsEstimated.ShouldBeFalse();
        search.Provider.ShouldBe(ProviderName);

        invocations.Single(static call => string.Equals(call.ToolName, "python", StringComparison.Ordinal)).Usage.ShouldNotBeNull()
            .IsEstimated.ShouldBeTrue("a code interpreter is billed by container, and the response names none");

        var nonToken = run.NonTokenCost.ShouldNotBeNull();
        nonToken.Amount.ShouldBe(WebSearchPrice + CodeInterpreterPrice);
        nonToken.Currency.ShouldBe("USD");
        nonToken.IsEstimated.ShouldBeTrue();
        nonToken.UnsummedItemCount.ShouldBe(0);

        // Counted once, wherever a decision reads it: the quota entry and the webhook.
        (await NodeUsageHost.DailyCounterAsync(host, AgentName)).Cost.ShouldBe(WebSearchPrice + CodeInterpreterPrice);

        var completed = publisher.Runs(WebhookEvents.RunCompleted).ShouldHaveSingleItem();
        completed.Cost.ShouldBe(WebSearchPrice + CodeInterpreterPrice);
        completed.NonTokenCost.ShouldBe(WebSearchPrice + CodeInterpreterPrice);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_call_a_stream_shows_in_several_contents_is_one_call(bool streaming)
    {
        // Measured against OpenAI: a web search comes when it starts and again when it ends, a code interpreter
        // comes once for every fragment of its code, all with the same call identifier.
        var provider = new ContentModelProvider(
            [
                new WebSearchToolCallContent("ws_1"),
                new CodeInterpreterToolCallContent("ci_1"),
                new CodeInterpreterToolCallContent("ci_1"),
                new CodeInterpreterToolCallContent("ci_1"),
                new WebSearchToolCallContent("ws_1") { Queries = ["private query text"] },
                new WebSearchToolResultContent("ws_1"),
                new TextContent("the answer"),
            ],
            ProviderName);

        await using var host = await StartAsync(provider, ["search", "python"]);

        await RunAsync(host, streaming);

        var run = await OutputTruncationTests.SingleRunAsync(host);
        var events = await OutputTruncationTests.EventsAsync(host, run.Id);

        events.Where(static e => e.Type is RunEventType.ToolInvoking or RunEventType.ToolInvoked)
            .Select(static e => (e.Type, e.ToolName))
            .ShouldBe(
            [
                (RunEventType.ToolInvoking, "search"),
                (RunEventType.ToolInvoked, "search"),
                (RunEventType.ToolInvoking, "python"),
                (RunEventType.ToolInvoked, "python"),
            ]);
        (await host.Services.GetRequiredService<IRunStore>().ListToolInvocationsAsync(run.Id)).Count.ShouldBe(2);
        run.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(WebSearchPrice + CodeInterpreterPrice);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_kind_with_no_price_is_listed_on_the_run_and_never_priced_at_zero(bool streaming)
    {
        var provider = new ContentModelProvider([new WebSearchToolCallContent("ws_1"), new TextContent("the answer")], ProviderName);

        await using var host = await StartAsync(provider, ["search"], priced: false);

        await RunAsync(host, streaming);

        var run = await OutputTruncationTests.SingleRunAsync(host);
        var nonToken = run.NonTokenCost.ShouldNotBeNull();

        nonToken.Amount.ShouldBeNull();
        nonToken.UnsummedItemCount.ShouldBe(1);

        var invocation = (await host.Services.GetRequiredService<IRunStore>().ListToolInvocationsAsync(run.Id)).ShouldHaveSingleItem();
        invocation.ToolName.ShouldBe("search");
        invocation.Usage.ShouldNotBeNull().Cost.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_call_of_a_tool_the_agent_did_not_select_is_a_content_event_and_is_billed(bool streaming)
    {
        // The harness adds its own web search, an extension may add another: a tool nobody registered.
        // It keeps the content event K-1034 gave it, and its call is billed.
        var provider = new ContentModelProvider([new WebSearchToolCallContent("ws_1"), new TextContent("the answer")], ProviderName);

        await using var host = await StartAsync(provider, []);

        await RunAsync(host, streaming);

        var run = await OutputTruncationTests.SingleRunAsync(host);
        var events = await OutputTruncationTests.EventsAsync(host, run.Id);

        events.ShouldContain(static e => string.Equals(e.CustomType, RunEventCustomTypes.Content, StringComparison.Ordinal) && e.Text == "web_search call");
        events.ShouldNotContain(static e => e.Type == RunEventType.ToolInvoking);
        run.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(WebSearchPrice);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_harness_web_search_is_billed_and_stays_a_content_event(bool streaming)
    {
        var provider = new ContentModelProvider([new WebSearchToolCallContent("ws_1"), new TextContent("the answer")], ProviderName);
        var publisher = new RecordingWebhookPublisher();

        await using var host = await StartAsync(provider, [], harness: new HarnessSettings { EnableWebSearch = true }, publisher: publisher);

        await RunAsync(host, streaming);

        var run = await OutputTruncationTests.SingleRunAsync(host);

        provider.Options.ShouldNotBeEmpty();
        provider.Options[0]!.Tools.ShouldNotBeNull().ShouldContain(static tool => tool is HostedWebSearchTool);

        run.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(WebSearchPrice);
        (await NodeUsageHost.DailyCounterAsync(host, AgentName)).Cost.ShouldBe(WebSearchPrice);
        publisher.Runs(WebhookEvents.RunCompleted).ShouldHaveSingleItem().NonTokenCost.ShouldBe(WebSearchPrice);

        var events = await OutputTruncationTests.EventsAsync(host, run.Id);
        events.ShouldContain(static e => string.Equals(e.CustomType, RunEventCustomTypes.Content, StringComparison.Ordinal));
        events.ShouldNotContain(static e => e.Type == RunEventType.ToolInvoking);
    }

    [Fact]
    public async Task A_hosted_call_that_spends_the_ceiling_stops_the_next_model_turn()
    {
        // Turn one holds a code interpreter call AND a function call, so the tool-call loop asks the model again.
        // The 0.03 the call cost is over the 0.01 ceiling: the budget ring has to know before turn two.
        var provider = new TurnsModelProvider(
            ProviderName,
            [new CodeInterpreterToolCallContent("ci_1"), new FunctionCallContent("fc_1", "lookup")],
            [new TextContent("the answer")]);

        await using var host = await StartAsync(provider, ["python", "lookup"], configureOptions: options => options.AgentGraph.MaxTotalCost = 0.01m);

        var agent = await ResolveAsync(host);

        await Should.ThrowAsync<TraconRunBudgetExceededException>(() => agent.RunAsync("go"));

        provider.Options.Count.ShouldBe(1, "the second turn never reached the provider");
    }

    [Fact]
    public async Task A_hosted_call_that_spends_the_ceiling_stops_the_next_model_turn_of_a_streaming_run_too()
    {
        // The streamed call is reported when its content arrives, before the stream ends, so the budget ring
        // that guards turn two already knows.
        var provider = new TurnsModelProvider(
            ProviderName,
            [new CodeInterpreterToolCallContent("ci_1"), new FunctionCallContent("fc_1", "lookup")],
            [new TextContent("the answer")]);

        await using var host = await StartAsync(provider, ["python", "lookup"], configureOptions: options => options.AgentGraph.MaxTotalCost = 0.01m);

        var agent = await ResolveAsync(host);

        await Should.ThrowAsync<TraconRunBudgetExceededException>(async () =>
        {
            await foreach (var _ in agent.RunStreamingAsync("go"))
            {
                // Drain.
            }
        });

        provider.Options.Count.ShouldBe(1, "the second turn never reached the provider");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_store_that_refuses_the_tool_events_does_not_fail_the_answer(bool streaming)
    {
        // Observability does not break functionality: the provider's search happened and is billed; the record of it may not be written.
        var provider = new ContentModelProvider([new WebSearchToolCallContent("ws_1"), new TextContent("the answer")], ProviderName);

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider)
                    .AddHostedTool("search", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool())
                    .AddAgent(Definition(["search"]));
                builder.Services.ThrowFrom<IRunStore>(
                    nameof(IRunStore.AppendEventAsync),
                    static () => new InvalidOperationException("the event table is down"),
                    when: static arguments => arguments[0] is RunEvent { Type: RunEventType.ToolInvoking or RunEventType.ToolInvoked });
            });

        if (streaming)
        {
            using var stream = await host.Client.PostAsJsonAsync(
                new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative),
                new AgentRunRequest { Message = "hello" },
                TestContext.Current.CancellationToken);
            await using var body = await stream.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);

            (await SseReader.ReadAllAsync(body)).ShouldContain(static frame => string.Equals(frame.Event, "done", StringComparison.Ordinal));

            return;
        }

        await RunAsync(host, streaming: false);
    }

    [Fact]
    public async Task A_response_cache_hit_replays_a_hosted_call_as_a_content_event_and_bills_nothing()
    {
        // The run did not search: a hit replays the stored response. The call is recorded as content the response held,
        // not as a tool the run invoked, and the cost ring (inside the cache) never saw it.
        var provider = new ContentModelProvider([new WebSearchToolCallContent("ws_1"), new TextContent("the answer")], ProviderName);

        await using var host = await StartAsync(
            provider,
            ["search"],
            configureAgent: static definition => definition with
            {
                Model = definition.Model with { ResponseCache = new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(5) } },
            },
            configureServices: static services => services.AddDistributedMemoryCache());

        await RunAsync(host, streaming: false);
        await RunAsync(host, streaming: false);

        provider.Options.Count.ShouldBe(1, "the second run was a cache hit");

        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(new Uri("/tracon/api/runs", UriKind.Relative), TestContext.Current.CancellationToken);
        var hit = (await host.WaitForTerminalRunAsync(runs!.OrderBy(static run => run.StartedAt).Last().Id));
        var events = await OutputTruncationTests.EventsAsync(host, hit.Id);

        events.ShouldNotContain(static e => e.Type == RunEventType.ToolInvoking);
        events.ShouldContain(static e => string.Equals(e.CustomType, RunEventCustomTypes.Content, StringComparison.Ordinal) && e.Text == "web_search call");
        hit.NonTokenCost.ShouldBeNull();
    }

    [Fact]
    public async Task Without_a_hosted_price_the_same_two_turn_run_completes()
    {
        var provider = new TurnsModelProvider(
            ProviderName,
            [new CodeInterpreterToolCallContent("ci_1"), new FunctionCallContent("fc_1", "lookup")],
            [new TextContent("the answer")]);

        await using var host = await StartAsync(provider, ["python", "lookup"], priced: false, configureOptions: options => options.AgentGraph.MaxTotalCost = 0.01m);

        var agent = await ResolveAsync(host);
        var response = await agent.RunAsync("go");

        response.Text.ShouldContain("the answer", Case.Sensitive);
        provider.Options.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Each_kind_reaches_the_model_call_as_the_tool_its_adapter_maps()
    {
        var provider = new ContentModelProvider([new TextContent("the answer")], ProviderName);

        await using var host = await StartAsync(provider, ["search", "python", "docs", "lookup"]);

        await RunAsync(host, streaming: false);

        var tools = provider.Options.ShouldHaveSingleItem().ShouldNotBeNull().Tools.ShouldNotBeNull();

        tools.Count.ShouldBe(4);
        tools[0].ShouldBeOfType<HostedWebSearchTool>();
        tools[1].ShouldBeOfType<HostedCodeInterpreterTool>();
        tools[2].ShouldBeOfType<HostedFileSearchTool>().Inputs.ShouldHaveSingleItem()
            .ShouldBeOfType<HostedVectorStoreContent>().VectorStoreId.ShouldBe("vs_default");
        tools[3].Name.ShouldBe("lookup");
    }

    [Fact]
    public async Task An_agent_that_selects_no_hosted_tool_sends_none_and_runs_no_factory()
    {
        var provider = new ContentModelProvider([new TextContent("the answer")], ProviderName);
        var factories = 0;

        await using var host = await StartAsync(provider, ["lookup"], onFactory: () => Interlocked.Increment(ref factories));

        await RunAsync(host, streaming: false);

        provider.Options.ShouldHaveSingleItem()!.Tools.ShouldNotBeNull().ShouldAllBe(static tool => tool.Name == "lookup");
        factories.ShouldBe(0);
    }

    [Fact]
    public async Task A_tenant_gets_the_tool_its_own_factory_context_builds_and_a_replaced_store_reaches_the_next_run()
    {
        var provider = new ContentModelProvider([new TextContent("the answer")], ProviderName);
        var stores = new Dictionary<string, string>(StringComparer.Ordinal) { ["tenant-a"] = "vs_a1", ["tenant-b"] = "vs_b1" };

        await using var host = await StartAsync(provider, ["docs"], vectorStore: context => stores.GetValueOrDefault(context.TenantId, "vs_default"), tenancy: true);

        await RunAsTenantAsync(host, "tenant-a");
        await RunAsTenantAsync(host, "tenant-b");

        // The cache serves the compiled agent of each tenant, and never the other tenant's.
        StoreOf(provider.Options[0]).ShouldBe("vs_a1");
        StoreOf(provider.Options[1]).ShouldBe("vs_b1");

        // The tenant replaces its vector store; the definition did not change. The next run must see it: the
        // compiled-agent cache key follows what the factory builds.
        stores["tenant-a"] = "vs_a2";

        await RunAsTenantAsync(host, "tenant-a");
        await RunAsTenantAsync(host, "tenant-b");

        StoreOf(provider.Options[2]).ShouldBe("vs_a2");
        StoreOf(provider.Options[3]).ShouldBe("vs_b1");
    }

    private static string StoreOf(ChatOptions? options)
        => options.ShouldNotBeNull().Tools.ShouldNotBeNull().OfType<HostedFileSearchTool>().Single().Inputs.ShouldHaveSingleItem()
            .ShouldBeOfType<HostedVectorStoreContent>().VectorStoreId;

    private static List<AIContent> Calls() =>
    [
        new WebSearchToolCallContent("ws_1") { Queries = ["private query text"] },
        new WebSearchToolResultContent("ws_1"),
        new CodeInterpreterToolCallContent("ci_1"),
        new CodeInterpreterToolResultContent("ci_1"),
        new TextContent("the answer"),
    ];

    private static AgentDefinition Definition(IReadOnlyList<string> toolNames, HarnessSettings? harness = null) => new()
    {
        Name = AgentName,
        Description = "Uses hosted tools.",
        Instructions = "Answer briefly.",
        Model = new ModelBinding { Provider = ProviderName, Model = $"{ProviderName}-1" },
        ToolNames = toolNames,
        Harness = harness,
        Origin = AgentDefinitionOrigin.Code,
    };

    private static Task<TraconTestHost> StartAsync(
        IModelProvider provider,
        IReadOnlyList<string> toolNames,
        HarnessSettings? harness = null,
        bool priced = true,
        RecordingWebhookPublisher? publisher = null,
        Action<TraconOptions>? configureOptions = null,
        Func<HostedToolContext, string>? vectorStore = null,
        Action? onFactory = null,
        bool tenancy = false,
        Func<AgentDefinition, AgentDefinition>? configureAgent = null,
        Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            builder => WithTenancy(builder, tenancy)
                .AddModelProvider(provider)
                .AddTool((Func<string>)(static () => "42"), name: "lookup", description: "Looks something up.")
                .AddHostedTool("search", HostedToolKind.WebSearch, _ => Counted(onFactory, new HostedWebSearchTool()), "Searches the web.")
                .AddHostedTool("python", HostedToolKind.CodeInterpreter, _ => Counted(onFactory, new HostedCodeInterpreterTool()))
                .AddHostedTool(
                    "docs",
                    HostedToolKind.FileSearch,
                    context => Counted(
                        onFactory,
                        new HostedFileSearchTool { Inputs = [new HostedVectorStoreContent(vectorStore?.Invoke(context) ?? "vs_default")] }))
                .AddAgent(configureAgent is null ? Definition(toolNames, harness) : configureAgent(Definition(toolNames, harness))),
            configureServices: services =>
            {
                configureServices?.Invoke(services);

                services.Configure<TraconOptions>(options =>
                {
                    options.Pricing.Currency = "USD";

                    if (priced)
                    {
                        options.Pricing.HostedTools[provider.Name] = new Dictionary<string, HostedToolPriceOverride>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["WebSearch"] = new HostedToolPriceOverride { PerCall = WebSearchPrice },
                            ["CodeInterpreter"] = new HostedToolPriceOverride { PerCall = CodeInterpreterPrice },
                            ["FileSearch"] = new HostedToolPriceOverride { PerCall = 0.0025m },
                        };
                    }

                    configureOptions?.Invoke(options);
                });

                if (publisher is not null)
                {
                    services.AddSingleton<IWebhookPublisher>(publisher);
                }
            });

    private static ITraconBuilder WithTenancy(ITraconBuilder builder, bool tenancy)
        => tenancy
            ? builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            })
            : builder;

    private static T Counted<T>(Action? onFactory, T tool)
    {
        onFactory?.Invoke();

        return tool;
    }

    private static async Task<AIAgent> ResolveAsync(TraconTestHost host)
        => (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, culture: null, CancellationToken.None)).ShouldNotBeNull();

    private static async Task RunAsync(TraconTestHost host, bool streaming)
    {
        var uri = new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

        if (streaming)
        {
            using var stream = await host.Client.PostAsJsonAsync(uri, new AgentRunRequest { Message = "hello" }, TestContext.Current.CancellationToken);
            await using var body = await stream.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            var frames = await SseReader.ReadAllAsync(body);

            frames.ShouldContain(static frame => string.Equals(frame.Event, "done", StringComparison.Ordinal));

            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static async Task RunAsTenantAsync(TraconTestHost host, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add("X-Tracon-Tenant", tenant);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
