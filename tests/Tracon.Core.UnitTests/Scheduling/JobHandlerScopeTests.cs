using System.Collections.Concurrent;
using Tracon.Core.UnitTests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// Phase 137 §137.3 — the worker resolves its handler from a FRESH scope for
/// every execution.
/// </summary>
/// <remarks>
/// 🚨 The handler is registered by TYPE here, never as an instance. A test that
/// hands the container a ready-made handler cannot observe a per-execution
/// instance at all — it would stay green with the handler resolved once from
/// the root provider, which is exactly the behavior this phase replaced.
/// </remarks>
public sealed class JobHandlerScopeTests
{
    [Fact]
    public async Task Two_jobs_get_two_handler_instances_and_two_scoped_dependencies()
    {
        var probe = new ScopeProbe();
        using var host = TestJobHandlerHost.With(
            services =>
            {
                services.AddSingleton(probe);
                services.AddScoped<ScopedMarker>();
            },
            (JobHandlerKeys.AgentBatch, typeof(RecordingHandler)));

        var jobs = new InMemoryJobStore();
        await EnqueueAsync(jobs, "tenant-a");
        await EnqueueAsync(jobs, "tenant-a");

        using var worker = NewWorker(jobs, host);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await probe.WaitForAsync(2);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        probe.HandlerIds.Distinct().Count().ShouldBe(2, "every execution builds its own handler");
        probe.MarkerIds.Distinct().Count().ShouldBe(2, "every execution gets its own scoped dependency");
    }

    [Fact]
    public async Task A_retry_runs_in_a_new_scope_too()
    {
        var probe = new ScopeProbe { FailFirstAttempt = true };
        using var host = TestJobHandlerHost.With(
            services =>
            {
                services.AddSingleton(probe);
                services.AddScoped<ScopedMarker>();
            },
            (JobHandlerKeys.AgentBatch, typeof(RecordingHandler)));

        var jobs = new InMemoryJobStore();
        await EnqueueAsync(jobs, "tenant-a");

        using var worker = NewWorker(jobs, host);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await probe.WaitForAsync(2);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        // Attempt one threw and was released for retry; attempt two ran. The
        // two attempts are the SAME job, so a per-process handler would report
        // the same instance twice.
        probe.HandlerIds.Distinct().Count().ShouldBe(2);
        probe.MarkerIds.Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task A_handler_whose_dependency_is_missing_fails_the_job_instead_of_re_leasing_forever()
    {
        // No ScopedMarker registration: the container cannot construct the
        // handler. Before Phase 137 this broke the host at startup (the
        // handlers were resolved eagerly); now it has to be caught per
        // execution, or the job never reaches a terminal status.
        using var host = TestJobHandlerHost.With(
            services => services.AddSingleton(new ScopeProbe()),
            (JobHandlerKeys.AgentBatch, typeof(RecordingHandler)));

        var jobs = new InMemoryJobStore();
        var jobId = await EnqueueAsync(jobs, "tenant-a");

        using var worker = NewWorker(jobs, host);

        await worker.StartAsync(TestContext.Current.CancellationToken);

        JobRecord? record = null;

        for (var attempt = 0; attempt < 100 && record?.Status is not JobStatus.Failed; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
            record = await jobs.GetAsync("tenant-a", jobId, TestContext.Current.CancellationToken);
        }

        await worker.StopAsync(TestContext.Current.CancellationToken);

        record.ShouldNotBeNull();
        record.Status.ShouldBe(JobStatus.Failed);
        record.ErrorMessage.ShouldNotBeNull();
        record.ErrorMessage.ShouldContain(JobErrorCodes.HandlerActivationFailed);
    }

    private static JobWorkerBackgroundService NewWorker(InMemoryJobStore jobs, TestJobHandlerHost host)
        => new(
            jobs,
            new InMemoryJobScheduleStore(),
            host.Registry,
            host.Scopes,
            new StaticOptionsMonitor<TraconSchedulingOptions>(new TraconSchedulingOptions
            {
                MaxConcurrentJobs = 1,
                PollInterval = TimeSpan.FromMilliseconds(5),
                MaxAttempts = 2,
            }),
            new SchemaReadyGate([]),
            new NotDrainingState(),
            logger: NullLogger<JobWorkerBackgroundService>.Instance);

    private static async ValueTask<Guid> EnqueueAsync(InMemoryJobStore jobs, string tenantId)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        await jobs.EnqueueAsync(
            new JobRecord
            {
                Id = id,
                TenantId = tenantId,
                HandlerKey = JobHandlerKeys.AgentBatch,
                TargetName = "target",
                Status = JobStatus.Pending,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            TestContext.Current.CancellationToken);

        return id;
    }

    /// <summary>A scoped dependency whose identity tells two scopes apart.</summary>
    private sealed class ScopedMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    /// <summary>Collects what each execution saw, across executions.</summary>
    private sealed class ScopeProbe
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _executions;

        public ConcurrentBag<Guid> HandlerIds { get; } = [];

        public ConcurrentBag<Guid> MarkerIds { get; } = [];

        public bool FailFirstAttempt { get; init; }

        public int Executions => Volatile.Read(ref _executions);

        public void Record(Guid handlerId, Guid markerId, int expected)
        {
            HandlerIds.Add(handlerId);
            MarkerIds.Add(markerId);

            if (Interlocked.Increment(ref _executions) >= expected)
            {
                _reached.TrySetResult();
            }
        }

        public async Task WaitForAsync(int executions)
        {
            _ = executions;
            await _reached.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Reports its own identity and its scoped dependency's, once per execution.</summary>
    private sealed class RecordingHandler(ScopeProbe probe, ScopedMarker marker) : IJobHandler
    {
        private readonly Guid _id = Guid.NewGuid();

        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            var first = probe.Executions == 0;
            probe.Record(_id, marker.Id, 2);

            return probe.FailFirstAttempt && first
                ? throw new TraconException("simulated first-attempt failure")
                : default;
        }
    }

    private sealed class NotDrainingState : ITraconDrainState
    {
        public bool IsDraining => false;
    }
}
