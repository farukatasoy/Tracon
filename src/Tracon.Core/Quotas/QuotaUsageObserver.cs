using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Cached data source for the <c>tracon.quota.usage</c>/<c>tracon.quota.limit</c>
/// observable gauges.
/// </summary>
/// <remarks>
/// <para>
/// The gauge callbacks only read the last snapshot; a background refresh reads
/// the database every
/// <see cref="TraconObservabilityOptions.QuotaUsageRefreshInterval"/>. A poll
/// never waits for the database, and consecutive polls issue no query. The
/// published value is therefore at most one interval old, and the first poll
/// after start can be empty. See <see cref="CachedGaugeSource{TSample}"/>.
/// </para>
/// <para>
/// As long as <see cref="TraconObservabilityOptions.EnableQuotaUsageGauge"/>
/// is <see langword="false"/>, the database is never queried — the instrument
/// names still exist (so the gauge can be turned on without changing the
/// consumer's OTel configuration), but no measurement is published.
/// </para>
/// <para>
/// <strong>Known limitation:</strong> tenant registration (<see cref="ITenantStore"/>)
/// is not mandatory; this gauge only scans REGISTERED tenants. A quota rule for
/// an unregistered tenant is still enforced correctly by
/// <see cref="QuotaEnforcer"/>, it simply does NOT APPEAR on this dashboard.
/// This is an accepted limitation: reporting them would need its own table and
/// endpoint, which this gauge deliberately avoids.
/// </para>
/// </remarks>
internal sealed class QuotaUsageObserver : CachedGaugeSource<QuotaUsageObserver.QuotaGaugeSample>
{
    private readonly IQuotaStore _quotaStore;
    private readonly ITenantStore _tenantStore;
    private readonly IOptionsMonitor<TraconQuotaOptions> _quotaOptions;
    private readonly ILogger<QuotaUsageObserver>? _logger;
    private readonly Meter _meter;
    private readonly bool _ownsMeter;

