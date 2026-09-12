using System.Diagnostics.Metrics;
using Tracon.Core.UnitTests.Fakes;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Quotas;

/// <summary>
/// Validates the phase 35 contract of the <c>tracon.quota.usage</c>/
/// <c>tracon.quota.limit</c> observable gauges: disabled by default; when
/// enabled, the value matches the quota record; and consecutive polls within
/// the cache interval do not hit the database.
/// </summary>
public sealed class QuotaUsageObserverTests
{
    private const string Tenant = "acme";
    private const string Agent = "support";

    [Fact]
    public void No_measurement_is_produced_and_the_store_is_not_queried_while_disabled()
    {
        var tenants = new InMemoryTenantStore();
        var store = new CountingQuotaStore(new InMemoryQuotaStore());

        using var meterFactory = new TestMeterFactory();

        using var observer = new QuotaUsageObserver(
            store,
            tenants,
            Options(new TraconOptions { Observability = new TraconObservabilityOptions { EnableQuotaUsageGauge = false } }),
            Options(new TraconQuotaOptions()),
            meterFactory);

        using var collector = new GaugeCollector(meterFactory.Meter);

        collector.Trigger(TraconDiagnostics.QuotaUsageGaugeName).ShouldBeEmpty();
        collector.Trigger(TraconDiagnostics.QuotaLimitGaugeName).ShouldBeEmpty();

        // While EnableQuotaUsageGauge is disabled, the cache is NEVER touched:
        // neither the tenant list nor the quota counters are queried.
        store.ListCalls.ShouldBe(0);
        store.UsageCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Value_matches_the_quota_record_when_enabled()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero));
        var (store, tenants) = await SeedAsync(clock, maxRuns: 100, maxTokens: 5000);

        await RecordUsageAsync(store, clock, runs: 3, tokens: 400);

        using var meterFactory = new TestMeterFactory();

        using var observer = new QuotaUsageObserver(
            store,
            tenants,
            Options(new TraconOptions { Observability = new TraconObservabilityOptions { EnableQuotaUsageGauge = true } }),
            Options(new TraconQuotaOptions()),
            meterFactory,
            timeProvider: clock);

        using var collector = new GaugeCollector(meterFactory.Meter);

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
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero));
        var (store, tenants) = await SeedAsync(clock, maxRuns: 10, enabled: false);
        await RecordUsageAsync(store, clock, runs: 5);

        using var meterFactory = new TestMeterFactory();

        using var observer = new QuotaUsageObserver(
            store,
            tenants,
            Options(new TraconOptions { Observability = new TraconObservabilityOptions { EnableQuotaUsageGauge = true } }),
            Options(new TraconQuotaOptions()),
            meterFactory,
            timeProvider: clock);

        using var collector = new GaugeCollector(meterFactory.Meter);

        collector.Trigger(TraconDiagnostics.QuotaUsageGaugeName).ShouldBeEmpty();
    }

    [Fact]
    public async Task Ten_consecutive_polls_produce_one_database_query()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero));
        var (inner, tenants) = await SeedAsync(clock, maxRuns: 10);
        var store = new CountingQuotaStore(inner);

        using var meterFactory = new TestMeterFactory();

        using var observer = new QuotaUsageObserver(
            store,
            tenants,
            Options(new TraconOptions
            {
                Observability = new TraconObservabilityOptions
                {
                    EnableQuotaUsageGauge = true,
                    QuotaUsageRefreshInterval = TimeSpan.FromSeconds(30),
                },
            }),
            Options(new TraconQuotaOptions()),
            meterFactory,
            timeProvider: clock);

        using var collector = new GaugeCollector(meterFactory.Meter);

        for (var i = 0; i < 10; i++)
        {
            collector.Trigger(TraconDiagnostics.QuotaUsageGaugeName);
        }

        store.UsageCalls.ShouldBe(1);

        // The cache interval elapsed: the next poll must produce a NEW query.
        clock.Advance(TimeSpan.FromSeconds(31));
        collector.Trigger(TraconDiagnostics.QuotaUsageGaugeName);

        store.UsageCalls.ShouldBe(2);
    }

    [Fact]
    public async Task Tag_set_does_not_carry_an_unbounded_field()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero));
        var (store, tenants) = await SeedAsync(clock, maxRuns: 10);
        await RecordUsageAsync(store, clock, runs: 1);

        using var meterFactory = new TestMeterFactory();

        using var observer = new QuotaUsageObserver(
            store,
            tenants,
            Options(new TraconOptions { Observability = new TraconObservabilityOptions { EnableQuotaUsageGauge = true } }),
            Options(new TraconQuotaOptions()),
            meterFactory,
            timeProvider: clock);

        using var collector = new GaugeCollector(meterFactory.Meter);

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

    private static StaticOptionsMonitor<T> Options<T>(T value) => new(value);

    private static QuotaMetric Metric((double Value, Dictionary<string, object?> Tags) measurement)
        => Enum.Parse<QuotaMetric>((string)measurement.Tags[TraconDiagnostics.Tags.QuotaMetric]!);

    private static string? Tag((double Value, Dictionary<string, object?> Tags) measurement, string key)
        => measurement.Tags.GetValueOrDefault(key) as string;

    private static async Task<(IQuotaStore Store, ITenantStore Tenants)> SeedAsync(
        ManualTimeProvider clock,
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

        await store.SaveAsync(new QuotaDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            AgentName = Agent,
            Period = QuotaPeriod.Daily,
            MaxRuns = maxRuns,
            MaxTokens = maxTokens,
            MaxCost = maxCost,
            Enabled = enabled,
            CreatedAt = clock.GetUtcNow(),
            UpdatedAt = clock.GetUtcNow(),
        });

        return (store, tenants);
    }

    private static async Task RecordUsageAsync(
        IQuotaStore store,
        ManualTimeProvider clock,
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

    /// <summary><see cref="IQuotaStore"/> decorator that tracks call counts.</summary>
    private sealed class CountingQuotaStore(IQuotaStore inner) : IQuotaStore
    {
        public int ListCalls { get; private set; }

        public int UsageCalls { get; private set; }

        public ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            ListCalls++;

            return inner.ListAsync(tenantId, cancellationToken);
        }

        public ValueTask<QuotaDefinition?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => inner.GetAsync(tenantId, id, cancellationToken);

        public ValueTask<QuotaDefinition> SaveAsync(QuotaDefinition definition, CancellationToken cancellationToken = default)
            => inner.SaveAsync(definition, cancellationToken);

        public ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(tenantId, id, cancellationToken);

        public ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(QuotaUsageQuery query, CancellationToken cancellationToken = default)
        {
            UsageCalls++;

            return inner.GetUsageAsync(query, cancellationToken);
        }

        public ValueTask AddUsageAsync(
            QuotaConsumption consumption,
            IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
            CancellationToken cancellationToken = default)
            => inner.AddUsageAsync(consumption, periodStarts, cancellationToken);

        public ValueTask<bool> TryClaimThresholdNotificationAsync(
            string tenantId,
            string agentName,
            QuotaPeriod period,
            DateOnly periodStart,
            QuotaMetric metric,
            int thresholdPercent,
            CancellationToken cancellationToken = default)
            => inner.TryClaimThresholdNotificationAsync(tenantId, agentName, period, periodStart, metric, thresholdPercent, cancellationToken);
    }

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
