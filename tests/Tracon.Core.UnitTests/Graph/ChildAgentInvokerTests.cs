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
/// Framework's background task tool calls it. The tests
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
        var running = invoker.RunAsync("run", cancellationToken: cts.Token);

        // A CancelAfter(50 ms) timer stood here. Under load it fired
        // before the child run had even been recorded, and the read below found
        // no run at all ("Sequence contains no elements", measured on the
        // net8.0 leg). Cancel once the child is really running.
        await WaitUntil.TrueAsync(async () =>
            (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).Count == 1);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await running);

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
        // That decision regression: the provider reports its OWN request timeout as an
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

        // The sub-run itself already recorded the truth (that decision applies in
        // RunRecordingAgent): a provider fault is Failed, never Canceled.
        var child = (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
        child.Status.ShouldBe(RunStatus.Failed);
    }

    // --- an earlier fix: a workflow step fails instead of returning the refusal as content ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task In_fail_mode_the_wait_limit_throws_a_timeout_naming_the_agent(bool streaming)
    {
        var (invoker, _) = CreateInvoker(
            ChildRefusalMode.Fail,
            new BlockingChatClient(),
            childDeadline: TimeSpan.FromMilliseconds(50),
            waitTimeout: TimeSpan.FromSeconds(5));

        SetScope(depth: 0, budget: new AgentRunBudget { MaxDepth = 3 });

        var exception = await Should.ThrowAsync<TraconChildRunTimeoutException>(
            async () => await RunAsync(invoker, streaming));

        exception.AgentName.ShouldBe("researcher");
        exception.Timeout.ShouldBe(TimeSpan.FromMilliseconds(50));
        exception.ErrorType.ShouldBe(TraconChildRunTimeoutException.ChildRunTimeoutErrorType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task In_fail_mode_an_exhausted_budget_throws_the_budget_exception(bool streaming)
    {
        var (invoker, _) = CreateInvoker(ChildRefusalMode.Fail);
        var budget = new AgentRunBudget { MaxDepth = 3, MaxTotalRuns = 1 };

        SetScope(depth: 0, budget: budget);
        await RunAsync(invoker, streaming);

        SetScope(depth: 0, budget: budget);
        var exception = await Should.ThrowAsync<TraconRunBudgetExceededException>(
            async () => await RunAsync(invoker, streaming));

        exception.Message.ShouldContain("child-run limit is reached", Case.Sensitive);
    }

    [Fact]
    public async Task A_step_hosted_by_the_workflow_passes_a_tool_approval_on_and_its_row_waits_for_the_answer()
    {
        // K-1051: the host turns the request into a workflow request, so the wrapper neither throws nor
        // rewrites the answer, and the step's row closes the way a root run's does.
        var approvalTool = new ApprovalRequiredAIFunction(TestData.Tool("dangerous_task"));
        var client = new FakeChatClient(_ => new ChatResponse(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "dangerous_task", null)])));

        var (invoker, store) = CreateInvoker(ChildRefusalMode.Fail, client, tools: [approvalTool], hostsApproval: true);

        SetScope(depth: 0, budget: new AgentRunBudget { MaxDepth = 3 });

        var response = await invoker.RunAsync("run");

        response.Messages.SelectMany(static message => message.Contents).OfType<ToolApprovalRequestContent>()
            .ShouldHaveSingleItem().ToolCall.ShouldBeOfType<FunctionCallContent>().Name.ShouldBe("dangerous_task");

        var child = (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();

        child.Status.ShouldBe(RunStatus.AwaitingApproval);
        child.Error.ShouldBeNull();
    }

    [Fact]
    public async Task A_wrapper_in_tool_result_mode_still_refuses_an_approval_and_its_row_fails()
    {
        // The wrapper of a sub-agent is in tool-result mode and builds its own options; it never gets the
        // hosted step's flag, so below the step the refusal of that decision holds. The tree below a real step is
        // measured by WorkflowToolApprovalPatternTests.A_sub_agent_the_step_calls_is_still_refused_and_the_workflow_does_not_wait.
        var approvalTool = new ApprovalRequiredAIFunction(TestData.Tool("dangerous_task"));
        var client = new FakeChatClient(_ => new ChatResponse(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "dangerous_task", null)])));

        var (invoker, store) = CreateInvoker(ChildRefusalMode.ToolResult, client, tools: [approvalTool], hostsApproval: false);

        SetScope(depth: 1, budget: new AgentRunBudget { MaxDepth = 3 });

        var response = await invoker.RunAsync("run");

        response.Text.ShouldContain("requires user approval", Case.Sensitive);
        (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem().Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task A_step_that_is_a_plain_function_still_refuses_a_tool_approval_and_its_row_fails()
    {
        var approvalTool = new ApprovalRequiredAIFunction(TestData.Tool("dangerous_task"));
        var client = new FakeChatClient(_ => new ChatResponse(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "dangerous_task", null)])));

        var (invoker, store) = CreateInvoker(ChildRefusalMode.Fail, client, tools: [approvalTool], hostsApproval: false);

        SetScope(depth: 0, budget: new AgentRunBudget { MaxDepth = 3 });

        await Should.ThrowAsync<TraconException>(async () => await invoker.RunAsync("run"));

        (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem().Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task In_fail_mode_a_tool_waiting_for_approval_throws()
    {
        // Non-streaming only: FakeChatClient's stream ignores its responder, so
        // it cannot produce the approval request; the streaming branch is the
        // same RefusedUpdate call.
        var approvalTool = new ApprovalRequiredAIFunction(TestData.Tool("dangerous_task"));
        var client = new FakeChatClient(_ => new ChatResponse(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "dangerous_task", null)])));

        var (invoker, _) = CreateInvoker(ChildRefusalMode.Fail, client, tools: [approvalTool]);

        SetScope(depth: 0, budget: new AgentRunBudget { MaxDepth = 3 });

        var exception = await Should.ThrowAsync<TraconException>(async () => await invoker.RunAsync("run"));

        exception.Message.ShouldContain("A workflow step cannot wait for a tool approval", Case.Sensitive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task In_fail_mode_a_provider_timeout_fails_with_the_providers_own_exception(bool streaming)
    {
        var (invoker, _) = CreateInvoker(
            ChildRefusalMode.Fail,
            new TimingOutChatClient(),
            childDeadline: TimeSpan.FromSeconds(30),
            waitTimeout: TimeSpan.FromSeconds(60));

        SetScope(depth: 0, budget: new AgentRunBudget { MaxDepth = 3 });

        var exception = await Should.ThrowAsync<OperationCanceledException>(async () => await RunAsync(invoker, streaming));

        exception.ShouldNotBeOfType<TraconChildRunTimeoutException>();
    }

    [Fact]
    public async Task In_fail_mode_the_depth_limit_throws()
    {
        var (invoker, _) = CreateInvoker(ChildRefusalMode.Fail);

        SetScope(depth: 3, budget: new AgentRunBudget { MaxDepth = 3 });

        var exception = await Should.ThrowAsync<TraconException>(async () => await invoker.RunAsync("run"));

        exception.Message.ShouldContain("call depth limit was exceeded", Case.Sensitive);
    }

    private static async Task<string> RunAsync(ChildAgentInvoker invoker, bool streaming)
        => streaming ? await ReadStreamTextAsync(invoker) : (await invoker.RunAsync("run")).Text;

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
        => CreateInvoker(ChildRefusalMode.ToolResult, client, childDeadline, waitTimeout, hostsApproval: false, tools);

    private static (ChildAgentInvoker Invoker, InMemoryRunStore Store) CreateInvoker(
        ChildRefusalMode refusalMode,
        IChatClient? client = null,
        TimeSpan? childDeadline = null,
        TimeSpan? waitTimeout = null,
        bool hostsApproval = false,
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
            waitTimeout: waitTimeout ?? TimeSpan.FromSeconds(60),
            refusalMode: refusalMode,
            hostsApproval: hostsApproval);

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
