using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>The lifecycle state of a live voice session.</summary>
internal enum LiveVoiceSessionState
{
    /// <summary>Created at the provider; the media peer has not connected.</summary>
    Pending = 0,

    /// <summary>The sideband is attached and the conversation is running.</summary>
    Active = 1,

    /// <summary>The session has ended.</summary>
    Ended = 2,
}

/// <summary>The facts a live voice session is opened with.</summary>
internal sealed record LiveVoiceSessionRequest
{
    /// <summary>Gets the tenant resolved when the session was created.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the agent session the conversation runs in.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the agent being talked to.</summary>
    public required string AgentName { get; init; }

    /// <summary>Gets the actor who created the session.</summary>
    public string? CreatedBy { get; init; }
}

/// <summary>
/// Supervises one provider-hosted live voice session: it pumps the sideband, turns
/// delegations into runs, and writes the session record.
/// </summary>
/// <remarks>
/// <para>
/// <c>VoiceConversationDriver</c> is deliberately untouched by this class. That
/// driver owns a client socket, an utterance buffer, a turn state machine and a turn
/// task; none of those exist here, because the provider carries the media and drives
/// the conversation. Sharing the two would couple two designs that agree on nothing
/// but the word "voice".
/// </para>
/// <para>
/// The pump runs <strong>outside the HTTP request</strong> that created the
/// session. The ambient tenant and the audit actor are therefore re-established on
/// the pump itself, before the first event is handled: without that the delegated
/// runs land in the default tenant, every test stays green, and the defect shows up
/// only in a multi-tenant installation.
/// </para>
/// </remarks>
internal sealed class LiveVoiceSessionHost : IAsyncDisposable
{
    private readonly ILiveVoiceProvider _provider;
    private readonly LiveVoiceSessionRequest _request;
    private readonly VoiceLiveOptions _options;
    private readonly IVoiceSessionStore _store;
    private readonly VoiceHistoryWriter _history;
    private readonly VoiceDurationPricing _pricing;
    private readonly ContentGuardPipeline? _guard;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly AIAgent _agent;
    private readonly AgentSession _session;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _delegations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> _delegationTasks = new(StringComparer.Ordinal);
    private readonly LiveTranscriptLedger _ledger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _closeGate = new(1, 1);

    private ILiveVoiceSideband? _sideband;
    private Task _pump = Task.CompletedTask;
    private int _turns;
    private bool _closed;

    /// <summary>Creates a host.</summary>
    public LiveVoiceSessionHost(
        ILiveVoiceProvider provider,
        LiveVoiceSessionRequest request,
        AIAgent agent,
        AgentSession session,
        VoiceLiveOptions options,
        IVoiceSessionStore store,
        VoiceHistoryWriter history,
        VoiceDurationPricing pricing,
        ContentGuardPipeline? guard,
        ILogger logger,
        TimeProvider timeProvider)
    {
        _provider = provider;
        _request = request;
        _agent = agent;
        _session = session;
        _options = options;
        _store = store;
        _history = history;
        _pricing = pricing;
        _guard = guard;
        _logger = logger;
        _timeProvider = timeProvider;
        _ledger = new LiveTranscriptLedger(options.MaxTranscriptLedgerCharacters);

        Id = TraconId.NewId();
        StartedAt = timeProvider.GetUtcNow();
    }

    /// <summary>Gets Tracon's identifier for this session.</summary>
    public Guid Id { get; }

    /// <summary>Gets the tenant that owns the session.</summary>
    public string TenantId => _request.TenantId;

    /// <summary>Gets when the session was created.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>Gets the provider's identifier for the session.</summary>
    public string? ProviderSessionId { get; private set; }

    /// <summary>Gets the lifecycle state.</summary>
    public LiveVoiceSessionState State { get; private set; } = LiveVoiceSessionState.Pending;

    /// <summary>Gets the billable duration the provider reported.</summary>
    public decimal? LiveSeconds { get; private set; }

    /// <summary>Gets how many delegations became runs.</summary>
    public int Turns => _turns;

