using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Applies a fixed-window rate limit per inbound trigger.</summary>
/// <remarks>
/// <para>
/// The limit lives in process memory: Tracon does not ship a
/// distributed rate limiter. In a multi-instance deployment the limit applies
/// PER INSTANCE, not per trigger across the whole deployment — the same
/// documented scope as <c>TraconRateLimitOptions</c>.
/// </para>
/// <para>
/// Hand-written rather than built on <c>System.Threading.RateLimiting</c>
/// (the type <c>TraconRateLimitFilter</c> uses): that namespace ships in
/// the ASP.NET Core shared framework's reference pack, which
/// <c>Tracon.Core</c> — a plain class library with no dependency on
/// ASP.NET Core — cannot see. A dispatcher that validates a signature and
/// queues a job has no reason to depend on a web framework.
/// </para>
/// </remarks>
public sealed class InboundTriggerRateLimiter
{
    private readonly ConcurrentDictionary<Guid, Window> _windows = new();
    private readonly IOptionsMonitor<TraconInboundTriggerOptions> _optionsMonitor;
    private readonly TimeProvider _clock;

    /// <summary>Creates a new trigger rate limiter.</summary>
    /// <param name="optionsMonitor">The inbound trigger settings.</param>
    /// <param name="timeProvider">The clock. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsMonitor"/> is <see langword="null"/>.</exception>
    public InboundTriggerRateLimiter(
        IOptionsMonitor<TraconInboundTriggerOptions> optionsMonitor,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _optionsMonitor = optionsMonitor;
        _clock = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Tries to acquire one permit for the given trigger.</summary>
    /// <param name="triggerId">The trigger identifier.</param>
    /// <returns><see langword="true"/> if the request is within the trigger's per-minute limit.</returns>
    public bool TryAcquire(Guid triggerId)
    {
        var limit = Math.Max(1, _optionsMonitor.CurrentValue.MaxRequestsPerMinute);
        var now = _clock.GetUtcNow();
        var window = _windows.GetOrAdd(triggerId, static _ => new Window());

        lock (window)
        {
            if (now - window.StartedAt >= TimeSpan.FromMinutes(1))
            {
                window.StartedAt = now;
                window.Count = 0;
            }

            if (window.Count >= limit)
            {
                return false;
            }

            window.Count++;

            return true;
        }
    }

    private sealed class Window
    {
        public DateTimeOffset StartedAt = DateTimeOffset.MinValue;

        public int Count;
    }
}
