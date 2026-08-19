using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Graph;

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

        AgentPrismRunContext.SetCurrent(null);

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
        var rootRunId = AgentPrismId.NewId();

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
        var rootRunId = AgentPrismId.NewId();
        var middleRunId = AgentPrismId.NewId();

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

        var (invoker, store) = CreateInvoker(client, approvalTool);

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

    /// <summary>Writes the scope to the current flow.</summary>
    /// <remarks>
    /// <see cref="AgentPrismRunContext"/> is built on an <c>AsyncLocal</c>;
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
        var id = runId ?? AgentPrismId.NewId();

        AgentPrismRunContext.SetCurrent(new AgentRunScope
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
        FakeChatClient? client = null,
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
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance);

        var resolver = new CallableAgentResolver(new SingleAgentServices(inner));

        var invoker = new ChildAgentInvoker(
            resolver,
            tenantContext,
            NullLogger.Instance,
            "router",
            new CallableAgentInfo("researcher", "Performs research.", 1));

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
