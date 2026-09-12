using System.Diagnostics.Metrics;
using Tracon.Core.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// Phase 133: what <c>tracon.job.executions</c> and
/// <c>tracon.job.duration</c> count, and what they deliberately do not.
/// </summary>
public sealed class JobMetricsTests
{
    [Fact]
    public async Task A_completed_job_is_counted_once_with_its_lane_kind_and_status()
    {
        using var harness = new WorkerHarness(new CompletingHandler());
        await harness.EnqueueAsync(lane: "media");

        await harness.RunUntilAsync(static tags => tags.Count > 0);

        var measurement = harness.Counter.ShouldHaveSingleItem();
        measurement.Value.ShouldBe(1);
        measurement.Tags[TraconDiagnostics.Tags.Lane].ShouldBe("media");
        measurement.Tags[TraconDiagnostics.Tags.JobHandlerKey].ShouldBe(JobHandlerKeys.AgentBatch);
        measurement.Tags[TraconDiagnostics.Tags.JobStatus].ShouldBe(nameof(JobStatus.Completed));
        measurement.Tags[TraconDiagnostics.Tags.TenantId].ShouldBe("tenant-a");
    }

    [Fact]
    public async Task The_duration_histogram_carries_no_tenant_tag()
    {
        using var harness = new WorkerHarness(new CompletingHandler());
        await harness.EnqueueAsync();

        await harness.RunUntilAsync(static tags => tags.Count > 0);

        var measurement = harness.Histogram.ShouldHaveSingleItem();
        measurement.Tags.ShouldNotContainKey(TraconDiagnostics.Tags.TenantId);
        measurement.Tags[TraconDiagnostics.Tags.Lane].ShouldBe(JobLanes.Default);
    }

    [Fact]
    public async Task A_release_for_retry_is_not_counted_only_the_final_failure_is()
    {
        // MaxAttempts 3 with a handler that always throws: the job is released
        // for retry twice and fails on the third attempt. The counter must read
        // 1, not 3 -- it counts jobs that FINISHED, not things that happened.
        using var harness = new WorkerHarness(
            new ThrowingHandler(),
            options => options.MaxAttempts = 3);

        await harness.EnqueueAsync();

        await harness.RunUntilAsync(static tags => tags.Count > 0);

        var measurement = harness.Counter.ShouldHaveSingleItem();
        measurement.Value.ShouldBe(1);
        measurement.Tags[TraconDiagnostics.Tags.JobStatus].ShouldBe(nameof(JobStatus.Failed));

        // And the job really did burn every attempt before it was counted.
        var jobs = await harness.Jobs.QueryAsync(new JobQuery { TenantId = "tenant-a" });
        jobs[0].Attempt.ShouldBe(3);
    }

    [Fact]
    public async Task A_job_with_no_registered_handler_is_counted_as_failed()
    {
        // The handler-missing path finalizes the job as Failed without ever
        // entering a handler; it is terminal, so it is counted like any other.
        using var harness = new WorkerHarness(handler: null);
        await harness.EnqueueAsync();

        await harness.RunUntilAsync(static tags => tags.Count > 0);

        harness.Counter.ShouldHaveSingleItem()
            .Tags[TraconDiagnostics.Tags.JobStatus].ShouldBe(nameof(JobStatus.Failed));
    }

    [Fact]
    public async Task A_job_cancelled_mid_flight_is_counted_as_cancelled()
    {
        var handler = new CancellingHandler();
        using var harness = new WorkerHarness(handler);
        var job = await harness.EnqueueAsync();

        handler.CancelWith(harness.Jobs, job.TenantId, job.Id);

        await harness.RunUntilAsync(static tags => tags.Count > 0);

        harness.Counter.ShouldHaveSingleItem()
            .Tags[TraconDiagnostics.Tags.JobStatus].ShouldBe(nameof(JobStatus.Cancelled));
    }

