using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon;

/// <summary>
/// Counts one request against the shared <see cref="IRateLimitCounterStore"/> for
/// <see cref="RateLimitScope.Deployment"/>, and says what to do when the store fails.
/// </summary>
/// <remarks>
/// <para>
/// Both rate limiters use it: the filter on the endpoint groups and the inbound trigger limiter.
/// It holds no limit of its own; the caller passes the limit and the window.
/// </para>
/// <para>
/// <strong>A store failure never lifts the limit and never rejects every request.</strong>
/// <see cref="TryCountAsync"/> returns <see langword="null"/> and the caller applies that
/// instance's local limiter, which is built with the same settings. The failure is counted on
/// <see cref="TraconDiagnostics.RateLimitStoreFailureCounterName"/> and logged at most once per
/// partition per <see cref="WarningInterval"/>. Fail-open would lift the limit during a database
/// outage, the moment a limit matters most; fail-closed would answer every caller with
/// <c>429</c> for the same outage.
/// </para>
/// </remarks>
internal sealed class DeploymentRateLimiter
{
    /// <summary>The shortest time between two warnings for the same partition.</summary>
    public static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(1);

    private readonly IRateLimitCounterStore _store;
    private readonly TimeProvider _clock;
    private readonly TraconMetrics? _metrics;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastWarning = new(StringComparer.Ordinal);
    private int _scopeWarningLogged;

    /// <summary>Creates a new deployment rate limiter.</summary>
    /// <param name="store">The counter store.</param>
    /// <param name="timeProvider">The clock. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="metrics">The metrics. When <see langword="null"/>, failures are logged only.</param>
    /// <param name="logger">The logger. When <see langword="null"/>, nothing is logged.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public DeploymentRateLimiter(
        IRateLimitCounterStore store,
        TimeProvider? timeProvider = null,
        TraconMetrics? metrics = null,
        ILogger<DeploymentRateLimiter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _clock = timeProvider ?? TimeProvider.System;
        _metrics = metrics;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>
    /// Gets a value indicating whether the registered store keeps its windows in the memory of this
    /// process, so <see cref="RateLimitScope.Deployment"/> cannot be shared with another instance.
    /// </summary>
    public bool StoreIsProcessLocal => _store is InMemoryRateLimitCounterStore;

    /// <summary>
    /// Writes the one warning that says <see cref="RateLimitScope.Deployment"/> is chosen but the
    /// counter is in process memory. Later calls do nothing.
    /// </summary>
    public void WarnWhenProcessLocal()
    {
        if (!StoreIsProcessLocal || Interlocked.Exchange(ref _scopeWarningLogged, 1) == 1)
        {
            return;
        }

        _logger.LogWarning(
            "Tracon:RateLimit:Scope is Deployment, but no shared rate-limit counter store is registered, so " +
            "the counter stays in the memory of this process and the limit applies per instance. Use a SQL " +
            "provider (UseSqlite, UsePostgreSql, UseSqlServer) or register an IRateLimitCounterStore.");
    }

    /// <summary>Counts one request and decides whether it is within the limit.</summary>
    /// <param name="key">The partition key.</param>
    /// <param name="permitLimit">The permit limit.</param>
    /// <param name="window">The window length.</param>
    /// <param name="limiter">Which limiter asks: <see cref="RateLimiterKind"/>.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>
    /// The decision, or <see langword="null"/> when the store failed and the caller must apply its
    /// local limiter.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async ValueTask<FixedWindowDecision?> TryCountAsync(
        RateLimitPartitionKey key,
        int permitLimit,
        TimeSpan window,
        string limiter,
        CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var windowStart = FixedWindowDecision.AlignWindowStart(now, window);

        try
        {
            var counted = await _store.IncrementAsync(key.Value, windowStart, cancellationToken).ConfigureAwait(false);

            return FixedWindowDecision.Decide(counted, permitLimit, window, now);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            RecordFailure(key, limiter, exception, now);

            return null;
        }
    }

    private void RecordFailure(RateLimitPartitionKey key, string limiter, Exception exception, DateTimeOffset now)
    {
        _metrics?.RecordRateLimitStoreFailure(limiter);

        var last = _lastWarning.GetOrAdd(key.Value, DateTimeOffset.MinValue);

        if (now - last < WarningInterval || !_lastWarning.TryUpdate(key.Value, now, last))
        {
            return;
        }

        _logger.LogWarning(
            exception,
            "The rate-limit counter store failed for partition '{Partition}' ({Limiter}). This instance applies its " +
            "local limit until the store answers again.",
            key.Value,
            limiter);
    }
}

/// <summary>The names of the two rate limiters, used as the value of the limiter metric tag.</summary>
internal static class RateLimiterKind
{
    /// <summary>The limiter on the endpoint groups.</summary>
    public const string Endpoint = "endpoint";

    /// <summary>The limiter on the inbound trigger accept endpoint.</summary>
    public const string Trigger = "trigger";
}
