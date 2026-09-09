using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Reduces a background service's execution to a single instance cluster-wide.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RunAsync"/> runs its own lease/renew loop at an interval that is
/// ONE THIRD of <see cref="SingletonExecutionOptions.LeaseDuration"/>; this
/// keeps the lease alive in cases where the calling service's own work
/// interval (e.g. MCP discovery's 5-minute refresh interval) may be much
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
/// <c>AgentPrism.Mcp</c>, reaches it through the explicit
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

    private volatile bool _holding;

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
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseName);

        _store = store;
        _optionsMonitor = optionsMonitor;
        _leaseName = leaseName;
        _logger = logger;

        _ownerId = optionsMonitor.CurrentValue.OwnerId is { Length: > 0 } configured
            ? configured
            : $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Whether this instance currently holds the lease. Always
    /// <see langword="true"/> when single-executor selection is disabled.
    /// Synchronous; the calling service can read this on every tick for free.
    /// </summary>
    public bool IsHeld => !_optionsMonitor.CurrentValue.Enabled || _holding;

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
        if (!_optionsMonitor.CurrentValue.Enabled)
        {
            return;
        }

        var renewInterval = ComputeRenewInterval(_optionsMonitor.CurrentValue.LeaseDuration);

        using var timer = new PeriodicTimer(renewInterval);

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
                catch (Exception exception) when (exception is not OperationCanceledException)
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
        try
        {
            var options = _optionsMonitor.CurrentValue;

            if (_holding)
            {
                var renewed = await _store
                    .RenewAsync(_leaseName, _ownerId, options.LeaseDuration, cancellationToken)
                    .ConfigureAwait(false);

                if (!renewed)
                {
                    _holding = false;
                    LogLost();
                }
            }
            else
            {
                _holding = await _store
                    .TryAcquireAsync(_leaseName, _ownerId, options.LeaseDuration, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailure(exception);
        }
    }

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
