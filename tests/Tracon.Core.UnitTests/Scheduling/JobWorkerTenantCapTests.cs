using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// The job worker hands the store the per-tenant ceiling its source names, and leases in plain age
/// order when there is none.
/// </summary>
/// <remarks>
/// The ceiling itself is the store's contract (<c>JobStoreContract</c>, on every store). These cases are the
/// other half: that the worker asks for it, on every lease, from the source it was given.
/// </remarks>
public sealed class JobWorkerTenantCapTests
{
    [Fact]
    public async Task The_worker_does_not_lease_a_second_job_of_a_tenant_at_the_ceiling_its_source_names()
    {
        var jobs = new RecordingJobStore();
        var handler = new HoldingHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var now = DateTimeOffset.UtcNow;
        var first = await EnqueueAsync(jobs, "tenant-a", now.AddMinutes(-30));
        var held = await EnqueueAsync(jobs, "tenant-a", now.AddMinutes(-20));
        var other = await EnqueueAsync(jobs, "tenant-b", now.AddMinutes(-10));

        using var worker = CreateWorker(jobs, host, new FixedCap(new JobTenantCap { HandlerKeys = [JobHandlerKeys.AgentBatch], MaxLeasedPerTenant = 1 }));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        // Tenant A's oldest job and tenant B's job run; A's second job is older than B's and is skipped.
        await WaitUntil.TrueAsync(() => handler.Started.Count >= 2, "the two eligible jobs to start");

        // A lease that found nothing is the positive signal that the loop passed over the held job.
        await WaitUntil.TrueAsync(() => jobs.EmptyLeases >= 1, "the worker to ask again and find nothing eligible");

        handler.Started.ToArray().ShouldBe([first, other], ignoreOrder: true);
        handler.Started.ShouldNotContain(held);

        handler.ReleaseAll();
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Every_lease_the_worker_makes_carries_the_ceiling()
    {
        var jobs = new RecordingJobStore();
        var handler = new HoldingHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));
        var cap = new JobTenantCap { HandlerKeys = [JobHandlerKeys.AgentBatch], MaxLeasedPerTenant = 3 };

