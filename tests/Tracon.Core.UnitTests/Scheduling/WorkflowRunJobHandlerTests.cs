using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// F-318: the queued workflow run's payload and the handler's outcome rules.
/// </summary>
/// <remarks>
/// A payload is durable data, readable and writable by anyone with the job store, so
/// the worker reads it again as untrusted input. The job's outcome follows the run's:
/// a failed run fails the job without a retry, because a second lease would mistake it
/// for an interruption; a store error after the run must not fail the finished work.
/// </remarks>
public sealed class WorkflowRunJobHandlerTests
{
    private const string Tenant = "default";

    [Fact]
    public void A_payload_round_trips_every_field_of_a_response()
    {
        var runId = TraconId.NewId();
        var source = TraconId.NewId();

        var payload = WorkflowRunJobPayload.Write(new WorkflowRunJobRequest
        {
            Operation = WorkflowRunJobPayload.RespondOperation,
            RunId = runId,
            SessionId = "production-7",
            UserId = "ada",
            Labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "audio" },
            SourceRunId = source,
            CheckpointId = "cp-1",
            RequestId = "gate",
            Approved = true,
            Text = "ship it",
            Json = """{"note":"ok"}""",
        });

        var (request, readRunId, problem) = WorkflowRunJobPayload.Read(payload, logger: null);

