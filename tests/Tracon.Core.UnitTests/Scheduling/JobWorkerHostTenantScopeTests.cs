using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// The job worker opens the host's own tenant scope around every job it runs, and only when the host registered one.
/// </summary>
/// <remarks>
/// <para>
/// The scope is an <c>AsyncLocal</c> the way a framework's current-tenant accessor is, so these cases also show that
/// the value written in the worker's own body reaches the handler and the services built for it, and does not leak to
/// the next job.
/// </para>
/// <para>
/// Two jobs of two tenants are held at the same moment by a barrier the handler waits on, not by a delay.
/// </para>
/// </remarks>
public sealed class JobWorkerHostTenantScopeTests
{
    private const string Key = "test.host-scope";

    [Fact]
    public async Task The_handler_and_the_services_built_for_it_run_inside_the_hosts_scope_of_the_jobs_tenant()
    {
        var events = new ConcurrentQueue<string>();
        var scope = new RecordingHostScope(events);
        using var host = Host(events, expectedJobs: 1);
        var jobs = new InMemoryJobStore();

        var id = await EnqueueAsync(jobs, "tenant-a");

        using var worker = CreateWorker(jobs, host, scope);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        var job = await WaitForSettledAsync(jobs, "tenant-a", id);

        // The scope ends when the execution ends, after the outcome is written.
        await WaitUntil.TrueAsync(() => events.Contains("end:tenant-a", StringComparer.Ordinal), "the host's scope to end");
        await worker.StopAsync(TestContext.Current.CancellationToken);

        job.Status.ShouldBe(JobStatus.Completed, job.ErrorMessage);

        // The scope opens after Tracon's own ambient tenant is set and before the container scope: the host sees the
        // ambient tenant already in place and its own scope empty, and the service built for the handler sees the host's
        // scope open. It ends last, after the handler returned.
        events.ToArray().ShouldBe(
            [
                "begin:tenant-a:entry-host=:ambient=tenant-a",
                "built:tenant-a",
                "handler:host=tenant-a:ambient=tenant-a",
                "end:tenant-a",
            ]);
    }

