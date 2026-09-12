using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Graph;

/// <summary>
/// The limits enforced by the child agent wrapper: depth, budget, tenant, and approval.
/// </summary>
/// <remarks>
/// The wrapper is called with <c>options = null</c> - this is how Microsoft Agent
/// Framework's background task tool calls it (measured in Phase 12). The tests
/// repeat this real call shape; passing settings as a parameter would skip the
/// real path and miss regressions.
/// </remarks>
public sealed class ChildAgentInvokerTests
{
    [Fact]
    public async Task Call_is_rejected_when_no_scope()
    {
        var (invoker, _) = CreateInvoker();

        TraconRunContext.SetCurrent(null);

        var response = await invoker.RunAsync("run");

        response.Text.ShouldContain("run recording is off", Case.Sensitive);
    }

    [Fact]
    public async Task Call_is_rejected_when_depth_limit_exceeded()
    {
        var (invoker, store) = CreateInvoker();

        SetScope(depth: 3, budget: new AgentRunBudget { MaxDepth = 3 });

        var response = await invoker.RunAsync("run");

        response.Text.ShouldContain("call depth limit was exceeded", Case.Sensitive);

        // A rejection is not an exception and the child run NEVER starts.
        (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldBeEmpty();
    }

    [Fact]
    public async Task New_child_run_does_not_start_once_budget_is_exhausted()
    {
        var (invoker, store) = CreateInvoker();
        var budget = new AgentRunBudget { MaxDepth = 3, MaxTotalRuns = 1 };

        SetScope(depth: 0, budget: budget);
        await invoker.RunAsync("first");

        SetScope(depth: 0, budget: budget);
        var second = await invoker.RunAsync("second");

        second.Text.ShouldContain("child-run limit is reached", Case.Sensitive);
        (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).Count.ShouldBe(1);
    }

    [Fact]
    public async Task New_child_run_does_not_start_once_the_deadline_has_passed()
    {
        // 128.2: TryReserveRun (child-run start) also checks the deadline —
        // no new child run starts in a tree whose time has run out. A
        // negative MaxDuration constructs an already-passed Deadline
        // deterministically, with no fake clock needed.
        var (invoker, store) = CreateInvoker();
        var budget = new AgentRunBudget(TimeSpan.FromMinutes(-1)) { MaxDepth = 3 };

        SetScope(depth: 0, budget: budget);
        var response = await invoker.RunAsync("run");

        response.Text.ShouldContain("time budget is exhausted", Case.Sensitive);
        (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Call_is_rejected_when_tenant_changes()
    {
        // The child call runs on a different thread. If the tenant context is
        // lost it falls back to the default tenant, and one tenant's agent
        // would operate on another tenant's data. This is exactly where the leak occurs.
        var (invoker, store) = CreateInvoker();

        SetScope(depth: 0, budget: new AgentRunBudget { MaxDepth = 3 }, tenantId: "other-tenant");

        var response = await invoker.RunAsync("run");

        response.Text.ShouldContain("cannot leave the caller's tenant", Case.Sensitive);
        (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Child_run_attaches_to_the_tree()
    {
        var (invoker, store) = CreateInvoker();
        var rootRunId = TraconId.NewId();

        SetScope(depth: 0, budget: new AgentRunBudget { MaxDepth = 3 }, runId: rootRunId);

        await invoker.RunAsync("run");

        var child = (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
        child.ParentRunId.ShouldBe(rootRunId);
        child.RootRunId.ShouldBe(rootRunId);
        child.Depth.ShouldBe(1);
        child.AgentName.ShouldBe("researcher");
    }

    [Fact]
    public async Task Second_layer_preserves_the_root()
    {
        var (invoker, store) = CreateInvoker();
        var rootRunId = TraconId.NewId();
        var middleRunId = TraconId.NewId();

        // The middle run is already inside the tree: the root id is carried
        // from the scope, not from it. A copied RootRunId would break here.
        SetScope(depth: 1, budget: new AgentRunBudget { MaxDepth = 3 }, runId: middleRunId, rootRunId: rootRunId);

        await invoker.RunAsync("run");

        var child = (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
        child.ParentRunId.ShouldBe(middleRunId);
        child.RootRunId.ShouldBe(rootRunId);
        child.Depth.ShouldBe(2);
    }

    [Fact]
    public async Task Budget_is_a_SINGLE_instance_across_the_tree()
    {
        var (invoker, _) = CreateInvoker();
        var budget = new AgentRunBudget { MaxDepth = 3, MaxTotalTokens = 1_000_000 };

        SetScope(depth: 0, budget: budget);

        await invoker.RunAsync("run");

        // The child run's spend is recorded into the same counter the caller sees.
        // If the budget were copied, each branch would start from its own zero
        // and the limit would lose its meaning.
        budget.ConsumedTokens.ShouldBe(42);
        budget.StartedRuns.ShouldBe(1);
    }

    [Fact]
    public async Task Child_run_requiring_approval_returns_an_understandable_error()
    {
        var approvalTool = new ApprovalRequiredAIFunction(TestData.Tool("dangerous_task"));

        var client = new FakeChatClient(_ => new ChatResponse(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "dangerous_task", null)])));

        var (invoker, store) = CreateInvoker(client, tools: [approvalTool]);

        SetScope(depth: 0, budget: new AgentRunBudget { MaxDepth = 3 });

        var response = await invoker.RunAsync("run");

        response.Text.ShouldContain("requires user approval", Case.Sensitive);
        response.Text.ShouldContain("auto-approval rule", Case.Sensitive);

        // The child run is not counted as successful: the model returned a
        // question that cannot be answered, not a result.
        var child = (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
        child.Status.ShouldBe(RunStatus.Failed);
        child.Error!.Message.ShouldContain("A child agent cannot request approval", Case.Sensitive);
    }

    [Fact]
    public async Task Callers_own_cancellation_still_propagates_when_no_timeout_fires()
    {
        // 144.1 regression: the caller's real cancellation must not be
        // mistaken for either wait-limit layer. Both layers are set far
        // longer than the test's own cancellation, so only the caller's
        // token can possibly end this call.
        var (invoker, store) = CreateInvoker(
            new BlockingChatClient(), childDeadline: TimeSpan.FromSeconds(30), waitTimeout: TimeSpan.FromSeconds(60));

        SetScope(depth: 0, budget: new AgentRunBudget { MaxDepth = 3 });

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await invoker.RunAsync("run", cancellationToken: cts.Token));

        // The refusal-as-tool-result path (used for both wait-limit layers)
        // was NOT taken - a real cancellation propagates as an exception.
        var events = new List<RunEventType>();

        await foreach (var runEvent in store.ReadEventsAsync(
            (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).Single().Id))
        {
            events.Add(runEvent.Type);
        }

        events.ShouldNotContain(RunEventType.ChildRunTimedOut);
    }

    [Fact]
    public async Task One_child_timing_out_does_not_affect_a_sibling_call_that_completes_normally()
    {
        var budget = new AgentRunBudget { MaxDepth = 3 };

        var (hangingInvoker, hangingStore) = CreateInvoker(
            new BlockingChatClient(), childDeadline: TimeSpan.FromMilliseconds(50), waitTimeout: TimeSpan.FromSeconds(5));
        var (fastInvoker, fastStore) = CreateInvoker();

        SetScope(depth: 0, budget: budget);
        var hangingCall = hangingInvoker.RunAsync("run");

        SetScope(depth: 0, budget: budget);
        var fastCall = fastInvoker.RunAsync("run");

        var hangingResponse = await hangingCall;
        var fastResponse = await fastCall;

        hangingResponse.Text.ShouldContain("did not respond in time", Case.Sensitive);
        fastResponse.Text.ShouldBe("research result");

        var hangingChild = (await hangingStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
        hangingChild.Status.ShouldBe(RunStatus.Canceled);

        var fastChild = (await fastStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
        fastChild.Status.ShouldBe(RunStatus.Completed);

        // Both calls reserved a run against the SAME shared budget.
        budget.StartedRuns.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_provider_timeout_is_not_reported_as_the_sub_agents_own_wait_limit(bool streaming)
    {
        // K-737 regression: the provider reports its OWN request timeout as an
        // OperationCanceledException while NOTHING was cancelled. Both wait
        // limits are set far longer than this call, so neither layer can fire -
        // the only honest sentence here is "the provider did not answer".
        var (invoker, store) = CreateInvoker(
            new TimingOutChatClient(),
            childDeadline: TimeSpan.FromSeconds(30),
            waitTimeout: TimeSpan.FromSeconds(60));

        SetScope(depth: 0, budget: new AgentRunBudget { MaxDepth = 3 });

        var text = streaming
            ? await ReadStreamTextAsync(invoker)
            : (await invoker.RunAsync("run")).Text;

        text.ShouldContain("did not answer", Case.Sensitive);
        text.ShouldNotContain("did not respond in time", Case.Sensitive);

        // The sub-run itself already recorded the truth (K-737 applies in
        // RunRecordingAgent): a provider fault is Failed, never Canceled.
        var child = (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
        child.Status.ShouldBe(RunStatus.Failed);
    }

    /// <summary>Drains a streaming sub-call into the text the caller would see.</summary>
    private static async Task<string> ReadStreamTextAsync(ChildAgentInvoker invoker)
    {
        var text = new System.Text.StringBuilder();

        await foreach (var update in invoker.RunStreamingAsync("run"))
        {
            text.Append(update.Text);
        }

        return text.ToString();
    }

    /// <summary>Writes the scope to the current flow.</summary>
    /// <remarks>
    /// <see cref="TraconRunContext"/> is built on an <c>AsyncLocal</c>;
    /// the assignment must happen in the test's own body, not in a helper
    /// <c>async</c> method. This method is deliberately synchronous.
    /// </remarks>
    private static void SetScope(
        int depth,
        AgentRunBudget budget,
        Guid? runId = null,
        Guid? rootRunId = null,
        string tenantId = "test")
    {
        var id = runId ?? TraconId.NewId();

        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = id,
            RootRunId = rootRunId ?? id,
            Depth = depth,
            AgentName = "router",
            TenantId = tenantId,
            Budget = budget,
        });
    }

    private static (ChildAgentInvoker Invoker, InMemoryRunStore Store) CreateInvoker(
        IChatClient? client = null,
        TimeSpan? childDeadline = null,
        TimeSpan? waitTimeout = null,
        params AIFunction[] tools)
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var tenantContext = new FixedTenantContext();

        var chatClient = client ?? new FakeChatClient(_ => new ChatResponse(
            new ChatMessage(ChatRole.Assistant, "research result"))
        {
            Usage = new UsageDetails { TotalTokenCount = 42 },
        });

        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(chatClient)),
            TestData.Registry(tools));

        var inner = new RunRecordingAgent(
            compiler.Compile(TestData.Definition(
                name: "researcher",
                toolNames: [.. tools.Select(static tool => tool.Name)])),
            store,
            tenantContext,
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance);

        var resolver = new CallableAgentResolver(new SingleAgentServices(inner));

        var invoker = new ChildAgentInvoker(
            resolver,
            tenantContext,
            NullLogger.Instance,
            "router",
            new CallableAgentInfo("researcher", "Performs research.", 1),
            childDeadline: childDeadline ?? TimeSpan.FromSeconds(30),
            waitTimeout: waitTimeout ?? TimeSpan.FromSeconds(60));

        return (invoker, store);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }

    /// <summary>The smallest catalog, returning a single agent.</summary>
    private sealed class SingleAgentServices(AIAgent agent) : IServiceProvider, IAgentCatalog
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IAgentCatalog) ? this : null;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)
            [
                new AgentDescriptor
                {
                    Name = "researcher",
                    Origin = AgentDefinitionOrigin.Database,
                    SourceName = "database",
                },
            ]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
            => new(string.Equals(agentName, "researcher", StringComparison.Ordinal) ? agent : null);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default)
            => ResolveAsync(agentName, culture, cancellationToken);
    }
}
