using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Cached data source for the <c>tracon.job.queue.depth</c> observable gauge.
/// </summary>
/// <remarks>
/// <para>
/// The <c>ObservableGauge</c> callback is synchronous; a database read CANNOT
/// be done directly inside it. This class compares the cache's age against
/// <see cref="TraconObservabilityOptions.JobQueueDepthRefreshInterval"/> on
/// every callback: if the cache is fresh it returns directly, if it is stale it
/// refreshes ONCE (blocking). Consecutive scrapes inside that interval issue no
/// second query. The same shape as <see cref="QuotaUsageObserver"/>.
/// </para>
/// <para>
/// As long as <see cref="TraconObservabilityOptions.EnableJobQueueDepthGauge"/>
/// is <see langword="false"/> — the default — the cache is NEVER touched. The
/// instrument name still exists, so the gauge can be turned on without changing
/// the consumer's OTel configuration, but no measurement is published and the
/// database is never queried.
/// </para>
/// <para>
/// <strong>No tenant tag.</strong> Queue depth is an operator signal about the
/// worker pool, which leases across every tenant. Adding a tenant tag would
/// multiply the cardinality by the tenant count and imply a tenant boundary that
/// does not exist here.
/// </para>
/// <para>
/// The lane tag goes through the SAME cardinality guard the job counter uses, and
/// through the same <see cref="TraconMetrics"/> instance, so a lane is named
/// identically on both. A separate guard would let one instrument publish a lane
/// under its own name while the other reported it as <c>other</c>.
/// </para>
/// </remarks>
internal sealed class JobQueueDepthObserver : IHostedService, IDisposable
{
    private readonly IJobStore _jobStore;
    private readonly IOptionsMonitor<TraconOptions> _options;
    private readonly TraconMetrics? _metrics;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobQueueDepthObserver>? _logger;
    private readonly Meter _meter;
    private readonly bool _ownsMeter;
    // SemaphoreSlim: System.Threading.Lock cannot be used because net8.0 is
    // also a target (MA0158 also forbids a separate object field); used with
    // Wait()/Release() on the synchronous code path.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<JobQueueDepth> _snapshot = [];
    private DateTimeOffset? _lastRefreshedAt;

    /// <summary>Creates a new queue-depth gauge.</summary>
    /// <param name="jobStore">The job store the depth is counted from.</param>
    /// <param name="options">
    /// The root type that <see cref="TraconOptions.Observability"/>, which
    /// carries the gauge on/off and cache interval settings, hangs off.
    /// <see cref="TraconObservabilityOptions"/> is NOT registered standalone
    /// anywhere with <c>services.Configure&lt;TraconObservabilityOptions&gt;</c>
    /// — it is only reached through <see cref="TraconOptions.Observability"/>.
    /// </param>
    /// <param name="meterFactory">
    /// The meter factory. If <see langword="null"/>, a private <see cref="Meter"/>
    /// instance is created and owned by this object.
    /// </param>
    /// <param name="timeProvider">The time source. If <see langword="null"/>, <see cref="TimeProvider.System"/>.</param>
    /// <param name="logger">The logger that cache-refresh errors are logged to.</param>
    /// <param name="metrics">
    /// The metric set whose lane-cardinality guard this gauge shares. When
    /// <see langword="null"/>, lane names are published unguarded — acceptable
    /// only in a test that constructs this type directly; the DI registration
    /// always supplies it.
    /// </param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public JobQueueDepthObserver(
        IJobStore jobStore,
        IOptionsMonitor<TraconOptions> options,
        IMeterFactory? meterFactory = null,
        TimeProvider? timeProvider = null,
        ILogger<JobQueueDepthObserver>? logger = null,
        TraconMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(jobStore);
        ArgumentNullException.ThrowIfNull(options);

        _jobStore = jobStore;
        _options = options;
        _clock = timeProvider ?? TimeProvider.System;
        _logger = logger;
        _metrics = metrics;

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
            TraconDiagnostics.JobQueueDepthGaugeName,
            ObserveDepth,
            unit: "{job}",
            description: "Outstanding jobs per lane and open status.");
    }

    /// <summary>
    /// Does nothing extra — the constructor already creates the instrument at
    /// REGISTRATION time. The only purpose of adding this type as an
    /// <see cref="IHostedService"/> is to make the container resolve and
    /// construct this object EARLY (while the host is starting); otherwise the
    /// instrument would never be created unless some consumer happens to resolve it.
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

    private IEnumerable<Measurement<long>> ObserveDepth()
        => Snapshot().Select(depth => new Measurement<long>(
            depth.Count,
            new KeyValuePair<string, object?>(
                TraconDiagnostics.Tags.Lane,
                _metrics?.ResolveLaneTag(depth.Lane) ?? depth.Lane),
            new KeyValuePair<string, object?>(TraconDiagnostics.Tags.JobStatus, depth.Status.ToString())));

    private IReadOnlyList<JobQueueDepth> Snapshot()
    {
        if (!_options.CurrentValue.Observability.EnableJobQueueDepthGauge)
        {
            return [];
        }

        _gate.Wait();

        try
        {
            var interval = _options.CurrentValue.Observability.JobQueueDepthRefreshInterval;
            var now = _clock.GetUtcNow();

            if (_lastRefreshedAt is null || now - _lastRefreshedAt.Value >= interval)
            {
                // 🚨 The database is reached in a BLOCKING way from a
                // synchronous callback. This is the cost QuotaUsageObserver
                // already accepts and documents: call frequency is at worst
                // JobQueueDepthRefreshInterval, and gauge collection typically
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

    private async Task<IReadOnlyList<JobQueueDepth>> RefreshAsync()
    {
        try
        {
            return await _jobStore.GetQueueDepthAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, CancellationToken.None))
        {
            if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(exception, "Could not refresh the job queue-depth gauge cache; keeping the previous value.");
            }

            // 🚨 Observability must not break functionality. A failed depth
            // query leaves the worker and every run untouched; the previous
            // valid cache is kept so a transient failure does not drop the
            // gauge to zero and read as "the queue drained".
            return _snapshot;
        }
    }
}
