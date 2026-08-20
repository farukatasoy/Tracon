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
/// This type is NOT <c>internal</c>, it is <c>public</c>: it needs to be
/// usable from a separate assembly such as <c>AgentPrism.Mcp</c>, and
/// <c>InternalsVisibleTo</c> covers only its own test projects, not sibling
/// packages. This is why the plan's suggestion of an "internal helper" could
/// not be implemented.
/// </para>
/// </remarks>
public sealed class SingletonGuard
{
    private readonly ISingletonLeaseStore _store;
    private readonly IOptionsMonitor<SingletonExecutionOptions> _optionsMonitor;
    private readonly string _leaseName;
    private readonly string _ownerId;
    private readonly ILogger? _logger;

    private volatile bool _holding;

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

        var leaseDuration = _optionsMonitor.CurrentValue.LeaseDuration;
        var renewInterval = TimeSpan.FromTicks(Math.Max(leaseDuration.Ticks / 3, TimeSpan.FromSeconds(1).Ticks));

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