    /// <summary>Creates a new quota gauge.</summary>
    /// <param name="quotaStore">The quota rule and counter store.</param>
    /// <param name="tenantStore">The store of registered tenants.</param>
    /// <param name="options">
    /// The root type that <see cref="TraconOptions.Observability"/>, which
    /// carries the gauge on/off and refresh interval settings, hangs off.
    /// <see cref="TraconObservabilityOptions"/> is NOT registered standalone
    /// anywhere with <c>services.Configure&lt;TraconObservabilityOptions&gt;</c>
    /// — it is only reached through <see cref="TraconOptions.Observability"/>.
    /// </param>
    /// <param name="quotaOptions">The time-zone setting for the quota period calculation.</param>
    /// <param name="schemaReadyGate">The gate the first database read waits on.</param>
    /// <param name="meterFactory">
    /// The meter factory. If <see langword="null"/>, a private <see cref="Meter"/>
    /// instance is created and owned by this object.
    /// </param>
    /// <param name="timeProvider">The time source. If <see langword="null"/>, <see cref="TimeProvider.System"/>.</param>
    /// <param name="logger">The logger that refresh errors are logged to.</param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public QuotaUsageObserver(
        IQuotaStore quotaStore,
        ITenantStore tenantStore,
        IOptionsMonitor<TraconOptions> options,
        IOptionsMonitor<TraconQuotaOptions> quotaOptions,
        SchemaReadyGate schemaReadyGate,
        IMeterFactory? meterFactory = null,
        TimeProvider? timeProvider = null,
        ILogger<QuotaUsageObserver>? logger = null)
        : base(options, schemaReadyGate, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(quotaStore);
        ArgumentNullException.ThrowIfNull(tenantStore);
        ArgumentNullException.ThrowIfNull(quotaOptions);

        _quotaStore = quotaStore;
        _tenantStore = tenantStore;
        _quotaOptions = quotaOptions;
        _logger = logger;

        if (meterFactory is null)
        {
            _meter = new Meter(TraconDiagnostics.MeterName);
            _ownsMeter = true;
        }
        else
        {
            _meter = meterFactory.Create(TraconDiagnostics.MeterName);
        }

        _meter.CreateObservableGauge(
            TraconDiagnostics.QuotaUsageGaugeName,
            ObserveUsage,
            description: "The quota scope's consumption in the current period.");

        _meter.CreateObservableGauge(
            TraconDiagnostics.QuotaLimitGaugeName,
            ObserveLimit,
            description: "The quota scope's defined limits.");
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_ownsMeter)
        {
            _meter.Dispose();
        }

        base.Dispose();
    }

    /// <inheritdoc />
    protected override bool IsEnabled(TraconObservabilityOptions options)
        => options.EnableQuotaUsageGauge;

    /// <inheritdoc />
    protected override TimeSpan GetRefreshInterval(TraconObservabilityOptions options)
        => options.QuotaUsageRefreshInterval;

    /// <inheritdoc />
    protected override async ValueTask<IReadOnlyList<QuotaGaugeSample>> ReadAsync(CancellationToken cancellationToken)
    {
        var tenants = await _tenantStore.ListAsync(cancellationToken).ConfigureAwait(false);
        var timeZone = _quotaOptions.CurrentValue.ResolveTimeZone();
        var now = Clock.GetUtcNow();
        var samples = new List<QuotaGaugeSample>();

        foreach (var tenant in tenants)
        {
            var definitions = await _quotaStore.ListAsync(tenant.Slug, cancellationToken).ConfigureAwait(false);

            if (definitions.Count == 0)
            {
                continue;
            }

            var usage = await _quotaStore
                .GetUsageAsync(
                    new QuotaUsageQuery
                    {
                        TenantId = tenant.Slug,
                        PeriodStarts = QuotaPeriodCalculator.GetAllPeriodStarts(now, timeZone),
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (var definition in definitions)
            {
                if (!definition.Enabled)
                {
                    continue;
                }

                // A tenant-wide rule reads the counter with an empty name;
                // a rule scoped to an agent reads its own agent's counter
                // (same contract as QuotaEnforcer.Evaluate).
                var scope = definition.AgentName ?? string.Empty;
                var periodStart = QuotaPeriodCalculator.GetPeriodStart(now, definition.Period, timeZone);

                var current = usage.FirstOrDefault(record =>
                    string.Equals(record.AgentName, scope, StringComparison.Ordinal)
                    && record.Period == definition.Period
                    && record.PeriodStart == periodStart);

                if (current is null)
                {
                    continue;
                }

                foreach (var (metric, limit, used) in QuotaEnforcer.EnumerateLimits(definition, current))
                {
                    samples.Add(new QuotaGaugeSample(tenant.Slug, scope, definition.Period, metric, used, limit));
                }
            }
        }

        return samples;
    }

    /// <inheritdoc />
    protected override void LogRefreshFailure(Exception exception)
    {
        if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(exception, "Could not refresh the quota gauge cache; keeping the previous value.");
        }
    }

    private IEnumerable<Measurement<double>> ObserveUsage()
        => Snapshot().Select(static sample => new Measurement<double>((double)sample.Used, BuildTags(sample)));

    private IEnumerable<Measurement<double>> ObserveLimit()
        => Snapshot().Select(static sample => new Measurement<double>((double)sample.Limit, BuildTags(sample)));

    private static KeyValuePair<string, object?>[] BuildTags(QuotaGaugeSample sample) =>
    [
        new(TraconDiagnostics.Tags.TenantId, sample.TenantId),
        new(TraconDiagnostics.Tags.QuotaScope, sample.Scope),
        new(TraconDiagnostics.Tags.QuotaPeriod, sample.Period.ToString()),
        new(TraconDiagnostics.Tags.QuotaMetric, sample.Metric.ToString()),
    ];

    /// <summary>One cached gauge sample: a limit of one quota scope and its consumption.</summary>
    /// <param name="TenantId">The tenant.</param>
    /// <param name="Scope">The agent name, or empty for a tenant-wide rule.</param>
    /// <param name="Period">The quota period.</param>
    /// <param name="Metric">The limited metric.</param>
    /// <param name="Used">The consumption in the current period.</param>
    /// <param name="Limit">The limit.</param>
    internal readonly record struct QuotaGaugeSample(
        string TenantId,
        string Scope,
        QuotaPeriod Period,
        QuotaMetric Metric,
        decimal Used,
        decimal Limit);
}
