using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// Phase 133.4: the <c>tracon.job.queue.depth</c> gauge is off by default,
/// caches its reads, carries no tenant tag, and never breaks the worker when
/// the store fails.
/// </summary>
public sealed class JobQueueDepthGaugeTests
{
    [Fact]
    public void No_measurement_is_produced_and_the_store_is_not_queried_while_disabled()
    {
        var store = new CountingJobStore();

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, enabled: false, meterFactory);
        using var collector = new GaugeCollector(meterFactory.Meter);

        collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName).ShouldBeEmpty();

        // Default off means DEFAULT OFF: not one query reaches the store.
        store.DepthCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Depth_is_reported_per_lane_and_status_when_enabled()
    {
        var store = new CountingJobStore();
        await EnqueueAsync(store, lane: "default", count: 3);
        await EnqueueAsync(store, lane: "media", count: 1);

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, enabled: true, meterFactory);
        using var collector = new GaugeCollector(meterFactory.Meter);

        var measurements = collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName);

        Value(measurements, "default", JobStatus.Pending).ShouldBe(3);
        Value(measurements, "media", JobStatus.Pending).ShouldBe(1);
    }

    [Fact]
    public async Task The_gauge_carries_no_tenant_tag()
    {
        var store = new CountingJobStore();
        await EnqueueAsync(store, lane: "default", count: 1, tenantId: "tenant-a");
        await EnqueueAsync(store, lane: "default", count: 1, tenantId: "tenant-b");

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, enabled: true, meterFactory);
        using var collector = new GaugeCollector(meterFactory.Meter);

        var measurements = collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName);

        // Queue depth is an operator signal about a pool that leases across
        // every tenant. A tenant tag would multiply cardinality AND imply a
        // tenant boundary that does not exist here.
        var single = measurements.ShouldHaveSingleItem();
        single.Tags.ShouldNotContainKey(TraconDiagnostics.Tags.TenantId);
        single.Value.ShouldBe(2);
    }

    [Fact]
    public async Task A_second_scrape_inside_the_refresh_interval_does_not_query_the_store()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var store = new CountingJobStore();
        await EnqueueAsync(store, lane: "default", count: 1);

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, enabled: true, meterFactory, clock: clock);
        using var collector = new GaugeCollector(meterFactory.Meter);

        collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName);
        store.DepthCalls.ShouldBe(1);

        clock.Advance(TimeSpan.FromSeconds(10));
        collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName);
        store.DepthCalls.ShouldBe(1, "the 30 s cache window has not elapsed");

        clock.Advance(TimeSpan.FromSeconds(25));
        collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName);
        store.DepthCalls.ShouldBe(2, "the cache went stale, so exactly one refresh happened");
    }

    [Fact]
    public void A_failing_depth_query_is_swallowed_and_the_gauge_stays_readable()
    {
        var store = new ThrowingJobStore();

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, enabled: true, meterFactory);
        using var collector = new GaugeCollector(meterFactory.Meter);

        // Observability must not break functionality: the scrape returns empty
        // rather than propagating the store's failure to the collector.
        Should.NotThrow(() => collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task A_transient_failure_keeps_the_previous_value_instead_of_dropping_to_zero()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var store = new CountingJobStore();
        await EnqueueAsync(store, lane: "default", count: 4);

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, enabled: true, meterFactory, clock: clock);
        using var collector = new GaugeCollector(meterFactory.Meter);

        collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName)
            .ShouldHaveSingleItem().Value.ShouldBe(4);

        store.FailNextRefresh = true;
        clock.Advance(TimeSpan.FromMinutes(1));

        // A blip must not read as "the queue drained" on a dashboard.
        collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName)
            .ShouldHaveSingleItem().Value.ShouldBe(4);
    }

    [Fact]
    public async Task The_gauge_applies_the_same_lane_cardinality_guard_as_the_counter()
    {
        var store = new CountingJobStore();
        await EnqueueAsync(store, lane: "alpha", count: 1);
        await EnqueueAsync(store, lane: "beta", count: 1);
        await EnqueueAsync(store, lane: "gamma", count: 1);

        var options = new StaticOptionsMonitor<TraconOptions>(new TraconOptions
        {
            Observability = new TraconObservabilityOptions
            {
                EnableJobQueueDepthGauge = true,
                JobQueueDepthRefreshInterval = TimeSpan.FromSeconds(30),
                MaxJobLaneCardinality = 2,
            },
        });

        using var meterFactory = new TestMeterFactory();
        using var metrics = new TraconMetrics(meterFactory, options);

        // The counter is what names "alpha" and "beta" first and spends the budget.
        metrics.RecordJob("alpha", JobHandlerKeys.AgentBatch, JobStatus.Completed, "t", TimeSpan.Zero);
        metrics.RecordJob("beta", JobHandlerKeys.AgentBatch, JobStatus.Completed, "t", TimeSpan.Zero);

        using var observer = new JobQueueDepthObserver(
            store,
            options,
            meterFactory,
            logger: NullLogger<JobQueueDepthObserver>.Instance,
            metrics: metrics);

        using var collector = new GaugeCollector(meterFactory.Meter);

        var lanes = collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName)
            .Select(static m => (string?)m.Tags.GetValueOrDefault(TraconDiagnostics.Tags.Lane))
            .OrderBy(static lane => lane, StringComparer.Ordinal)
            .ToList();

        // 🚨 The guard is shared, not duplicated. "gamma" is past the limit and
        // must overflow HERE too — otherwise a consumer deriving one lane per
        // user floods the backend through the gauge, and the same lane appears
        // under its own name on one instrument and as "other" on the other.
        lanes.ShouldBe(["alpha", "beta", "other"]);
    }

    private static JobQueueDepthObserver Observer(
        IJobStore store,
        bool enabled,
        IMeterFactory meterFactory,
        ManualTimeProvider? clock = null)
        => new(
            store,
            new StaticOptionsMonitor<TraconOptions>(new TraconOptions
            {
                Observability = new TraconObservabilityOptions
                {
                    EnableJobQueueDepthGauge = enabled,
                    JobQueueDepthRefreshInterval = TimeSpan.FromSeconds(30),
                },
            }),
            meterFactory,
            timeProvider: clock,
            logger: NullLogger<JobQueueDepthObserver>.Instance);

    private static async Task EnqueueAsync(IJobStore store, string lane, int count, string tenantId = "tenant-a")
    {
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < count; index++)
        {
            await store.EnqueueAsync(
                new JobRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
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
    }

    private static long Value(
        List<(long Value, Dictionary<string, object?> Tags)> measurements,
        string lane,
        JobStatus status)
        => measurements
            .Where(m => Equals(m.Tags.GetValueOrDefault(TraconDiagnostics.Tags.Lane), lane)
                && Equals(m.Tags.GetValueOrDefault(TraconDiagnostics.Tags.JobStatus), status.ToString()))
            .Sum(static m => m.Value);

    /// <summary>Wraps the in-memory store and counts the depth queries it receives.</summary>
    private sealed class CountingJobStore : DelegatingJobStore
    {
        public int DepthCalls { get; private set; }

        public bool FailNextRefresh { get; set; }

        public override ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(
            CancellationToken cancellationToken = default)
        {
            DepthCalls++;

            if (FailNextRefresh)
            {
                throw new TraconException("simulated store outage");
            }

            return Inner.GetQueueDepthAsync(cancellationToken);
        }
    }

    private sealed class ThrowingJobStore : DelegatingJobStore
    {
        public override ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(
            CancellationToken cancellationToken = default)
            => throw new TraconException("simulated store outage");
    }

    /// <summary>Forwards every <see cref="IJobStore"/> member to an in-memory store.</summary>
    private abstract class DelegatingJobStore : IJobStore
    {
        protected InMemoryJobStore Inner { get; } = new();

        public ValueTask<JobRecord> EnqueueAsync(JobRecord job, IReadOnlyList<string> items, CancellationToken cancellationToken = default)
            => Inner.EnqueueAsync(job, items, cancellationToken);

        public ValueTask<JobRecord?> LeaseAsync(string owner, TimeSpan leaseDuration, IReadOnlyList<string>? lanes, CancellationToken cancellationToken = default)
            => Inner.LeaseAsync(owner, leaseDuration, lanes, cancellationToken);

        public ValueTask RenewLeaseAsync(Guid jobId, string owner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
            => Inner.RenewLeaseAsync(jobId, owner, leaseDuration, cancellationToken);

        public ValueTask<bool> MarkRunningAsync(Guid jobId, string owner, CancellationToken cancellationToken = default)
            => Inner.MarkRunningAsync(jobId, owner, cancellationToken);

        public ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default)
            => Inner.CompleteAsync(completion, cancellationToken);

        public ValueTask ReleaseForRetryAsync(Guid jobId, string errorMessage, TimeSpan? retryAfter = null, CancellationToken cancellationToken = default)
            => Inner.ReleaseForRetryAsync(jobId, errorMessage, retryAfter, cancellationToken);

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

        public virtual ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(CancellationToken cancellationToken = default)
            => Inner.GetQueueDepthAsync(cancellationToken);
    }

    /// <summary>Polls observable instruments and returns their <c>long</c> measurements with tags.</summary>
    private sealed class GaugeCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, long Value, Dictionary<string, object?> Tags)> _measurements = [];

        public GaugeCollector(Meter meter)
        {
            // Filtering by the meter's NAME would also catch a different Meter
            // instance with the same name published by a test running in
            // parallel (MeterListenerIsolationTests enforces this).
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                _measurements.Add((instrument.Name, value, ToDictionary(tags))));

            _listener.Start();
        }

        public List<(long Value, Dictionary<string, object?> Tags)> Trigger(string name)
        {
            _measurements.Clear();
            _listener.RecordObservableInstruments();

            return [.. _measurements
                .Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))
                .Select(static m => (m.Value, m.Tags))];
        }

        public void Dispose() => _listener.Dispose();

        private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                result[tag.Key] = tag.Value;
            }

            return result;
        }
    }
}
