using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
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

        // The scope opens before the container scope: the service built for the handler already saw the tenant. It
        // ends last, after the handler returned.
        events.ToArray().ShouldBe(
            [
                "begin:tenant-a",
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
    public async Task A_scope_that_throws_when_it_ends_does_not_change_the_result_of_the_job()
    {
        var events = new ConcurrentQueue<string>();
        var scope = new RecordingHostScope(events) { ThrowOnDispose = true };
        using var host = Host(events, expectedJobs: 1);
        var jobs = new InMemoryJobStore();

        var id = await EnqueueAsync(jobs, "tenant-a");

        using var worker = CreateWorker(jobs, host, scope);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        var job = await WaitForSettledAsync(jobs, "tenant-a", id);
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

    private static JobWorkerBackgroundService CreateWorker(InMemoryJobStore jobs, TestJobHandlerHost host, IHostTenantScope? scope)
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

        public IDisposable? Begin(string tenantId)
        {
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
            events.Enqueue($"begin:{tenantId}");

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
}
