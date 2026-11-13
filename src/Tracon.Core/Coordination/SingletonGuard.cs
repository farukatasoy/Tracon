using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Reduces a background service's execution to a single instance cluster-wide.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RunAsync"/> runs its own lease/renew loop at an interval that is
/// ONE THIRD of <see cref="SingletonExecutionOptions.LeaseDuration"/>; this
/// keeps the lease alive in cases where the calling service's own work
/// interval (e.g. a model-health refresh interval of minutes) may be much
/// longer than the lease duration. The calling service reads only
/// <see cref="IsHeld"/> on each of its own ticks - this is synchronous and
/// free, it produces no extra database round-trip.
/// </para>
/// <para>
/// While <see cref="SingletonExecutionOptions.Enabled"/> is <see langword="false"/>
/// (the default), <see cref="RunAsync"/> returns immediately without issuing
/// ANY query to the store, and <see cref="IsHeld"/> always stays
/// <see langword="true"/> - today's single-instance behavior is preserved
/// exactly.
/// </para>
/// <para>
/// This type stays <c>internal</c>. A sibling package that needs it, such as
/// <c>Tracon.Mcp</c>, reaches it through the explicit
/// <c>InternalsVisibleTo</c> entries in <c>Properties/AssemblyInfo.cs</c>, so
/// the guard never becomes part of the public surface.
/// </para>
/// </remarks>
internal sealed class SingletonGuard
{
    private readonly ISingletonLeaseStore _store;
    private readonly IOptionsMonitor<SingletonExecutionOptions> _optionsMonitor;
    private readonly string _leaseName;
    private readonly string _ownerId;
    private readonly ILogger? _logger;

    // The guard has two readers of the options on two threads of control: the service that reads IsHeld on its own
    // tick, and the lease loop. A TickOptions serves one loop, so each reader has its own.
    private readonly TickOptions<SingletonExecutionOptions> _scanOptions;
    private readonly TickOptions<SingletonExecutionOptions> _leaseOptions;

    private readonly TimeProvider _timeProvider;

    private volatile bool _holding;

    // The timestamp at which the last acquire or renewal that the store confirmed STARTED. The store
    // extends the lease from the moment it handles the call, which is no earlier than the start, so
    // counting from the start never believes the lease lives longer than it does.
    private long _confirmedAt;

    /// <summary>
    /// The shortest interval at which the lease is renewed. It keeps a very
    /// short <see cref="SingletonExecutionOptions.LeaseDuration"/> from turning
    /// the renewal loop into an unreasonably frequent round-trip to the store.
    /// </summary>
    internal static readonly TimeSpan MinimumRenewInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The shortest lease a validated configuration may set. It is THREE TIMES
    /// <see cref="MinimumRenewInterval"/>, because renewal happens at one third
    /// of the lease: below this value the renewal floor would push the renewal
    /// onto or past the expiry, and another instance would take the lease over
    /// while its owner is alive and working.
    /// </summary>
    /// <remarks>
    /// <see cref="SingletonExecutionOptionsValidator"/> rejects anything shorter
    /// while selection is on.
    /// </remarks>
    internal static readonly TimeSpan MinimumLeaseDuration = MinimumRenewInterval * 3;

    /// <summary>Creates a new single-executor guard.</summary>
    /// <param name="store">Lease store.</param>
    /// <param name="optionsMonitor">Single-executor selection settings.</param>
    /// <param name="leaseName">Cluster-wide unique name of the protected work.</param>
    /// <param name="logger">Logger to which lease-loss and error logs are written.</param>
    /// <param name="timeProvider">The clock the lease is counted on; the system clock when omitted.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="store"/>
    /// or
    /// <paramref name="optionsMonitor"/>
    /// is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="leaseName"/> is empty.</exception>
    public SingletonGuard(
        ISingletonLeaseStore store,
        IOptionsMonitor<SingletonExecutionOptions> optionsMonitor,
        string leaseName,
        ILogger? logger = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseName);

        _store = store;
        _optionsMonitor = optionsMonitor;
        _leaseName = leaseName;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _scanOptions = new TickOptions<SingletonExecutionOptions>(
            optionsMonitor, logger, $"single-executor guard '{leaseName}' (the scan that asks whether it holds the lease)");
        _leaseOptions = new TickOptions<SingletonExecutionOptions>(
            optionsMonitor, logger, $"single-executor guard '{leaseName}' (lease renewal)");