        problem.ShouldBeNull();
        readRunId.ShouldBe(runId);
        request.ShouldNotBeNull();
        request.Operation.ShouldBe("respond");
        request.SessionId.ShouldBe("production-7");
        request.UserId.ShouldBe("ada");
        request.Labels.ShouldNotBeNull()["team"].ShouldBe("audio");
        request.SourceRunId.ShouldBe(source);
        request.CheckpointId.ShouldBe("cp-1");
        request.RequestId.ShouldBe("gate");
        request.Approved.ShouldBe(true);
        request.Text.ShouldBe("ship it");
        JsonDocument.Parse(request.Json!).RootElement.GetProperty("note").GetString().ShouldBe("ok");
    }

    [Theory]
    [InlineData("""{"operation":"run"}""", "runId")]
    [InlineData("""{"operation":"delete","runId":"01a1340e-ad31-75e4-b64b-e3d0a26ab0a1"}""", "operation")]
    [InlineData("""{"operation":"resume","runId":"01a1340e-ad31-75e4-b64b-e3d0a26ab0a1"}""", "sourceRunId")]
    [InlineData("""{"operation":"respond","runId":"01a1340e-ad31-75e4-b64b-e3d0a26ab0a1","sourceRunId":"01a1340e-ad31-75e4-b64b-e3d0a26ab0a2"}""", "requestId")]
    [InlineData("""{"operation":"run","runId":"01a1340e-ad31-75e4-b64b-e3d0a26ab0a1","sessionId":"has spaces"}""", "sessionId")]
    [InlineData("""{"operation":"run","runId":"01a1340e-ad31-75e4-b64b-e3d0a26ab0a1","parameters":{"tone":7}}""", "parameters")]
    [InlineData("""{"operation":"run","runId":"01a1340e-ad31-75e4-b64b-e3d0a26ab0a1","approved":"yes"}""", "approved")]
    public void A_malformed_payload_is_refused_naming_the_field(string json, string field)
    {
        var (request, _, problem) = WorkflowRunJobPayload.Read(JsonDocument.Parse(json).RootElement, logger: null);

        request.ShouldBeNull();
        problem.ShouldNotBeNull().ShouldContain(field, Case.Sensitive);
    }

    [Fact]
    public void A_malformed_attribution_is_dropped_not_refused()
    {
        var json = $$"""{"operation":"run","runId":"01a1340e-ad31-75e4-b64b-e3d0a26ab0a1","userId":"{{new string('u', 1000)}}","labels":"not-an-object"}""";

        var (request, _, problem) = WorkflowRunJobPayload.Read(JsonDocument.Parse(json).RootElement, logger: null);

        problem.ShouldBeNull();
        request.ShouldNotBeNull().UserId.ShouldBeNull();
        request.Labels.ShouldBeNull();
    }

    [Fact]
    public async Task A_malformed_payload_closes_the_queued_row_and_fails_the_job_without_a_retry()
    {
        var runs = new InMemoryRunStore();
        var runId = await QueueAsync(runs);
        var handler = new WorkflowRunJobHandler(new ScriptedRunner(), Recovery(runs), NullLogger<WorkflowRunJobHandler>.Instance);

        var exception = await Should.ThrowAsync<NonRetryableJobException>(
            () => handler.ExecuteAsync(Context(runId, $$"""{"operation":"resume","runId":"{{runId}}"}""")).AsTask());

        exception.Code.ShouldBe(WorkflowRunJobHandler.PayloadInvalidCode);
        (await runs.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task A_run_that_failed_on_its_own_fails_the_job_without_a_retry()
    {
        var runs = new InMemoryRunStore();
        var runId = await QueueAsync(runs);
        var handler = new WorkflowRunJobHandler(new ScriptedRunner(runs, RunStatus.Failed), Recovery(runs), NullLogger<WorkflowRunJobHandler>.Instance);

        var exception = await Should.ThrowAsync<NonRetryableJobException>(
            () => handler.ExecuteAsync(Context(runId, RunPayload(runId))).AsTask());

        exception.Code.ShouldBe(WorkflowRunJobHandler.RunFailedCode);
    }

    /// <summary>
    /// F-348's rule for this handler: the engine closed the run; a store error while
    /// the handler reads the outcome afterwards completes the job instead of failing it,
    /// so the finished run is not repeated.
    /// </summary>
    [Fact]
    public async Task A_store_error_while_reading_the_finished_runs_status_does_not_fail_the_job()
    {
        var inner = new InMemoryRunStore();
        var runId = await QueueAsync(inner);
        var runs = new FaultyRunStore(inner);
        var handler = new WorkflowRunJobHandler(new ScriptedRunner(inner, RunStatus.Completed, () => runs.FailReads = true), Recovery(runs), NullLogger<WorkflowRunJobHandler>.Instance);

        await handler.ExecuteAsync(Context(runId, RunPayload(runId)));

        runs.ReadsRefused.ShouldBe(1);
    }

    [Fact]
    public async Task A_job_cancelled_after_its_lease_does_not_start_the_run()
    {
        var runs = new InMemoryRunStore();
        var runId = await QueueAsync(runs);
        var runner = new ScriptedRunner(runs, RunStatus.Completed);
        var handler = new WorkflowRunJobHandler(runner, Recovery(runs), NullLogger<WorkflowRunJobHandler>.Instance);

        await handler.ExecuteAsync(Context(runId, RunPayload(runId), cancelled: true));

        runner.Started.ShouldBe(0);
    }

    /// <summary>
    /// A store error while the chain is read fails the attempt, not the work: the
    /// worker retries it, and nothing was written that the next attempt would misread.
    /// </summary>
    [Fact]
    public async Task With_recovery_on_a_store_error_while_reading_the_chain_fails_the_attempt_not_the_work()
    {
        var inner = new InMemoryRunStore();
        var runId = await QueueAsync(inner);
        var runs = new FaultyRunStore(inner) { FailReads = true };
        var runner = new ScriptedRunner(inner);
        var handler = new WorkflowRunJobHandler(runner, RecoveryOn(runs), NullLogger<WorkflowRunJobHandler>.Instance);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => handler.ExecuteAsync(Context(runId, RunPayload(runId))).AsTask());

        exception.ShouldNotBeAssignableTo<NonRetryableJobException>();
        runner.Started.ShouldBe(0);
        (await inner.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Queued, "the next attempt starts it");

        runs.FailReads = false;
        await handler.ExecuteAsync(Context(runId, RunPayload(runId)));

        (await inner.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Completed);
    }

    /// <summary>
    /// The continuation's row is written BEFORE the interrupted run is closed naming
    /// it. A write that fails leaves the run open, not closed with the name of a run
    /// that does not exist, and the next attempt continues it.
    /// </summary>
    [Fact]
    public async Task With_recovery_on_a_continuation_that_cannot_be_opened_leaves_the_interrupted_run_for_the_next_attempt()
    {
        var inner = new InMemoryRunStore();
        var runId = await QueueAsync(inner);
        await inner.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "podcast",
            Kind = RunKind.Workflow,
            WorkflowName = "podcast",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = Tenant,
        });

        var runs = new FaultyRunStore(inner) { FailContinuationStart = true };
        var handler = new WorkflowRunJobHandler(new ScriptedRunner(inner), RecoveryOn(runs), NullLogger<WorkflowRunJobHandler>.Instance);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(Context(runId, RunPayload(runId), attempt: 2, maxAttempts: 3)).AsTask());

        exception.ShouldNotBeAssignableTo<NonRetryableJobException>();
        (await inner.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Running, "nothing names a continuation that was never written");

        runs.FailContinuationStart = false;
        await handler.ExecuteAsync(Context(runId, RunPayload(runId), attempt: 3, maxAttempts: 4));

        var continuation = (await inner.QueryRunsAsync(new RunQuery { TenantId = Tenant, ContinuedFromRunId = runId })).ShouldHaveSingleItem();
        continuation.Status.ShouldBe(RunStatus.Completed);

        var head = (await inner.GetRunAsync(runId))!;
        head.Status.ShouldBe(RunStatus.Failed);
        head.Error.ShouldNotBeNull().Message.ShouldContain(continuation.Id.ToString(), Case.Sensitive);
    }

    /// <summary>
    /// The engine refused the run and the store failed the write that closes its
    /// queued row: the attempt fails instead of the work, so the row is not left
    /// Queued with no attempt coming back for it.
    /// </summary>
    [Fact]
    public async Task With_recovery_on_a_refused_run_whose_row_cannot_be_closed_is_closed_by_the_next_attempt()
    {
        var inner = new InMemoryRunStore();
        var runId = await QueueAsync(inner);
        var runs = new FaultyRunStore(inner) { FailCompletes = true };
        var handler = new WorkflowRunJobHandler(new ScriptedRunner(inner, refuse: true), RecoveryOn(runs), NullLogger<WorkflowRunJobHandler>.Instance);

        var first = await Should.ThrowAsync<TraconException>(() => handler.ExecuteAsync(Context(runId, RunPayload(runId))).AsTask());

        first.ShouldNotBeAssignableTo<NonRetryableJobException>();
        (await inner.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Queued);

        runs.FailCompletes = false;
        var second = await Should.ThrowAsync<NonRetryableJobException>(() => handler.ExecuteAsync(Context(runId, RunPayload(runId))).AsTask());

        second.Code.ShouldBe(WorkflowRunJobHandler.RunFailedCode);
        (await inner.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Failed);
    }

    private static WorkflowRecovery RecoveryOn(IRunStore runs) => new(runs, new InMemoryWorkflowCheckpointStore(), new OnPolicy());

    private static WorkflowRecovery Recovery(IRunStore runs) => new(runs, new InMemoryWorkflowCheckpointStore(), policy: null);

    private static async Task<Guid> QueueAsync(InMemoryRunStore runs)
    {
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "podcast",
            Kind = RunKind.Workflow,
            WorkflowName = "podcast",
            Status = RunStatus.Queued,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = Tenant,
        });

        return runId;
    }

    private static string RunPayload(Guid runId) => $$"""{"operation":"run","runId":"{{runId}}","message":"episode 7"}""";

    private static JobContext Context(Guid runId, string payload, bool cancelled = false, int attempt = 1, int maxAttempts = 3) => new()
    {
        Job = new JobRecord
        {
            Id = runId,
            TenantId = Tenant,
            HandlerKey = JobHandlerKeys.WorkflowRun,
            TargetName = "podcast",
            Status = JobStatus.Running,
            Attempt = attempt,
            MaxAttempts = maxAttempts,
            Payload = JsonDocument.Parse(payload).RootElement.Clone(),
            ScheduledFor = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        },
        Items = [],
        ReportItemAsync = static (_, _) => default,
        IsCancelledAsync = _ => new ValueTask<bool>(cancelled),
    };

    /// <summary>Opens and closes the run row the way the engine does, with a scripted outcome.</summary>
    private sealed class ScriptedRunner(
        IRunStore? runs = null,
        RunStatus outcome = RunStatus.Completed,
        Action? afterClose = null,
        bool refuse = false) : IWorkflowRunner
    {
        public int Started { get; private set; }

        public ValueTask<IReadOnlyList<WorkflowDescriptor>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<WorkflowDescriptor?> GetAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<WorkflowGraph?> GetGraphAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<WorkflowPendingRequest>> ListPendingRequestsAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<RunEvent> RespondStreamingAsync(WorkflowRespondRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<RunEvent> ResumeStreamingAsync(WorkflowResumeRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<RunEvent> RunStreamingAsync(
            WorkflowRunRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started++;
            var runId = request.RunId!.Value;

            if (refuse)
            {
                // The engine checks the request before it writes the run's row.
                throw new TraconException("The workflow's schema changed.");
            }

            await runs!.StartRunAsync(new RunStartInfo
            {
                RunId = runId,
                AgentName = request.WorkflowName,
                Kind = RunKind.Workflow,
                WorkflowName = request.WorkflowName,
                StartedAt = DateTimeOffset.UtcNow,
                TenantId = Tenant,
            }, cancellationToken);

            yield return new RunEvent { RunId = runId, Sequence = 0, Type = RunEventType.RunStarted, Timestamp = DateTimeOffset.UtcNow };

            await runs.CompleteRunAsync(new RunCompletion { RunId = runId, Status = outcome, CompletedAt = DateTimeOffset.UtcNow }, cancellationToken);
            afterClose?.Invoke();

            yield return new RunEvent
            {
                RunId = runId,
                Sequence = 1,
                Type = outcome == RunStatus.Completed ? RunEventType.RunCompleted : RunEventType.RunFailed,
                Timestamp = DateTimeOffset.UtcNow,
            };
        }
    }

    private sealed class OnPolicy : IWorkflowRecoveryPolicy
    {
        public bool ResumeInterruptedRuns => true;

        public int MaxResumeAttempts => 1;
    }
}
