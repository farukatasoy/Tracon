using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// What <see cref="WorkflowRecovery"/> decides for each state a crash, a
/// deploy or a graceful shutdown can leave a queued workflow's chain in.
/// </summary>
/// <remarks>
/// The decision is a pure function of the chain, its checkpoints and the job's attempt, so every state
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
        var plan = await Recovery(max: 1).PlanAsync(TraconId.NewId(), Job(), CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.NotStarted);
        plan.Chain.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_head_still_queued_has_not_started()
    {
        var head = await StartAsync(RunStatus.Queued);

        (await Recovery(max: 1).PlanAsync(head, Job(), CancellationToken.None)).Step.ShouldBe(WorkflowRecoveryStep.NotStarted);
    }

    [Theory]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.AwaitingInput)]
    public async Task A_chain_whose_last_run_finished_or_waits_is_settled_and_nothing_runs_again(RunStatus status)
    {
        var head = await StartAsync(RunStatus.Failed);
        var last = await StartAsync(status, continuedFrom: head);

        var plan = await Recovery(max: 3).PlanAsync(head, Job(), CancellationToken.None);

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

        var plan = await Recovery(max: 1).PlanAsync(head, Job(), CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.Continue);
        plan.Last!.Id.ShouldBe(head);
        plan.ContinuationRunId.ShouldNotBeNull().ShouldNotBe(head);
        plan.Checkpoint.ShouldBe(new WorkflowCheckpointPointer(head, "second"));
        plan.SessionId.ShouldBe("session-1");
    }

    [Fact]
    public async Task A_chain_with_no_checkpoint_continues_by_starting_again()
    {
        var head = await StartAsync(RunStatus.Running);

        var plan = await Recovery(max: 1).PlanAsync(head, Job(), CancellationToken.None);

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

        var plan = await Recovery(max: 2).PlanAsync(head, Job(), CancellationToken.None);

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

        var plan = await Recovery(max).PlanAsync(head, Job(), CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.Exhausted);
        plan.Last!.Id.ShouldBe(last);
        plan.Chain.Count.ShouldBe(runs);
    }

    /// <summary>
    /// An attempt opened a continuation and stopped before the engine started it. That
    /// attempt was interrupted like any other: the opened row counts against the limit
    /// and is continued by a new run, not started again under its own id.
    /// </summary>
    [Fact]
    public async Task A_continuation_opened_but_never_started_counts_as_an_interruption()
    {
        var head = await StartAsync(RunStatus.Running);
        await CheckpointAsync(head, "first");
        var opened = await StartAsync(RunStatus.Queued, continuedFrom: head);

        var plan = await Recovery(max: 2).PlanAsync(head, Job(), CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.Continue);
        plan.Last!.Id.ShouldBe(opened);
        plan.ContinuationRunId.ShouldNotBeNull().ShouldNotBe(opened);
        plan.Checkpoint.ShouldBe(new WorkflowCheckpointPointer(head, "first"));
    }

    [Fact]
    public async Task A_continuation_opened_but_never_started_at_the_limit_exhausts_the_chain()
    {
        var head = await StartAsync(RunStatus.Running);
        var opened = await StartAsync(RunStatus.Queued, continuedFrom: head);

        var plan = await Recovery(max: 1).PlanAsync(head, Job(), CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.Exhausted);
        plan.Last!.Id.ShouldBe(opened);
        plan.NotContinuedReason.ShouldNotBeNull().ShouldContain("MaxResumeAttempts", Case.Sensitive);
    }

    /// <summary>
    /// The job's last attempt never starts a continuation: the worker fails the next
    /// lease before any handler runs, so nothing would close it if it were interrupted.
    /// </summary>
    [Fact]
    public async Task On_the_jobs_last_attempt_an_interrupted_chain_is_exhausted_whatever_continuations_are_left()
    {
        var head = await StartAsync(RunStatus.Running);
        await CheckpointAsync(head, "first");

        var plan = await Recovery(max: 5).PlanAsync(head, Job(attempt: 3, maxAttempts: 3), CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.Exhausted);
        plan.Last!.Id.ShouldBe(head);
        plan.NotContinuedReason.ShouldNotBeNull().ShouldContain("last attempt (3 of 3)", Case.Sensitive);
    }

    [Fact]
    public async Task Before_the_jobs_last_attempt_an_interrupted_chain_is_continued()
    {
        var head = await StartAsync(RunStatus.Running);

        var plan = await Recovery(max: 5).PlanAsync(head, Job(attempt: 2, maxAttempts: 3), CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.Continue);
    }

    /// <summary>A job that sets no limit has the queue's default, as the worker reads it.</summary>
    [Fact]
    public async Task A_job_without_its_own_limit_is_on_its_last_attempt_at_the_queues_default()
    {
        var head = await StartAsync(RunStatus.Running);
        var recovery = new WorkflowRecovery(
            _runs,
            _checkpoints,
            new FixedPolicy(5),
            new FixedOptions(new TraconSchedulingOptions { MaxAttempts = 2 }));

        var plan = await recovery.PlanAsync(head, Job(attempt: 2, maxAttempts: null), CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.Exhausted);
        plan.NotContinuedReason.ShouldNotBeNull().ShouldContain("last attempt (2 of 2)", Case.Sensitive);
    }

    [Fact]
    public async Task Another_tenants_head_reads_as_not_started_and_its_chain_is_not_read()
    {
        var head = await StartAsync(RunStatus.Running, tenant: "other-tenant");

        var plan = await Recovery(max: 1).PlanAsync(head, Job(), CancellationToken.None);

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

        await Recovery(max: 1).CloseChainAsync(
            new WorkflowRecoveryPlan { Step = WorkflowRecoveryStep.Continue, Chain = [record], Last = record, ContinuationRunId = continuation },
            CancellationToken.None);

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

    /// <summary>
    /// An attempt that opened a continuation and stopped before it closed the run the
    /// continuation continues leaves two open rows; both are closed, each naming what
    /// comes after it.
    /// </summary>
    [Fact]
    public async Task Closing_an_exhausted_chain_closes_every_open_run_each_naming_what_comes_after_it()
    {
        var head = await StartAsync(RunStatus.Running);
        var opened = await StartAsync(RunStatus.Queued, continuedFrom: head);
        var recovery = Recovery(max: 1);

        var plan = await recovery.PlanAsync(head, Job(), CancellationToken.None);
        await recovery.CloseChainAsync(plan, CancellationToken.None);

        var closedHead = (await _runs.GetRunAsync(head))!;
        closedHead.Status.ShouldBe(RunStatus.Failed);
        closedHead.Error.ShouldNotBeNull().Message.ShouldContain(opened.ToString(), Case.Sensitive);

        var closedOpened = (await _runs.GetRunAsync(opened))!;
        closedOpened.Status.ShouldBe(RunStatus.Failed);
        closedOpened.Error.ShouldNotBeNull().Type.ShouldBe(WorkflowRecovery.InterruptedErrorType);
        closedOpened.Error.Message.ShouldContain("before the run started", Case.Sensitive);
        closedOpened.Error.Message.ShouldContain("MaxResumeAttempts", Case.Sensitive);
    }

    /// <summary>
    /// A run its job stopped at the job's time limit ran out of
    /// time, it was not interrupted. Continuing it would make the limit per attempt and
    /// the work unbounded, so the plan ends the work instead - whatever continuations
    /// are left and whatever attempts the job has.
    /// </summary>
    [Fact]
    public async Task A_run_its_job_stopped_at_the_time_limit_is_never_continued()
    {
        var head = TraconId.NewId();

        await _runs.StartRunAsync(new RunStartInfo
        {
            RunId = head,
            AgentName = "podcast",
            Kind = RunKind.Workflow,
            WorkflowName = "podcast",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = Tenant,
            SessionId = "session-1",
        });
        await _runs.CompleteRunAsync(new RunCompletion
        {
            RunId = head,
            Status = RunStatus.Canceled,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = new RunError { Type = JobAttemptSignals.MaxDurationExceededRunErrorType, Message = "stopped" },
        });
        await CheckpointAsync(head, "first");

        var plan = await Recovery(max: 3).PlanAsync(head, Job(), CancellationToken.None);

        plan.Step.ShouldBe(WorkflowRecoveryStep.Stopped);
        plan.Last!.Id.ShouldBe(head);
        plan.ContinuationRunId.ShouldBeNull();
    }

    [Fact]
    public async Task A_run_that_is_not_running_keeps_the_error_it_was_closed_with()
    {
        var head = await StartAsync(RunStatus.Canceled);
        var record = (await _runs.GetRunAsync(head))!;

        await Recovery(max: 1).CloseChainAsync(
            new WorkflowRecoveryPlan { Step = WorkflowRecoveryStep.Continue, Chain = [record], Last = record, ContinuationRunId = TraconId.NewId() },
            CancellationToken.None);

        (await _runs.GetRunAsync(head))!.Status.ShouldBe(RunStatus.Canceled);
    }

    private WorkflowRecovery Recovery(int max)
        => new(_runs, _checkpoints, new FixedPolicy(max));

    private static JobRecord Job(int attempt = 1, int? maxAttempts = 10) => new()
    {
        Id = TraconId.NewId(),
        TenantId = Tenant,
        HandlerKey = JobHandlerKeys.Workflow,
        TargetName = "podcast",
        Status = JobStatus.Running,
        Attempt = attempt,
        MaxAttempts = maxAttempts,
        ScheduledFor = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
    };

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

    private sealed class FixedOptions(TraconSchedulingOptions value) : IOptionsMonitor<TraconSchedulingOptions>
    {
        public TraconSchedulingOptions CurrentValue => value;

        public TraconSchedulingOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<TraconSchedulingOptions, string?> listener) => null;
    }
}
