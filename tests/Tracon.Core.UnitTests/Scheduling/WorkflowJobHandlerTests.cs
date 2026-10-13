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

    private static JobItemRecord Item(int seq, string input, JobItemStatus status = JobItemStatus.Pending)
        => new() { Id = Guid.NewGuid(), JobId = Guid.NewGuid(), Seq = seq, Input = input, Status = status };

    private static JobContext BuildContext(IReadOnlyList<JobItemRecord> items, List<JobItemResult>? reported = null)
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
            ReportItemAsync = (result, _) =>
            {
                reported.Add(result);
                return default;
            },
            IsCancelledAsync = _ => new ValueTask<bool>(false),
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
}