    [Fact]
    public async Task Two_jobs_of_two_tenants_at_the_same_moment_each_see_their_own_tenant()
    {
        var events = new ConcurrentQueue<string>();
        var scope = new RecordingHostScope(events);
        using var host = Host(events, expectedJobs: 2);
        var jobs = new InMemoryJobStore();

        var first = await EnqueueAsync(jobs, "tenant-a");
        var second = await EnqueueAsync(jobs, "tenant-b");

        using var worker = CreateWorker(jobs, host, scope);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitForSettledAsync(jobs, "tenant-a", first);
        await WaitForSettledAsync(jobs, "tenant-b", second);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        var seen = events.Where(static line => line.StartsWith("handler:", StringComparison.Ordinal)).ToArray();

        seen.ShouldBe(
            ["handler:host=tenant-a:ambient=tenant-a", "handler:host=tenant-b:ambient=tenant-b"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task The_scope_of_a_job_that_ended_is_not_there_when_the_next_job_of_another_tenant_begins()
    {
        var events = new ConcurrentQueue<string>();
        var scope = new RecordingHostScope(events);
        using var host = Host(events, expectedJobs: 1);
        var jobs = new InMemoryJobStore();

        var first = await EnqueueAsync(jobs, "tenant-a");

        using var worker = CreateWorker(jobs, host, scope);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitForSettledAsync(jobs, "tenant-a", first);
        await WaitUntil.TrueAsync(() => events.Contains("end:tenant-a", StringComparer.Ordinal), "the first scope to end");

        var second = await EnqueueAsync(jobs, "tenant-b");

        await WaitForSettledAsync(jobs, "tenant-b", second);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        // The second job began with nothing held by the host: the first job's tenant did not stay behind.
        events.ShouldContain("begin:tenant-b:entry-host=:ambient=tenant-b", StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_scope_that_cannot_be_opened_fails_the_job_before_it_runs_and_the_handler_is_never_called()
    {
        var events = new ConcurrentQueue<string>();
        var scope = new RecordingHostScope(events) { ThrowOnBegin = true };
        using var host = Host(events, expectedJobs: 1);
        var jobs = new InMemoryJobStore();

        var id = await EnqueueAsync(jobs, "tenant-a");

        using var worker = CreateWorker(jobs, host, scope);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        var job = await WaitForSettledAsync(jobs, "tenant-a", id);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        job.Status.ShouldBe(JobStatus.Failed);
        job.ErrorMessage.ShouldNotBeNull().ShouldStartWith(JobErrorCodes.HostTenantScopeFailed, Case.Sensitive);
        job.ErrorMessage.ShouldNotContain("the tenant is unknown to the host", Case.Sensitive);
        events.ShouldBeEmpty("the handler never ran and no scope was opened");
    }

    [Fact]
    public async Task A_host_call_that_times_out_with_a_cancellation_while_the_worker_runs_fails_the_job_like_any_other_error()
    {
        // An HttpClient timeout inside the host's lookup is an OperationCanceledException that the worker's own
        // shutdown did not cause.
        var events = new ConcurrentQueue<string>();
        var scope = new RecordingHostScope(events) { ThrowCancellationOnBegin = true };
        using var host = Host(events, expectedJobs: 1);
        var jobs = new InMemoryJobStore();

        var id = await EnqueueAsync(jobs, "tenant-a");

        using var worker = CreateWorker(jobs, host, scope);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        var job = await WaitForSettledAsync(jobs, "tenant-a", id);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        job.Status.ShouldBe(JobStatus.Failed);
        job.ErrorMessage.ShouldNotBeNull().ShouldStartWith(JobErrorCodes.HostTenantScopeFailed, Case.Sensitive);
    }

    [Fact]
    public async Task A_scope_that_is_cancelled_by_the_workers_own_shutdown_leaves_the_job_to_its_lease()
    {
        var events = new ConcurrentQueue<string>();
        var scope = new RecordingHostScope(events);
        using var host = Host(events, expectedJobs: 1);
        var jobs = new InMemoryJobStore();

        var id = await EnqueueAsync(jobs, "tenant-a");

        using var worker = CreateWorker(jobs, host, scope);

        // Stopping the worker cancels its token at once; the host's Begin then reports the cancellation, the way a call
        // that observes the shutdown does.
        Task? stopping = null;

        scope.BeforeBegin = () => stopping = worker.StopAsync(CancellationToken.None);
        scope.ThrowCancellationOnBegin = true;

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => stopping is not null, "the scope to stop the worker");
        await stopping!.WaitAsync(TestContext.Current.CancellationToken);

        var job = (await jobs.GetAsync("tenant-a", id, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        job.Status.ShouldNotBe(JobStatus.Failed, "a shutdown is not a failure of the host's scope");
        job.Status.ShouldNotBe(JobStatus.Completed);
        events.ShouldBeEmpty("the handler never ran");
    }

    [Fact]
    public async Task A_scope_that_throws_when_it_ends_is_logged_and_does_not_change_the_result_of_the_job()
    {
        var events = new ConcurrentQueue<string>();
        var scope = new RecordingHostScope(events) { ThrowOnDispose = true };
        var logger = new CollectingLogger();
        using var host = Host(events, expectedJobs: 1);
        var jobs = new InMemoryJobStore();

        var id = await EnqueueAsync(jobs, "tenant-a");

        using var worker = CreateWorker(jobs, host, scope, logger);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        var job = await WaitForSettledAsync(jobs, "tenant-a", id);
        await WaitUntil.TrueAsync(() => events.Contains("end:tenant-a", StringComparer.Ordinal), "the host's scope to end");
        await WaitUntil.TrueAsync(
            () => logger.Warnings.Any(static message => message.Contains("tenant scope threw when it ended", StringComparison.Ordinal)),
            "the warning about the scope that failed to end");
        await worker.StopAsync(TestContext.Current.CancellationToken);

        job.Status.ShouldBe(JobStatus.Completed, job.ErrorMessage);
    }

    [Fact]
    public async Task A_host_that_has_no_scope_to_open_for_a_tenant_returns_nothing_and_the_job_runs()
    {
        var events = new ConcurrentQueue<string>();
        var scope = new RecordingHostScope(events) { ReturnNothing = true };
        using var host = Host(events, expectedJobs: 1);
        var jobs = new InMemoryJobStore();

        var id = await EnqueueAsync(jobs, "tenant-a");

        using var worker = CreateWorker(jobs, host, scope);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        var job = await WaitForSettledAsync(jobs, "tenant-a", id);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        job.Status.ShouldBe(JobStatus.Completed, job.ErrorMessage);
        events.ShouldContain("handler:host=:ambient=tenant-a", StringComparer.Ordinal);
    }

    [Fact]
    public async Task Without_a_registration_the_worker_calls_no_scope_and_the_job_runs_as_before()
    {
        var events = new ConcurrentQueue<string>();
        using var host = Host(events, expectedJobs: 1);
        var jobs = new InMemoryJobStore();

        var id = await EnqueueAsync(jobs, "tenant-a");

        using var worker = CreateWorker(jobs, host, scope: null);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        var job = await WaitForSettledAsync(jobs, "tenant-a", id);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        job.Status.ShouldBe(JobStatus.Completed, job.ErrorMessage);
        events.ShouldContain("handler:host=:ambient=tenant-a", StringComparer.Ordinal);
    }

    [Fact]
    public async Task The_host_is_given_the_canonical_tenant_even_when_the_store_hands_back_another_spelling()
    {
        // A third-party IJobStore may return the tenant as it was written. Tracon's own scope holds the canonical form,
        // and the host's has to agree with it.
        var events = new ConcurrentQueue<string>();
        var scope = new RecordingHostScope(events);
        using var host = Host(events, expectedJobs: 1);
        var jobs = new MixedCaseLeaseStore();

        var id = await EnqueueAsync(jobs.Inner, "tenant-a");

        using var worker = CreateWorker(jobs, host, scope);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitForSettledAsync(jobs.Inner, "tenant-a", id);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        events.ShouldContain("begin:tenant-a:entry-host=:ambient=tenant-a", StringComparer.Ordinal);
    }

    private static TestJobHandlerHost Host(ConcurrentQueue<string> events, int expectedJobs)
    {
        var barrier = new JobBarrier(expectedJobs);

        return TestJobHandlerHost.With(
            services =>
            {
                services.AddSingleton(events);
                services.AddSingleton(barrier);
                services.AddScoped(provider =>
                {
                    // Built when the handler's scope is created: the host's scope has to be open by then.
                    provider.GetRequiredService<ConcurrentQueue<string>>().Enqueue($"built:{RecordingHostScope.Current}");

                    return new ScopeProbe();
                });
            },
            (Key, typeof(ProbeHandler)));
    }

    private static JobWorkerBackgroundService CreateWorker(IJobStore jobs, TestJobHandlerHost host, IHostTenantScope? scope, ILogger<JobWorkerBackgroundService>? logger = null)
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
            logger: logger ?? NullLogger<JobWorkerBackgroundService>.Instance,
            tenancy: new JobTenantBindings(CapSource: null, scope));

    private static async Task<Guid> EnqueueAsync(InMemoryJobStore jobs, string tenantId)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);

        await jobs.EnqueueAsync(
            new JobRecord
            {
                Id = id,
                TenantId = tenantId,
                HandlerKey = Key,
                TargetName = "host-scope",
                Status = JobStatus.Pending,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            TestContext.Current.CancellationToken);

        return id;
    }

    private static async Task<JobRecord> WaitForSettledAsync(InMemoryJobStore jobs, string tenantId, Guid id)
        => (await WaitUntil.ValueAsync(
            async () => await jobs.GetAsync(tenantId, id, TestContext.Current.CancellationToken),
            static job => job is { Status: JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled },
            $"job {id} to settle"))!;

    private sealed class NotDraining : ITraconDrainState
    {
        public bool IsDraining => false;
    }

    /// <summary>Holds each job until the expected number of jobs is running at once.</summary>
    private sealed class JobBarrier(int expected)
    {
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _running;

        public async Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _running) >= expected)
            {
                _all.TrySetResult();
            }

            await _all.Task.ConfigureAwait(false);
        }
    }

    private sealed class ScopeProbe;

    private sealed class ProbeHandler(ConcurrentQueue<string> events, JobBarrier barrier, ScopeProbe probe) : IJobHandler
    {
        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(probe);

            await barrier.ArriveAsync().ConfigureAwait(false);

            events.Enqueue($"handler:host={RecordingHostScope.Current}:ambient={AmbientTenantScope.Current}");
        }
    }