        using var worker = CreateWorker(jobs, host, new FixedCap(cap));

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => jobs.Requests.Count >= 3, "the worker to poll a few times");
        await worker.StopAsync(TestContext.Current.CancellationToken);

        jobs.Requests.ShouldAllBe(request => ReferenceEquals(request.TenantCap, cap));
    }

    [Fact]
    public async Task With_no_source_the_worker_leases_without_a_ceiling_in_age_order()
    {
        var jobs = new RecordingJobStore();
        var handler = new HoldingHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var now = DateTimeOffset.UtcNow;
        var first = await EnqueueAsync(jobs, "tenant-a", now.AddMinutes(-30));
        var second = await EnqueueAsync(jobs, "tenant-a", now.AddMinutes(-20));

        using var worker = CreateWorker(jobs, host, capSource: null);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => handler.Started.Count >= 2, "both jobs of the tenant to start");

        handler.Started.ToArray().ShouldBe([first, second], ignoreOrder: true);
        jobs.Requests.ShouldAllBe(static request => request.TenantCap == null);

        handler.ReleaseAll();
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_ceiling_set_in_the_scheduling_options_keeps_one_tenants_burst_from_filling_every_slot()
    {
        // Four slots, one lane. Tenant A queued six jobs, all older than the one tenant B queued last. In plain age order
        // A's first four fill the slots and B waits for one of them to end.
        var jobs = new RecordingJobStore();
        var handler = new HoldingHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));
        var options = new TraconSchedulingOptions { MaxLeasedPerTenant = 2 };
        options.TenantCapHandlerKeys.Add(JobHandlerKeys.AgentBatch);

        var now = DateTimeOffset.UtcNow;
        var burst = new List<Guid>();

        for (var index = 0; index < 6; index++)
        {
            burst.Add(await EnqueueAsync(jobs, "tenant-a", now.AddMinutes(-60 + index)));
        }

        var other = await EnqueueAsync(jobs, "tenant-b", now.AddMinutes(-10));

        using var worker = CreateWorker(jobs, host, options);

        await worker.StartAsync(TestContext.Current.CancellationToken);

        // B's job is leased although four of A's older jobs were ready first; A holds two, which is its ceiling.
        await WaitUntil.TrueAsync(() => handler.Started.Count >= 3, "the ceiling's two jobs of tenant A and the job of tenant B to start");
        await WaitUntil.TrueAsync(() => jobs.EmptyLeases >= 1, "the worker to ask again and find nothing eligible");

        handler.Started.ShouldContain(other);
        handler.Started.Count(burst.Contains).ShouldBe(2, "tenant A held more jobs than its ceiling while a slot stood free");
        jobs.Requests.ShouldAllBe(static request => request.TenantCap != null && request.TenantCap.MaxLeasedPerTenant == 2);

        // The ceiling holds jobs back; it does not drop them.
        handler.ReleaseAll();
        await WaitUntil.TrueAsync(() => handler.Started.Count >= 7, "the rest of tenant A's jobs to run once its slots were free");

        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Without_the_setting_one_tenants_burst_fills_the_slots_in_age_order()
    {
        var jobs = new RecordingJobStore();
        var handler = new HoldingHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var now = DateTimeOffset.UtcNow;
        var burst = new List<Guid>();

        for (var index = 0; index < 6; index++)
        {
            burst.Add(await EnqueueAsync(jobs, "tenant-a", now.AddMinutes(-60 + index)));
        }

        var other = await EnqueueAsync(jobs, "tenant-b", now.AddMinutes(-10));

        using var worker = CreateWorker(jobs, host, new TraconSchedulingOptions());

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => handler.Started.Count >= 4, "the four slots to fill");

        // The pre-existing order: nothing asks for a ceiling, and B waits behind A's four.
        handler.Started.ToArray().ShouldBe(burst.Take(4), ignoreOrder: true);
        handler.Started.ShouldNotContain(other);
        jobs.Requests.ShouldAllBe(static request => request.TenantCap == null);

        handler.ReleaseAll();
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    private static JobWorkerBackgroundService CreateWorker(IJobStore jobs, TestJobHandlerHost host, TraconSchedulingOptions settings)
        => CreateWorker(jobs, host, JobTenantBindings.Create(capSource: null, hostScope: null, settings));

    private static JobWorkerBackgroundService CreateWorker(IJobStore jobs, TestJobHandlerHost host, IJobTenantCapSource? capSource)
        => CreateWorker(jobs, host, new JobTenantBindings(capSource, HostScope: null));

    private static JobWorkerBackgroundService CreateWorker(IJobStore jobs, TestJobHandlerHost host, JobTenantBindings tenancy)
        => new(
            jobs,
            new InMemoryJobScheduleStore(),
            host.Registry,
            host.Scopes,
            new StaticOptionsMonitor<TraconSchedulingOptions>(new TraconSchedulingOptions
            {
                MaxConcurrentJobs = 4,
                PollInterval = TimeSpan.FromMilliseconds(5),
                LeaseDuration = TimeSpan.FromMinutes(5),
                MaxAttempts = 1,
            }),
            new SchemaReadyGate([]),
            new NotDraining(),
            logger: NullLogger<JobWorkerBackgroundService>.Instance,
            tenancy: tenancy);

    private static async Task<Guid> EnqueueAsync(IJobStore jobs, string tenantId, DateTimeOffset scheduledFor)
    {
        var id = Guid.NewGuid();

        await jobs.EnqueueAsync(
            new JobRecord
            {
                Id = id,
                TenantId = tenantId,
                HandlerKey = JobHandlerKeys.AgentBatch,
                TargetName = "tenant-cap",
                Status = JobStatus.Pending,
                ScheduledFor = scheduledFor,
                CreatedAt = scheduledFor,
            },
            [],
            TestContext.Current.CancellationToken);

        return id;
    }

    private sealed class FixedCap(JobTenantCap cap) : IJobTenantCapSource
    {
        public JobTenantCap? Current => cap;
    }

    private sealed class NotDraining : ITraconDrainState
    {
        public bool IsDraining => false;
    }

    /// <summary>Holds every job it is given until released, and remembers which ones it started.</summary>
    private sealed class HoldingHandler : IJobHandler
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<Guid> Started { get; } = new();

        public void ReleaseAll() => _release.TrySetResult();

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Started.Enqueue(context.Job.Id);
            await _release.Task.ConfigureAwait(false);
        }
    }

    /// <summary>An in-memory store that remembers each lease request and how many found nothing.</summary>
    private sealed class RecordingJobStore : IJobStore
    {
        private readonly InMemoryJobStore _inner = new();
        private int _emptyLeases;

        public ConcurrentQueue<JobLeaseRequest> Requests { get; } = new();

        public int EmptyLeases => Volatile.Read(ref _emptyLeases);

        public async ValueTask<JobRecord?> LeaseAsync(JobLeaseRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request);

            var job = await _inner.LeaseAsync(request, cancellationToken).ConfigureAwait(false);

            if (job is null)
            {
                Interlocked.Increment(ref _emptyLeases);
            }

            return job;
        }

        public ValueTask<JobRecord> EnqueueAsync(JobRecord job, IReadOnlyList<string> items, CancellationToken cancellationToken = default)
            => _inner.EnqueueAsync(job, items, cancellationToken);

        public ValueTask<bool> RenewLeaseAsync(JobLease lease, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
            => _inner.RenewLeaseAsync(lease, leaseDuration, cancellationToken);

        public ValueTask<bool> MarkRunningAsync(JobLease lease, CancellationToken cancellationToken = default)
            => _inner.MarkRunningAsync(lease, cancellationToken);

        public ValueTask<bool> CompleteAsync(JobLease lease, JobCompletion completion, CancellationToken cancellationToken = default)
            => _inner.CompleteAsync(lease, completion, cancellationToken);

        public ValueTask<bool> ReleaseForRetryAsync(JobLease lease, string errorMessage, TimeSpan? retryAfter = null, CancellationToken cancellationToken = default)
            => _inner.ReleaseForRetryAsync(lease, errorMessage, retryAfter, cancellationToken);

        public ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => _inner.CancelAsync(tenantId, jobId, cancellationToken);

        public ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => _inner.GetAsync(tenantId, jobId, cancellationToken);

        public ValueTask<IReadOnlyList<JobRecord>> QueryAsync(JobQuery query, CancellationToken cancellationToken = default)
            => _inner.QueryAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default)
            => _inner.ListItemsAsync(jobId, cancellationToken);

        public ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default)
            => _inner.ReportItemAsync(item, cancellationToken);

        public ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(CancellationToken cancellationToken = default)
            => _inner.GetQueueDepthAsync(cancellationToken);
    }
}