    /// <summary>Creates the provider session and returns the SDP answer for the media peer.</summary>
    /// <param name="sdpOffer">The media peer's offer.</param>
    /// <param name="voice">The requested output voice, when the caller named one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The provider's handle.</returns>
    public async Task<LiveVoiceSessionHandle> CreateAsync(
        string sdpOffer,
        string? voice,
        CancellationToken cancellationToken)
    {
        var handle = await _provider
            .CreateSessionAsync(
                new LiveVoiceCreateRequest
                {
                    SdpOffer = sdpOffer,
                    Options = new LiveVoiceSessionOptions
                    {
                        Voice = voice,
                        Instructions = _options.Instructions,
                        DelegationMode = _options.DelegationMode,
                    },
                },
                cancellationToken)
            .ConfigureAwait(false);

        ProviderSessionId = handle.ProviderSessionId;
        Model = handle.Model ?? _provider.ModelId;

        await SaveRecordAsync(endReason: null, CancellationToken.None).ConfigureAwait(false);

        return handle;
    }

    /// <summary>Gets the model the session is bound to.</summary>
    public string? Model { get; private set; }

    /// <summary>Attaches the sideband and starts the pump.</summary>
    /// <param name="cancellationToken">Cancels only the attach, not the pump.</param>
    /// <returns>The completion task.</returns>
    public async Task AttachAsync(CancellationToken cancellationToken)
    {
        var providerSessionId = ProviderSessionId
            ?? throw new InvalidOperationException("The session has not been created yet.");

        _sideband = await _provider.AttachAsync(providerSessionId, cancellationToken).ConfigureAwait(false);

        // 🚨 The state deliberately stays Pending. Attaching proves only that
        // Tracon reached the provider; it says nothing about whether the media
        // peer ever arrived. Measured: `session.started` fires the moment the
        // sideband connects, even when no browser is on the other end. The session
        // becomes Active on the first event that can only exist if media flowed —
        // otherwise the sweeper could never see an abandoned session and
        // VoiceSessionEndReason.Abandoned would be a promise nothing could keep.

        // 🚨 The pump deliberately does NOT take the request's cancellation token:
        // it outlives the request that created it, and binding it there would kill
        // the conversation the moment the HTTP response was written.
        _pump = Task.Run(() => PumpAsync(), CancellationToken.None);
    }

    /// <summary>Closes the session and writes its record.</summary>
    /// <param name="reason">Why it closed.</param>
    /// <returns>The completion task.</returns>
    public Task CloseAsync(VoiceSessionEndReason reason)
        => CloseCoreAsync(reason, waitForPump: true);

    /// <summary>Closes the host, optionally waiting for the receive pump.</summary>
    /// <param name="reason">Why the session ended.</param>
    /// <param name="waitForPump">
    /// Whether to wait for the receive pump to finish. The pump itself must pass
    /// <see langword="false"/> from its <c>finally</c> block or it would wait for
    /// the task that is currently executing the close.
    /// </param>
    private async Task CloseCoreAsync(VoiceSessionEndReason reason, bool waitForPump)
    {
        await _closeGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            State = LiveVoiceSessionState.Ended;

            // 🚨 The tenant is re-established HERE as well as on the pump. CloseAsync
            // is also called by the sweeper and by application shutdown, neither of
            // which runs inside the pump — and the history write below resolves the
            // tenant from ambient state. Without this, a session ended by the
            // duration cap or by a restart writes under the DEFAULT tenant, the
            // tenant-scoped update finds no row, and the transcript is lost in
            // silence. Same class as the pump's own scope, different path.
            using var tenant = AmbientTenantScope.Begin(_request.TenantId);

            // Shutdown order: cancel every delegation, wait for all of them, stop
            // the receive pump, flush the history, then write the session record.
            // Writing the history before the delegation tasks finish would race a
            // task still appending to the session.
            await StopDelegationsAsync().ConfigureAwait(false);

            // The receive pump may already have transcript frames queued in the
            // sideband. Stop the sideband first, then wait for the pump to finish
            // consuming what it has already received. Cancelling _lifetime before
            // that wait made an external close race the pump: the socket stopped
            // before the transcript reached _ledger, so FlushHistoryAsync saved an
            // empty history. The provider close is also the only bounded signal
            // available through ILiveVoiceSideband that tells ReceiveAsync to end.
            await DisposeSidebandAsync().ConfigureAwait(false);
            await _lifetime.CancelAsync().ConfigureAwait(false);

            if (waitForPump)
            {
                await _pump.ConfigureAwait(false);

                // The pump may have received a delegation event while the first
                // stop pass was waiting. No task can be added after the pump has
                // ended, so this second pass closes that small hand-off window
                // before history is flushed.
                await StopDelegationsAsync().ConfigureAwait(false);
            }

            await FlushHistoryAsync().ConfigureAwait(false);

            // 🚨 The record is written on EVERY path, a provider failure included.
            // A session that produced cost and left no trace is the worst outcome
            // available here.
            await SaveRecordAsync(reason, CancellationToken.None).ConfigureAwait(false);

        }
        finally
        {
            _closeGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync(VoiceSessionEndReason.ServerShutdown).ConfigureAwait(false);

        _lifetime.Dispose();
        _closeGate.Dispose();
    }