    [Fact]
    public async Task The_duration_comes_from_the_monotonic_clock_not_the_wall_clock()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));

        // The handler advances the monotonic clock by 5 s and simultaneously
        // drags the WALL clock an hour backwards -- an NTP correction mid-job.
        var handler = new ClockMovingHandler(clock, forward: TimeSpan.FromSeconds(5), rewind: TimeSpan.FromHours(1));

        using var harness = new WorkerHarness(handler, clock: clock);
        await harness.EnqueueAsync();

        await harness.RunUntilAsync(static tags => tags.Count > 0);

        var measurement = harness.Histogram.ShouldHaveSingleItem();
        measurement.Value.ShouldBe(5, tolerance: 0.001);
        measurement.Value.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_throwing_metric_listener_does_not_stop_the_job_from_completing()
    {
        using var factory = new TestMeterFactory();

        // A MeterListener callback runs SYNCHRONOUSLY inside Counter.Add. If the
        // worker did not guard the write, this exception would escape into
        // ExecuteJobAsync AFTER the store was already told the job finished.
        using var saboteur = new ThrowingListener(factory.Meter);

        var jobs = new InMemoryJobStore();
        var now = DateTimeOffset.UtcNow;

        var job = await jobs.EnqueueAsync(
            new JobRecord
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-a",
                HandlerKey = JobHandlerKeys.AgentBatch,
                TargetName = "target",
                Status = JobStatus.Pending,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            TestContext.Current.CancellationToken);

        using var handlers = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, new CompletingHandler()));

        using var worker = new JobWorkerBackgroundService(
            jobs,
            new InMemoryJobScheduleStore(),
            handlers.Registry,
            handlers.Scopes,
            new StaticOptionsMonitor<TraconSchedulingOptions>(new TraconSchedulingOptions
            {
                MaxConcurrentJobs = 1,
                PollInterval = TimeSpan.FromMilliseconds(5),
            }),
            new SchemaReadyGate([]),
            new NotDrainingState(),
            logger: NullLogger<JobWorkerBackgroundService>.Instance,
            metrics: new TraconMetrics(factory));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        JobRecord? current = null;

        while (DateTime.UtcNow < deadline)
        {
            current = await jobs.GetAsync(job.TenantId, job.Id);

            if (current is { Status: JobStatus.Completed })
            {
                break;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await worker.StopAsync(TestContext.Current.CancellationToken);

        // Observability must not break functionality: the job is Completed even
        // though every measurement write threw.
        current.ShouldNotBeNull();
        current.Status.ShouldBe(JobStatus.Completed);
        saboteur.Threw.ShouldBeTrue("the listener never ran, so the test proved nothing");
    }

    /// <summary>A listener whose measurement callback always throws.</summary>
    private sealed class ThrowingListener : IDisposable
    {
        private readonly MeterListener _listener = new();

        public ThrowingListener(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((_, _, _, _) =>
            {
                Threw = true;
                throw new InvalidOperationException("simulated exporter failure");
            });

            _listener.Start();
        }

        public bool Threw { get; private set; }

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>Drives a real <see cref="JobWorkerBackgroundService"/> and collects what it publishes.</summary>
    private sealed class WorkerHarness : IDisposable
    {
        private readonly TestMeterFactory _meterFactory = new();
        private readonly TaggedCollector _collector;
        private readonly TestJobHandlerHost _handlers;
        private readonly JobWorkerBackgroundService _worker;
        private readonly ManualTimeProvider? _clock;

        public WorkerHarness(
            IJobHandler? handler,
            Action<TraconSchedulingOptions>? configure = null,
            ManualTimeProvider? clock = null)
        {
            _clock = clock;

            // The store must share the worker's clock: leasing compares
            // ScheduledFor against it, and a job stamped on the fake clock is
            // not yet due on the real one.
            Jobs = new InMemoryJobStore(clock);
            _collector = new TaggedCollector(_meterFactory.Meter);

            var schedulingOptions = new TraconSchedulingOptions
            {
                MaxConcurrentJobs = 1,
                PollInterval = TimeSpan.FromMilliseconds(5),
                LeaseDuration = TimeSpan.FromMinutes(5),
            };

            configure?.Invoke(schedulingOptions);

            _handlers = handler is null
                ? TestJobHandlerHost.For()
                : TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

            _worker = new JobWorkerBackgroundService(
                Jobs,
                new InMemoryJobScheduleStore(),
                _handlers.Registry,
                _handlers.Scopes,
                new StaticOptionsMonitor<TraconSchedulingOptions>(schedulingOptions),
                new SchemaReadyGate([]),
                new NotDrainingState(),
                timeProvider: clock,
                logger: NullLogger<JobWorkerBackgroundService>.Instance,
                metrics: new TraconMetrics(_meterFactory));
        }

        public InMemoryJobStore Jobs { get; }

        public List<Measured> Counter => _collector.Named(TraconDiagnostics.JobCounterName);

        public List<Measured> Histogram => _collector.Named(TraconDiagnostics.JobDurationName);

        public async Task<JobRecord> EnqueueAsync(string lane = JobLanes.Default)
        {
            var now = _clock?.GetUtcNow() ?? DateTimeOffset.UtcNow;

            return await Jobs.EnqueueAsync(
                new JobRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = "tenant-a",
                    HandlerKey = JobHandlerKeys.AgentBatch,
                    TargetName = "target",
                    Lane = lane,
                    Status = JobStatus.Pending,
                    ScheduledFor = now,
                    CreatedAt = now,
                },
                [],
                TestContext.Current.CancellationToken);
        }

        /// <summary>Starts the worker and stops it once <paramref name="until"/> holds.</summary>
        /// <param name="until">The condition, checked against the counter measurements collected so far.</param>
        /// <returns>The completion task.</returns>
        public async Task RunUntilAsync(Func<List<Measured>, bool> until)
        {
            await _worker.StartAsync(TestContext.Current.CancellationToken);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

            while (DateTime.UtcNow < deadline && !until(Counter))
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            await _worker.StopAsync(TestContext.Current.CancellationToken);

            until(Counter).ShouldBeTrue("the worker did not publish the expected measurement in time");
        }

        public void Dispose()
        {
            _worker.Dispose();
            _handlers.Dispose();
            _collector.Dispose();
            _meterFactory.Dispose();
        }
    }

    private sealed record Measured(double Value, Dictionary<string, object?> Tags);

    /// <summary>Collects both <c>long</c> and <c>double</c> measurements together with their tags.</summary>
    private sealed class TaggedCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, Measured Measurement)> _measurements = [];
        private readonly Lock _gate = new();

        public TaggedCollector(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument.Name, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument.Name, value, tags));

            _listener.Start();
        }

        public List<Measured> Named(string name)
        {
            lock (_gate)
            {
                return [.. _measurements
                    .Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))
                    .Select(static m => m.Measurement)];
            }
        }

        public void Dispose() => _listener.Dispose();

        private void Add(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                map[tag.Key] = tag.Value;
            }

            lock (_gate)
            {
                _measurements.Add((name, new Measured(value, map)));
            }
        }
    }

    private sealed class CompletingHandler : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default) => default;
    }

    private sealed class ThrowingHandler : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
            => throw new TraconException("simulated handler failure");
    }

    /// <summary>Cancels its own job through the store before finishing, so the worker sees a Cancelled record.</summary>
    private sealed class CancellingHandler : IJobHandler
    {
        private IJobStore? _store;
        private string? _tenantId;
        private Guid _jobId;

        public void CancelWith(IJobStore store, string tenantId, Guid jobId)
        {
            _store = store;
            _tenantId = tenantId;
            _jobId = jobId;
        }

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            if (_store is not null && _tenantId is not null)
            {
                await _store.CancelAsync(_tenantId, _jobId, cancellationToken);
            }
        }
    }

    /// <summary>Moves the monotonic clock forward and the wall clock backwards while the job runs.</summary>
    private sealed class ClockMovingHandler(ManualTimeProvider clock, TimeSpan forward, TimeSpan rewind) : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            clock.Advance(forward);
            clock.Advance(-rewind);

            return default;
        }
    }

    private sealed class NotDrainingState : ITraconDrainState
    {
        public bool IsDraining => false;
    }
}
