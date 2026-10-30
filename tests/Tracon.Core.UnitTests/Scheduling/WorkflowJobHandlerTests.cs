using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

public sealed class WorkflowJobHandlerTests
{
    [Fact]
    public async Task Throws_when_no_runner_is_registered()
    {
        var handler = new WorkflowJobHandler(runner: null, TestWorkflowRecovery.Off(), NullLogger<WorkflowJobHandler>.Instance);

        var exception = await Should.ThrowAsync<TraconException>(
            () => handler.ExecuteAsync(BuildContext([Item(0, "input")])).AsTask());

        exception.Message.ShouldContain("UseWorkflows", Case.Sensitive);
    }

    [Fact]
    public async Task A_successful_run_is_reported_as_completed()
    {
        var runner = new ScriptedWorkflowRunner(shouldFail: false);
        var handler = new WorkflowJobHandler(runner, TestWorkflowRecovery.Off(), NullLogger<WorkflowJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(BuildContext([Item(0, "input")], reported));

        reported.ShouldHaveSingleItem();
        reported[0].Status.ShouldBe(JobItemStatus.Completed);
        reported[0].RunId.ShouldBe(runner.LastRunId);
    }

    [Fact]
    public async Task A_RunFailed_event_is_reported_as_failed()
    {
        var runner = new ScriptedWorkflowRunner(shouldFail: true);
        var handler = new WorkflowJobHandler(runner, TestWorkflowRecovery.Off(), NullLogger<WorkflowJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(BuildContext([Item(0, "input")], reported));

        reported.ShouldHaveSingleItem();
        reported[0].Status.ShouldBe(JobItemStatus.Failed);
        reported[0].Error.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>197.3: an item's session id is the run's execution session.</summary>
    [Fact]
    public async Task An_items_session_id_is_handed_to_the_run()
    {
        var runner = new ScriptedWorkflowRunner(shouldFail: false);
        var handler = new WorkflowJobHandler(runner, TestWorkflowRecovery.Off(), NullLogger<WorkflowJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(BuildContext([Item(0, """{"message":"episode 7","sessionId":"production-7"}""")], reported));

        runner.Requests.ShouldHaveSingleItem().SessionId.ShouldBe("production-7");
        reported.ShouldHaveSingleItem().Status.ShouldBe(JobItemStatus.Completed);
    }

    /// <summary>
    /// Failure mode 22: an item whose session id breaks the rule fails alone, with the
    /// rule named and the id not repeated; the next item runs.
    /// </summary>
    [Theory]
    [InlineData("""{"message":"episode 7","sessionId":"has spaces"}""")]
    [InlineData("""{"message":"episode 7","sessionId":""}""")]
    [InlineData("""{"message":"episode 7","sessionId":7}""")]
    public async Task An_item_with_a_bad_session_id_fails_alone_and_the_next_item_runs(string bad)
    {
        var runner = new ScriptedWorkflowRunner(shouldFail: false);
        var handler = new WorkflowJobHandler(runner, TestWorkflowRecovery.Off(), NullLogger<WorkflowJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(BuildContext([Item(0, bad), Item(1, "episode 8")], reported));

        reported.Count.ShouldBe(2);
        reported[0].Status.ShouldBe(JobItemStatus.Failed);
        reported[0].Error.ShouldNotBeNull().ShouldContain("sessionId", Case.Sensitive);
        reported[0].Error!.ShouldNotContain("has spaces", Case.Sensitive);
        reported[1].Status.ShouldBe(JobItemStatus.Completed);
        runner.Requests.ShouldHaveSingleItem().Message.ShouldBe("episode 8");
    }

    [Fact]
    public async Task An_item_with_an_overlong_session_id_fails_alone()
    {
        var runner = new ScriptedWorkflowRunner(shouldFail: false);
        var handler = new WorkflowJobHandler(runner, TestWorkflowRecovery.Off(), NullLogger<WorkflowJobHandler>.Instance);
        var reported = new List<JobItemResult>();
        var overlong = new string('s', WorkflowSessionId.MaxLength + 1);

        await handler.ExecuteAsync(BuildContext([Item(0, $$"""{"message":"episode 7","sessionId":"{{overlong}}"}""")], reported));

        reported.ShouldHaveSingleItem().Status.ShouldBe(JobItemStatus.Failed);
        reported[0].Error.ShouldNotBeNull().ShouldContain(WorkflowSessionId.MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture), Case.Sensitive);
        reported[0].Error!.ShouldNotContain(overlong, Case.Sensitive);
        runner.Requests.ShouldBeEmpty();
    }

    /// <summary>Failure mode 22: an empty message is still a run: the workflow decides what it needs.</summary>
    [Fact]
    public async Task An_empty_message_item_runs_with_an_empty_message()
    {
        var runner = new ScriptedWorkflowRunner(shouldFail: false);
        var handler = new WorkflowJobHandler(runner, TestWorkflowRecovery.Off(), NullLogger<WorkflowJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(BuildContext([Item(0, string.Empty)], reported));

        runner.Requests.ShouldHaveSingleItem().Message.ShouldBe(string.Empty);
        reported.ShouldHaveSingleItem().Status.ShouldBe(JobItemStatus.Completed);
    }

    /// <summary>
    /// 🚨 The worker is stopping: the run's stream ends because its token was
    /// cancelled. The item must stay pending for the next lease, not be reported.
    /// </summary>
    [Fact]
    public async Task An_item_whose_run_stopped_because_the_worker_is_stopping_is_not_reported()
    {
        using var stopping = new CancellationTokenSource();
        var runner = new ScriptedWorkflowRunner(shouldFail: true, beforeLastEvent: stopping.Cancel);
        var handler = new WorkflowJobHandler(runner, TestWorkflowRecovery.Off(), NullLogger<WorkflowJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        await Should.ThrowAsync<OperationCanceledException>(
            () => handler.ExecuteAsync(BuildContext([Item(0, "episode 7")], reported), stopping.Token).AsTask());

        reported.ShouldBeEmpty();
    }

    /// <summary>
    /// With recovery on, a store error while the item's run id is written fails the
    /// attempt, not the item: the item stays pending, nothing runs, and the next
    /// attempt writes the run id again.
    /// </summary>
    [Fact]
    public async Task With_recovery_on_a_store_error_while_writing_the_items_run_id_fails_the_attempt_not_the_item()
    {
        var runner = new ScriptedWorkflowRunner(shouldFail: false);
        var recovery = new WorkflowRecovery(new InMemoryRunStore(), new InMemoryWorkflowCheckpointStore(), new OnPolicy());
        var handler = new WorkflowJobHandler(runner, recovery, NullLogger<WorkflowJobHandler>.Instance);
        var reported = new List<JobItemResult>();
        var context = BuildContext(
            [Item(0, "episode 7")],
            reported,
            report: (result, _) => result.Status == JobItemStatus.Pending
                ? throw new InvalidOperationException("the store is unavailable")
                : AddAsync(reported, result));

        await Should.ThrowAsync<InvalidOperationException>(() => handler.ExecuteAsync(context).AsTask());

        reported.ShouldBeEmpty();
        runner.Requests.ShouldBeEmpty();
    }

    private static ValueTask AddAsync(List<JobItemResult> reported, JobItemResult result)
    {
        reported.Add(result);
        return default;
    }

    /// <summary>
    /// A time limit that arrives while the item is still being prepared
    /// - here, while its run id is written - found no run of this attempt to end. The
    /// item stays pending with no report, as on a shutdown, and the next attempt runs it.
    /// It used to fail with the limit and no run id, and the job failed with it.
    /// </summary>
    [Fact]
    public async Task A_time_limit_while_the_items_run_id_is_written_leaves_the_item_pending()
    {
        using var deadline = new CancellationTokenSource();
        var runner = new ScriptedWorkflowRunner(shouldFail: false);
        var recovery = new WorkflowRecovery(new InMemoryRunStore(), new InMemoryWorkflowCheckpointStore(), new OnPolicy());
        var handler = new WorkflowJobHandler(runner, recovery, NullLogger<WorkflowJobHandler>.Instance);
        var reported = new List<JobItemResult>();
        var context = BuildContext(
            [Item(0, "episode 7")],
            reported,
            report: (result, cancellationToken) =>
            {
                if (result.Status == JobItemStatus.Pending)
                {
                    // The limit fires while the store writes the marker; the store
                    // observes the handler's token.
                    deadline.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return AddAsync(reported, result);
            },
            signals: LimitOf(deadline));

        var exception = await Should.ThrowAsync<OperationCanceledException>(
            () => handler.ExecuteAsync(context, deadline.Token).AsTask());

        exception.ShouldNotBeAssignableTo<NonRetryableJobException>();
        reported.ShouldBeEmpty("an item whose run never started is not failed by the limit");
        runner.Requests.ShouldBeEmpty();
    }

    /// <summary>
    /// The limit arrives while the chain is closed, after the
    /// continuation's row was written. The item stays pending; the continuation row stays
    /// <see cref="RunStatus.Queued"/> only until the next attempt, which reads it as
    /// interrupted and closes the chain. It used to fail the item and leave the row
    /// queued for good.
    /// </summary>
    [Fact]
    public async Task A_time_limit_while_the_chain_is_closed_leaves_the_item_pending_and_the_next_attempt_closes_the_continuation()
    {
        using var deadline = new CancellationTokenSource();
        var inner = new InMemoryRunStore(tenantContext: new FixedTenantContext("tenant"));
        var marked = TraconId.NewId();

        await inner.StartRunAsync(new RunStartInfo
        {
            RunId = marked,
            AgentName = "daily-report",
            Kind = RunKind.Workflow,
            WorkflowName = "daily-report",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "tenant",
        });

        var runs = new FaultyRunStore(inner) { BeforeComplete = deadline.Cancel };
        var runner = new ScriptedWorkflowRunner(shouldFail: false);
        var handler = new WorkflowJobHandler(
            runner,
            new WorkflowRecovery(runs, new InMemoryWorkflowCheckpointStore(), new OnPolicy()),
            NullLogger<WorkflowJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        var exception = await Should.ThrowAsync<OperationCanceledException>(
            () => handler.ExecuteAsync(BuildContext([Item(0, "episode 7", runId: marked)], reported, signals: LimitOf(deadline)), deadline.Token).AsTask());

        exception.ShouldNotBeAssignableTo<NonRetryableJobException>();
        reported.ShouldBeEmpty("the continuation never started: the item stays pending");
        runner.Requests.ShouldBeEmpty();

        var opened = (await inner.QueryRunsAsync(new RunQuery { TenantId = "tenant", ContinuedFromRunId = marked })).ShouldHaveSingleItem();
        opened.Status.ShouldBe(RunStatus.Queued);

        // The next attempt, under a fresh limit, closes what the stopped one left open.
        runs.BeforeComplete = null;
        await handler.ExecuteAsync(BuildContext([Item(0, "episode 7", runId: marked)], reported));

        (await inner.GetRunAsync(opened.Id))!.Status.ShouldNotBe(RunStatus.Queued);
        reported.ShouldHaveSingleItem().Status.ShouldBe(JobItemStatus.Failed);
    }

    /// <summary>
    /// An item whose interrupted run wrote no checkpoint starts again, and it names the
    /// version that run recorded, so an edit made while the job waited does not change what runs.
    /// </summary>
    [Fact]
    public async Task With_recovery_on_an_item_restarted_with_no_checkpoint_runs_the_version_its_interrupted_run_recorded()
    {
        var inner = new InMemoryRunStore(tenantContext: new FixedTenantContext("tenant"));
        var marked = TraconId.NewId();

        await inner.StartRunAsync(new RunStartInfo
        {
            RunId = marked,
            AgentName = "daily-report",
            Kind = RunKind.Workflow,
            WorkflowName = "daily-report",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "tenant",
            AgentVersion = 4,
        });

        var runner = new ScriptedWorkflowRunner(shouldFail: false);
        var handler = new WorkflowJobHandler(
            runner,
            new WorkflowRecovery(inner, new InMemoryWorkflowCheckpointStore(), new OnPolicy()),
            NullLogger<WorkflowJobHandler>.Instance);

        await handler.ExecuteAsync(BuildContext([Item(0, "episode 7", runId: marked)]));

        runner.Requests.ShouldHaveSingleItem().DefinitionVersion.ShouldBe(4);
    }

    /// <summary>
    /// The last item's run completes in the same instant
    /// as the limit. The work is done, so the job completes, as it does for a queued run
    /// and for any handler that returns after its limit. It used to fail with the code.
    /// </summary>
    [Fact]
    public async Task When_the_last_items_run_completes_in_the_same_instant_as_the_limit_the_job_completes()
    {
        using var deadline = new CancellationTokenSource();
        var runner = new ScriptedWorkflowRunner(shouldFail: false, beforeLastEvent: deadline.Cancel);
        var handler = new WorkflowJobHandler(runner, TestWorkflowRecovery.Off(), NullLogger<WorkflowJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(BuildContext([Item(0, "episode 7")], reported, signals: LimitOf(deadline)), deadline.Token);

        reported.ShouldHaveSingleItem().Status.ShouldBe(JobItemStatus.Completed);
    }

    /// <summary>A limit of five minutes whose deadline the test fires by hand.</summary>
    private static JobAttemptSignals LimitOf(CancellationTokenSource deadline)
        => new(TimeSpan.FromMinutes(5), CancellationToken.None, CancellationToken.None, deadline.Token);

    private static JobItemRecord Item(int seq, string input, JobItemStatus status = JobItemStatus.Pending, Guid? runId = null)
        => new() { Id = Guid.NewGuid(), JobId = Guid.NewGuid(), Seq = seq, Input = input, Status = status, RunId = runId };

    private static JobContext BuildContext(
        IReadOnlyList<JobItemRecord> items,
        List<JobItemResult>? reported = null,
        Func<JobItemResult, CancellationToken, ValueTask>? report = null,
        JobAttemptSignals? signals = null)
    {
        reported ??= [];

        return new JobContext
        {
            Job = new JobRecord
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant",
                HandlerKey = JobHandlerKeys.Workflow,
                TargetName = "daily-report",
                Status = JobStatus.Running,
                ScheduledFor = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            Items = items,
            ReportItemAsync = report ?? ((result, _) => AddAsync(reported, result)),
            IsCancelledAsync = _ => new ValueTask<bool>(false),
            Signals = signals,
        };
    }

    /// <summary>
    /// Only <see cref="RunStreamingAsync"/> is actually used; the others are
    /// members this handler never calls.
    /// </summary>
    private sealed class ScriptedWorkflowRunner(bool shouldFail, Action? beforeLastEvent = null) : IWorkflowRunner
    {
        public Guid LastRunId { get; private set; }

        public List<WorkflowRunRequest> Requests { get; } = [];

        public ValueTask<IReadOnlyList<WorkflowDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<WorkflowDescriptor?> GetAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<WorkflowGraph?> GetGraphAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<WorkflowPendingRequest>> ListPendingRequestsAsync(
            Guid runId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<RunEvent> RespondStreamingAsync(
            WorkflowRespondRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<RunEvent> RunStreamingAsync(
            WorkflowRunRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            LastRunId = request.RunId ?? TraconId.NewId();
            var now = DateTimeOffset.UtcNow;

            yield return new RunEvent { RunId = LastRunId, Sequence = 0, Type = RunEventType.RunStarted, Timestamp = now };
            await Task.Yield();
            beforeLastEvent?.Invoke();

            if (shouldFail)
            {
                yield return new RunEvent
                {
                    RunId = LastRunId,
                    Sequence = 1,
                    Type = RunEventType.RunFailed,
                    Timestamp = now,
                    Text = "workflow failed",
                };
            }
            else
            {
                yield return new RunEvent { RunId = LastRunId, Sequence = 1, Type = RunEventType.RunCompleted, Timestamp = now };
            }
        }

        public IAsyncEnumerable<RunEvent> ResumeStreamingAsync(
            WorkflowResumeRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class OnPolicy : IWorkflowRecoveryPolicy
    {
        public bool ResumeInterruptedRuns => true;

        public int MaxResumeAttempts => 1;
    }
}
