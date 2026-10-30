using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// A tenant's quota is read where a workflow's work begins, not only where the request comes in: a function node
/// or an agent step of a run that is already going does not start once the quota is spent.
/// </summary>
/// <remarks>
/// <para>
/// The quota used to be read at the door. A workflow that waited for a person, or that simply ran for hours, passed
/// a quota that was spent in its first minutes. These cases drive the real runner over the in-memory stores and the
/// real enforcer, with real function nodes: the same gate stands in front of each registered node.
/// </para>
/// <para>
/// Only a start is refused. The counter lags behind a running tree, because a run writes its consumption when it
/// ends, so the check adds the tree's own spend that the counter has not seen; what an earlier run of the same
/// chain wrote is in the counter already and is never added a second time.
/// </para>
/// </remarks>
public sealed class TreeQuotaGateTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_function_node_does_not_start_once_the_tenants_cost_quota_is_spent()
    {
        var quota = new QuotaHarness(Start);
        var host = new WorkflowTestHost();
        var rendered = Register(host, "render");

        await quota.RuleAsync(maxCost: 10m);
        await quota.SpentAsync(cost: 10m);
        await host.SaveAsync(Sequential("render"));

        var run = await RunAsync(host, quota, "chain");

        run.Status.ShouldBe(RunStatus.Failed);

        var error = run.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconTenantQuotaExceededException.TenantQuotaExceededErrorType);
        error.Message.ShouldContain("Could not start function node 'render'", Case.Sensitive);
        error.Message.ShouldContain("daily cost quota for the whole tenant has been exceeded (10/10)", Case.Sensitive);
        error.Message.ShouldContain("A node that is already running is not stopped.", Case.Sensitive);
        rendered.Count.ShouldBe(0, "the handler was called after the refusal");
    }

    [Fact]
    public async Task The_trees_own_earlier_node_counts_before_the_root_has_written_anything()
    {
        // The workflow root writes its share when it ends. Without the tree's own spend the second node saw
        // an empty counter and started.
        var quota = new QuotaHarness(Start);
        var host = new WorkflowTestHost();
        var rendered = Register(host, "render");

        host.TraconOptions.Pricing.Currency = "USD";
        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "speak",
            async (messages, context, token) =>
            {
                (await quota.Store.GetUsageAsync(new QuotaUsageQuery { TenantId = QuotaHarness.Tenant }, token)).ShouldBeEmpty();
                await context.ReportUsageAsync(BudgetedApprovalWorkflow.Spent(5m));

                return messages;
            },
            static _ => { });

        await quota.RuleAsync(maxCost: 5m);
        await host.SaveAsync(Sequential("speak", "render"));

        var run = await RunAsync(host, quota, "chain");

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Type.ShouldBe(TraconTenantQuotaExceededException.TenantQuotaExceededErrorType);
        rendered.Count.ShouldBe(0);

        // The refused run still wrote what its first node spent, once (the counter is not asked twice).
        var counter = await quota.TodayAsync();

        counter.Cost.ShouldBe(5m);
        counter.Runs.ShouldBe(1);
    }

    [Fact]
    public async Task A_cost_below_the_quota_starts_the_node()
    {
        var quota = new QuotaHarness(Start);
        var host = new WorkflowTestHost();
        var rendered = Register(host, "render");

        await quota.RuleAsync(maxCost: 10m);
        await quota.SpentAsync(cost: 9.99m);
        await host.SaveAsync(Sequential("render"));

        var run = await RunAsync(host, quota, "chain");

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
        rendered.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_spent_token_quota_does_not_stop_a_function_node_which_spends_none()
    {
        var quota = new QuotaHarness(Start);
        var host = new WorkflowTestHost();
        var rendered = Register(host, "render");

        await quota.RuleAsync(maxTokens: 100);
        await quota.SpentAsync(tokens: 5_000);
        await host.SaveAsync(Sequential("render"));

        (await RunAsync(host, quota, "chain")).Status.ShouldBe(RunStatus.Completed);
        rendered.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_node_registered_to_start_when_the_budget_is_spent_is_exempt_from_the_quota_too()
    {
        var quota = new QuotaHarness(Start);
        var host = new WorkflowTestHost();
        var stored = Register(host, "store", static options => options.StartsWhenBudgetIsSpent = true);
        var rendered = Register(host, "render");

        await quota.RuleAsync(maxCost: 1m);
        await quota.SpentAsync(cost: 5m);
        await host.SaveAsync(Sequential("store", "render"));

        var run = await RunAsync(host, quota, "chain");

        stored.Count.ShouldBe(1, "the exempt node was refused");
        rendered.Count.ShouldBe(0, "the exemption is the node's alone");
        run.Error.ShouldNotBeNull().Type.ShouldBe(TraconTenantQuotaExceededException.TenantQuotaExceededErrorType);
    }

    [Fact]
    public async Task A_new_day_gives_the_node_a_counter_that_is_empty()
    {
        var quota = new QuotaHarness(Start);
        var host = new WorkflowTestHost();
        var rendered = Register(host, "render");

        await quota.RuleAsync(maxCost: 1m);
        await quota.SpentAsync(cost: 5m);
        await host.SaveAsync(Sequential("render"));

        (await RunAsync(host, quota, "chain")).Status.ShouldBe(RunStatus.Failed);

        quota.Clock.Advance(TimeSpan.FromDays(1));

        (await RunAsync(host, quota, "chain")).Status.ShouldBe(RunStatus.Completed);
        rendered.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Another_tenants_quota_is_not_read()
    {
        var quota = new QuotaHarness(Start);
        var host = new WorkflowTestHost();
        var rendered = Register(host, "render");

        await quota.RuleAsync(maxCost: 1m, tenant: "poor");
        await quota.SpentAsync(cost: 5m, tenant: "poor");

        host.TenantContext.TenantId = "poor";
        await host.SaveAsync(Sequential("render"));

        (await RunAsync(host, quota, "chain")).Status.ShouldBe(RunStatus.Failed);

        // The same host, another tenant: poor's counter is not rich's.
        host.TenantContext.TenantId = "rich";
        await host.SaveAsync(Sequential("render"));

        (await RunAsync(host, quota, "chain")).Status.ShouldBe(RunStatus.Completed);
        rendered.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_rule_of_a_workflow_reads_the_counter_of_that_workflow_and_not_of_another()
    {
        var quota = new QuotaHarness(Start);
        var host = new WorkflowTestHost();
        var rendered = Register(host, "render");

        await quota.RuleAsync(maxCost: 1m, agentName: "chain");
        await quota.SpentAsync(cost: 5m, agentName: "another-workflow");
        await host.SaveAsync(Sequential("render"));

        (await RunAsync(host, quota, "chain")).Status.ShouldBe(RunStatus.Completed);

        await quota.SpentAsync(cost: 5m, agentName: "chain");

        (await RunAsync(host, quota, "chain")).Status.ShouldBe(RunStatus.Failed);
        rendered.Count.ShouldBe(1);
    }

    // --- A chain of runs ---

    [Fact]
    public async Task What_an_earlier_run_of_the_chain_wrote_is_not_counted_a_second_time()
    {
        // The run that waited wrote 0.8 when it ended, and the answer's budget holds the same 0.8 (it starts from
        // what the chain spent). Counted twice the answer's node saw 1.6 against a quota of 1 and was refused.
        var quota = new QuotaHarness(Start);
        var host = new WorkflowTestHost();
        var flow = new BudgetedApprovalWorkflow(host, firstAmount: 0.8m, publishAmount: 0.1m);

        await quota.RuleAsync(maxCost: 1m);

        var runner = host.CreateRunnerWith(timeProvider: quota.Clock, quotaEnforcer: quota.Enforcer, services: flow.Services, codeWorkflows: flow.Registration);
        var waiting = await ToTheRequestAsync(runner);

        (await quota.TodayAsync()).Cost.ShouldBe(0.8m);

        var answer = await AnswerAsync(runner, waiting);
        var record = (await host.RunStore.GetRunAsync(answer)).ShouldNotBeNull();

        record.Status.ShouldBe(RunStatus.Completed, record.Error?.Message);
        flow.PublishCalls.ShouldBe(1);
        (await quota.TodayAsync()).Cost.ShouldBe(0.9m, "the answer wrote its own 0.1");
    }

    [Fact]
    public async Task An_answer_to_a_workflow_whose_quota_was_spent_by_the_run_that_asked_is_refused_at_its_first_node()
    {
        var quota = new QuotaHarness(Start);
        var host = new WorkflowTestHost();
        var flow = new BudgetedApprovalWorkflow(host, firstAmount: 1m, publishAmount: 0.1m);

        await quota.RuleAsync(maxCost: 1m);

        var runner = host.CreateRunnerWith(timeProvider: quota.Clock, quotaEnforcer: quota.Enforcer, services: flow.Services, codeWorkflows: flow.Registration);
        var waiting = await ToTheRequestAsync(runner);

        var answer = await AnswerAsync(runner, waiting);
        var record = (await host.RunStore.GetRunAsync(answer)).ShouldNotBeNull();

        record.Status.ShouldBe(RunStatus.Failed);
        record.Error.ShouldNotBeNull().Type.ShouldBe(TraconTenantQuotaExceededException.TenantQuotaExceededErrorType);
        flow.PublishCalls.ShouldBe(0);
    }

    // --- An agent step ---

    [Fact]
    public async Task An_agent_step_is_refused_before_it_starts_and_no_child_run_is_opened()
    {
        var quota = new QuotaHarness(Start);
        var host = new WorkflowTestHost("writer");

        await quota.RuleAsync(maxCost: 1m);
        await quota.SpentAsync(cost: 1m);
        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes = [new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent }],
        });

        var run = await RunAsync(host, quota, "chain");

        run.Status.ShouldBe(RunStatus.Failed);

        var error = run.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconTenantQuotaExceededException.TenantQuotaExceededErrorType);
        error.Message.ShouldContain("Could not call agent 'writer'", Case.Sensitive);
        error.Message.ShouldContain("daily cost quota for the whole tenant has been exceeded (1/1)", Case.Sensitive);

        (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .ShouldAllBe(static record => record.Kind == RunKind.Workflow, "the refused step opened a run row");
    }

    [Fact]
    public async Task An_agent_step_below_the_quota_runs()
    {
        var quota = new QuotaHarness(Start);
        var host = new WorkflowTestHost("writer");

        await quota.RuleAsync(maxCost: 1m);
        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes = [new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent }],
        });

        (await RunAsync(host, quota, "chain")).Status.ShouldBe(RunStatus.Completed);
    }

    // --- A store that cannot be read ---

    [Fact]
    public async Task A_quota_store_that_cannot_be_read_lets_the_node_start_when_AllowOnStoreFailure_is_on()
    {
        var quota = new QuotaHarness(Start, store: new UnreadableQuotaStore());
        var host = new WorkflowTestHost();
        var rendered = Register(host, "render");

        await host.SaveAsync(Sequential("render"));

        (await RunAsync(host, quota, "chain")).Status.ShouldBe(RunStatus.Completed);
        rendered.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_quota_store_that_cannot_be_read_refuses_the_node_when_AllowOnStoreFailure_is_off()
    {
        var quota = new QuotaHarness(Start, configure: static options => options.AllowOnStoreFailure = false, store: new UnreadableQuotaStore());
        var host = new WorkflowTestHost();
        var rendered = Register(host, "render");

        await host.SaveAsync(Sequential("render"));

        var run = await RunAsync(host, quota, "chain");

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Type.ShouldBe(TraconTenantQuotaExceededException.TenantQuotaExceededErrorType);
        run.Error.Message.ShouldContain("Quota could not be verified.", Case.Sensitive);
        rendered.Count.ShouldBe(0);
    }

    [Fact]
    public async Task A_quota_that_is_off_never_refuses_a_node()
    {
        var quota = new QuotaHarness(Start, configure: static options => options.Enabled = false);
        var host = new WorkflowTestHost();
        var rendered = Register(host, "render");

        await quota.RuleAsync(maxCost: 1m);
        await quota.SpentAsync(cost: 5m);
        await host.SaveAsync(Sequential("render"));

        (await RunAsync(host, quota, "chain")).Status.ShouldBe(RunStatus.Completed);
        rendered.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_run_with_no_quota_registered_is_not_asked_anything()
    {
        var host = new WorkflowTestHost();
        var rendered = Register(host, "render");

        await host.SaveAsync(Sequential("render"));

        // The runner built without an enforcer: the host registered no quota.
        var runner = host.CreateRunner();
        var run = await RunToTheEndAsync(host, runner, "chain");

        run.Status.ShouldBe(RunStatus.Completed);
        rendered.Count.ShouldBe(1);
    }

    // --- Helpers ---

    private static WorkflowDefinition Sequential(params string[] functions)
        => new()
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes = [.. functions.Select(static name => new WorkflowNodeReference { Name = name, Kind = WorkflowNodeKind.Function })],
        };

    /// <summary>Registers a function node that counts its calls.</summary>
    private static Counter Register(WorkflowTestHost host, string name, Action<WorkflowFunctionOptions>? configure = null)
    {
        var counter = new Counter();

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            name,
            (messages, _, _) =>
            {
                counter.Hit();

                return new ValueTask<List<ChatMessage>>(messages);
            },
            configure ?? (static _ => { }));

        return counter;
    }

    private static async Task<RunRecord> RunAsync(WorkflowTestHost host, QuotaHarness quota, string workflow)
        => await RunToTheEndAsync(
            host,
            host.CreateRunnerWith(timeProvider: quota.Clock, quotaEnforcer: quota.Enforcer),
            workflow);

    private static async Task<RunRecord> RunToTheEndAsync(WorkflowTestHost host, WorkflowRunner runner, string workflow)
    {
        var first = Guid.Empty;

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = workflow, Message = "go" }))
        {
            if (first == Guid.Empty)
            {
                first = runEvent.RunId;
            }
        }

        return (await host.RunStore.GetRunAsync(first)).ShouldNotBeNull();
    }

    private static async Task<Guid> ToTheRequestAsync(WorkflowRunner runner)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = BudgetedApprovalWorkflow.Name,
            Message = "publish the report",
        }))
        {
            events.Add(runEvent);
        }

        events[^1].Type.ShouldBe(RunEventType.RunAwaitingInput, events[^1].Text);

        return events[0].RunId;
    }

    private static async Task<Guid> AnswerAsync(WorkflowRunner runner, Guid waiting)
    {
        var pending = (await runner.ListPendingRequestsAsync(waiting)).Single();
        var first = Guid.Empty;

        await foreach (var runEvent in runner.RespondStreamingAsync(new WorkflowRespondRequest
        {
            RunId = waiting,
            RequestId = pending.RequestId,
            Approved = true,
        }))
        {
            if (first == Guid.Empty)
            {
                first = runEvent.RunId;
            }
        }

        return first;
    }

    private sealed class Counter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Hit() => Interlocked.Increment(ref _count);
    }

    /// <summary>A quota store whose reads fail; the write side is not reached by these cases.</summary>
    private sealed class UnreadableQuotaStore : IQuotaStore
    {
        public ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the quota store is down");

        public ValueTask<QuotaDefinition?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the quota store is down");

        public ValueTask<QuotaDefinition> SaveAsync(QuotaDefinition definition, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the quota store is down");

        public ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the quota store is down");

        public ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(QuotaUsageQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the quota store is down");

        public ValueTask AddUsageAsync(
            QuotaConsumption consumption,
            IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the quota store is down");

        public ValueTask<bool> TryClaimThresholdNotificationAsync(
            string tenantId,
            string agentName,
            QuotaPeriod period,
            DateOnly periodStart,
            QuotaMetric metric,
            int thresholdPercent,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the quota store is down");
    }
}