    private async Task PumpAsync()
    {
        // 🚨 Ambient state is established at the OUTERMOST point of the pump, before
        // any event is handled, so every delegation task started below inherits it.
        // The allowed direction is parent to child; nothing here reads state back
        // out of a child.
        using var tenant = AmbientTenantScope.Begin(_request.TenantId);
        using var attribution = AmbientRunAttributionScope.Begin(
            _request.CreatedBy,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["tracon.voice"] = "live-delegation" });

        var reason = VoiceSessionEndReason.Client;

        try
        {
            await foreach (var evt in _sideband!.ReceiveAsync(_lifetime.Token).ConfigureAwait(false))
            {
                switch (evt.Kind)
                {
                    case LiveVoiceEventKind.InputTranscript:
                        State = LiveVoiceSessionState.Active;

                        _ledger.Append(
                            LiveTranscriptRole.User,
                            evt.Text,
                            evt.StartMilliseconds ?? 0,
                            evt.EndMilliseconds ?? evt.StartMilliseconds ?? 0);

                        break;

                    case LiveVoiceEventKind.OutputTranscript:
                        State = LiveVoiceSessionState.Active;

                        _ledger.Append(
                            LiveTranscriptRole.Assistant,
                            evt.Text,
                            evt.StartMilliseconds ?? 0,
                            evt.EndMilliseconds ?? evt.StartMilliseconds ?? 0);

                        break;

                    case LiveVoiceEventKind.DelegationCreated when evt.DelegationId is { Length: > 0 } delegationId:
                        State = LiveVoiceSessionState.Active;

                        StartDelegation(delegationId, evt.OffsetMilliseconds ?? 0);

                        break;

                    case LiveVoiceEventKind.UsageUpdated when evt.Seconds is { } seconds:
                        // 🚨 The provider's own number. Tracon does not see the
                        // media and must not time the session with a wall clock.
                        LiveSeconds = seconds;

                        break;

                    case LiveVoiceEventKind.Closed:
                        if (evt.Seconds is { } closingSeconds)
                        {
                            LiveSeconds = closingSeconds;
                        }

                        // A close on a session that never saw media means nothing
                        // ever connected — that is an abandonment, not the provider
                        // ending a working conversation.
                        reason = State == LiveVoiceSessionState.Active
                            ? VoiceSessionEndReason.Provider
                            : VoiceSessionEndReason.Abandoned;

                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation(
                                "Live voice session {SessionId} was closed by the provider: {Reason}.",
                                _request.SessionId,
                                evt.Reason ?? "no reason given");
                        }

                        return;

                    case LiveVoiceEventKind.Error:
                        // The session is recorded as failed even if the stream ends
                        // cleanly afterwards: a provider error is why it ended.
                        reason = VoiceSessionEndReason.Error;

                        _logger.LogWarning(
                            "The provider reported an error on live voice session {SessionId}: {Error}.",
                            _request.SessionId,
                            evt.Text ?? "no detail given");

                        break;

                    case LiveVoiceEventKind.SessionStarted:
                    case LiveVoiceEventKind.AppendAccepted:
                    default:
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            reason = VoiceSessionEndReason.Client;
        }
        catch (Exception exception)
        {
            reason = VoiceSessionEndReason.Error;

            _logger.LogWarning(
                exception,
                "The sideband of live voice session {SessionId} failed.",
                _request.SessionId);
        }
        finally
        {
            if (!_closed)
            {
                // This code runs on _pump itself. CloseCoreAsync must not await
                // _pump here; the external close path does that after stopping the
                // sideband, which keeps the history flush ordered after the pump.
                await CloseCoreAsync(reason, waitForPump: false).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Stops the sideband without allowing cleanup to block history persistence.</summary>
    private async Task DisposeSidebandAsync()
    {
        if (_sideband is null)
        {
            return;
        }

        try
        {
            await _sideband.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "The sideband could not be closed cleanly.");
        }
    }

    private void StartDelegation(string delegationId, int offsetMilliseconds)
    {
        if (_closed)
        {
            return;
        }

        if (_delegations.Count >= _options.MaxConcurrentDelegations)
        {
            // 🚨 No run is opened. The provider does not get to decide how many
            // agent runs Tracon starts.
            _logger.LogWarning(
                "Live voice session {SessionId} refused a delegation: {Limit} are already running.",
                _request.SessionId,
                _options.MaxConcurrentDelegations);

            _ = Task.Run(
                () => NotifyBusyAsync(delegationId),
                CancellationToken.None);

            return;
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        cancellation.CancelAfter(_options.DelegationTimeout);

        if (!_delegations.TryAdd(delegationId, cancellation))
        {
            cancellation.Dispose();

            return;
        }

        _delegationTasks[delegationId] = Task.Run(
            () => RunDelegationAsync(delegationId, offsetMilliseconds, cancellation),
            CancellationToken.None);
    }

    /// <summary>Stops all delegation tasks currently known to the host.</summary>
    private async Task StopDelegationsAsync()
    {
        foreach (var cancellation in _delegations.Values)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }

        foreach (var task in _delegationTasks.Values)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "A delegation ended with an error while the session is closing.");
            }
        }
    }

    private async Task RunDelegationAsync(
        string delegationId,
        int offsetMilliseconds,
        CancellationTokenSource cancellation)
    {
        var runner = new LiveVoiceDelegationRunner(
            _agent,
            _session,
            _sideband!,
            _ledger,
            _options,
            _provider.MaxAppendCharacters,
            _guard,
            _request.SessionId,
            _logger);

        try
        {
            var runId = await runner.RunAsync(delegationId, offsetMilliseconds, cancellation.Token).ConfigureAwait(false);

            if (runId is not null)
            {
                Interlocked.Increment(ref _turns);
            }
        }
        catch (OperationCanceledException)
        {
            // A cancelled delegation still contributes what it already said, so the
            // next turn sees its own half sentence rather than a hole.
            if (runner.SpokenText is { Length: > 0 } partial)
            {
                await _history.RecordInterruptionAsync(_agent, _session, partial).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The delegation {DelegationId} of live voice session {SessionId} failed.",
                delegationId,
                _request.SessionId);
        }
        finally
        {
            if (_delegations.TryRemove(delegationId, out var removed))
            {
                removed.Dispose();
            }

            _delegationTasks.TryRemove(delegationId, out _);
        }
    }

    private async Task NotifyBusyAsync(string delegationId)
    {
        try
        {
            await _sideband!
                .AppendAsync(
                    new LiveVoiceAppend
                    {
                        Channel = LiveVoiceAppendChannel.Thinking,
                        DelegationId = delegationId,
                        Text = "Still finishing the previous request.",
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "The busy notice could not be appended.");
        }
    }

    private async Task FlushHistoryAsync()
    {
        if (!_options.PersistTranscript)
        {
            // The ledger stays in memory only. Delegation kept working with the same
            // context; nothing about the conversation becomes durable.
            return;
        }

        _ledger.Commit();

        // A snapshot, not the live list: the pump may still be appending.
        var messages = new List<ChatMessage>();

        foreach (var entry in _ledger.Snapshot())
        {
            messages.Add(new ChatMessage(
                entry.Role == LiveTranscriptRole.User ? ChatRole.User : ChatRole.Assistant,
                entry.Text));
        }

        if (messages.Count == 0)
        {
            return;
        }

        await _history.RecordTurnAsync(_agent, _session, messages).ConfigureAwait(false);
    }

    private async Task SaveRecordAsync(VoiceSessionEndReason? endReason, CancellationToken cancellationToken)
    {
        var cost = _pricing.Price(_provider.ProviderName, Model, LiveSeconds);

        var record = new VoiceSessionRecord
        {
            Id = Id,
            TenantId = _request.TenantId,
            SessionId = _request.SessionId,
            AgentName = _request.AgentName,
            StartedAt = StartedAt,
            EndedAt = endReason is null ? null : _timeProvider.GetUtcNow(),
            Turns = _turns,
            InputSeconds = null,
            OutputChars = null,
            EndReason = endReason,
            CreatedBy = _request.CreatedBy,
            Provider = _provider.ProviderName,
            Model = Model,
            LiveSeconds = LiveSeconds,
            Cost = cost,
        };

        try
        {
            await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Observability does not break functionality: a store that refuses the
            // write is logged and the session carries on.
            _logger.LogWarning(
                exception,
                "The record of live voice session {SessionId} could not be written.",
                _request.SessionId);
        }
    }
}