    /// <summary>A host scope written the way a framework's current-tenant accessor is: an <c>AsyncLocal</c>.</summary>
    private sealed class RecordingHostScope(ConcurrentQueue<string> events) : IHostTenantScope
    {
        private static readonly AsyncLocal<string?> CurrentTenant = new();

        public static string? Current => CurrentTenant.Value;

        public bool ThrowOnBegin { get; init; }

        public bool ThrowOnDispose { get; init; }

        public bool ReturnNothing { get; init; }

        public bool ThrowCancellationOnBegin { get; set; }

        public Action? BeforeBegin { get; set; }

        public IDisposable? Begin(string tenantId)
        {
            BeforeBegin?.Invoke();

            if (ThrowCancellationOnBegin)
            {
                throw new OperationCanceledException("the host's lookup was cancelled");
            }

            if (ThrowOnBegin)
            {
                throw new InvalidOperationException("the tenant is unknown to the host");
            }

            if (ReturnNothing)
            {
                return null;
            }

            var previous = CurrentTenant.Value;

            CurrentTenant.Value = tenantId;
            events.Enqueue($"begin:{tenantId}:entry-host={previous}:ambient={AmbientTenantScope.Current}");

            return new Restore(previous, tenantId, events, ThrowOnDispose);
        }

        private sealed class Restore(string? previous, string tenantId, ConcurrentQueue<string> events, bool fail) : IDisposable
        {
            public void Dispose()
            {
                CurrentTenant.Value = previous;
                events.Enqueue($"end:{tenantId}");

                if (fail)
                {
                    throw new InvalidOperationException("the host failed to close its scope");
                }
            }
        }
    }

