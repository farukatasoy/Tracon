using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Applies a fixed-window rate limit per inbound trigger.</summary>
/// <remarks>
/// <para>
/// Where the window is counted follows <see cref="TraconRateLimitOptions.Scope"/>. With
/// <see cref="RateLimitScope.Instance"/> (the default) it lives in process memory and the limit
/// applies PER INSTANCE, not per trigger across the whole deployment. With
/// <see cref="RateLimitScope.Deployment"/> the window is counted in the
/// <see cref="IRateLimitCounterStore"/> under a key per trigger, so the limit holds across
/// instances. The trigger limit itself (<see cref="TraconInboundTriggerOptions.MaxRequestsPerMinute"/>)
/// is always on; <see cref="TraconRateLimitOptions.Enabled"/> does not switch it.
/// </para>
/// <para>
/// When the counter store fails, the request is counted in the local window instead
/// (<see cref="DeploymentRateLimiter"/>).
/// </para>
/// <para>
/// The local window is hand-written rather than built on <c>System.Threading.RateLimiting</c>
/// (the type <c>TraconRateLimitFilter</c> uses): that namespace ships in
/// the ASP.NET Core shared framework's reference pack, which
/// <c>Tracon.Core</c> — a plain class library with no dependency on
/// ASP.NET Core — cannot see. A dispatcher that validates a signature and
/// queues a job has no reason to depend on a web framework.
/// </para>
/// </remarks>
internal sealed class InboundTriggerRateLimiter
{
    private readonly ConcurrentDictionary<Guid, Window> _windows = new();
    private static readonly TimeSpan WindowLength = TimeSpan.FromMinutes(1);

    private readonly IOptionsMonitor<TraconInboundTriggerOptions> _optionsMonitor;
    private readonly TimeProvider _clock;
    private readonly IOptionsMonitor<TraconRateLimitOptions>? _rateLimitOptions;
    private readonly DeploymentRateLimiter? _deployment;

    /// <summary>Creates a new trigger rate limiter.</summary>
    /// <param name="optionsMonitor">The inbound trigger settings.</param>
    /// <param name="timeProvider">The clock. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="rateLimitOptions">
    /// The rate limit settings that carry <see cref="TraconRateLimitOptions.Scope"/>. When
    /// <see langword="null"/>, the window is always counted in process memory.
    /// </param>
    /// <param name="deployment">
    /// The shared counter. When <see langword="null"/>, the window is always counted in process memory.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsMonitor"/> is <see langword="null"/>.</exception>
    public InboundTriggerRateLimiter(
        IOptionsMonitor<TraconInboundTriggerOptions> optionsMonitor,
        TimeProvider? timeProvider = null,
        IOptionsMonitor<TraconRateLimitOptions>? rateLimitOptions = null,
        DeploymentRateLimiter? deployment = null)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _optionsMonitor = optionsMonitor;
        _clock = timeProvider ?? TimeProvider.System;
        _rateLimitOptions = rateLimitOptions;
        _deployment = deployment;
    }

    /// <summary>Tries to acquire one permit for the given trigger.</summary>
    /// <param name="triggerId">The trigger identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the request is within the trigger's per-minute limit.</returns>
    public ValueTask<bool> TryAcquireAsync(Guid triggerId, CancellationToken cancellationToken = default)
    {
        var limit = Math.Max(1, _optionsMonitor.CurrentValue.MaxRequestsPerMinute);

        return _deployment is not null && _rateLimitOptions?.CurrentValue.Scope == RateLimitScope.Deployment
            ? TryAcquireInDeploymentAsync(_deployment, triggerId, limit, cancellationToken)
            : new ValueTask<bool>(TryAcquireLocally(triggerId, limit));
    }

    private async ValueTask<bool> TryAcquireInDeploymentAsync(
        DeploymentRateLimiter deployment,
        Guid triggerId,
        int limit,
        CancellationToken cancellationToken)
    {
        var decision = await deployment
            .TryCountAsync(
                RateLimitPartitionKey.ForTrigger(triggerId),
                limit,
                WindowLength,
                RateLimiterKind.Trigger,
                cancellationToken)
            .ConfigureAwait(false);

        // A null decision is a store failure: this instance's own window decides.
        return decision?.Allowed ?? TryAcquireLocally(triggerId, limit);
    }

    private bool TryAcquireLocally(Guid triggerId, int limit)
    {
        var now = _clock.GetUtcNow();
        var window = _windows.GetOrAdd(triggerId, static _ => new Window());

        lock (window)
        {
            if (now - window.StartedAt >= WindowLength)
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
