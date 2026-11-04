using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Holds the live voice sessions this server instance is hosting.</summary>
/// <remarks>
/// <para>
/// The registry is the HTTP layer's only handle on a running session: the endpoints
/// close and query through it rather than holding a host themselves. It also sweeps,
/// because two things can go wrong without anyone asking a question — a session that
/// is created and never connected to, and a session that outlives its duration cap.
/// </para>
/// <para>
/// Sessions are per <strong>instance</strong>. A live session binds to the
/// server that created it, exactly like the conversation layer's socket: a
/// multi-instance deployment needs sticky routing.
/// </para>
/// </remarks>
internal sealed class LiveVoiceSessionRegistry : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, LiveVoiceSessionHost> _sessions = new();
    private readonly TenantSlotCounter _slots = new();
    private readonly IOptionsMonitor<VoiceLiveOptions> _options;
    private readonly ILogger<LiveVoiceSessionRegistry> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _stopping = new();

    private Task _sweeper = Task.CompletedTask;

    /// <summary>Creates a registry.</summary>
    /// <param name="options">The live layer's options.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="timeProvider">The time source.</param>
    public LiveVoiceSessionRegistry(
        IOptionsMonitor<VoiceLiveOptions> options,
        ILogger<LiveVoiceSessionRegistry> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Starts the sweeper.</summary>
    public void Start()
        => _sweeper = Task.Run(SweepAsync, CancellationToken.None);

    /// <summary>Reserves a slot for a tenant and adds the session in one step.</summary>
    /// <param name="host">The session.</param>
    /// <param name="limit">How many sessions the tenant may hold open.</param>
    /// <returns><see langword="true"/> when the slot was reserved and the session added.</returns>
    /// <remarks>
    /// Reserving the slot <strong>is</strong> the limit check, in one atomic step. A
    /// read followed by a separate add would let two simultaneous requests both see a
    /// count under the limit and both pass — and on a connection billed by the second
    /// that is the spending ceiling failing open under exactly the burst it exists to
    /// stop.
    /// </remarks>
    public bool TryAdd(LiveVoiceSessionHost host, int limit)
    {
        ArgumentNullException.ThrowIfNull(host);

        // 🚨 Compare-and-swap (TenantSlotCounter), for the same reason VoiceConnectionLimiter uses one.
        if (!_slots.TryReserve(host.TenantId, limit))
        {
            return false;
        }

        _sessions[host.Id] = host;

        return true;
    }

    /// <summary>Finds a session a tenant owns.</summary>
    /// <param name="id">Tracon's session identifier.</param>
    /// <param name="tenantId">The tenant.</param>
    /// <returns>The session, or <see langword="null"/> when there is none this tenant may see.</returns>
    /// <remarks>
    /// The tenant check is part of the lookup, not a caller's duty: a caller
    /// that forgets it hands another tenant's session out, and the endpoint layer is
    /// exactly where that is easiest to forget.
    /// </remarks>
    public LiveVoiceSessionHost? Find(Guid id, string tenantId)
        => _sessions.TryGetValue(id, out var host)
            && string.Equals(host.TenantId, tenantId, StringComparison.Ordinal)
                ? host
                : null;

    /// <summary>Gets a tenant's reserved slot count.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <returns>The count.</returns>
    public int CountFor(string tenantId)
        => _slots.CountFor(tenantId);

    /// <summary>Closes a session and removes it.</summary>
    /// <param name="host">The session.</param>
    /// <param name="reason">Why it closed.</param>
    /// <returns>The completion task.</returns>
    public async Task CloseAsync(LiveVoiceSessionHost host, VoiceSessionEndReason reason)
    {
        ArgumentNullException.ThrowIfNull(host);

        try
        {
            await host.CloseAsync(reason).ConfigureAwait(false);
        }
        finally
        {
            // A close that threw still ended the session (the host marks itself closed first); keeping its
            // slot would hold the tenant at the limit for as long as the process lives.
            Release(host);
        }
    }

    /// <summary>Closes every session, for a server shutdown.</summary>
    /// <returns>The completion task.</returns>
    public async Task ShutdownAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);

        // One session at a time, each in its own try: a session whose close throws must not decide
        // whether the sessions after it are closed and their records written.
        foreach (var host in _sessions.Values)
        {
            try
            {
                await CloseAsync(host, VoiceSessionEndReason.ServerShutdown).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "The live voice session {Id} could not be closed at shutdown.", host.Id);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);

        try
        {
            await _sweeper.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "The live voice sweeper ended with an error.");
        }

        _stopping.Dispose();
    }

    /// <summary>Removes a session and gives its tenant's slot back.</summary>
    /// <param name="host">The session.</param>
    /// <remarks>
    /// The slot is released only when the session was actually still registered, so
    /// a second close cannot decrement the count twice and let a tenant hold more
    /// sessions than the limit allows.
    /// </remarks>
    private void Release(LiveVoiceSessionHost host)
    {
        if (!_sessions.TryRemove(host.Id, out _))
        {
            return;
        }

        _slots.Release(host.TenantId);
    }

    private async Task SweepAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), _timeProvider, _stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var options = _options.CurrentValue;
            var now = _timeProvider.GetUtcNow();

            foreach (var host in _sessions.Values)
            {
                var age = now - host.StartedAt;

                var reason = host.State switch
                {
                    LiveVoiceSessionState.Pending when age > options.PendingSessionTimeout
                        => (VoiceSessionEndReason?)VoiceSessionEndReason.Abandoned,
                    LiveVoiceSessionState.Active when age > options.MaxSessionDuration
                        => VoiceSessionEndReason.DurationLimit,
                    LiveVoiceSessionState.Ended => VoiceSessionEndReason.Client,
                    _ => null,
                };

                if (host.State == LiveVoiceSessionState.Ended)
                {
                    Release(host);

                    continue;
                }

                if (reason is not { } endReason)
                {
                    continue;
                }

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "The live voice session {Id} is being closed by the sweeper: {Reason}.",
                        host.Id,
                        endReason);
                }

                try
                {
                    await CloseAsync(host, endReason).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "The live voice session {Id} could not be closed.", host.Id);
                }
            }
        }
    }
}
