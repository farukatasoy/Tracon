using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The background refresher behind a database-backed observable gauge.
/// </summary>
/// <typeparam name="TSample">One cached gauge sample.</typeparam>
/// <remarks>
/// <para>
/// An <c>ObservableGauge</c> callback runs on the thread that collects EVERY
/// instrument of the meter provider: the exporter's reader, or a scrape
/// request. It must never wait for I/O, because a slow database would delay the
/// whole export. So the callback only reads the last snapshot
/// (<see cref="Snapshot"/>), and this service refreshes that snapshot: once at
/// start, then on every tick of a <see cref="PeriodicTimer"/> driven by the
/// injected <see cref="TimeProvider"/>.
/// </para>
/// <para>
/// Until the first refresh completes, the gauge publishes nothing. A failed
/// refresh keeps the previous snapshot, so a transient store error does not
/// read as a drop to zero. The store's own command timeout and host shutdown
/// bound a refresh; there is no separate timeout.
/// </para>
/// <para>
/// When the gauge is off at start (the default), <see cref="ExecuteAsync"/>
/// returns at once: no timer exists and the store is never queried. The
/// service is registered either way, so the instruments exist and the gauge can
/// be turned on without a change to the consumer's telemetry configuration.
/// Turning it on takes effect at the next host start. Turning it off while the
/// host runs stops the publication at once and the queries at the next tick.
/// </para>
/// <para>
/// The first query waits until the SQL schema is ready, whatever the order in
/// which the consumer registered the persistence provider.
/// </para>
/// </remarks>
internal abstract class CachedGaugeSource<TSample> : BackgroundService
{
    private readonly IOptionsMonitor<TraconOptions> _options;
    private readonly SchemaReadyGate _schemaReadyGate;
    private IReadOnlyList<TSample> _snapshot = [];
    private int _completedRefreshes;

    /// <summary>Initializes the shared refresh state.</summary>
    /// <param name="options">
    /// The root options. The gauge settings hang off <see cref="TraconOptions.Observability"/>;
    /// <see cref="TraconObservabilityOptions"/> is not registered on its own.
    /// </param>
    /// <param name="schemaReadyGate">The gate the first query waits on.</param>
    /// <param name="timeProvider">The time source for the refresh timer. If <see langword="null"/>, <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    protected CachedGaugeSource(
        IOptionsMonitor<TraconOptions> options,
        SchemaReadyGate schemaReadyGate,
        TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(schemaReadyGate);

        _options = options;
        _schemaReadyGate = schemaReadyGate;
        Clock = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Gets how many refreshes have finished, successful or not. A test waits
    /// on this instead of sleeping.
    /// </summary>
    internal int CompletedRefreshes => Volatile.Read(ref _completedRefreshes);

    /// <summary>Gets the time source for the timer and for any time-dependent read.</summary>
    protected TimeProvider Clock { get; }

    /// <summary>Reads whether this gauge is on.</summary>
    /// <param name="options">The current observability options.</param>
    /// <returns><see langword="true"/> when the gauge publishes and refreshes.</returns>
    protected abstract bool IsEnabled(TraconObservabilityOptions options);

    /// <summary>Reads this gauge's refresh interval.</summary>
    /// <param name="options">The current observability options.</param>
    /// <returns>The time between two refreshes.</returns>
    protected abstract TimeSpan GetRefreshInterval(TraconObservabilityOptions options);

    /// <summary>Reads the samples from the store.</summary>
    /// <param name="cancellationToken">Cancelled when the host stops.</param>
    /// <returns>The new snapshot.</returns>
    protected abstract ValueTask<IReadOnlyList<TSample>> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Logs a refresh that failed. The previous snapshot stays in place.</summary>
    /// <param name="exception">The failure.</param>
    protected abstract void LogRefreshFailure(Exception exception);

    /// <summary>
    /// Returns the last snapshot for the gauge callback. Never performs I/O and
    /// never waits.
    /// </summary>
    /// <returns>The last snapshot; empty while the gauge is off or before the first refresh.</returns>
    protected IReadOnlyList<TSample> Snapshot()
        => IsEnabled(_options.CurrentValue.Observability) ? Volatile.Read(ref _snapshot) : [];

    /// <inheritdoc />
    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var observability = _options.CurrentValue.Observability;

        // Off by default: no timer, no query (K1).
        if (!IsEnabled(observability))
        {
            return;
        }

        try
        {
            // 🚨 BackgroundService.StartAsync does not wait for this method,
            // so the first query can run before the migrations do (K-354).
            await _schemaReadyGate.WaitAsync(stoppingToken).ConfigureAwait(false);

            using var timer = new PeriodicTimer(GetRefreshInterval(observability), Clock);

            do
            {
                if (IsEnabled(_options.CurrentValue.Observability))
                {
                    await RefreshAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task RefreshAsync(CancellationToken stoppingToken)
    {
        try
        {
            var samples = await ReadAsync(stoppingToken).ConfigureAwait(false);

            Volatile.Write(ref _snapshot, samples);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
        {
            // Observability must not break functionality. The previous valid
            // snapshot stays, so a transient failure does not drop the gauge
            // to zero and read as "nothing is there".
            LogRefreshFailure(exception);
        }
        finally
        {
            Interlocked.Increment(ref _completedRefreshes);
        }
    }
}
