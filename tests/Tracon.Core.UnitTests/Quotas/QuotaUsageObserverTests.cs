using System.Diagnostics.Metrics;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Quotas;

/// <summary>
/// Validates the contract of the <c>tracon.quota.usage</c>/<c>tracon.quota.limit</c>
/// observable gauges: disabled by default and then silent; when enabled, the
/// value matches the quota record; a poll only reads the cache and never waits
/// for the store; and only a refresh-timer tick queries the store.
/// </summary>
/// <remarks>
/// Every enabled test waits for the first refresh before it polls. Before that
/// refresh the gauge publishes nothing by design, so a test that polled at once
/// would prove nothing about the value.
/// </remarks>
public sealed class QuotaUsageObserverTests
{
    private const string Tenant = "acme";
    private const string Agent = "support";

    private static readonly DateTimeOffset Start = new(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task No_measurement_is_produced_and_the_store_is_not_queried_while_disabled()
    {
        var clock = new TriggerableTimeProvider(Start);
        var tenants = new InMemoryTenantStore();
        var store = new CountingQuotaStore(new InMemoryQuotaStore());

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, tenants, enabled: false, meterFactory, clock);
        using var collector = new GaugeCollector(meterFactory.Meter);

        await observer.StartAsync(TestContext.Current.CancellationToken);

        // Off means off: the refresher returns at once and creates no timer.
        await observer.ExecuteTask!.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
        clock.ActiveTimerCount.ShouldBe(0);

        collector.Trigger(TraconDiagnostics.QuotaUsageGaugeName).ShouldBeEmpty();
        collector.Trigger(TraconDiagnostics.QuotaLimitGaugeName).ShouldBeEmpty();

        // Neither the tenant list nor the quota counters are queried.
        store.ListCalls.ShouldBe(0);
        store.UsageCalls.ShouldBe(0);
        observer.CompletedRefreshes.ShouldBe(0);
    }

    /// <summary>
    /// A refresh reads only the current periods' counters (F-275), not the
    /// tenant's whole usage history.
    /// </summary>
    [Fact]
    public async Task A_refresh_reads_only_the_current_periods()
    {
        var clock = new TriggerableTimeProvider(Start);
        var (inner, tenants) = await SeedAsync(clock, maxRuns: 10);
        var store = new CountingQuotaStore(inner);

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, tenants, enabled: true, meterFactory, clock);

        await StartAndWaitForRefreshAsync(observer);