        _ownerId = optionsMonitor.CurrentValue.OwnerId is { Length: > 0 } configured
            ? configured
            : $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Whether this instance currently holds the lease. Always
    /// <see langword="true"/> when single-executor selection is disabled.
    /// Synchronous; the calling service can read this on every tick for free.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> while the options cannot be read (a reload the options validation refused): the
    /// instance cannot tell whether selection is on or how long its lease lasts, and acting as the executor on a
    /// lease it cannot reckon is the worse mistake. Every instance reads the same configuration, so all of them skip
    /// the scan until a valid value is configured, and the first tick that can read one works again. The service
    /// that asks must not have to catch anything.
    /// </remarks>
    public bool IsHeld
        => _scanOptions.TryRead(out var options)
        && (!options.Enabled || (_holding && !LeaseElapsed(options.LeaseDuration)));

    /// <summary>
    /// Starts the lease/renew loop. Returns immediately without issuing ANY
    /// query to the store when single-executor selection is disabled. When
    /// <paramref name="stoppingToken"/> is cancelled, the lease (if held) is
    /// released and the task completes.
    /// </summary>
    /// <param name="stoppingToken">The host service's stop token.</param>
    /// <returns>The task representing the loop.</returns>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        // The one read that is not skipped: the loop starts with the host, after the options were validated at the
        // start, and the renewal interval it fixes is the one of this value. A value that cannot be read here is a
        // host that did not start properly, not a reload.
        var atStart = _optionsMonitor.CurrentValue;

        if (!atStart.Enabled)
        {
            return;
        }

        var renewInterval = ComputeRenewInterval(atStart.LeaseDuration);

        using var timer = new PeriodicTimer(renewInterval, _timeProvider);

        try
        {
            await TickAsync(stoppingToken).ConfigureAwait(false);

            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            if (_holding)
            {
                try
                {
                    await _store.ReleaseAsync(_leaseName, _ownerId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (OperationCancellation.IsFailure(exception, CancellationToken.None))
                {
                    // Best-effort release; the lease expires on its own anyway.
                    LogFailure(exception);
                }

                _holding = false;
            }
        }
    }

    /// <summary>
    /// Computes the renewal interval of a lease: one third of its duration, but
    /// never shorter than <see cref="MinimumRenewInterval"/>.
    /// </summary>
    /// <param name="leaseDuration">The lease duration.</param>
    /// <returns>The interval between two renewal attempts.</returns>
    /// <remarks>
    /// The result MUST stay strictly shorter than <paramref name="leaseDuration"/>;
    /// otherwise the lease expires under a live owner. The floor holds only for
    /// a lease shorter than <see cref="MinimumLeaseDuration"/>, which
    /// <see cref="SingletonExecutionOptionsValidator"/> rejects.
    /// </remarks>
    internal static TimeSpan ComputeRenewInterval(TimeSpan leaseDuration)
        => TimeSpan.FromTicks(Math.Max(leaseDuration.Ticks / 3, MinimumRenewInterval.Ticks));

    /// <summary>Performs a single lease/renew attempt.</summary>
    internal async ValueTask TickAsync(CancellationToken cancellationToken)
    {
        // A tick that cannot read its options does not talk to the store. A lease this instance holds runs out by its
        // own duration, and IsHeld says so from the clock; the loop is not the one that gives it up.
        if (!_leaseOptions.TryRead(out var options))
        {
            return;
        }

        try
        {
            var startedAt = _timeProvider.GetTimestamp();

            if (_holding)
            {
                var renewed = await _store
                    .RenewAsync(_leaseName, _ownerId, options.LeaseDuration, cancellationToken)
                    .ConfigureAwait(false);

                if (renewed)
                {
                    Volatile.Write(ref _confirmedAt, startedAt);
                }
                else
                {
                    _holding = false;
                    LogLost();
                }
            }
            else
            {
                var acquired = await _store
                    .TryAcquireAsync(_leaseName, _ownerId, options.LeaseDuration, cancellationToken)
                    .ConfigureAwait(false);

                if (acquired)
                {
                    Volatile.Write(ref _confirmedAt, startedAt);
                }

                _holding = acquired;
            }
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            LogFailure(exception);

            // One failed renewal is inside the margin a third of the lease leaves. A renewal that keeps
            // failing past the whole lease is not: the store has handed the lease to someone else by now,
            // and this instance must stop acting as the executor instead of waiting for a call that works.
            if (_holding && LeaseElapsed(options.LeaseDuration))
            {
                _holding = false;
                LogLost();
            }
        }
    }

    /// <summary>Gets whether the lease this instance last had confirmed has run out by its own duration.</summary>
    /// <param name="leaseDuration">The duration the options of the reader that asks gave; the options are not read again here.</param>
    private bool LeaseElapsed(TimeSpan leaseDuration)
        => _timeProvider.GetElapsedTime(Volatile.Read(ref _confirmedAt)) >= leaseDuration;

    private void LogLost()
    {
        if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning("Single-executor lease lost: {LeaseName}.", _leaseName);
        }
    }

    private void LogFailure(Exception exception)
    {
        if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(exception, "Single-executor lease attempt failed: {LeaseName}.", _leaseName);
        }
    }
}
