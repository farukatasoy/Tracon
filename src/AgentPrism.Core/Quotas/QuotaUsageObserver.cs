using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Cached data source for the <c>agentprism.quota.usage</c>/<c>agentprism.quota.limit</c>
/// observable gauges.
/// </summary>
/// <remarks>
/// <para>
/// The <c>ObservableGauge</c> callback is synchronous; a database read CANNOT
/// be done directly inside it. This class compares the cache's age against
/// <see cref="AgentPrismObservabilityOptions.QuotaUsageRefreshInterval"/> on
/// every callback: if the cache is fresh it returns directly, if it is stale it
/// refreshes ONCE (blocking). If consecutive polls fall within this interval,
/// no second database query happens.
/// </para>
/// <para>
/// As long as <see cref="AgentPrismObservabilityOptions.EnableQuotaUsageGauge"/>
/// is <see langword="false"/>, the cache is NEVER touched — the instrument name
/// still exists (so it can be turned on without changing the consumer's OTel
/// configuration), but no measurement is published and the database is never queried.
/// </para>
/// <para>
/// <strong>Known limitation:</strong> tenant registration (<see cref="ITenantStore"/>)
/// is not mandatory; this gauge only scans REGISTERED tenants. A quota rule for
/// an unregistered tenant is still enforced correctly by
/// <see cref="QuotaEnforcer"/>, it simply does NOT APPEAR on this dashboard.
/// This is an accepted limitation given the phase's "no new table/endpoint"
/// goal.
/// </para>
/// </remarks>
public sealed class QuotaUsageObserver : IHostedService, IDisposable
{
    private readonly IQuotaStore _quotaStore;
    private readonly ITenantStore _tenantStore;
    private readonly IOptionsMonitor<AgentPrismOptions> _options;
    private readonly IOptionsMonitor<AgentPrismQuotaOptions> _quotaOptions;
    private readonly TimeProvider _clock;
    private readonly ILogger<QuotaUsageObserver>? _logger;
    private readonly Meter _meter;
    private readonly bool _ownsMeter;
    // SemaphoreSlim: System.Threading.Lock cannot be used because net8.0 is
    // also a target (MA0158 also forbids a separate object field); used with
    // Wait()/Release() on the synchronous code path.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<QuotaGaugeSample> _snapshot = [];
    private DateTimeOffset? _lastRefreshedAt;

    /// <summary>Creates a new quota gauge.</summary>
    /// <param name="quotaStore">The quota rule and counter store.</param>
    /// <param name="tenantStore">The store of registered tenants.</param>
    /// <param name="options">
    /// The root type that <see cref="AgentPrismOptions.Observability"/>, which
    /// carries the gauge on/off and cache interval settings, hangs off.
    /// <see cref="AgentPrismObservabilityOptions"/> is NOT registered standalone
    /// anywhere with <c>services.Configure&lt;AgentPrismObservabilityOptions&gt;</c>
    /// — it is only reached through <see cref="AgentPrismOptions.Observability"/>.
    /// </param>
    /// <param name="quotaOptions">The time-zone setting for the quota period calculation.</param>
    /// <param name="meterFactory">
    /// The meter factory. If <see langword="null"/>, a private <see cref="Meter"/>
    /// instance is created and owned by this object.
    /// </param>
    /// <param name="timeProvider">The time source. If <see langword="null"/>, <see cref="TimeProvider.System"/>.</param>
    /// <param name="logger">The logger that cache-refresh errors are logged to.</param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public QuotaUsageObserver(
        IQuotaStore quotaStore,
        ITenantStore tenantStore,
        IOptionsMonitor<AgentPrismOptions> options,
        IOptionsMonitor<AgentPrismQuotaOptions> quotaOptions,
        IMeterFactory? meterFactory = null,
        TimeProvider? timeProvider = null,
        ILogger<QuotaUsageObserver>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(quotaStore);
        ArgumentNullException.ThrowIfNull(tenantStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(quotaOptions);

        _quotaStore = quotaStore;
        _tenantStore = tenantStore;
        _options = options;
        _quotaOptions = quotaOptions;
        _clock = timeProvider ?? TimeProvider.System;
        _logger = logger;

        if (meterFactory is null)
        {
            _meter = new Meter(AgentPrismDiagnostics.MeterName);
            _ownsMeter = true;
        }
        else
        {
            _meter = meterFactory.Create(AgentPrismDiagnostics.MeterName);
        }

        _meter.CreateObservableGauge(
            AgentPrismDiagnostics.QuotaUsageGaugeName,
            ObserveUsage,
            description: "The quota scope's consumption in the current period.");

        _meter.CreateObservableGauge(
            AgentPrismDiagnostics.QuotaLimitGaugeName,
            ObserveLimit,
            description: "The quota scope's defined limits.");
    }

