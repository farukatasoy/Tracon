using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using AgentPrism.Testing.Contracts.Storage;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// What actually happens when one of two worker processes dies mid-job
/// (Phase 157).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>This is a measurement, not a support statement.</strong> It does
/// not claim AgentPrism "supports multiple nodes"; it records what the SHIPPED
/// lease behaviour does when a second process is present. Three things are
/// measured: how long the queue waits before the dead worker's job becomes
/// leasable again, whether a second worker takes it over, and whether the two
/// workers can ever be inside the handler at the same time.
/// </para>
/// <para>
/// The third is the important one. Execution is documented as
/// <strong>at-least-once</strong> (see <see cref="IJobHandler"/>): a crash
/// costs the in-flight attempt and the job is re-executed from scratch. What
/// must never happen is two workers executing the same job CONCURRENTLY, and
/// that is what the log timestamps below prove.
/// </para>
/// <para>
/// In-memory workers cannot prove any of it: a killed thread still releases
/// its locks, and the whole scenario is about a process that gets NO chance to
/// release anything. Every worker here is a separate operating-system process
/// running the real host, killed with <c>SIGKILL</c>.
/// </para>
/// </remarks>
[Collection(TwoProcessFailureProof.Name)]
public sealed class TwoProcessLeaseTakeoverTests(PostgresFixture fixture) : IAsyncLifetime
{
    /// <summary>
    /// The lease the killed worker takes. Short, because it is the clock the
    /// test waits on; it is a CONFIGURED value, not the shipped five-minute
    /// default, and the report says so.
    /// </summary>
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(6);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How long the handler stays busy. Both workers get the SAME value,
    /// because the test cannot know in advance which of them wins the race for
    /// the job - and a setup where the answer changes the outcome is not a
    /// measurement, it is a coin toss. It is comfortably longer than
    /// <see cref="LeaseDuration"/>, so the first attempt is always killed
    /// mid-work; the worker that takes over renews its own lease while it
    /// works, so it is never stolen back.
    /// </summary>
    private static readonly TimeSpan WorkDuration = TimeSpan.FromSeconds(10);

    private string _logDirectory = null!;

    private string LogPath => Path.Combine(_logDirectory, "executions.log");

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        _logDirectory = Directory.CreateTempSubdirectory("agentprism-two-process-").FullName;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A killed worker may still hold the file open for a moment; the
            // operating system reclaims the temp directory either way.
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_dead_workers_job_is_not_taken_over_before_its_lease_expires()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var observer = await MigratedContextAsync(schemaName);

        // 🚨 BOTH workers are started before the job exists. Starting the second
        // one after the lease was taken would put a whole process launch inside
        // the lease window the assertion below depends on, and a loaded agent
        // would spend that window on `dotnet` startup rather than on the lease
        // clock - the test would flake, or worse, pass while worker B never got
        // a chance to poll at all.
        await using var workerA = await StartWorkerAsync(schemaName, "worker-a", WorkDuration);
        await using var workerB = await StartWorkerAsync(schemaName, "worker-b", WorkDuration);

        var job = await EnqueueHarnessJobAsync(observer);

        var leased = await WaitForLeaseAsync(observer, job.Id);
        var leaseUntil = leased.LeaseUntil!.Value;
        var owner = leased.LeaseOwner;

        await WaitForExecutionsAsync(HarnessExecutionLog.StartedStage, count: 1, TimeSpan.FromSeconds(30));

        // Whichever worker won the race is the one that gets killed; the claim
        // is about the SURVIVOR leaving the lease alone, not about which
        // process happened to be first.
        var (doomed, survivor) = string.Equals(FirstStarter(), "worker-a", StringComparison.Ordinal)
            ? (workerA, "worker-b")
            : (workerB, "worker-a");

        await doomed.KillAsync();

        // The claim: while the dead worker's lease is still valid, worker B
        // leaves the job alone. Waiting until just BEFORE the recorded
        // lease_until is a condition on the database's own clock, not a
        // hard-coded sleep, so a slow agent cannot turn this green by accident.
        await WaitUntilAsync(leaseUntil - TimeSpan.FromSeconds(1));

        var starts = HarnessExecutionLog.Read(LogPath)
            .Where(entry => string.Equals(entry.Stage, HarnessExecutionLog.StartedStage, StringComparison.Ordinal))
            .ToList();

        starts.Count.ShouldBe(1, FormattableString.Invariant($"{survivor} must not enter the handler before {leaseUntil:O}."));
        string.Equals(starts[0].WorkerName, survivor, StringComparison.Ordinal)
            .ShouldBeFalse("the surviving worker must not have been the one that started the job.");

