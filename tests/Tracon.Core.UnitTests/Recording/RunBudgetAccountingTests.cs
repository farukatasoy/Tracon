using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// Phase 114's central claim, measured end to end through the REAL pipeline
/// <see cref="AgentDefinitionCompiler"/>/<see cref="ModelProviderRegistry"/>
/// assemble (not a mock of it): <see cref="RunBudgetChatClient"/> owns 100% of
/// a run tree's usage accounting, cuts a run off exactly at a tool-turn
/// boundary, and never double-counts against <see cref="RunRecordingAgent.CompleteAsync"/>.
/// </summary>
public sealed class RunBudgetAccountingTests
{
    private const string ToolName = "lookup";

    [Fact]
    public async Task Two_turn_tool_loop_is_recorded_exactly_once_no_cap()
    {
        // Regression guard for the double-counting class of bug (Open
        // Question 1): before this phase, BOTH RunBudgetChatClient's
        // predecessor-less Completion.cs line AND this decorator would have
        // recorded the same tokens, doubling the budget's total silently.
        var (store, client) = CreateToolLoopClient(firstTurnTokens: 600, secondTurnTokens: 300);
        var budget = new AgentRunBudget();

        var agent = CreateAgent(store, client, out _);

        await agent.RunAsync("go", options: new TraconRunOptions { Budget = budget });

        budget.ConsumedTokens.ShouldBe(900);
        client.CallCount.ShouldBe(2);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);
        run.Usage!.TotalTokens.ShouldBe(900);
    }

    [Fact]
    public async Task Third_turn_is_cut_off_before_it_reaches_the_provider()
    {
        // Cap sits BETWEEN turn 2's spend (900) and what a third turn would
        // add: turn 3 must never reach the fake client at all.
        var (store, client) = CreateThreeTurnToolLoopClient(600, 300, 999);
        var budget = new AgentRunBudget { MaxTotalTokens = 900 };

        var agent = CreateAgent(store, client, out _, errorClassifier: new DefaultRunErrorClassifier());

        var exception = await Should.ThrowAsync<TraconRunBudgetExceededException>(
            () => agent.RunAsync("go", options: new TraconRunOptions { Budget = budget }));

        exception.ErrorType.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);

        // The blocked turn never made a real call: exactly the two turns that
        // fit under the cap reached the fake provider.
        client.CallCount.ShouldBe(2);
        budget.ConsumedTokens.ShouldBe(900);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        run.Error.Class.ShouldBe(RunErrorClass.QuotaExceeded);
    }

    [Fact]
    public async Task Streaming_run_is_cut_off_at_the_same_turn_boundary()
    {
        var callCount = 0;

        var streamingByCall = new Func<int, IReadOnlyList<ChatResponseUpdate>>(call => call switch
        {
            1 => [ToolCallUpdate("call-1", withUsage: 600)],
            2 => [ToolCallUpdate("call-2", withUsage: 999)],
            _ => throw new InvalidOperationException("a third real call must never happen — the budget must cut it off first"),
        });

        // FakeChatClient does not support per-call streaming sequences, so
        // this test drives the fake provider directly through a small
        // counting wrapper instead of FakeChatClient's fixed list.
        var countingClient = new CountingStreamingChatClient(() =>
        {
            callCount++;
            return streamingByCall(callCount);
        });

        var (store, definitionCompiler) = CreateCompiler(countingClient);
        var budget = new AgentRunBudget { MaxTotalTokens = 900 };

        var agent = CreateAgent(store, definitionCompiler, errorClassifier: new DefaultRunErrorClassifier());

        var exception = await Should.ThrowAsync<TraconRunBudgetExceededException>(async () =>
        {
            await foreach (var _ in agent.RunStreamingAsync("go", options: new TraconRunOptions { Budget = budget }))
            {
                // draining is enough to drive the loop
            }
        });

        exception.ErrorType.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        callCount.ShouldBe(2);
        budget.ConsumedTokens.ShouldBe(1_599);
    }

    [Fact]
    public async Task No_cap_configured_keeps_todays_unlimited_behaviour()
    {
        var (store, client) = CreateThreeTurnToolLoopClient(600, 600, 600);
        var budget = new AgentRunBudget();

        var agent = CreateAgent(store, client, out _);

        await agent.RunAsync("go", options: new TraconRunOptions { Budget = budget });

        client.CallCount.ShouldBe(3);
        budget.ConsumedTokens.ShouldBe(1_800);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task Cost_cap_cuts_the_run_off_once_the_resolver_reports_enough_spend()
    {
        var (store, client) = CreateThreeTurnToolLoopClient(100, 100, 100);
        var budget = new AgentRunBudget { MaxTotalCost = 1.00m };

        // Each turn "costs" 0.60: the cap trips after turn 2 (1.20 >= 1.00),
        // and turn 3 must never reach the provider.
        var resolver = new FakeRunPricingResolver((_, _, _) => new RunCost { Source = PricingSource.Catalog, InputCost = 0.60m });

        var agent = CreateAgent(store, client, out _, resolver: resolver);

        await Should.ThrowAsync<TraconRunBudgetExceededException>(
            () => agent.RunAsync("go", options: new TraconRunOptions { Budget = budget }));

        client.CallCount.ShouldBe(2);
        budget.ConsumedCost.ShouldBe(1.20m);
        resolver.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task Unknown_pricing_falls_back_to_the_token_cap_and_never_cuts_on_cost_alone()
    {
        var (store, client) = CreateThreeTurnToolLoopClient(100, 100, 100);
        var budget = new AgentRunBudget { MaxTotalCost = 0.01m };

        var resolver = new FakeRunPricingResolver((_, _, _) => new RunCost { Source = PricingSource.Unknown });

        var agent = CreateAgent(store, client, out _, resolver: resolver);

        await agent.RunAsync("go", options: new TraconRunOptions { Budget = budget });

        client.CallCount.ShouldBe(3);
        budget.ConsumedCost.ShouldBe(0m);
        budget.IsCostBudgetExhausted.ShouldBeFalse();
    }

    [Fact]
    public async Task Pricing_resolver_is_never_consulted_when_no_cost_cap_is_set()
    {
        var (store, client) = CreateToolLoopClient(firstTurnTokens: 100, secondTurnTokens: 100);
        var budget = new AgentRunBudget(); // MaxTotalCost stays null

        var resolver = new FakeRunPricingResolver((_, _, _) => new RunCost { Source = PricingSource.Catalog, InputCost = 1m });

        var agent = CreateAgent(store, client, out _, resolver: resolver);

        await agent.RunAsync("go", options: new TraconRunOptions { Budget = budget });

        resolver.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_response_with_no_usage_is_neither_recorded_nor_fatal()
    {
        // The "empty/extreme input" failure mode the plan calls out: a
        // provider that reports no usage at all must not throw inside the
        // decorator and must not silently register as a zero-cost charge.
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")) { Usage = null });
        var budget = new AgentRunBudget { MaxTotalTokens = 1 };

        var agent = CreateAgent(store, client, out _);

        await agent.RunAsync("go", options: new TraconRunOptions { Budget = budget });

        budget.ConsumedTokens.ShouldBe(0);
        budget.IsTokenBudgetExhausted.ShouldBeFalse();

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task Two_different_agents_sharing_one_tree_budget_are_cut_off_by_each_others_spend()
    {
        // A call tree's branches are DIFFERENT compiled agents, each with its
        // OWN RunBudgetChatClient instance (a separate ModelProviderRegistry
        // pipeline). ChildAgentInvokerTests already proves the SAME
        // AgentRunBudget object is handed down the tree
        // (CreateChildOptions.Budget = scope.Budget); what this test adds is
        // that two SEPARATE decorator instances reading that shared object
        // through the ambient TraconRunContext actually see and are cut
        // off by each other's spend — not just their own.
        var budget = new AgentRunBudget { MaxTotalTokens = 900 };

        var rootClient = new ModelProviderRegistry([new FakeModelProvider(
            new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "root turn"))
            {
                Usage = Usage(700),
            }))]).CreateChatClient(TestData.Binding());

        var childClient = new ModelProviderRegistry([new FakeModelProvider(
            new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "child turn"))
            {
                Usage = Usage(300),
            }))]).CreateChatClient(TestData.Binding());

        SetScope(budget);
        await rootClient.GetResponseAsync([new ChatMessage(ChatRole.User, "go")], cancellationToken: TestContext.Current.CancellationToken);
        budget.ConsumedTokens.ShouldBe(700);

        // The child's own instance has spent NOTHING yet, but the tree
        // already has: 700 (root) + 300 (child, about to spend) >= 900.
        SetScope(budget);
        await childClient.GetResponseAsync([new ChatMessage(ChatRole.User, "go")], cancellationToken: TestContext.Current.CancellationToken);
        budget.ConsumedTokens.ShouldBe(1_000);
        budget.IsTokenBudgetExhausted.ShouldBeTrue();

        // A THIRD call from the ROOT's own client (which has personally only
        // ever spent 700, well under 900) is refused anyway: the exhaustion
        // check reads the tree's total, not "this decorator's own" total.
        SetScope(budget);
        await Should.ThrowAsync<TraconRunBudgetExceededException>(
            () => rootClient.GetResponseAsync([new ChatMessage(ChatRole.User, "go")], cancellationToken: TestContext.Current.CancellationToken));
    }

    private static void SetScope(AgentRunBudget budget)
        => TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = TraconId.NewId(),
            RootRunId = TraconId.NewId(),
            Budget = budget,
        });

    // ---- fixtures ----

    private static (InMemoryRunStore Store, FakeChatClient Client) CreateToolLoopClient(long firstTurnTokens, long secondTurnTokens)
    {
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var calls = 0;

        var client = new FakeChatClient(_ =>
        {
            calls++;

            return calls switch
            {
                1 => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("call-1", ToolName, new Dictionary<string, object?>(StringComparer.Ordinal))]))
                {
                    Usage = Usage(firstTurnTokens),
                },
                _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"))
                {
                    Usage = Usage(secondTurnTokens),
                },
            };
        });

        return (store, client);
    }

    private static (InMemoryRunStore Store, FakeChatClient Client) CreateThreeTurnToolLoopClient(
        long firstTurnTokens,
        long secondTurnTokens,
        long thirdTurnTokens)
    {
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var calls = 0;

        var client = new FakeChatClient(_ =>
        {
            calls++;

            return calls switch
            {
                1 => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("call-1", ToolName, new Dictionary<string, object?>(StringComparer.Ordinal))]))
                {
                    Usage = Usage(firstTurnTokens),
                },
                2 => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("call-2", ToolName, new Dictionary<string, object?>(StringComparer.Ordinal))]))
                {
                    Usage = Usage(secondTurnTokens),
                },
                3 => new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"))
                {
                    Usage = Usage(thirdTurnTokens),
                },
                _ => throw new InvalidOperationException(
                    "a fourth real call must never happen in these fixtures"),
            };
        });

        return (store, client);
    }

    private static (InMemoryRunStore Store, AgentDefinitionCompiler Compiler) CreateCompiler(IChatClient rawClient)
    {
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var provider = new FakeModelProvider(rawClient);
        var registry = TestData.Registry(TestData.Tool(ToolName));
        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), registry);

        return (store, compiler);
    }

    private static RunRecordingAgent CreateAgent(
        InMemoryRunStore store,
        FakeChatClient client,
        out AgentDefinitionCompiler compiler,
        IRunErrorClassifier? errorClassifier = null,
        IRunPricingResolver? resolver = null)
    {
        var provider = new FakeModelProvider(client);
        var registry = TestData.Registry(TestData.Tool(ToolName));

        var services = new ServiceCollection();
        if (resolver is not null)
        {
            services.AddSingleton(resolver);
        }

        var models = new ModelProviderRegistry([provider], services: services.BuildServiceProvider());
        compiler = new AgentDefinitionCompiler(models, registry);

        var definition = TestData.Definition(toolNames: [ToolName]);

        return new RunRecordingAgent(
            compiler.Compile(definition),
            store,
            FixedTenantContext.Default,
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            errorClassifier: errorClassifier);
    }

    private static RunRecordingAgent CreateAgent(
        InMemoryRunStore store,
        AgentDefinitionCompiler compiler,
        IRunErrorClassifier? errorClassifier = null)
    {
        var definition = TestData.Definition(toolNames: [ToolName]);

        return new RunRecordingAgent(
            compiler.Compile(definition),
            store,
            FixedTenantContext.Default,
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            errorClassifier: errorClassifier);
    }

    private static UsageDetails Usage(long total) => new() { InputTokenCount = total, OutputTokenCount = 0, TotalTokenCount = total };

    private static ChatResponseUpdate ToolCallUpdate(string callId, long withUsage)
        => new(
            ChatRole.Assistant,
            [
                new FunctionCallContent(callId, ToolName, new Dictionary<string, object?>(StringComparer.Ordinal)),
                new UsageContent(Usage(withUsage)),
            ]);

    /// <summary>
    /// A minimal <see cref="IChatClient"/> whose streaming path is driven by a
    /// per-call factory. <see cref="FakeChatClient"/> only supports one fixed
    /// update list; this fixture is needed to alternate content between calls
    /// for a streaming multi-turn tool loop.
    /// </summary>
    private sealed class CountingStreamingChatClient(Func<IReadOnlyList<ChatResponseUpdate>> next) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("this fixture is streaming-only");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in next())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return update;
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
