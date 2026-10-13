using System.Text.Json;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// F-321: what <see cref="WorkflowRecovery"/> decides for each state a crash, a
/// deploy or a graceful shutdown can leave a queued workflow's chain in.
/// </summary>
/// <remarks>
/// The decision is a pure function of the chain and its checkpoints, so every state
/// is pinned here over the in-memory stores; the functional tests
/// (<c>WorkflowRecoveryTests</c>) prove the same decisions through the job handlers.
/// </remarks>
public sealed class WorkflowRecoveryPlanTests
{
    private const string Tenant = "default";

    private readonly InMemoryRunStore _runs = new();
    private readonly InMemoryWorkflowCheckpointStore _checkpoints = new();

    [Fact]
    public async Task A_head_with_no_row_has_not_started()
    {
        var plan = await Recovery(max: 1).PlanAsync(TraconId.NewId(), Tenant, CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.NotStarted);
        plan.Chain.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_head_still_queued_has_not_started()
    {
        var head = await StartAsync(RunStatus.Queued);

        (await Recovery(max: 1).PlanAsync(head, Tenant, CancellationToken.None)).Step.ShouldBe(WorkflowRecoveryStep.NotStarted);
    }

    [Theory]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.AwaitingInput)]
    public async Task A_chain_whose_last_run_finished_or_waits_is_settled_and_nothing_runs_again(RunStatus status)
    {
        var head = await StartAsync(RunStatus.Failed);
        var last = await StartAsync(status, continuedFrom: head);

        var plan = await Recovery(max: 3).PlanAsync(head, Tenant, CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.Settled);
        plan.Last!.Id.ShouldBe(last);
    }

    [Theory]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.Canceled)]
    [InlineData(RunStatus.Failed)]
    public async Task An_interrupted_run_is_continued_from_its_latest_checkpoint(RunStatus status)
    {
        var head = await StartAsync(status);
        await CheckpointAsync(head, "first");
        await CheckpointAsync(head, "second");

        var plan = await Recovery(max: 1).PlanAsync(head, Tenant, CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.Continue);
        plan.Last!.Id.ShouldBe(head);
        plan.ContinuationRunId.ShouldNotBeNull().ShouldNotBe(head);
        plan.ContinuationIsOpen.ShouldBeFalse();
        plan.Checkpoint.ShouldBe(new WorkflowCheckpointPointer(head, "second"));
        plan.SessionId.ShouldBe("session-1");
    }

    [Fact]
    public async Task A_chain_with_no_checkpoint_continues_by_starting_again()
    {
        var head = await StartAsync(RunStatus.Running);

        var plan = await Recovery(max: 1).PlanAsync(head, Tenant, CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.Continue);
        plan.Checkpoint.ShouldBeNull();
    }

    /// <summary>Failure mode 15: the checkpoint is searched backward along the chain.</summary>
    [Fact]
    public async Task A_continuation_without_a_checkpoint_of_its_own_resumes_from_the_run_before_it()
    {
        var head = await StartAsync(RunStatus.Failed);
        await CheckpointAsync(head, "head-latest");
        var continuation = await StartAsync(RunStatus.Running, continuedFrom: head);

        var plan = await Recovery(max: 2).PlanAsync(head, Tenant, CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.Continue);
        plan.Last!.Id.ShouldBe(continuation);
        plan.Checkpoint.ShouldBe(new WorkflowCheckpointPointer(head, "head-latest"));
    }

    /// <summary>Failure mode 15: past the limit the chain is exhausted.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public async Task A_chain_at_its_limit_is_exhausted(int max, int runs)
    {
        var head = await StartAsync(RunStatus.Failed);
        var last = head;

        for (var index = 1; index < runs; index++)
        {
            last = await StartAsync(index == runs - 1 ? RunStatus.Running : RunStatus.Failed, continuedFrom: last);
        }

        await CheckpointAsync(head, "first");

        var plan = await Recovery(max).PlanAsync(head, Tenant, CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.Exhausted);
        plan.Last!.Id.ShouldBe(last);
        plan.Chain.Count.ShouldBe(runs);
    }

    [Fact]
    public async Task A_continuation_opened_but_never_started_is_started_under_its_own_id_and_does_not_count_again()
    {
        var head = await StartAsync(RunStatus.Failed);
        await CheckpointAsync(head, "first");
        var opened = await StartAsync(RunStatus.Queued, continuedFrom: head);

        var plan = await Recovery(max: 1).PlanAsync(head, Tenant, CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.Continue);
        plan.ContinuationRunId.ShouldBe(opened);
        plan.ContinuationIsOpen.ShouldBeTrue();
        plan.Last!.Id.ShouldBe(head);
        plan.Checkpoint.ShouldBe(new WorkflowCheckpointPointer(head, "first"));
    }

    [Fact]
    public async Task Another_tenants_head_reads_as_not_started_and_its_chain_is_not_read()
    {
        var head = await StartAsync(RunStatus.Running, tenant: "other-tenant");

        var plan = await Recovery(max: 1).PlanAsync(head, Tenant, CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.NotStarted);
        plan.Chain.ShouldBeEmpty();
    }

    [Fact]
    public async Task Closing_an_interrupted_run_writes_the_closing_event_and_names_the_continuation()
    {
        var head = await StartAsync(RunStatus.Running);
        await _runs.AppendEventAsync(new RunEvent { RunId = head, Sequence = 0, Type = RunEventType.RunStarted, Timestamp = DateTimeOffset.UtcNow });
        var record = (await _runs.GetRunAsync(head))!;
        var continuation = TraconId.NewId();

        await Recovery(max: 1).CloseInterruptedAsync(record, continuation, continuations: 0, CancellationToken.None);

        var closed = (await _runs.GetRunAsync(head))!;
        closed.Status.ShouldBe(RunStatus.Failed);
        closed.Error.ShouldNotBeNull().Type.ShouldBe(WorkflowRecovery.InterruptedErrorType);
        closed.Error.Message.ShouldContain(continuation.ToString(), Case.Sensitive);
        closed.EventCount.ShouldBe(2);

        var events = new List<RunEvent>();

        await foreach (var runEvent in _runs.ReadEventsAsync(head))
        {
            events.Add(runEvent);
        }

        events[^1].Type.ShouldBe(RunEventType.RunFailed);
        events[^1].Sequence.ShouldBe(1);
    }

    [Fact]
    public async Task A_run_that_is_not_running_keeps_the_error_it_was_closed_with()
    {
        var head = await StartAsync(RunStatus.Canceled);
        var record = (await _runs.GetRunAsync(head))!;

        await Recovery(max: 1).CloseInterruptedAsync(record, TraconId.NewId(), continuations: 0, CancellationToken.None);

        (await _runs.GetRunAsync(head))!.Status.ShouldBe(RunStatus.Canceled);
    }

    private WorkflowRecovery Recovery(int max)
        => new(_runs, _checkpoints, new FixedPolicy(max));

    private async Task<Guid> StartAsync(RunStatus status, Guid? continuedFrom = null, string tenant = Tenant)
    {
        var runId = TraconId.NewId();

        await _runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "podcast",
            Kind = RunKind.Workflow,
            WorkflowName = "podcast",
            Status = status is RunStatus.Queued ? RunStatus.Queued : RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = tenant,
            SessionId = "session-1",
            ContinuedFromRunId = continuedFrom,
        });

        if (status is not (RunStatus.Queued or RunStatus.Running))
        {
            await _runs.CompleteRunAsync(new RunCompletion { RunId = runId, Status = status, CompletedAt = DateTimeOffset.UtcNow });
        }

        return runId;
    }

    private async Task CheckpointAsync(Guid runId, string checkpointId)
    {
        await _checkpoints.CreateAsync(new WorkflowCheckpointRecord
        {
            Id = TraconId.NewId(),
            TenantId = Tenant,
            SessionId = "session-1",
            CheckpointId = checkpointId,
            RunId = runId,
            CreatedAt = DateTimeOffset.UtcNow,
            State = JsonDocument.Parse("{}").RootElement.Clone(),
        });
    }

    private sealed class FixedPolicy(int max) : IWorkflowRecoveryPolicy
    {
        public bool ResumeInterruptedRuns => true;

        public int MaxResumeAttempts => max;
    }
}
