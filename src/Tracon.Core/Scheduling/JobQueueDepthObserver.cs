using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Cached data source for the <c>tracon.job.queue.depth</c> observable gauge.
/// </summary>
/// <remarks>
/// <para>
/// The gauge callback only reads the last snapshot; a background refresh reads
/// the database every
/// <see cref="TraconObservabilityOptions.JobQueueDepthRefreshInterval"/>. A
/// scrape never waits for the database, and consecutive scrapes issue no query.
/// The published depth is therefore at most one interval old, and the first
/// scrape after start can be empty. The same shape as
/// <see cref="QuotaUsageObserver"/>; see <see cref="CachedGaugeSource{TSample}"/>.
/// </para>
/// <para>
/// As long as <see cref="TraconObservabilityOptions.EnableJobQueueDepthGauge"/>
/// is <see langword="false"/> — the default — the database is never queried. The
/// instrument name still exists, so the gauge can be turned on without changing
/// the consumer's OTel configuration, but no measurement is published.
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
internal sealed class JobQueueDepthObserver : CachedGaugeSource<JobQueueDepth>
{
    private readonly IJobStore _jobStore;
    private readonly TraconMetrics? _metrics;
    private readonly ILogger<JobQueueDepthObserver>? _logger;
    private readonly Meter _meter;
    private readonly bool _ownsMeter;

    /// <summary>Creates a new queue-depth gauge.</summary>
    /// <param name="jobStore">The job store the depth is counted from.</param>
    /// <param name="options">
    /// The root type that <see cref="TraconOptions.Observability"/>, which
    /// carries the gauge on/off and refresh interval settings, hangs off.
    /// <see cref="TraconObservabilityOptions"/> is NOT registered standalone
    /// anywhere with <c>services.Configure&lt;TraconObservabilityOptions&gt;</c>
    /// — it is only reached through <see cref="TraconOptions.Observability"/>.
    /// </param>
    /// <param name="schemaReadyGate">The gate the first database read waits on.</param>
    /// <param name="meterFactory">
    /// The meter factory. If <see langword="null"/>, a private <see cref="Meter"/>
    /// instance is created and owned by this object.
    /// </param>
    /// <param name="timeProvider">The time source. If <see langword="null"/>, <see cref="TimeProvider.System"/>.</param>
    /// <param name="logger">The logger that refresh errors are logged to.</param>
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
        SchemaReadyGate schemaReadyGate,
        IMeterFactory? meterFactory = null,
        TimeProvider? timeProvider = null,
        ILogger<JobQueueDepthObserver>? logger = null,
        TraconMetrics? metrics = null)
        : base(options, schemaReadyGate, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(jobStore);

        _jobStore = jobStore;
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
        => options.EnableJobQueueDepthGauge;

    /// <inheritdoc />
    protected override TimeSpan GetRefreshInterval(TraconObservabilityOptions options)
        => options.JobQueueDepthRefreshInterval;

    /// <inheritdoc />
    protected override ValueTask<IReadOnlyList<JobQueueDepth>> ReadAsync(CancellationToken cancellationToken)
        => _jobStore.GetQueueDepthAsync(cancellationToken);

    /// <inheritdoc />
    protected override void LogRefreshFailure(Exception exception)
    {
        // 🚨 A failed depth query leaves the worker and every run untouched;
        // the previous snapshot stays so a blip does not read as "the queue drained".
        if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(exception, "Could not refresh the job queue-depth gauge cache; keeping the previous value.");
        }
    }

    private IEnumerable<Measurement<long>> ObserveDepth()
        => Snapshot().Select(depth => new Measurement<long>(
            depth.Count,
            new KeyValuePair<string, object?>(
                TraconDiagnostics.Tags.Lane,
                _metrics?.ResolveLaneTag(depth.Lane) ?? depth.Lane),
            new KeyValuePair<string, object?>(TraconDiagnostics.Tags.JobStatus, depth.Status.ToString())));
}