        var stillHeld = await observer.Jobs.GetAsync(job.TenantId, job.Id);
        stillHeld!.LeaseOwner.ShouldBe(owner);
        stillHeld.Attempt.ShouldBe(1);
    }

    [Fact]
    public async Task A_dead_workers_job_is_taken_over_after_the_lease_expires_and_never_runs_concurrently()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var observer = await MigratedContextAsync(schemaName);

        // Both up before the job exists - see the sibling test for why.
        await using var workerA = await StartWorkerAsync(schemaName, "worker-a", WorkDuration);
        await using var workerB = await StartWorkerAsync(schemaName, "worker-b", WorkDuration);

        var job = await EnqueueHarnessJobAsync(observer);

        var leased = await WaitForLeaseAsync(observer, job.Id);
        var leaseUntil = leased.LeaseUntil!.Value;

        await WaitForExecutionsAsync(HarnessExecutionLog.StartedStage, count: 1, TimeSpan.FromSeconds(30));

        var first = FirstStarter();
        var firstIsA = string.Equals(first, "worker-a", StringComparison.Ordinal);
        var doomed = firstIsA ? workerA : workerB;
        var survivor = firstIsA ? "worker-b" : "worker-a";

        await doomed.KillAsync();

        var completed = await WaitForStatusAsync(
            observer,
            job,
            JobStatus.Completed,
            LeaseDuration + WorkDuration + TimeSpan.FromSeconds(60));

        var entries = HarnessExecutionLog.Read(LogPath);
        var starts = entries.Where(e => string.Equals(e.Stage, HarnessExecutionLog.StartedStage, StringComparison.Ordinal)).ToList();
        var finishes = entries.Where(e => string.Equals(e.Stage, HarnessExecutionLog.FinishedStage, StringComparison.Ordinal)).ToList();

        // Taken over: worker A started it, worker B is the one that finished.
        starts.Count.ShouldBe(2);
        starts[0].WorkerName.ShouldBe(first);
        starts[1].WorkerName.ShouldBe(survivor);
        finishes.Count.ShouldBe(1);
        finishes[0].WorkerName.ShouldBe(survivor);

        // Never concurrent: the takeover attempt began only after the dead
        // worker's lease had actually expired.
        starts[1].TimestampUtc.ShouldBeGreaterThanOrEqualTo(leaseUntil);

        // The attempt counter reflects two leases, and the ITEM was counted
        // once - ReportItemAsync is idempotent, so the re-execution did not
        // double the job's own progress counters.
        completed.Attempt.ShouldBe(2);
        completed.DoneItems.ShouldBe(1);
        completed.TotalItems.ShouldBe(1);
        completed.FailedItems.ShouldBe(0);
    }

    [Fact]
    public async Task An_api_node_leaves_the_queued_job_untouched()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var observer = await MigratedContextAsync(schemaName);

        var job = await EnqueueHarnessJobAsync(observer);

        await using var apiNode = await StartWorkerAsync(
            schemaName,
            "api-node",
            TimeSpan.FromSeconds(1),
            runWorker: false);

        // An API node polls nothing, so there is no event to wait for. The
        // wait is bounded by what a worker node WOULD have needed: several
        // poll intervals. If it leased anything, the status below moves.
        await WaitUntilAsync(DateTimeOffset.UtcNow + (PollInterval * 8));

        var untouched = await observer.Jobs.GetAsync(job.TenantId, job.Id);

        untouched!.Status.ShouldBe(JobStatus.Pending);
        untouched.LeaseOwner.ShouldBeNull();
        untouched.Attempt.ShouldBe(0);
        HarnessExecutionLog.Read(LogPath).ShouldBeEmpty();
    }

    private async Task<PostgresTestContext> MigratedContextAsync(string schemaName)
    {
        var context = PostgresTestContext.Create(fixture, schemaName);

        try
        {
            await context.Migrations.ApplyAsync();

            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    private static async Task<JobRecord> EnqueueHarnessJobAsync(PostgresTestContext context)
        => await context.Jobs.EnqueueAsync(
            TestData.Job() with { HandlerKey = WorkerHarnessContract.HandlerKey },
            ["only-item"]);

    private Task<ManagedProcess> StartWorkerAsync(
        string schemaName,
        string name,
        TimeSpan workDuration,
        bool runWorker = true)
        => WorkerProcessHost.StartAsync(new WorkerProcessOptions
        {
            Name = name,
            ConnectionString = fixture.ConnectionString,
            SchemaName = schemaName,
            ExecutionLogPath = LogPath,
            RunWorker = runWorker,
            LeaseDuration = LeaseDuration,
            PollInterval = PollInterval,
            WorkDuration = workDuration,
        });

    private static async Task<JobRecord> WaitForLeaseAsync(PostgresTestContext context, Guid jobId)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var job = await context.Jobs.GetAsync("default", jobId);

            if (job is { LeaseOwner: not null, LeaseUntil: not null })
            {
                return job;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"No worker leased job {jobId} within 30 seconds.");
    }

    private static async Task<JobRecord> WaitForStatusAsync(
        PostgresTestContext context,
        JobRecord job,
        JobStatus status,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var current = await context.Jobs.GetAsync(job.TenantId, job.Id);

            if (current?.Status == status)
            {
                return current;
            }

            await Task.Delay(100);
        }

        var last = await context.Jobs.GetAsync(job.TenantId, job.Id);

        throw new TimeoutException(
            $"Job {job.Id} did not reach {status} within {timeout}; it was {last?.Status.ToString() ?? "missing"}.");
    }

    /// <summary>The name of the worker that entered the handler first.</summary>
    private string FirstStarter()
        => HarnessExecutionLog.Read(LogPath)
            .First(entry => string.Equals(entry.Stage, HarnessExecutionLog.StartedStage, StringComparison.Ordinal))
            .WorkerName;

    private async Task WaitForExecutionsAsync(string stage, int count, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (HarnessExecutionLog.Read(LogPath).Count(entry => string.Equals(entry.Stage, stage, StringComparison.Ordinal)) >= count)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Fewer than {count} '{stage}' events were written within {timeout}.");
    }

    private static async Task WaitUntilAsync(DateTimeOffset instant)
    {
        var remaining = instant - DateTimeOffset.UtcNow;

        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }
    }
}

/// <summary>
/// Serializes the process-level failure proofs against each other.
/// </summary>
/// <remarks>
/// Each test in this collection starts two real hosts and waits on a lease
/// clock. Running them beside each other would put four hosts and their
/// connection pools on the same machine and stretch the very clock they
/// measure - the "isolated green, full run red" class this repository has hit
/// before (docs/hafiza/test-yalitimi.md).
/// </remarks>
[CollectionDefinition(Name)]
public sealed class TwoProcessFailureProof
{
    public const string Name = "Two-process failure proof";
}
