using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// The lease, attempt and cancel contract <see cref="IJobHandler"/> and
/// <see cref="TraconSchedulingOptions.MaxAttempts"/> promise, held by
/// <see cref="JobWorkerBackgroundService"/>.
/// </summary>
/// <remarks>
/// All three broke on the worker, not in a store. Renewal stopped for
/// good on its first failure, a job whose lease ran out was run again with no
/// attempt limit, and a cancelled job whose handler threw was retried or failed.
/// </remarks>
public sealed class JobWorkerLeaseContractTests
{
    private const string TenantId = "tenant-a";

    [Fact]
    public async Task Lease_renewal_continues_after_a_failed_renewal()
    {
        var jobs = new FailFirstRenewalJobStore();
        var handler = new BlockingHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        await EnqueueAsync(jobs, maxAttempts: 1, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMilliseconds(300));

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await handler.Started.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        // The first renewal throws. A loop that stops there never calls again,
        // and the lease runs out under a handler that is still working.
        await WaitUntil.TrueAsync(() => jobs.RenewCalls >= 3, "the lease to be renewed after the failed renewal");

        handler.Release();
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_job_whose_lease_ran_out_is_not_run_past_its_attempt_limit()
    {
        var clock = new ManualTimeProvider();
        var jobs = new InMemoryJobStore(clock);
        var handler = new CountingHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts: 1, clock.GetUtcNow());

        // A worker leased the job (attempt 1) and died without renewing it.
        (await jobs.LeaseAsync(new JobLeaseRequest { Owner = "dead-worker", LeaseDuration = TimeSpan.FromMinutes(1) }, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        clock.Advance(TimeSpan.FromMinutes(2));

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5), clock);

        await worker.StartAsync(TestContext.Current.CancellationToken);

        var record = await WaitUntil.ValueAsync(
            () => jobs.GetAsync(TenantId, jobId, TestContext.Current.CancellationToken).AsTask(),
            static job => job?.Status is JobStatus.Failed or JobStatus.Completed,
            "the re-leased job to finish");

        await worker.StopAsync(TestContext.Current.CancellationToken);

        handler.Calls.ShouldBe(0, "a job limited to one attempt ran a second time");
        record!.Status.ShouldBe(JobStatus.Failed);
        record.Attempt.ShouldBe(2);
        record.ErrorMessage.ShouldNotBeNull();
        record.ErrorMessage.ShouldStartWith(JobErrorCodes.AttemptsExhausted);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    public async Task A_cancelled_job_whose_handler_throws_stays_cancelled(int maxAttempts, bool throwsAfterTheWatchSawTheCancel)
    {
        var jobs = new ObservingJobStore();
        var handler = new CancelThenThrowHandler(jobs, throwsAfterTheWatchSawTheCancel);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));
        using var meters = new TestMeterFactory();
        using var collector = new MetricCollector(meters.Meter);
        using var metrics = new TraconMetrics(meters, TenantTaggedMetricsOptions.Create());

