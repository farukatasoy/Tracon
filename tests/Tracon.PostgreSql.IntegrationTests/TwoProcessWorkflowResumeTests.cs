using Tracon.PostgreSql.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// Failure mode 17 of an earlier change: a worker process killed with <c>SIGKILL</c> in the
/// middle of a workflow's second step leaves its run to the job, and the next worker
/// continues it from the first step's checkpoint.
/// </summary>
/// <remarks>
/// <para>
/// In-process tests play the crash (a hung step whose lease nobody renews); this one
/// does it for real, the way the earlier change's lease proof does: two separate harness
/// processes on one PostgreSQL schema, the one that entered the second step killed,
/// the other waiting out the dead worker's lease. A killed process gets no chance to
/// close its run, release its lease or report its item, which is exactly the state
/// recovery has to read back from the database.
/// </para>
/// <para>
/// The proof is the shared execution log: the first step ran once, in the first run;
/// the second step ran twice, once in each run; and the second run names the first
/// in <c>continued_from_run_id</c>.
/// </para>
/// </remarks>
[Collection(TwoProcessFailureProof.Name)]
public sealed class TwoProcessWorkflowResumeTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string WorkflowName = "harness-flow";

    /// <summary>The dead worker's lease: the clock the survivor waits on (a configured value, not the shipped five minutes).</summary>
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(6);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>How long the second step takes: longer than the time to kill the worker in it.</summary>
    private static readonly TimeSpan WorkDuration = TimeSpan.FromSeconds(8);

    private string _logDirectory = null!;

    private string LogPath => Path.Combine(_logDirectory, "executions.log");

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        _logDirectory = Directory.CreateTempSubdirectory("tracon-two-process-workflow-").FullName;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A killed worker may still hold the file open for a moment.
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_workflow_killed_in_its_second_step_is_continued_by_the_other_worker_from_the_first_steps_checkpoint()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        var context = PostgresTestContext.Create(fixture, schemaName);

        await using (context)
        {
            await context.Migrations.ApplyAsync();

            await context.Workflows.SaveAsync("default", new WorkflowDefinition
            {
                Name = WorkflowName,
                Kind = WorkflowKind.Sequential,
                Nodes =
                [
                    new WorkflowNodeReference { Name = WorkerHarnessContract.FirstStep, Kind = WorkflowNodeKind.Function },
                    new WorkflowNodeReference { Name = WorkerHarnessContract.SecondStep, Kind = WorkflowNodeKind.Function },
                ],
            });

            // Both workers are up before the job exists (see TwoProcessLeaseTakeoverTests).
            await using var workerA = await StartWorkerAsync(schemaName, "worker-a");
            await using var workerB = await StartWorkerAsync(schemaName, "worker-b");

            var job = await context.Jobs.EnqueueAsync(
                TestData.Job() with { HandlerKey = JobHandlerKeys.Workflow, TargetName = WorkflowName },
                ["episode 7"]);

            await WaitUntil.TrueAsync(
                () => Entries(WorkerHarnessContract.SecondStepStage).Count >= 1,
                "a worker to enter the second step",
                TimeSpan.FromSeconds(60));

            var doomedName = Entries(WorkerHarnessContract.SecondStepStage)[0].WorkerName;
            var doomed = string.Equals(doomedName, "worker-a", StringComparison.Ordinal) ? workerA : workerB;
            var survivor = string.Equals(doomedName, "worker-a", StringComparison.Ordinal) ? "worker-b" : "worker-a";

            await doomed.KillAsync();

            var finished = (await WaitUntil.ValueAsync(
                () => context.Jobs.GetAsync(job.TenantId, job.Id).AsTask(),
                static current => current is { Status: JobStatus.Completed or JobStatus.Failed },
                $"job {job.Id} to finish",
                LeaseDuration + WorkDuration + TimeSpan.FromSeconds(90)))!;

            finished.Status.ShouldBe(JobStatus.Completed, finished.ErrorMessage);
            finished.Attempt.ShouldBe(2);

            var firstSteps = Entries(WorkerHarnessContract.FirstStepStage);
            var secondSteps = Entries(WorkerHarnessContract.SecondStepStage);

            firstSteps.Count.ShouldBe(1, "the first step finished before the kill; its checkpoint covers it");
            firstSteps[0].WorkerName.ShouldBe(doomedName);

            secondSteps.Count.ShouldBe(2, "the step the worker died in runs again, once");
            secondSteps[1].WorkerName.ShouldBe(survivor);

            var headRunId = firstSteps[0].JobId;
            var continuationRunId = secondSteps[1].JobId;

            continuationRunId.ShouldNotBe(headRunId);

            var head = (await context.Runs.GetRunAsync(headRunId)).ShouldNotBeNull();
            var continuation = (await context.Runs.GetRunAsync(continuationRunId)).ShouldNotBeNull();

            head.Status.ShouldBe(RunStatus.Failed);
            head.Error.ShouldNotBeNull().Type.ShouldBe("interrupted");
            continuation.Status.ShouldBe(RunStatus.Completed, continuation.Error?.Message);
            continuation.ContinuedFromRunId.ShouldBe(headRunId);
            continuation.SessionId.ShouldBe(head.SessionId);

            var item = (await context.Jobs.ListItemsAsync(job.Id)).ShouldHaveSingleItem();
            item.Status.ShouldBe(JobItemStatus.Completed);
            item.RunId.ShouldBe(continuationRunId);
        }
    }

    private List<HarnessExecutionEntry> Entries(string stage)
        => [.. HarnessExecutionLog.Read(LogPath).Where(entry => string.Equals(entry.Stage, stage, StringComparison.Ordinal))];

    private Task<ManagedProcess> StartWorkerAsync(string schemaName, string name)
        => WorkerProcessHost.StartAsync(new WorkerProcessOptions
        {
            Name = name,
            ConnectionString = fixture.ConnectionString,
            SchemaName = schemaName,
            ExecutionLogPath = LogPath,
            LeaseDuration = LeaseDuration,
            PollInterval = PollInterval,
            WorkDuration = WorkDuration,
            Workflow = true,
        });
}