    /// <summary>A job store that leases a job with the tenant spelled differently from the canonical form.</summary>
    private sealed class MixedCaseLeaseStore : IJobStore
    {
        public InMemoryJobStore Inner { get; } = new();

        public async ValueTask<JobRecord?> LeaseAsync(JobLeaseRequest request, CancellationToken cancellationToken = default)
        {
            var job = await Inner.LeaseAsync(request, cancellationToken).ConfigureAwait(false);

            return job is null ? null : job with { TenantId = "Tenant-A" };
        }

        public ValueTask<JobRecord> EnqueueAsync(JobRecord job, IReadOnlyList<string> items, CancellationToken cancellationToken = default)
            => Inner.EnqueueAsync(job, items, cancellationToken);

        public ValueTask<bool> RenewLeaseAsync(JobLease lease, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
            => Inner.RenewLeaseAsync(lease, leaseDuration, cancellationToken);

        public ValueTask<bool> MarkRunningAsync(JobLease lease, CancellationToken cancellationToken = default)
            => Inner.MarkRunningAsync(lease, cancellationToken);

        public ValueTask<bool> CompleteAsync(JobLease lease, JobCompletion completion, CancellationToken cancellationToken = default)
            => Inner.CompleteAsync(lease, completion, cancellationToken);

        public ValueTask<bool> ReleaseForRetryAsync(JobLease lease, string errorMessage, TimeSpan? retryAfter = null, CancellationToken cancellationToken = default)
            => Inner.ReleaseForRetryAsync(lease, errorMessage, retryAfter, cancellationToken);

        public ValueTask<bool> ReleaseInterruptedAsync(JobLease lease, CancellationToken cancellationToken = default)
            => Inner.ReleaseInterruptedAsync(lease, cancellationToken);

        public ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => Inner.CancelAsync(tenantId, jobId, cancellationToken);

        public ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
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

    private sealed class CollectingLogger : ILogger<JobWorkerBackgroundService>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries.Where(static entry => entry.Level == LogLevel.Warning).Select(static entry => entry.Message)];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
