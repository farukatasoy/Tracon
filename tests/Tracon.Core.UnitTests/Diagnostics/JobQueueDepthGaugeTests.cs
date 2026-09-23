using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// Phase 133.4: the <c>tracon.job.queue.depth</c> gauge is off by default,
/// caches its reads, carries no tenant tag, and never breaks the worker when
/// the store fails. A scrape only reads the cache; a background refresh on a
/// timer queries the store.
/// </summary>
/// <remarks>
/// Every enabled test waits for the first refresh before it scrapes. Before
/// that refresh the gauge publishes nothing by design, so a test that scraped
/// at once would prove nothing about the depth.
/// </remarks>
public sealed class JobQueueDepthGaugeTests
{
    [Fact]
    public async Task No_measurement_is_produced_and_the_store_is_not_queried_while_disabled()
    {
        var clock = new TriggerableTimeProvider();
        var store = new CountingJobStore();
        await EnqueueAsync(store, lane: "default", count: 1);

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, enabled: false, meterFactory, clock);
        using var collector = new GaugeCollector(meterFactory.Meter);

        await observer.StartAsync(TestContext.Current.CancellationToken);

        // Default off means DEFAULT OFF: the refresher returns at once, creates
        // no timer, and not one query reaches the store.
        await observer.ExecuteTask!.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
        clock.ActiveTimerCount.ShouldBe(0);

        collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName).ShouldBeEmpty();
        store.DepthCalls.ShouldBe(0);
        observer.CompletedRefreshes.ShouldBe(0);
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

        await StartAndWaitForRefreshAsync(observer);

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

        await StartAndWaitForRefreshAsync(observer);

        var measurements = collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName);

        // Queue depth is an operator signal about a pool that leases across
        // every tenant. A tenant tag would multiply cardinality AND imply a
        // tenant boundary that does not exist here.
        var single = measurements.ShouldHaveSingleItem();
        single.Tags.ShouldNotContainKey(TraconDiagnostics.Tags.TenantId);
        single.Value.ShouldBe(2);
    }

    [Fact]
    public async Task Scrapes_read_the_cache_and_only_a_timer_tick_queries_the_store()
    {
        var clock = new TriggerableTimeProvider();
        var store = new CountingJobStore();
        await EnqueueAsync(store, lane: "default", count: 1);

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, enabled: true, meterFactory, clock);
        using var collector = new GaugeCollector(meterFactory.Meter);

        await StartAndWaitForRefreshAsync(observer);
        store.DepthCalls.ShouldBe(1, "the refresher reads once at start");

        collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName);
        collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName);
        store.DepthCalls.ShouldBe(1, "a scrape only reads the cache");

        clock.TriggerAll();
        await WaitUntil.TrueAsync(() => observer.CompletedRefreshes >= 2);

        store.DepthCalls.ShouldBe(2, "one timer tick is exactly one refresh");
    }

    [Fact]
    public async Task A_failing_depth_query_is_swallowed_and_the_gauge_stays_readable()
    {
        var store = new ThrowingJobStore();

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, enabled: true, meterFactory);
        using var collector = new GaugeCollector(meterFactory.Meter);

        await StartAndWaitForRefreshAsync(observer);

        // Observability must not break functionality: the refresher survives
        // the store's failure, and the scrape returns empty rather than
        // propagating it to the collector.
        observer.ExecuteTask!.IsCompleted.ShouldBeFalse("a failed refresh must not end the refresher");
        Should.NotThrow(() => collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task A_transient_failure_keeps_the_previous_value_instead_of_dropping_to_zero()
    {
        var clock = new TriggerableTimeProvider();
        var store = new CountingJobStore();
        await EnqueueAsync(store, lane: "default", count: 4);

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, enabled: true, meterFactory, clock);
        using var collector = new GaugeCollector(meterFactory.Meter);

        await StartAndWaitForRefreshAsync(observer);

        collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName)
            .ShouldHaveSingleItem().Value.ShouldBe(4);

        store.NextFailure = new TraconException("simulated store outage");
        clock.TriggerAll();
        await WaitUntil.TrueAsync(() => observer.CompletedRefreshes >= 2);

        // A blip must not read as "the queue drained" on a dashboard.
        collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName)
            .ShouldHaveSingleItem().Value.ShouldBe(4);
    }

    [Fact]
    public async Task A_cancellation_that_is_not_the_hosts_is_a_failure_and_the_refresher_keeps_running()
    {
        var clock = new TriggerableTimeProvider();
        var store = new CountingJobStore();
        await EnqueueAsync(store, lane: "default", count: 2);

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, enabled: true, meterFactory, clock);
        using var collector = new GaugeCollector(meterFactory.Meter);

        await StartAndWaitForRefreshAsync(observer);

        // A store written over HTTP reports its client timeout as a
        // TaskCanceledException. The host is not stopping, so this is an
        // ordinary failure: the refresher must log it and keep going, not end.
        store.NextFailure = new TaskCanceledException("simulated client timeout");
        clock.TriggerAll();
        await WaitUntil.TrueAsync(() => observer.CompletedRefreshes >= 2);

        observer.ExecuteTask!.IsCompleted.ShouldBeFalse("a foreign cancellation must not end the refresher");

        await EnqueueAsync(store, lane: "default", count: 1);
        clock.TriggerAll();
        await WaitUntil.TrueAsync(() => observer.CompletedRefreshes >= 3);

        collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName)
            .ShouldHaveSingleItem().Value.ShouldBe(3);
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
            new SchemaReadyGate([]),
            meterFactory,
            timeProvider: new TriggerableTimeProvider(),
            logger: NullLogger<JobQueueDepthObserver>.Instance,
            metrics: metrics);

        using var collector = new GaugeCollector(meterFactory.Meter);

        await StartAndWaitForRefreshAsync(observer);

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

    [Fact]
    public async Task A_scrape_never_waits_for_the_store()
    {
        var clock = new TriggerableTimeProvider();
        var store = new CountingJobStore { Stall = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await EnqueueAsync(store, lane: "default", count: 1);

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, enabled: true, meterFactory, clock: clock);
        using var collector = new GaugeCollector(meterFactory.Meter);

        await observer.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            // The scrape thread serves every instrument of the MeterProvider. A
            // depth query that never answers must not hold it: the scrape reads
            // the cache only, and no refresh has finished yet.
            var scrape = Task.Run(
                () => collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName),
                TestContext.Current.CancellationToken);

            (await scrape.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken))
                .ShouldBeEmpty();

            // The stopping token reaches the store, so a stuck refresh does not
            // hold the host's shutdown either.
            await WaitUntil.TrueAsync(() => store.DepthCalls >= 1);
            await observer.StopAsync(TestContext.Current.CancellationToken);

            observer.ExecuteTask!.IsCompletedSuccessfully.ShouldBeTrue();
        }
        finally
        {
            store.Stall.TrySetResult();
        }
    }

    [Fact]
    public async Task The_first_query_waits_until_the_schema_is_ready()
    {
        var clock = new TriggerableTimeProvider();
        var store = new CountingJobStore();
        await EnqueueAsync(store, lane: "default", count: 1);

        // A SQL provider is registered, so the gate stays closed until the
        // migration runner marks the schema ready.
        var gate = new SchemaReadyGate([new SqlPersistenceRegistrationMarker("SQLite")]);

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, enabled: true, meterFactory, clock, gate);
        using var collector = new GaugeCollector(meterFactory.Meter);

        await observer.StartAsync(TestContext.Current.CancellationToken);

        // 🚨 The host starts this refresher BEFORE a persistence provider
        // registered later in the chain has migrated. No query and no timer
        // may exist until the gate opens.
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken); // delay: negative
        store.DepthCalls.ShouldBe(0);
        clock.ActiveTimerCount.ShouldBe(0);

        gate.MarkReady();
        await WaitUntil.TrueAsync(() => observer.CompletedRefreshes >= 1);

        store.DepthCalls.ShouldBe(1);
        collector.Trigger(TraconDiagnostics.JobQueueDepthGaugeName).ShouldHaveSingleItem().Value.ShouldBe(1);
    }

    private static JobQueueDepthObserver Observer(
        IJobStore store,
        bool enabled,
        IMeterFactory meterFactory,
        TimeProvider? clock = null,
        SchemaReadyGate? gate = null)
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
            gate ?? new SchemaReadyGate([]),
            meterFactory,
            // A timer that fires only on request: no real 30 s tick can add a
            // refresh the test did not ask for.
            timeProvider: clock ?? new TriggerableTimeProvider(),
            logger: NullLogger<JobQueueDepthObserver>.Instance);

    private static async Task StartAndWaitForRefreshAsync(JobQueueDepthObserver observer)
    {
        await observer.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => observer.CompletedRefreshes >= 1);
    }

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

    /// <summary>
    /// Wraps the in-memory store and counts the depth queries it receives. The
    /// refresh runs on a background task, so the counter is read and written
    /// atomically. <see cref="NextFailure"/> fails the next query once; when
    /// <see cref="Stall"/> is set, a depth query waits for it.
    /// </summary>
    private sealed class CountingJobStore : DelegatingJobStore
    {
        private int _depthCalls;
        private Exception? _nextFailure;

        public int DepthCalls => Volatile.Read(ref _depthCalls);

        public Exception? NextFailure
        {
            get => Volatile.Read(ref _nextFailure);
            set => Volatile.Write(ref _nextFailure, value);
        }

        public TaskCompletionSource? Stall { get; init; }

        public override ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _depthCalls);

            if (Interlocked.Exchange(ref _nextFailure, null) is { } failure)
            {
                throw failure;
            }

            return Stall is { } stall
                ? StalledDepthAsync(stall, cancellationToken)
                : Inner.GetQueueDepthAsync(cancellationToken);
        }

        private async ValueTask<IReadOnlyList<JobQueueDepth>> StalledDepthAsync(
            TaskCompletionSource stall,
            CancellationToken cancellationToken)
        {
            await stall.Task.WaitAsync(cancellationToken);

            return await Inner.GetQueueDepthAsync(cancellationToken);
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