        store.UsageQueries.ShouldNotBeEmpty();
        store.UsageQueries.ShouldAllBe(query =>
            query.PeriodStarts != null
            && query.PeriodStarts[QuotaPeriod.Daily] == DateOnly.FromDateTime(Start.UtcDateTime)
            && query.PeriodStarts[QuotaPeriod.Monthly] == new DateOnly(2026, 8, 1));
    }

    [Fact]
    public async Task Value_matches_the_quota_record_when_enabled()
    {
        var clock = new TriggerableTimeProvider(Start);
        var (store, tenants) = await SeedAsync(clock, maxRuns: 100, maxTokens: 5000);

        await RecordUsageAsync(store, clock, runs: 3, tokens: 400);

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, tenants, enabled: true, meterFactory, clock);
        using var collector = new GaugeCollector(meterFactory.Meter);

        await StartAndWaitForRefreshAsync(observer);

        var usage = collector.Trigger(TraconDiagnostics.QuotaUsageGaugeName);
        var limit = collector.Trigger(TraconDiagnostics.QuotaLimitGaugeName);

        usage.Count.ShouldBe(2); // Runs + Tokens

        var runsUsage = usage.Single(m => Metric(m) == QuotaMetric.Runs);
        runsUsage.Value.ShouldBe(3.0);
        Tag(runsUsage, TraconDiagnostics.Tags.TenantId).ShouldBe(Tenant);
        Tag(runsUsage, TraconDiagnostics.Tags.QuotaScope).ShouldBe(Agent);
        Tag(runsUsage, TraconDiagnostics.Tags.QuotaPeriod).ShouldBe(nameof(QuotaPeriod.Daily));

        var tokensUsage = usage.Single(m => Metric(m) == QuotaMetric.Tokens);
        tokensUsage.Value.ShouldBe(400.0);

        var runsLimit = limit.Single(m => Metric(m) == QuotaMetric.Runs);
        runsLimit.Value.ShouldBe(100.0);

        var tokensLimit = limit.Single(m => Metric(m) == QuotaMetric.Tokens);
        tokensLimit.Value.ShouldBe(5000.0);
    }

    [Fact]
    public async Task Disabled_rule_does_not_appear_in_the_gauge()
    {
        var clock = new TriggerableTimeProvider(Start);
        var (store, tenants) = await SeedAsync(clock, maxRuns: 10, enabled: false);
        await SaveRuleAsync(store, clock, agentName: null, maxRuns: 50);
        await RecordUsageAsync(store, clock, runs: 5);

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, tenants, enabled: true, meterFactory, clock);
        using var collector = new GaugeCollector(meterFactory.Meter);

        await StartAndWaitForRefreshAsync(observer);

        // The enabled tenant-wide rule proves the refresh ran and published;
        // the disabled agent rule next to it must not appear.
        var measurement = collector.Trigger(TraconDiagnostics.QuotaUsageGaugeName).ShouldHaveSingleItem();

        Tag(measurement, TraconDiagnostics.Tags.QuotaScope).ShouldBe(string.Empty);
        measurement.Value.ShouldBe(5.0);
    }

    [Fact]
    public async Task Polls_read_the_cache_and_only_a_timer_tick_queries_the_store()
    {
        var clock = new TriggerableTimeProvider(Start);
        var (inner, tenants) = await SeedAsync(clock, maxRuns: 10);
        var store = new CountingQuotaStore(inner);

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, tenants, enabled: true, meterFactory, clock);
        using var collector = new GaugeCollector(meterFactory.Meter);

        await StartAndWaitForRefreshAsync(observer);
        store.UsageCalls.ShouldBe(1, "the refresher reads once at start");

        for (var i = 0; i < 10; i++)
        {
            collector.Trigger(TraconDiagnostics.QuotaUsageGaugeName);
        }

        store.UsageCalls.ShouldBe(1, "a poll only reads the cache");

        // The refresh interval elapsed: the timer tick produces ONE new query.
        clock.TriggerAll();
        await WaitUntil.TrueAsync(() => observer.CompletedRefreshes >= 2);

        store.UsageCalls.ShouldBe(2);
    }

    [Fact]
    public async Task A_new_value_appears_after_the_next_refresh_and_not_before()
    {
        var clock = new TriggerableTimeProvider(Start);
        var (store, tenants) = await SeedAsync(clock, maxRuns: 10);
        await RecordUsageAsync(store, clock, runs: 1);

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, tenants, enabled: true, meterFactory, clock);
        using var collector = new GaugeCollector(meterFactory.Meter);

        await StartAndWaitForRefreshAsync(observer);

        await RecordUsageAsync(store, clock, runs: 2);

        // The published value is at most one interval old: still the cache.
        collector.Trigger(TraconDiagnostics.QuotaUsageGaugeName).ShouldHaveSingleItem().Value.ShouldBe(1.0);

        clock.TriggerAll();
        await WaitUntil.TrueAsync(() => observer.CompletedRefreshes >= 2);

        collector.Trigger(TraconDiagnostics.QuotaUsageGaugeName).ShouldHaveSingleItem().Value.ShouldBe(3.0);
    }

    [Fact]
    public async Task Tag_set_does_not_carry_an_unbounded_field()
    {
        var clock = new TriggerableTimeProvider(Start);
        var (store, tenants) = await SeedAsync(clock, maxRuns: 10);
        await RecordUsageAsync(store, clock, runs: 1);

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, tenants, enabled: true, meterFactory, clock);
        using var collector = new GaugeCollector(meterFactory.Meter);

        await StartAndWaitForRefreshAsync(observer);

        var measurement = collector.Trigger(TraconDiagnostics.QuotaUsageGaugeName).ShouldHaveSingleItem();

        // 🚨 An unbounded-cardinality field like experiment_id/variant/run.id
        // must NEVER be added HERE (K-146).
        measurement.Tags.Keys.ShouldBe(
            [
                TraconDiagnostics.Tags.TenantId,
                TraconDiagnostics.Tags.QuotaScope,
                TraconDiagnostics.Tags.QuotaPeriod,
                TraconDiagnostics.Tags.QuotaMetric,
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task A_poll_never_waits_for_the_store()
    {
        var clock = new TriggerableTimeProvider(Start);
        var (inner, tenants) = await SeedAsync(clock, maxRuns: 10);
        var store = new CountingQuotaStore(inner) { Stall = new(TaskCreationOptions.RunContinuationsAsynchronously) };

        using var meterFactory = new TestMeterFactory();
        using var observer = Observer(store, tenants, enabled: true, meterFactory, clock);
        using var collector = new GaugeCollector(meterFactory.Meter);

        await observer.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            // The collection thread serves every instrument of the MeterProvider.
            // A store that never answers must not hold it: the poll reads the
            // cache only, and no refresh has finished yet.
            var poll = Task.Run(
                () => collector.Trigger(TraconDiagnostics.QuotaUsageGaugeName),
                TestContext.Current.CancellationToken);

            (await poll.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken))
                .ShouldBeEmpty();

            // The stopping token reaches the store, so a stuck refresh does not
            // hold the host's shutdown either.
            await WaitUntil.TrueAsync(() => store.UsageCalls >= 1);
            await observer.StopAsync(TestContext.Current.CancellationToken);

            observer.ExecuteTask!.IsCompletedSuccessfully.ShouldBeTrue();
        }
        finally
        {
            store.Stall.TrySetResult();
        }
    }

    private static QuotaUsageObserver Observer(
        IQuotaStore store,
        ITenantStore tenants,
        bool enabled,
        IMeterFactory meterFactory,
        TimeProvider clock)
        => new(
            store,
            tenants,
            new StaticOptionsMonitor<TraconOptions>(new TraconOptions
            {
                Observability = new TraconObservabilityOptions
                {
                    EnableQuotaUsageGauge = enabled,
                    QuotaUsageRefreshInterval = TimeSpan.FromSeconds(30),
                },
            }),
            new StaticOptionsMonitor<TraconQuotaOptions>(new TraconQuotaOptions()),
            new SchemaReadyGate([]),
            meterFactory,
            timeProvider: clock);

    private static async Task StartAndWaitForRefreshAsync(QuotaUsageObserver observer)
    {
        await observer.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => observer.CompletedRefreshes >= 1);
    }

    private static QuotaMetric Metric((double Value, Dictionary<string, object?> Tags) measurement)
        => Enum.Parse<QuotaMetric>((string)measurement.Tags[TraconDiagnostics.Tags.QuotaMetric]!);

    private static string? Tag((double Value, Dictionary<string, object?> Tags) measurement, string key)
        => measurement.Tags.GetValueOrDefault(key) as string;

    private static async Task<(IQuotaStore Store, ITenantStore Tenants)> SeedAsync(
        TimeProvider clock,
        long? maxRuns = null,
        long? maxTokens = null,
        decimal? maxCost = null,
        bool enabled = true)
    {
        var store = new InMemoryQuotaStore();
        var tenants = new InMemoryTenantStore();

        await tenants.SaveAsync(new TenantDescriptor
        {
            Id = TraconId.NewId(),
            Slug = Tenant,
            DisplayName = "Acme",
        });

        await SaveRuleAsync(store, clock, Agent, maxRuns, maxTokens, maxCost, enabled);

        return (store, tenants);
    }

    private static async Task SaveRuleAsync(
        IQuotaStore store,
        TimeProvider clock,
        string? agentName,
        long? maxRuns = null,
        long? maxTokens = null,
        decimal? maxCost = null,
        bool enabled = true)
        => await store.SaveAsync(new QuotaDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            AgentName = agentName,
            Period = QuotaPeriod.Daily,
            MaxRuns = maxRuns,
            MaxTokens = maxTokens,
            MaxCost = maxCost,
            Enabled = enabled,
            CreatedAt = clock.GetUtcNow(),
            UpdatedAt = clock.GetUtcNow(),
        });

    private static async Task RecordUsageAsync(
        IQuotaStore store,
        TimeProvider clock,
        long runs = 0,
        long tokens = 0,
        decimal? cost = null)
    {
        var occurredAt = clock.GetUtcNow();
        var periodStarts = QuotaPeriodCalculator.GetAllPeriodStarts(occurredAt, TimeZoneInfo.Utc);

        await store.AddUsageAsync(
            new QuotaConsumption
            {
                TenantId = Tenant,
                AgentName = Agent,
                Runs = runs,
                Tokens = tokens,
                Cost = cost,
                OccurredAt = occurredAt,
            },
            periodStarts);
    }

    /// <summary>
    /// <see cref="IQuotaStore"/> decorator that tracks call counts. The refresh
    /// runs on a background task, so the counters are read and written atomically.
    /// When <see cref="Stall"/> is set, a counter read waits for it.
    /// </summary>
    /// <summary>
    /// Simple listener that collects a given <c>Meter</c>'s ObservableGauge
    /// measurements by calling <c>RecordObservableInstruments</c> on each
    /// trigger.
    /// </summary>
    private sealed class GaugeCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, double Value, Dictionary<string, object?> Tags)> _measurements = [];

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

            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                _measurements.Add((instrument.Name, value, ToDictionary(tags))));

            _listener.Start();
        }

        /// <summary>Polls the observable instruments and returns the measurements with the given name.</summary>
        public List<(double Value, Dictionary<string, object?> Tags)> Trigger(string name)
        {
            _measurements.Clear();
            _listener.RecordObservableInstruments();

            return [.. _measurements
                .Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))
                .Select(m => (m.Value, m.Tags))];
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