    /// <summary>
    /// Does nothing extra — the constructor already creates the instruments at
    /// REGISTRATION time. The only purpose of adding this type as an
    /// <see cref="IHostedService"/> is to make the container resolve and
    /// construct this object EARLY (while the host is starting); otherwise the
    /// instruments would never be created unless some consumer happens to resolve it.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc cref="IHostedService.StopAsync(CancellationToken)" />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsMeter)
        {
            _meter.Dispose();
        }

        _gate.Dispose();
    }

    private IEnumerable<Measurement<double>> ObserveUsage()
        => Snapshot().Select(static sample => new Measurement<double>((double)sample.Used, BuildTags(sample)));

    private IEnumerable<Measurement<double>> ObserveLimit()
        => Snapshot().Select(static sample => new Measurement<double>((double)sample.Limit, BuildTags(sample)));

    private IReadOnlyList<QuotaGaugeSample> Snapshot()
    {
        if (!_options.CurrentValue.Observability.EnableQuotaUsageGauge)
        {
            return [];
        }

        _gate.Wait();

        try
        {
            var interval = _options.CurrentValue.Observability.QuotaUsageRefreshInterval;
            var now = _clock.GetUtcNow();

            if (_lastRefreshedAt is null || now - _lastRefreshedAt.Value >= interval)
            {
                // 🚨 The database is reached in a BLOCKING way from a
                // synchronous callback. This is a cost explicitly accepted in
                // this class's own docs: call frequency is at worst
                // QuotaUsageRefreshInterval, and gauge collection typically
                // runs in a separate, low-frequency background task.
                _snapshot = RefreshAsync().GetAwaiter().GetResult();
                _lastRefreshedAt = now;
            }

            return _snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<QuotaGaugeSample>> RefreshAsync()
    {
        try
        {
            var tenants = await _tenantStore.ListAsync().ConfigureAwait(false);
            var timeZone = _quotaOptions.CurrentValue.ResolveTimeZone();
            var now = _clock.GetUtcNow();
            var samples = new List<QuotaGaugeSample>();

            foreach (var tenant in tenants)
            {
                var definitions = await _quotaStore.ListAsync(tenant.Slug).ConfigureAwait(false);

                if (definitions.Count == 0)
                {
                    continue;
                }

                var usage = await _quotaStore
                    .GetUsageAsync(new QuotaUsageQuery { TenantId = tenant.Slug })
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(exception, "Could not refresh the quota gauge cache; keeping the previous value.");
            }

            // Keep the previous valid cache: a partial failure should not
            // drop the gauge to zero.
            return _snapshot;
        }
    }

    private static KeyValuePair<string, object?>[] BuildTags(QuotaGaugeSample sample) =>
    [
        new(AgentPrismDiagnostics.Tags.TenantId, sample.TenantId),
        new(AgentPrismDiagnostics.Tags.QuotaScope, sample.Scope),
        new(AgentPrismDiagnostics.Tags.QuotaPeriod, sample.Period.ToString()),
        new(AgentPrismDiagnostics.Tags.QuotaMetric, sample.Metric.ToString()),
    ];

    private readonly record struct QuotaGaugeSample(
        string TenantId,
        string Scope,
        QuotaPeriod Period,
        QuotaMetric Metric,
        decimal Used,
        decimal Limit);
}
