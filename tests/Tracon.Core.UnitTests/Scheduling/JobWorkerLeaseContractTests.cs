using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// The lease, attempt and cancel contract <see cref="IJobHandler"/> and
/// <see cref="TraconSchedulingOptions.MaxAttempts"/> promise, held by
/// <see cref="JobWorkerBackgroundService"/>.
/// </summary>
/// <remarks>
/// 🚨 F-326: all three broke on the worker, not in a store. Renewal stopped for
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
        (await jobs.LeaseAsync("dead-worker", TimeSpan.FromMinutes(1), lanes: null, TestContext.Current.CancellationToken))
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
    [InlineData(1)]
    [InlineData(3)]
    public async Task A_cancelled_job_whose_handler_throws_stays_cancelled(int maxAttempts)
    {
        var jobs = new ObservingJobStore();
        var handler = new CancelThenThrowHandler(jobs);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        // The worker's next store call after the throw decides the outcome: a
        // completion or a release overwrites the cancel; a read leaves it.
        await jobs.AfterHandlerThrew.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        var record = await jobs.GetAsync(TenantId, jobId, TestContext.Current.CancellationToken);

        await worker.StopAsync(TestContext.Current.CancellationToken);

        record!.Status.ShouldBe(JobStatus.Cancelled);
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

    private static JobWorkerBackgroundService CreateWorker(
        IJobStore jobs,
        TestJobHandlerHost host,
        TimeSpan leaseDuration,
        TimeProvider? clock = null)
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

    private sealed class CountingHandler : IJobHandler
    {
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task Called => _called.Task;

        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            _called.TrySetResult();

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Cancels its own job, as an operator would mid-run, then fails on its way out.</summary>
    private sealed class CancelThenThrowHandler(ObservingJobStore jobs) : IJobHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            (await jobs.CancelAsync(context.Job.TenantId, context.Job.Id, cancellationToken)).ShouldBeTrue();
            (await context.IsCancelledAsync(cancellationToken)).ShouldBeTrue();

            jobs.HandlerThrew = true;

            throw new InvalidOperationException("The handler stopped because its job was cancelled.");
        }
    }

    private abstract class DelegatingJobStore : IJobStore
    {
        protected InMemoryJobStore Inner { get; } = new();

        public ValueTask<JobRecord> EnqueueAsync(JobRecord job, IReadOnlyList<string> items, CancellationToken cancellationToken = default)
            => Inner.EnqueueAsync(job, items, cancellationToken);

        public ValueTask<JobRecord?> LeaseAsync(string owner, TimeSpan leaseDuration, IReadOnlyList<string>? lanes, CancellationToken cancellationToken = default)
            => Inner.LeaseAsync(owner, leaseDuration, lanes, cancellationToken);

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

        public ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default)
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
    /// Records the worker's store calls around the moments the cases care about.
    /// Each signal fires AFTER the call it reports has reached the inner store.
    /// </summary>
    private sealed class ObservingJobStore : DelegatingJobStore
    {
        private readonly TaskCompletionSource _afterHandlerThrew = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _readAfterMarkRunning = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _markedRunning;

        /// <summary>Cancels the job just before the worker marks it running.</summary>
        public bool CancelBeforeMarkRunning { get; init; }

        /// <summary>Set by the handler right before it throws.</summary>
        public bool HandlerThrew { get; set; }

        /// <summary>The worker's first completion, release or read after the handler threw.</summary>
        public Task AfterHandlerThrew => _afterHandlerThrew.Task;

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

        public override async ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default)
        {
            await base.CompleteAsync(completion, cancellationToken);
            Signal();
        }

        public override async ValueTask ReleaseForRetryAsync(Guid jobId, string errorMessage, TimeSpan? retryAfter = null, CancellationToken cancellationToken = default)
        {
            await base.ReleaseForRetryAsync(jobId, errorMessage, retryAfter, cancellationToken);
            Signal();
        }

        public override async ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
        {
            var record = await base.GetAsync(tenantId, jobId, cancellationToken);

            Signal();

            if (_markedRunning)
            {
                _readAfterMarkRunning.TrySetResult();
            }

            return record;
        }

        private void Signal()
        {
            if (HandlerThrew)
            {
                _afterHandlerThrew.TrySetResult();
            }
        }
    }
}