        var jobId = await EnqueueAsync(jobs, maxAttempts, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5), metrics: metrics);

        await worker.StartAsync(TestContext.Current.CancellationToken);

        // The attempt's job metric is the one signal both orders share. A
        // handler that throws before the watch reads the cancel goes through the
        // failure path, which reads the status back; when the watch reads it first,
        // the worker writes and reads nothing more. Waiting for that read hung the
        // second order for 30 s, and a busy machine made it the order that ran.
        await WaitUntil.TrueAsync(
            () => collector.LongTags(TraconDiagnostics.JobCounterName).Count > 0,
            "the worker to record the attempt's outcome");

        var record = await jobs.GetAsync(TenantId, jobId, TestContext.Current.CancellationToken);

        await worker.StopAsync(TestContext.Current.CancellationToken);

        record!.Status.ShouldBe(JobStatus.Cancelled);
        collector.LongTags(TraconDiagnostics.JobCounterName).ShouldHaveSingleItem()[TraconDiagnostics.Tags.JobStatus]
            .ShouldBe(nameof(JobStatus.Cancelled));
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_job_cancelled_between_lease_and_start_is_not_run()
    {
        var jobs = new ObservingJobStore { CancelBeforeMarkRunning = true };
        var handler = new CountingHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts: 1, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        // Either the handler runs, or the worker reads the job back and stops.
        await Task.WhenAny(handler.Called, jobs.ReadAfterMarkRunning)
            .WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        await worker.StopAsync(TestContext.Current.CancellationToken);

        var record = await jobs.GetAsync(TenantId, jobId, TestContext.Current.CancellationToken);

        handler.Calls.ShouldBe(0);
        record!.Status.ShouldBe(JobStatus.Cancelled);
    }

    /// <summary>
    /// The store calls that write a successful handler's outcome sat in
    /// the handler's own <c>try</c>. One store error there was taken for a
    /// handler failure: under one attempt the job became Failed while its work
    /// had finished; under three the handler ran a second time.
    /// </summary>
    [Theory]
    [InlineData(StoreCall.Get, 1)]
    [InlineData(StoreCall.Get, 3)]
    [InlineData(StoreCall.ListItems, 1)]
    [InlineData(StoreCall.ListItems, 3)]
    [InlineData(StoreCall.Complete, 1)]
    [InlineData(StoreCall.Complete, 3)]
    public async Task A_store_error_after_the_handler_succeeded_neither_fails_nor_reruns_the_job(StoreCall failing, int maxAttempts)
    {
        var jobs = new ThrowOnceJobStore(failing);
        var handler = new CountingHandler(jobs.Arm);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        var record = await WaitUntil.ValueAsync(
            () => jobs.PeekAsync(jobId).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed,
            "the job to finish");

        await worker.StopAsync(TestContext.Current.CancellationToken);

        jobs.Threw.ShouldBeTrue("the store error was never raised, so the case proves nothing");
        handler.Calls.ShouldBe(1, "a handler that succeeded ran again because the store failed once");
        record!.Status.ShouldBe(JobStatus.Completed);
        record.Attempt.ShouldBe(1);
    }

    /// <summary>
    /// A handler whose work is done but whose last write kept failing
    /// throws <see cref="NonRetryableJobException"/>. Releasing that job ran
    /// the work again (a second model call for a queued run).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task A_handler_whose_finished_work_could_not_be_recorded_fails_at_once_and_does_not_run_again(int maxAttempts)
    {
        var jobs = new InMemoryJobStore();
        var handler = new NonRetryableHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        var record = await WaitUntil.ValueAsync(
            () => jobs.GetAsync(TenantId, jobId, TestContext.Current.CancellationToken).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed,
            "the job to finish");

        await worker.StopAsync(TestContext.Current.CancellationToken);

        handler.Calls.ShouldBe(1, "a handler whose work was done ran again");
        record!.Status.ShouldBe(JobStatus.Failed);
        record.Attempt.ShouldBe(1);
        record.ErrorMessage.ShouldBe(JobErrorCodes.Format(QueuedTurn.SessionSaveFailedCode, "abcd1234"));
    }

    [Fact]
    public async Task A_store_error_while_releasing_a_failed_attempt_still_releases_the_job()
    {
        var jobs = new ThrowOnceJobStore(StoreCall.ReleaseForRetry);
        var handler = new FailingHandler(failures: 1, jobs.Arm);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts: 3, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        // A release that escaped left the job Running under a five-minute lease.
        var record = await WaitUntil.ValueAsync(
            () => jobs.PeekAsync(jobId).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed,
            "the released job to run again and finish");

        await worker.StopAsync(TestContext.Current.CancellationToken);

        jobs.Threw.ShouldBeTrue();
        handler.Calls.ShouldBe(2);
        record!.Status.ShouldBe(JobStatus.Completed);
        record.Attempt.ShouldBe(2);
    }

    [Fact]
    public async Task A_store_error_while_failing_the_last_attempt_still_fails_the_job()
    {
        var jobs = new ThrowOnceJobStore(StoreCall.Complete);
        var handler = new FailingHandler(failures: int.MaxValue, jobs.Arm);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts: 1, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        var record = await WaitUntil.ValueAsync(
            () => jobs.PeekAsync(jobId).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed,
            "the failed job to be closed");

        await worker.StopAsync(TestContext.Current.CancellationToken);

        jobs.Threw.ShouldBeTrue();
        handler.Calls.ShouldBe(1);
        record!.Status.ShouldBe(JobStatus.Failed);
        record.Attempt.ShouldBe(1);
        record.ErrorMessage.ShouldNotBeNull();
        record.ErrorMessage.ShouldNotStartWith(JobErrorCodes.AttemptsExhausted);
    }

    /// <summary>
    /// The completion retry stops where this worker can no longer prove its
    /// lease: past that point another worker may own the job, and a store
    /// write carries no owner check. The job then takes the lease-expiry path.
    /// </summary>
    [Fact]
    public async Task A_completion_the_store_keeps_refusing_is_left_to_lease_expiry()
    {
        var clock = new ManualTimeProvider();
        var jobs = new RefusingCompletionJobStore(clock);
        var handler = new CountingHandler(jobs.Arm);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts: 1, clock.GetUtcNow());

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5), clock);

        await worker.StartAsync(TestContext.Current.CancellationToken);

        await WaitUntil.TrueAsync(() => jobs.RefusedCompletions >= 2, "the worker to retry the refused completion");

        // The lease runs out on the store's clock and on the worker's.
        clock.Advance(TimeSpan.FromMinutes(6));

        var record = await WaitUntil.ValueAsync(
            () => jobs.PeekAsync(jobId).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed,
            "the expired job to be re-leased and closed");

        await worker.StopAsync(TestContext.Current.CancellationToken);

        handler.Calls.ShouldBe(1);
        record!.Status.ShouldBe(JobStatus.Failed);
        record.Attempt.ShouldBe(2);
        record.ErrorMessage.ShouldNotBeNull();
        record.ErrorMessage.ShouldStartWith(JobErrorCodes.AttemptsExhausted);
    }

    [Fact]
    public async Task Stopping_the_worker_ends_the_completion_retries()
    {
        var jobs = new RefusingCompletionJobStore();
        var handler = new CountingHandler(jobs.Arm);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts: 1, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        await WaitUntil.TrueAsync(() => jobs.RefusedCompletions >= 2, "the worker to retry the refused completion");

        using var stopBound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stopBound.CancelAfter(WaitUntil.DefaultTimeout);

        await worker.StopAsync(stopBound.Token);

        (worker.ExecuteTask?.IsCompleted ?? false).ShouldBeTrue("a retry loop that ignores shutdown holds the host open");

        var record = await jobs.PeekAsync(jobId);

        handler.Calls.ShouldBe(1);
        record!.Status.ShouldBe(JobStatus.Running);
    }

    /// <summary>The store operation a <see cref="ThrowOnceJobStore"/> fails.</summary>
    public enum StoreCall
    {
        /// <summary><see cref="IJobStore.GetAsync"/>.</summary>
        Get,

        /// <summary><see cref="IJobStore.ListItemsAsync"/>.</summary>
        ListItems,

        /// <summary><see cref="IJobStore.CompleteAsync"/>.</summary>
        Complete,

        /// <summary><see cref="IJobStore.ReleaseForRetryAsync"/>.</summary>
        ReleaseForRetry,
    }

    private static JobWorkerBackgroundService CreateWorker(
        IJobStore jobs,
        TestJobHandlerHost host,
        TimeSpan leaseDuration,
        TimeProvider? clock = null,
        TraconMetrics? metrics = null)
        => new(
            jobs,
            new InMemoryJobScheduleStore(),
            host.Registry,
            host.Scopes,
            new StaticOptionsMonitor<TraconSchedulingOptions>(new TraconSchedulingOptions
            {
                MaxConcurrentJobs = 1,
                PollInterval = TimeSpan.FromMilliseconds(5),
                LeaseDuration = leaseDuration,
                MaxAttempts = 3,
            }),
            new SchemaReadyGate([]),
            new NotDraining(),
            timeProvider: clock,
            metrics: metrics,
            logger: NullLogger<JobWorkerBackgroundService>.Instance);

    private static async Task<Guid> EnqueueAsync(IJobStore jobs, int maxAttempts, DateTimeOffset now)
    {
        var id = Guid.NewGuid();

        await jobs.EnqueueAsync(
            new JobRecord
            {
                Id = id,
                TenantId = TenantId,
                HandlerKey = JobHandlerKeys.AgentBatch,
                TargetName = "lease-contract",
                Status = JobStatus.Pending,
                MaxAttempts = maxAttempts,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            TestContext.Current.CancellationToken);

        return id;
    }

    private sealed class NotDraining : ITraconDrainState
    {
        public bool IsDraining => false;
    }

    private sealed class BlockingHandler : IJobHandler
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task.ConfigureAwait(false);
        }
    }

    /// <summary>Counts its calls and succeeds; <paramref name="onCall"/> runs inside each call.</summary>
    private sealed class CountingHandler(Action? onCall = null) : IJobHandler
    {
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task Called => _called.Task;

        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            onCall?.Invoke();
            _called.TrySetResult();

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Throws on its first <paramref name="failures"/> calls, then succeeds.</summary>
    private sealed class FailingHandler(int failures, Action? onCall = null) : IJobHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            onCall?.Invoke();

            return call <= failures
                ? throw new InvalidOperationException("The handler failed on purpose.")
                : ValueTask.CompletedTask;
        }
    }

    /// <summary>Did its work, then could not record it: what a queued turn throws when its session never saves.</summary>
    private sealed class NonRetryableHandler : IJobHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            throw new NonRetryableJobException(
                QueuedTurn.SessionSaveFailedCode,
                "abcd1234",
                new TimeoutException("The session store did not answer in time."));
        }
    }

    /// <summary>Cancels its own job, as an operator would mid-run, then fails on its way out.</summary>
    /// <param name="jobs">The store the handler cancels its job in.</param>
    /// <param name="waitForTheWatch">
    /// Throws only after the worker's cancel watch has read the cancel and stopped the
    /// handler's token. Without the wait the order is a race between the throw and the
    /// watch's next tick, and the worker writes nothing after the throw when the watch wins.
    /// </param>
    private sealed class CancelThenThrowHandler(ObservingJobStore jobs, bool waitForTheWatch) : IJobHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            (await jobs.CancelAsync(context.Job.TenantId, context.Job.Id, cancellationToken)).ShouldBeTrue();
            (await context.IsCancelledAsync(cancellationToken)).ShouldBeTrue();

            if (waitForTheWatch)
            {
                await Should.ThrowAsync<OperationCanceledException>(
                    () => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)); // delay: simulated
            }

            throw new InvalidOperationException("The handler stopped because its job was cancelled.");
        }
    }

    private abstract class DelegatingJobStore(TimeProvider? clock = null) : IJobStore
    {
        protected InMemoryJobStore Inner { get; } = new(clock);

        /// <summary>Reads a job past any fault the derived store injects.</summary>
        public ValueTask<JobRecord?> PeekAsync(Guid jobId)
            => Inner.GetAsync(TenantId, jobId, TestContext.Current.CancellationToken);

        public ValueTask<JobRecord> EnqueueAsync(JobRecord job, IReadOnlyList<string> items, CancellationToken cancellationToken = default)
            => Inner.EnqueueAsync(job, items, cancellationToken);

        public ValueTask<JobRecord?> LeaseAsync(JobLeaseRequest request, CancellationToken cancellationToken = default)
            => Inner.LeaseAsync(request, cancellationToken);

        public virtual ValueTask RenewLeaseAsync(Guid jobId, string owner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
            => Inner.RenewLeaseAsync(jobId, owner, leaseDuration, cancellationToken);

        public virtual ValueTask<bool> MarkRunningAsync(Guid jobId, string owner, CancellationToken cancellationToken = default)
            => Inner.MarkRunningAsync(jobId, owner, cancellationToken);

        public virtual ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default)
            => Inner.CompleteAsync(completion, cancellationToken);

        public virtual ValueTask ReleaseForRetryAsync(Guid jobId, string errorMessage, TimeSpan? retryAfter = null, CancellationToken cancellationToken = default)
            => Inner.ReleaseForRetryAsync(jobId, errorMessage, retryAfter, cancellationToken);

        public ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => Inner.CancelAsync(tenantId, jobId, cancellationToken);

        public virtual ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => Inner.GetAsync(tenantId, jobId, cancellationToken);

        public ValueTask<IReadOnlyList<JobRecord>> QueryAsync(JobQuery query, CancellationToken cancellationToken = default)
            => Inner.QueryAsync(query, cancellationToken);

        public virtual ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default)
            => Inner.ListItemsAsync(jobId, cancellationToken);

        public ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default)
            => Inner.ReportItemAsync(item, cancellationToken);

        public ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(CancellationToken cancellationToken = default)
            => Inner.GetQueueDepthAsync(cancellationToken);
    }

    /// <summary>Throws on the first renewal, the way a store that blips once does.</summary>
    private sealed class FailFirstRenewalJobStore : DelegatingJobStore
    {
        private int _renewCalls;

        public int RenewCalls => Volatile.Read(ref _renewCalls);

        public override ValueTask RenewLeaseAsync(Guid jobId, string owner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
            => Interlocked.Increment(ref _renewCalls) == 1
                ? throw new TimeoutException("The store did not answer in time.")
                : base.RenewLeaseAsync(jobId, owner, leaseDuration, cancellationToken);
    }

    /// <summary>
    /// Fails the first call of one operation made after <see cref="Arm"/>, the
    /// way a store that blips once does. Every other call reaches the store.
    /// </summary>
    private sealed class ThrowOnceJobStore(StoreCall failing) : DelegatingJobStore
    {
        private int _armed;
        private int _thrown;

        public bool Threw => Volatile.Read(ref _thrown) == 1;

        /// <summary>Called by the handler, so only the worker's calls after it can fail.</summary>
        public void Arm() => Volatile.Write(ref _armed, 1);

        public override ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
        {
            ThrowOnce(StoreCall.Get);
            return base.GetAsync(tenantId, jobId, cancellationToken);
        }

        public override ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            ThrowOnce(StoreCall.ListItems);
            return base.ListItemsAsync(jobId, cancellationToken);
        }

        public override ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default)
        {
            ThrowOnce(StoreCall.Complete);
            return base.CompleteAsync(completion, cancellationToken);
        }

        public override ValueTask ReleaseForRetryAsync(Guid jobId, string errorMessage, TimeSpan? retryAfter = null, CancellationToken cancellationToken = default)
        {
            ThrowOnce(StoreCall.ReleaseForRetry);
            return base.ReleaseForRetryAsync(jobId, errorMessage, retryAfter, cancellationToken);
        }

        private void ThrowOnce(StoreCall call)
        {
            if (call == failing
                && Volatile.Read(ref _armed) == 1
                && Interlocked.CompareExchange(ref _thrown, 1, 0) == 0)
            {
                throw new TimeoutException("The store did not answer in time.");
            }
        }
    }

    /// <summary>
    /// Refuses every <see cref="JobStatus.Completed"/> completion made after
    /// <see cref="Arm"/>; a <see cref="JobStatus.Failed"/> one goes through.
    /// </summary>
    private sealed class RefusingCompletionJobStore(TimeProvider? clock = null) : DelegatingJobStore(clock)
    {
        private int _armed;
        private int _refused;

        public int RefusedCompletions => Volatile.Read(ref _refused);

        public void Arm() => Volatile.Write(ref _armed, 1);

        public override ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default)
        {
            if (completion.Status == JobStatus.Completed && Volatile.Read(ref _armed) == 1)
            {
                Interlocked.Increment(ref _refused);
                throw new TimeoutException("The store did not answer in time.");
            }

            return base.CompleteAsync(completion, cancellationToken);
        }
    }

    /// <summary>
    /// Records the worker's store calls around the moments the cases care about.
    /// Each signal fires AFTER the call it reports has reached the inner store.
    /// </summary>
    private sealed class ObservingJobStore : DelegatingJobStore
    {
        private readonly TaskCompletionSource _readAfterMarkRunning = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _markedRunning;

        /// <summary>Cancels the job just before the worker marks it running.</summary>
        public bool CancelBeforeMarkRunning { get; init; }

        /// <summary>The worker's first read after marking the job running failed.</summary>
        public Task ReadAfterMarkRunning => _readAfterMarkRunning.Task;

        public override async ValueTask<bool> MarkRunningAsync(Guid jobId, string owner, CancellationToken cancellationToken = default)
        {
            if (CancelBeforeMarkRunning)
            {
                await Inner.CancelAsync(TenantId, jobId, cancellationToken);
            }

            var marked = await base.MarkRunningAsync(jobId, owner, cancellationToken);

            _markedRunning = true;

            return marked;
        }

        public override async ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
        {
            var record = await base.GetAsync(tenantId, jobId, cancellationToken);

            if (_markedRunning)
            {
                _readAfterMarkRunning.TrySetResult();
            }

            return record;
        }
    }
}
