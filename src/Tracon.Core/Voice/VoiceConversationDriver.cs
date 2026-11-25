using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>The immutable facts of a conversation connection.</summary>
/// <remarks>
/// The tenant and the session resolve while the connection is established and
/// stay <strong>constant</strong> for the whole connection. A tenant or
/// session value that arrives inside a frame is not accepted: to change the tenant
/// on a long-lived connection is to move authorization after the handshake.
/// </remarks>
internal sealed record VoiceConversationRequest
{
    /// <summary>Gets the tenant that was resolved while the connection was established.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the identifier of the agent session that the conversation runs on.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the actor that opened the connection, for the audit trail.</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Gets the authenticated caller, handed to the host's connection resolver; <see langword="null"/> when none was given.</summary>
    public ClaimsPrincipal? User { get; init; }
}

/// <summary>The two speech providers of one conversation connection.</summary>
/// <remarks>
/// Resolved once, while the connection is established, through the tenant's
/// provider checks; every turn of the connection uses the same pair.
/// </remarks>
/// <param name="Transcriber">The transcriber, carrying the tenant's credential when it has one.</param>
/// <param name="Synthesizer">The synthesizer, carrying the tenant's credential when it has one.</param>
internal readonly record struct VoiceConversationProviders(ISpeechTranscriber Transcriber, ISpeechSynthesizer Synthesizer);

/// <summary>
/// Runs the real-time conversation pipeline: audio comes in, text is transcribed,
/// the <strong>existing run path</strong> processes it, and audio goes out.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Option A.</strong> Audio is not proxied to the real-time API of the
/// provider; the streaming run path of the agent itself is called. The price is
/// latency, and the return is <em>everything</em>: the run record, spans, cost,
/// tool approval, tenancy and quota all work in a voice turn exactly as they do
/// elsewhere. Tracon is a control plane; it cannot suspend those promises for
/// voice.
/// </para>
/// <para>
/// The driver lives in <c>Tracon.Core</c> and is <strong>provider
/// independent</strong>: it knows only the <see cref="ISpeechTranscriber"/> and
/// <see cref="ISpeechSynthesizer"/> abstractions. ElevenLabs is one implementation.
/// </para>
/// <para>
/// The concurrency model is three rules: a <em>single</em> receive loop reads the
/// frames; turn processing runs on a separate task (otherwise <c>cancel</c> could
/// not be read until the turn ended); and every send passes through one gate,
/// because a <see cref="WebSocket"/> supports only one send at a time.
/// </para>
/// </remarks>
internal sealed class VoiceConversationDriver
{
    private const int ReceiveBufferSize = 16 * 1024;

    // How long the server waits for the client to answer its close frame before it drops the connection. A client that
    // reads nothing never answers, and without a bound the handler and the tenant's connection slot would be held for ever.
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    private readonly IAgentCatalog _catalog;
    private readonly AgentSessionManager _sessions;
    private readonly ISpeechTranscriber? _transcriber;
    private readonly ISpeechSynthesizer? _synthesizer;
    private readonly ISpeechProviderResolver _speechProviders;
    private readonly ChatHistoryProvider _chatHistory;
    private readonly IVoiceSessionStore _store;
    private readonly VoiceAudioArchive _audioArchive;
    private readonly VoiceConversationOptions _options;
    private readonly ILogger<VoiceConversationDriver> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly VoiceHistoryWriter _history;
    private readonly VoiceConversationEndRegistry _endRegistry;
    private readonly IVoicePricingReader? _pricing;
    private readonly VoiceHostContext _host;

    /// <summary>Initializes a new instance of the <see cref="VoiceConversationDriver"/> class.</summary>
    /// <param name="catalog">The agent catalog.</param>
    /// <param name="sessions">The session manager.</param>
    /// <param name="chatHistory">The chat history provider.</param>
    /// <param name="store">The conversation record store.</param>
    /// <param name="audioArchive">Stores the spoken audio when the host keeps it.</param>
    /// <param name="options">The conversation options.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="transcriber">
    /// The transcription provider. When it is <see langword="null"/> a conversation
    /// cannot open (see <see cref="IsReady"/>).
    /// </param>
    /// <param name="synthesizer">The synthesis provider.</param>
    /// <param name="speechProviders">
    /// Resolves the providers of each connection through the tenant's checks.
    /// </param>
    /// <param name="endRegistry">Finds the connection that runs a turn when the agent asks to end the conversation.</param>
    /// <param name="pricing">Prices the conversation for its record; <see langword="null"/> leaves the record unpriced.</param>
    /// <param name="host">What the host around the connections says: that it is stopping.</param>
    /// <param name="timeProvider">The time source.</param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    public VoiceConversationDriver(
        IAgentCatalog catalog,
        AgentSessionManager sessions,
        ChatHistoryProvider chatHistory,
        IVoiceSessionStore store,
        VoiceAudioArchive audioArchive,
        IOptions<VoiceConversationOptions> options,
        ILogger<VoiceConversationDriver> logger,
        ISpeechTranscriber? transcriber,
        ISpeechSynthesizer? synthesizer,
        ISpeechProviderResolver speechProviders,
        VoiceConversationEndRegistry endRegistry,
        IVoicePricingReader? pricing,
        VoiceHostContext host,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(chatHistory);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(audioArchive);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(speechProviders);
        ArgumentNullException.ThrowIfNull(endRegistry);
        ArgumentNullException.ThrowIfNull(host);

        _catalog = catalog;
        _sessions = sessions;
        _chatHistory = chatHistory;
        _store = store;
        _audioArchive = audioArchive;
        _options = options.Value;
        _logger = logger;
        _transcriber = transcriber;
        _synthesizer = synthesizer;
        _speechProviders = speechProviders;
        _endRegistry = endRegistry;
        _pricing = pricing;
        _host = host;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _history = new VoiceHistoryWriter(chatHistory, sessions, logger);

        Limiter = new VoiceConnectionLimiter(_options.MaxConcurrentConnectionsPerTenant);
    }

    /// <summary>Gets the per-tenant concurrent connection limiter.</summary>
    /// <remarks>
    /// The endpoint reserves a slot <strong>before it upgrades</strong> the socket:
    /// when the limit is full the client sees an HTTP error. To close the socket
    /// after the upgrade is a far worse way to tell the client the reason.
    /// </remarks>
    public VoiceConnectionLimiter Limiter { get; }

    /// <summary>
    /// Gets a value that indicates whether both providers that a conversation needs are registered.
    /// </summary>
    /// <remarks>
    /// Transcription <em>and</em> synthesis are both necessary: with only one of them
    /// the conversation would be one way, and that is not a conversation.
    /// </remarks>
    public bool IsReady => _transcriber is not null && _synthesizer is not null;

    /// <summary>Gets a value that indicates whether the audio is stored.</summary>
    /// <remarks>
    /// The user interface <strong>shows</strong> this value to the user; the audio is
    /// never recorded silently.
    /// </remarks>
    public bool PersistAudio => _options.PersistAudio;

    /// <summary>
    /// Resolves the providers of one connection through the current tenant's checks.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The transcriber and the synthesizer the connection uses.</returns>
    /// <exception cref="TraconTenantProviderRefusedException">The tenant's checks refuse one of the providers.</exception>
    /// <remarks>
    /// The endpoint calls this BEFORE it reserves a slot and upgrades the socket: a
    /// refused tenant sees an HTTP error, holds no slot, and no audio reaches a
    /// provider. Both providers are checked; audio goes to the transcriber and the
    /// answer's text to the synthesizer.
    /// </remarks>
    public async ValueTask<VoiceConversationProviders> ResolveProvidersAsync(CancellationToken cancellationToken)
    {
        var transcriber = await _speechProviders.GetTranscriberAsync(null, cancellationToken).ConfigureAwait(false);
        var synthesizer = await _speechProviders.GetSynthesizerAsync(null, cancellationToken).ConfigureAwait(false);

        return new VoiceConversationProviders(transcriber, synthesizer);
    }

    /// <summary>Runs one conversation connection from start to end.</summary>
    /// <param name="socket">The upgraded socket.</param>
    /// <param name="request">The immutable facts of the connection.</param>
    /// <param name="providers">The providers <see cref="ResolveProvidersAsync"/> returned for the connection.</param>
    /// <param name="cancellationToken">The server shutdown token.</param>
    /// <returns>A task that completes when the connection closes.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public async Task RunAsync(
        WebSocket socket,
        VoiceConversationRequest request,
        VoiceConversationProviders providers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(request);

        if (providers.Transcriber is null || providers.Synthesizer is null)
        {
            throw new ArgumentException(
                "Resolve the providers with ResolveProvidersAsync before running the connection.",
                nameof(providers));
        }

        using var connection = new VoiceConnection(this, socket, request, providers.Transcriber, providers.Synthesizer);

        await connection.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A received WebSocket frame.</summary>
    private readonly record struct VoiceFrame(WebSocketMessageType Type, ReadOnlyMemory<byte> Payload)
    {
        public static VoiceFrame Close => new(WebSocketMessageType.Close, ReadOnlyMemory<byte>.Empty);

        public bool IsClose => Type == WebSocketMessageType.Close;

        public bool IsBinary => Type == WebSocketMessageType.Binary;
    }

    /// <summary>The state and the loop of a single connection.</summary>
    /// <remarks>
    /// It is a separate class for one reason: per-connection state (the buffer, the
    /// session, the counters) cannot live in the fields of the driver — the driver is
    /// a singleton and every connection shares it.
    /// </remarks>
    private sealed class VoiceConnection(
        VoiceConversationDriver driver,
        WebSocket socket,
        VoiceConversationRequest request,
        ISpeechTranscriber transcriber,
        ISpeechSynthesizer synthesizer) : IDisposable
    {
        private readonly SemaphoreSlim _sendGate = new(1, 1);

        /// <summary>
        /// The lock of the state machine. The lock <em>is the object itself</em>: a
        /// separate <c>object</c> field would trigger MA0158, and the state machine
        /// needs no other lock.
        /// </summary>
        private readonly VoiceConversationStateMachine _state = new();
        private readonly VoiceConversationOptions _options = driver._options;
        private readonly byte[] _receiveBuffer = new byte[ReceiveBufferSize];

        private VoiceUtteranceBuffer? _buffer;
        private AIAgent? _agent;
        private AgentSession? _session;
        private string _agentName = string.Empty;
        private string? _voiceId;

        // What the host's resolver decided (null / false without one): the parameter values every turn binds, whether the
        // run reads them as the host's own, and how the conversation opens.
        private IReadOnlyDictionary<string, string>? _parameters;
        private bool _hostDecided;
        private string? _greeting;
        private string? _openingPrompt;

        private decimal _inputSeconds;
        private long _outputChars;
        private string? _transcriptionModel;

        // Set by the end_conversation tool from inside a run, read by the turn task when the answer is spoken.
        private volatile bool _endRequested;

        // Why the connection will close; the first decision stands, and it is guarded by the state lock. Decided by the agent,
        // a limit, the host stopping or a failed start; a close the client starts is decided only when nothing else was.
        private VoiceSessionEndReason? _endReason;

        // The limit the host's resolver set for this connection (null: the global one), and whether it is what ended it.
        private TimeSpan? _hostLimit;
        private bool _hostLimitReached;

        // Completed when something other than a frame ended the conversation (the agent, the host stopping), to wake the
        // receive loop. The pending read is never cancelled: cancelling a read aborts a real socket and resets the
        // connection, so the close the client should read never goes out.
        private readonly TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<VoiceFrame>? _pendingReceive;

        private Task _turn = Task.CompletedTask;
        private CancellationTokenSource? _turnCancellation;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var started = driver._timeProvider.GetUtcNow();
            var recordId = TraconId.NewId();
            var reason = VoiceSessionEndReason.Client;

            // A host that stops ends the conversation with its own reason and wakes the loop that waits for a frame.
            var stopping = driver._host.OnStopping(HostIsStopping);

            try
            {
                reason = await PumpAsync(started, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The request was aborted: the client is gone, or the host cut the connection at the end of its shutdown wait
                // (the host's own stop decided the reason before that).
                reason = VoiceSessionEndReason.Client;
            }
            catch (WebSocketException exception)
            {
                // The client cut the connection abruptly. This is not a server error; the record is still written.
                reason = VoiceSessionEndReason.Client;
                driver._logger.LogDebug(exception, "The voice socket closed unexpectedly.");
            }
            catch (Exception exception) when (exception is TraconException or InvalidOperationException or JsonException)
            {
                reason = VoiceSessionEndReason.Error;
                driver._logger.LogError(exception, "The voice connection closed with an error.");
            }
            finally
            {
                await stopping.DisposeAsync().ConfigureAwait(false);

                // 🚨 The first decision stands. A client that drops the connection the moment it reads `ended` makes the read
                // fail, and the reason of that failure must not replace the one that was decided before the frame went out.
                reason = GetEndReason() ?? reason;

                await ShutdownAsync(recordId, started, reason).ConfigureAwait(false);
            }
        }

        /// <summary>The host began to stop: the conversation ends and the loop that waits for a frame wakes.</summary>
        private void HostIsStopping()
        {
            SetEndReason(VoiceSessionEndReason.ServerShutdown);
            _wake.TrySetResult();
        }

        /// <summary>The single loop that reads the frames.</summary>
        /// <remarks>
        /// One read stays pending and is never cancelled by this loop. What ends the wait is the first of the read, the wake and
        /// a timer on the host's clock; a timer that fires says which limit was reached.
        /// </remarks>
        private async Task<VoiceSessionEndReason> PumpAsync(DateTimeOffset started, CancellationToken cancellationToken)
        {
            while (!IsClosed() && socket.State == WebSocketState.Open)
            {
                if (GetEndReason() is { } decided)
                {
                    return decided;
                }

                // The limit of a connection is the host's, once its resolver has set one, and otherwise the global one.
                var deadline = started + (_hostLimit ?? _options.MaxConnectionDuration);
                var untilDeadline = deadline - driver._timeProvider.GetUtcNow();

                if (untilDeadline <= TimeSpan.Zero)
                {
                    return LimitReached();
                }

                var wait = untilDeadline < _options.IdleTimeout ? untilDeadline : _options.IdleTimeout;
                var durationIsNext = wait == untilDeadline;

                _pendingReceive ??= ReceiveAsync(cancellationToken);

                using var timerCancellation = new CancellationTokenSource();
                var timer = Task.Delay(wait, driver._timeProvider, timerCancellation.Token);

                await Task.WhenAny(_pendingReceive, _wake.Task, timer).ConfigureAwait(false);
                await timerCancellation.CancelAsync().ConfigureAwait(false);

                // What woke the loop was decided before it woke it (the agent, the host), so that decision answers.
                if (_wake.Task.IsCompleted)
                {
                    return GetEndReason() ?? VoiceSessionEndReason.Client;
                }

                if (!_pendingReceive.IsCompleted)
                {
                    // The timer fired. The wait we used tells which limit was reached; the two limits close differently.
                    return durationIsNext ? LimitReached() : IdleLimitReached();
                }

                var frame = await _pendingReceive.ConfigureAwait(false);

                _pendingReceive = null;

                if (frame.IsClose)
                {
                    Stop();
                    return VoiceSessionEndReason.Client;
                }

                if (frame.IsBinary)
                {
                    HandleAudio(frame.Payload.Span);
                    continue;
                }

                if (await HandleControlAsync(frame.Payload, cancellationToken).ConfigureAwait(false))
                {
                    return VoiceSessionEndReason.Client;
                }
            }

            return VoiceSessionEndReason.Client;
        }

        private VoiceSessionEndReason IdleLimitReached()
        {
            TrySetEndReason(VoiceSessionEndReason.IdleTimeout);

            return VoiceSessionEndReason.IdleTimeout;
        }

        /// <summary>The connection's time limit was reached; the host's own limit tells the client not to come back.</summary>
        private VoiceSessionEndReason LimitReached()
        {
            if (TrySetEndReason(VoiceSessionEndReason.DurationLimit) && _hostLimit is not null)
            {
                _hostLimitReached = true;
            }

            return VoiceSessionEndReason.DurationLimit;
        }

        /// <summary>Appends a binary frame to the current utterance.</summary>
        private void HandleAudio(ReadOnlySpan<byte> payload)
        {
            bool listening;

            lock (_state)
            {
                listening = _state.Audio() == VoiceTransitionOutcome.Accepted;
            }

            if (!listening || _buffer is null)
            {
                return;
            }

            _buffer.Append(payload);

            if (!_buffer.IsFull)
            {
                return;
            }

            // 🚨 Safety net: the VAD of the client never fired. The utterance closes
            // on its own; without this the buffer fills, the audio that follows is
            // dropped silently and the conversation is never answered.
            if (driver._logger.IsEnabled(LogLevel.Information))
            {
                driver._logger.LogInformation(
                    "The voice utterance reached the limit and closed on its own ({Bytes} bytes).",
                    _buffer.Length);
            }

            // The result is discarded on purpose: a full buffer always has audio to
            // take, so this path cannot be the one that owes an `idle` frame - and
            // it holds a span, so it could not await a send anyway.
            _ = Commit();
        }

        /// <summary>Handles a text frame.</summary>
        /// <returns><see langword="true"/> when the connection is about to close.</returns>
        private async Task<bool> HandleControlAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            VoiceClientMessage? message;

            try
            {
                message = JsonSerializer.Deserialize(
                    payload.Span,
                    VoiceConversationJsonContext.Default.VoiceClientMessage);
            }
            catch (JsonException)
            {
                await SendAsync(Error("The control message is not valid JSON.")).ConfigureAwait(false);
                return false;
            }

            switch (message?.Type)
            {
                case VoiceConversationProtocol.ClientStart:
                    await StartAsync(message, cancellationToken).ConfigureAwait(false);
                    return false;

                case VoiceConversationProtocol.ClientCommit:
                    if (Commit())
                    {
                        await SendAsync(Idle()).ConfigureAwait(false);
                    }

                    return false;

                case VoiceConversationProtocol.ClientCancel:
                    Cancel();
                    return false;

                case VoiceConversationProtocol.ClientStop:
                    Stop();
                    return true;

                default:
                    await SendAsync(Error($"Unknown message: '{message?.Type}'.")).ConfigureAwait(false);
                    return false;
            }
        }

        private async Task StartAsync(VoiceClientMessage message, CancellationToken cancellationToken)
        {
            bool accepted;

            lock (_state)
            {
                accepted = _state.Start() == VoiceTransitionOutcome.Accepted;
            }

            if (!accepted)
            {
                // A second `start` would mean a change of agent.
                await FailAsync("The conversation is already started; the agent does not change during the connection.").ConfigureAwait(false);
                return;
            }

            if (!VoiceAudioFormats.IsKnown(message.InputFormat))
            {
                await FailAsync($"Unknown audio format: '{message.InputFormat}'.").ConfigureAwait(false);
                return;
            }

            if (message.Agent is not { Length: > 0 } agentName)
            {
                await FailAsync("The 'agent' field is required.").ConfigureAwait(false);
                return;
            }

            // The agent is named now, and the caller's right to run it is asked before the host's own code is: a connection that is
            // turned away never reaches the resolver, and the handshake could not ask, because it does not know the agent yet.
            if (await RunRefusalAsync(agentName, cancellationToken).ConfigureAwait(false) is { } refusal)
            {
                SetEndReason(VoiceSessionEndReason.Error);
                await FailAsync(refusal).ConfigureAwait(false);
                return;
            }

            if (driver._options.ResolveConnection is { } resolveConnection)
            {
                if (!await ResolveConnectionAsync(resolveConnection, agentName, message.VoiceId, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
            else
            {
                _voiceId = message.VoiceId ?? _options.VoiceId;
            }

            try
            {
                _agent = await driver._catalog.ResolveAsync(agentName, ResolveOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (TraconException exception)
            {
                // Values the host's own resolver supplied that the agent refuses are the host's mistake, not the client's.
                if (_hostDecided)
                {
                    SetEndReason(VoiceSessionEndReason.Error);
                }

                await FailAsync($"The agent could not be compiled: {exception.Message}").ConfigureAwait(false);
                return;
            }

            if (_agent is null)
            {
                await FailAsync($"There is no agent named '{agentName}'.").ConfigureAwait(false);
                return;
            }

            try
            {
                _session = await driver._sessions
                    .GetOrCreateSessionAsync(_agent, request.SessionId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TraconException exception)
            {
                await FailAsync(exception.Message).ConfigureAwait(false);
                return;
            }

            _agentName = agentName;

            _buffer = new VoiceUtteranceBuffer(
                message.InputFormat ?? VoiceAudioFormats.WebmOpus,
                _options.InputSampleRate,
                _options.MaxUtteranceBytes,
                _options.MaxUtteranceDuration);

            await SendAsync(new VoiceServerMessage
            {
                Type = VoiceConversationProtocol.ServerReady,
                Agent = _agentName,
                SessionId = request.SessionId,
                PersistAudio = _options.PersistAudio,
            }).ConfigureAwait(false);

            await BeginOpeningAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The resolve options of every turn: the host's parameter values, or none.</summary>
        private AgentResolveOptions? ResolveOptions
            => _parameters is null ? null : new AgentResolveOptions { Parameters = _parameters };

        /// <summary>
        /// Asks the host's resolver how this connection runs and keeps what it answered; the front end's voice is an input of
        /// the resolver and nothing more.
        /// </summary>
        /// <returns><see langword="true"/> when the connection goes on; <see langword="false"/> when it was refused.</returns>
        /// <remarks>
        /// The agent is known to exist before the host's code is asked about it: a name nothing carries is a plain "no such
        /// agent", not a failure of the resolver. A resolver that fails ends the connection and its text never reaches the
        /// front end.
        /// </remarks>
        private async Task<bool> ResolveConnectionAsync(
            Func<VoiceConnectionRequest, CancellationToken, ValueTask<VoiceConnectionSettings?>> resolve,
            string agentName,
            string? clientVoiceId,
            CancellationToken cancellationToken)
        {
            if (await driver._catalog.DescribeAsync(agentName, cancellationToken).ConfigureAwait(false) is null)
            {
                await FailAsync($"There is no agent named '{agentName}'.").ConfigureAwait(false);
                return false;
            }

            try
            {
                var settings = await resolve(
                        new VoiceConnectionRequest
                        {
                            AgentName = agentName,
                            SessionId = request.SessionId,
                            TenantId = request.TenantId,
                            User = request.User ?? new ClaimsPrincipal(new ClaimsIdentity()),
                            ClientVoiceId = clientVoiceId,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                Accept(settings);

                return true;
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                driver._logger.LogError(
                    exception,
                    "The voice connection resolver failed for agent '{AgentName}', session {SessionId}; the conversation was not started.",
                    agentName,
                    request.SessionId);

                SetEndReason(VoiceSessionEndReason.Error);
                await FailAsync("The conversation could not be started.").ConfigureAwait(false);

                return false;
            }
        }

        /// <summary>Checks what the resolver returned and keeps a copy of it.</summary>
        /// <exception cref="InvalidOperationException">The result is not one a connection can run on.</exception>
        private void Accept(VoiceConnectionSettings? settings)
        {
            _hostDecided = true;
            _voiceId = NullIfBlank(settings?.VoiceId) ?? _options.VoiceId;

            var greeting = NullIfBlank(settings?.Greeting);
            var openingPrompt = NullIfBlank(settings?.OpeningPrompt);

            if (greeting is not null && openingPrompt is not null)
            {
                throw new InvalidOperationException(
                    "The voice connection resolver returned both a Greeting and an OpeningPrompt; a conversation opens one way.");
            }

            if (settings?.MaxDuration is { } maxDuration
                && (maxDuration <= TimeSpan.Zero || maxDuration > _options.MaxConnectionDuration))
            {
                // Shortening or lengthening it silently would hide the resolver's mistake; the global limit is the ceiling.
                throw new InvalidOperationException(
                    "The voice connection resolver returned a MaxDuration that is not greater than zero and at most MaxConnectionDuration.");
            }

            _hostLimit = settings?.MaxDuration;
            _greeting = greeting;
            _openingPrompt = openingPrompt;

            if (settings?.Parameters is not { Count: > 0 } supplied)
            {
                return;
            }

            // A copy, made inside the same guard as the call: the resolver may keep its dictionary and change it while it is
            // read. A value that is not text cannot be checked against the schema, and a run must not start on a guess.
            var copy = new Dictionary<string, string>(supplied.Count, StringComparer.Ordinal);

            foreach (var (name, value) in supplied)
            {
                if (name is null || value is null)
                {
                    throw new InvalidOperationException("The voice connection resolver returned a parameter name or value that is null.");
                }

                copy[name] = value;
            }

            _parameters = copy;
        }

        private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        /// <summary>
        /// Asks whether the caller may start a run of the agent in this connection's session: the registered authorization
        /// handler, the session's owner and the tenant's quota, the three questions every other way to start a run asks.
        /// </summary>
        /// <returns>The text to refuse with, which is the text the HTTP endpoints give; <see langword="null"/> when the run may start.</returns>
        /// <remarks>
        /// A check that fails (a handler or a quota store that throws) refuses: a gate that opens on an exception is not a
        /// gate. The exception stays in the log; the client reads a fixed sentence.
        /// </remarks>
        private async Task<string?> RunRefusalAsync(string agentName, CancellationToken cancellationToken)
        {
            try
            {
                var decision = await driver._host.CheckRunAsync(agentName, request.SessionId, cancellationToken).ConfigureAwait(false);

                return decision.IsAllowed ? null : decision.Reason ?? RunAdmissionRules.HandlerDeniedDetail;
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                driver._logger.LogError(
                    exception,
                    "The run check failed for agent '{AgentName}', session {SessionId}; the run is refused.",
                    agentName,
                    request.SessionId);

                return RunAdmissionRules.FailedCheckDetail;
            }
        }

        private void SetEndReason(VoiceSessionEndReason reason) => TrySetEndReason(reason);

        /// <summary>Records why the connection ends unless something already decided it.</summary>
        /// <returns><see langword="true"/> when this call decided it.</returns>
        private bool TrySetEndReason(VoiceSessionEndReason reason)
        {
            lock (_state)
            {
                if (_endReason is not null)
                {
                    return false;
                }

                _endReason = reason;

                return true;
            }
        }

        private VoiceSessionEndReason? GetEndReason()
        {
            lock (_state)
            {
                return _endReason;
            }
        }

        /// <summary>Closes the current utterance and starts the turn.</summary>
        /// <returns>
        /// <see langword="true"/> when the commit was accepted but there was no
        /// audio to take, so the caller owes the client an
        /// <see cref="VoiceConversationProtocol.ServerIdle"/> frame. The
        /// buffer-full caller never sees this - that buffer is full by definition.
        /// </returns>
        private bool Commit()
        {
            lock (_state)
            {
                if (_state.Commit() != VoiceTransitionOutcome.Accepted)
                {
                    // The state machine refused: no turn was closed, so the client
                    // is not waiting on one either. Saying `idle` here would push a
                    // client that is mid-turn back to listening.
                    return false;
                }
            }

            if (_buffer?.Take() is not { } utterance)
            {
                // `commit` arrived before any audio. This is not an error (a short
                // cough can also trigger the VAD of the client); go back to listening.
                //
                // 🚨 Going back in SILENCE is what hung the panel: the client puts
                // itself in 'thinking' the moment the user presses send, and only a
                // server frame moves it back AND restarts its recorder. Measured
                // through the browser suite - pressing send inside the recorder's
                // first 250 ms timeslice reaches here, and the panel never recovered.
                FinishTurn(counted: false);
                return true;
            }

            // 🚨 The token of the previous turn is disposed HERE, not inside the turn
            // task: the task writes the `done` frame AFTER FinishTurn, and to cancel a
            // token that was already disposed at that moment produced an
            // ObjectDisposedException. Commit, Cancel and shutdown all run from the
            // same receive loop, so there is no race.
            _turnCancellation?.Dispose();
            _turnCancellation = new CancellationTokenSource();
            _turn = ProcessTurnAsync(utterance, _turnCancellation.Token);
            return false;
        }

        /// <summary>Handles an interruption (barge-in).</summary>
        /// <remarks>
        /// The state <strong>does not change</strong> here. Only the turn task
        /// returns to listening (<see cref="FinishTurn"/>); otherwise the client could
        /// send a new <c>commit</c> at once and two turns would run at the same time.
        /// </remarks>
        private void Cancel()
        {
            lock (_state)
            {
                if (_state.Cancel() != VoiceTransitionOutcome.Accepted)
                {
                    return;
                }
            }

            _buffer?.Clear();
            _turnCancellation?.Cancel();
        }

        /// <summary>Processes one conversation turn from start to end.</summary>
        private async Task ProcessTurnAsync(VoiceUtterance utterance, CancellationToken cancellationToken)
        {
            var spoken = new StringBuilder();
            var cancelled = false;
            var failed = false;
            string? attachmentId = null;

            try
            {
                // Before the audio goes to a provider: a turn that may not run costs nothing. The turn fails like any other
                // (the client reads `error`, then `done`) and the conversation goes on; it is not counted, because nothing ran.
                if (await RunRefusalAsync(_agentName, cancellationToken).ConfigureAwait(false) is { } refusal)
                {
                    await SendAsync(Error(refusal)).ConfigureAwait(false);
                    FinishTurn(counted: false);

                    await SendAsync(new VoiceServerMessage
                    {
                        Type = VoiceConversationProtocol.ServerDone,
                        Cancelled = false,
                        Turn = _state.Turns,
                    }).ConfigureAwait(false);

                    return;
                }

                var transcript = await TranscribeAsync(utterance, cancellationToken).ConfigureAwait(false);
                var text = transcript?.Text?.Trim() ?? string.Empty;

                _transcriptionModel ??= transcript?.ModelId;

                await SendAsync(new VoiceServerMessage
                {
                    Type = VoiceConversationProtocol.ServerTranscript,
                    Text = text,
                    Final = true,
                }).ConfigureAwait(false);

                if (text.Length == 0)
                {
                    // 🚨 Same class as the empty commit in `Commit`, and likelier in
                    // production: the audio arrived, but the transcriber heard
                    // nothing usable (background noise, a knock on the desk). The
                    // client IGNORES a transcript frame whose text is empty, and the
                    // `done` frame below is skipped by this return - so without this
                    // the panel stays in 'thinking' with a stopped recorder.
                    FinishTurn(counted: false);
                    await SendAsync(Idle()).ConfigureAwait(false);
                    return;
                }

                _inputSeconds += (decimal)(transcript?.AudioDuration ?? utterance.Duration ?? TimeSpan.Zero)
                    .TotalSeconds;

                lock (_state)
                {
                    _state.BeginResponse();
                }

                attachmentId = await RespondAsync(text, spoken, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
            }
            catch (Exception exception) when (exception is TraconException or HttpRequestException or InvalidOperationException or OperationCanceledException)
            {
                // An OperationCanceledException reaches here only when it is NOT ours:
                // a model or speech call that timed out on its own is a failed turn,
                // not a barge-in.
                //
                // HttpRequestException and InvalidOperationException are foreign here (the
                // former can carry a host:port); only TraconException's own message is ours.
                failed = true;

                var correlationId = SafeErrorText.NewCorrelationId();
                driver._logger.LogError(exception, "The voice turn failed. (ref: {CorrelationId})", correlationId);
                await SendAsync(Error(SafeErrorText.ForPersistence(exception, correlationId))).ConfigureAwait(false);
            }

            if (cancelled)
            {
                await driver._history.RecordInterruptionAsync(_agent!, _session!, spoken.ToString()).ConfigureAwait(false);
            }

            FinishTurn(counted: true);

            await SendAsync(new VoiceServerMessage
            {
                Type = VoiceConversationProtocol.ServerDone,
                Cancelled = cancelled,
                Turn = _state.Turns,
                AttachmentId = attachmentId,
            }).ConfigureAwait(false);

            await EndIfTheAgentAskedAsync(interruptedOrFailed: cancelled || failed).ConfigureAwait(false);
        }

        /// <summary>Opens the conversation, when the host asked for it and the session has not begun yet.</summary>
        /// <remarks>
        /// Only a session without history is opened. A client that reconnects to a conversation in progress would otherwise be
        /// greeted again, and a greeting is the host's text on the host's terms, not a reply. A history that cannot be read is
        /// treated as "do not open": talking over a conversation that may exist is the worse mistake.
        /// </remarks>
        private async Task BeginOpeningAsync(CancellationToken cancellationToken)
        {
            if (_greeting is null && _openingPrompt is null)
            {
                return;
            }

            try
            {
                var history = await ChatHistoryReader
                    .ReadAsync(_agent!, _session!, driver._chatHistory, cancellationToken)
                    .ConfigureAwait(false);

                if (history.Count > 0)
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is TraconException or InvalidOperationException or NotSupportedException or JsonException)
            {
                driver._logger.LogWarning(exception, "The session history could not be read, so the conversation was not opened.");
                return;
            }

            bool begun;

            lock (_state)
            {
                begun = _state.BeginGreeting() == VoiceTransitionOutcome.Accepted;
            }

            if (!begun)
            {
                return;
            }

            _turnCancellation?.Dispose();
            _turnCancellation = new CancellationTokenSource();
            _turn = ProcessOpeningAsync(_turnCancellation.Token);
        }

        /// <summary>Speaks the greeting, or runs the opening prompt, as the first turn of the connection.</summary>
        private async Task ProcessOpeningAsync(CancellationToken cancellationToken)
        {
            var spoken = new StringBuilder();
            var cancelled = false;
            var failed = false;
            string? attachmentId = null;

            try
            {
                // The opening runs in the Responding state that BeginGreeting entered; nothing else moves it. A greeting is not a
                // run and asks nothing; an opening prompt is one, and is asked like a turn.
                if (_greeting is { } greeting)
                {
                    attachmentId = await SayGreetingAsync(greeting, spoken, cancellationToken).ConfigureAwait(false);
                }
                else if (await RunRefusalAsync(_agentName, cancellationToken).ConfigureAwait(false) is { } refusal)
                {
                    failed = true;
                    await SendAsync(Error(refusal)).ConfigureAwait(false);
                }
                else
                {
                    attachmentId = await RespondAsync(_openingPrompt!, spoken, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
            }
            catch (Exception exception) when (exception is TraconException or HttpRequestException or InvalidOperationException or OperationCanceledException)
            {
                failed = true;

                var correlationId = SafeErrorText.NewCorrelationId();
                driver._logger.LogError(exception, "The voice opening failed. (ref: {CorrelationId})", correlationId);
                await SendAsync(Error(SafeErrorText.ForPersistence(exception, correlationId))).ConfigureAwait(false);
            }

            if (cancelled)
            {
                await driver._history.RecordInterruptionAsync(_agent!, _session!, spoken.ToString()).ConfigureAwait(false);
            }

            // The opening is not a turn of the user's: it is not counted, and `done` says so with the turn number it found.
            FinishTurn(counted: false);

            await SendAsync(new VoiceServerMessage
            {
                Type = VoiceConversationProtocol.ServerDone,
                Cancelled = cancelled,
                Turn = _state.Turns,
                AttachmentId = attachmentId,
            }).ConfigureAwait(false);

            await EndIfTheAgentAskedAsync(interruptedOrFailed: cancelled || failed).ConfigureAwait(false);
        }

        /// <summary>Says a fixed text the way an answer is said, and records that it was said.</summary>
        /// <returns>The attachment identifier when the audio was stored.</returns>
        private async Task<string?> SayGreetingAsync(string greeting, StringBuilder spoken, CancellationToken cancellationToken)
        {
            spoken.Append(greeting);

            await SendAsync(new VoiceServerMessage
            {
                Type = VoiceConversationProtocol.ServerText,
                Delta = greeting,
            }).ConfigureAwait(false);

            var segmenter = CreateSegmenter();
            var spokenCharacters = 0;

            using var audio = _options.PersistAudio ? new MemoryStream() : null;

            foreach (var segment in segmenter.Append(greeting))
            {
                spokenCharacters = await SpeakAsync(segment, spokenCharacters, audio, cancellationToken).ConfigureAwait(false);
            }

            if (segmenter.Flush() is { Length: > 0 } tail)
            {
                spokenCharacters = await SpeakAsync(tail, spokenCharacters, audio, cancellationToken).ConfigureAwait(false);
            }

            // The agent has to know it greeted: the next turn's model reads this history.
            await driver._history
                .RecordTurnAsync(_agent!, _session!, [new ChatMessage(ChatRole.Assistant, greeting)])
                .ConfigureAwait(false);

            return audio is null
                ? null
                : await PersistAudioAsync(audio, cancellationToken).ConfigureAwait(false);
        }

        private VoiceSpeechSegmenter CreateSegmenter()
            // A segment longer than the synthesizer accepts would be refused mid-turn, after the captions already streamed. A
            // limit that is not positive is a broken implementation; the default limit then applies.
            => new(
                synthesizer.MaxCharactersPerRequest is > 0 and var limit
                    ? Math.Min(VoiceSpeechSegmenter.DefaultMaxSegmentLength, limit)
                    : VoiceSpeechSegmenter.DefaultMaxSegmentLength);

        /// <summary>
        /// Ends the conversation when the answer just spoken asked for it: the server says so and closes the connection.
        /// </summary>
        /// <remarks>
        /// An answer the user cut off (barge-in) or that failed does not end the conversation: the agent asked to end it
        /// after saying goodbye, and the user interrupted that goodbye.
        /// </remarks>
        private async Task EndIfTheAgentAskedAsync(bool interruptedOrFailed)
        {
            if (!_endRequested || interruptedOrFailed)
            {
                _endRequested = false;
                return;
            }

            _endRequested = false;

            // 🚨 Decided BEFORE the frame goes out: a client may drop the connection the moment it reads `ended`, and the
            // failed read that follows must find the reason already written.
            SetEndReason(VoiceSessionEndReason.Agent);

            lock (_state)
            {
                _state.Stop();
            }

            await SendAsync(new VoiceServerMessage
            {
                Type = VoiceConversationProtocol.ServerEnded,
                Reason = VoiceConversationProtocol.EndedByAgent,
            }).ConfigureAwait(false);

            // The receive loop is waiting for the next frame; waking it is what lets the connection close.
            _wake.TrySetResult();
        }

        private async Task<SpeechTranscript?> TranscribeAsync(
            VoiceUtterance utterance,
            CancellationToken cancellationToken)
        {
            using var audio = new MemoryStream(utterance.Data, writable: false);

            return await transcriber
                .TranscribeAsync(audio, utterance.MediaType, options: null, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Runs the agent, streams the text as captions and speaks it sentence by
        /// sentence.
        /// </summary>
        /// <returns>The attachment identifier when the audio was stored.</returns>
        private async Task<string?> RespondAsync(
            string prompt,
            StringBuilder spoken,
            CancellationToken cancellationToken)
        {
            var runId = TraconId.NewId();

            await SendAsync(new VoiceServerMessage
            {
                Type = VoiceConversationProtocol.ServerRunStarted,
                RunId = runId.ToString("D"),
            }).ConfigureAwait(false);

            var segmenter = CreateSegmenter();
            var spokenCharacters = 0;

            // The run is registered BEFORE it starts: the end_conversation tool finds this connection through the run's
            // identifier, and only a run this connection started can end this connection.
            _endRequested = false;
            using var endLease = driver._endRegistry.Register(runId, () => _endRequested = true);

            using var audio = _options.PersistAudio ? new MemoryStream() : null;

            // The agent is resolved again for every turn. A compiled agent embeds
            // its tool objects, and an MCP tool object calls the connection it was
            // discovered on: the agent resolved at `start` kept calling a closed
            // connection for the rest of the connection after its server was edited
            // or deleted. A cache hit while nothing changed; the name stays the one
            // `start` named. The newest instance also writes the history.
            _agent = await driver._catalog.ResolveAsync(_agentName!, ResolveOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new TraconException($"There is no agent named '{_agentName}'.");

            // A harness loop hands over its final iteration only: every
            // update below is spoken, and a draft must not be.
            var updates = AgentAnswerStream.RunAsync(
                _agent,
                [new ChatMessage(ChatRole.User, prompt)],
                _session,
                new TraconRunOptions
                {
                    RunId = runId,
                    SessionId = request.SessionId,

                    // Values the host's resolver decided are the host's own, whatever the front end said.
                    ParameterSource = _hostDecided ? ParameterValueSource.Host : null,
                },
                cancellationToken);

            await foreach (var update in updates.ConfigureAwait(false))
            {
                if (update.Text is not { Length: > 0 } delta)
                {
                    continue;
                }

                spoken.Append(delta);

                await SendAsync(new VoiceServerMessage
                {
                    Type = VoiceConversationProtocol.ServerText,
                    Delta = delta,
                }).ConfigureAwait(false);

                foreach (var segment in segmenter.Append(delta))
                {
                    spokenCharacters = await SpeakAsync(segment, spokenCharacters, audio, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (segmenter.Flush() is { Length: > 0 } tail)
            {
                spokenCharacters = await SpeakAsync(tail, spokenCharacters, audio, cancellationToken)
                    .ConfigureAwait(false);
            }

            await driver._sessions
                .SaveSessionAsync(_agent, _session!, cancellationToken)
                .ConfigureAwait(false);

            return audio is null
                ? null
                : await PersistAudioAsync(audio, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Speaks one sentence and streams its chunks.</summary>
        /// <returns>The updated count of spoken characters.</returns>
        private async Task<int> SpeakAsync(
            string segment,
            int spokenCharacters,
            MemoryStream? audio,
            CancellationToken cancellationToken)
        {
            if (spokenCharacters >= _options.MaxSpokenCharactersPerTurn)
            {
                // The limit is reached: the remaining text is NOT SPOKEN, but it keeps
                // streaming as captions. The user still sees the whole answer.
                return spokenCharacters;
            }

            // Counted when the sentence is handed to the provider, not when the turn ends: a sentence that was
            // sent and then cut off by a barge-in or a failure was still billed.
            _outputChars += segment.Length;

            await SendAsync(new VoiceServerMessage
            {
                Type = VoiceConversationProtocol.ServerAudioStart,
                MediaType = _options.OutputMediaType,
            }).ConfigureAwait(false);

            await foreach (var chunk in synthesizer
                .SynthesizeStreamingAsync(
                    new SpeechRequest { Text = segment, VoiceId = _voiceId },
                    cancellationToken)
                .ConfigureAwait(false))
            {
                await SendBinaryAsync(chunk, cancellationToken).ConfigureAwait(false);

                if (audio is not null)
                {
                    await audio.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                }
            }

            await SendAsync(new VoiceServerMessage
            {
                Type = VoiceConversationProtocol.ServerAudioEnd,
            }).ConfigureAwait(false);

            return spokenCharacters + segment.Length;
        }

        /// <summary>Stores the spoken response as an attachment.</summary>
        /// <returns>The attachment identifier, or <see langword="null"/> when it could not be stored.</returns>
        private Task<string?> PersistAudioAsync(MemoryStream audio, CancellationToken cancellationToken)
            => driver._audioArchive.SaveAsync(audio.ToArray(), request, cancellationToken);

        // --- state machine gates ---

        private bool IsClosed()
        {
            lock (_state)
            {
                return _state.IsClosed;
            }
        }

        private void Stop()
        {
            lock (_state)
            {
                _state.Stop();
            }
        }

        private void FinishTurn(bool counted)
        {
            lock (_state)
            {
                _state.FinishTurn(counted);
            }
        }

        private async Task FailAsync(string message)
        {
            await SendAsync(Error(message)).ConfigureAwait(false);
            Stop();
        }

        private static VoiceServerMessage Error(string message)
            => new() { Type = VoiceConversationProtocol.ServerError, Message = message };

        private static VoiceServerMessage Idle()
            => new() { Type = VoiceConversationProtocol.ServerIdle };

        /// <summary>Closes the connection: it ends the pending turn and writes the record.</summary>
        private async Task ShutdownAsync(Guid recordId, DateTimeOffset started, VoiceSessionEndReason reason)
        {
            _turnCancellation?.Cancel();

            try
            {
                await _turn.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or TraconException or InvalidOperationException)
            {
                // At shutdown a failure of the pending turn must not block the record.
                driver._logger.LogDebug(exception, "The pending voice turn ended during shutdown.");
            }

            // The host's own limit is told to the client after the turn it cut off has said so: the client reads `done`, then
            // `ended`, and does not reconnect.
            if (_hostLimitReached)
            {
                await SendAsync(new VoiceServerMessage
                {
                    Type = VoiceConversationProtocol.ServerEnded,
                    Reason = VoiceConversationProtocol.EndedByDuration,
                }).ConfigureAwait(false);
            }

            try
            {
                await WriteRecordAsync(recordId, started, reason).ConfigureAwait(false);
                await CloseSocketAsync(reason).ConfigureAwait(false);
            }
            finally
            {
                ObservePendingReceive();
            }
        }

        /// <summary>
        /// The read that was pending when the connection closed ends with the close handshake or with the socket's abort; its
        /// outcome is of no use, and it must not surface later as an unobserved failure.
        /// </summary>
        private void ObservePendingReceive()
        {
            if (_pendingReceive is { } pending)
            {
                _ = pending.ContinueWith(
                    static task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _turnCancellation?.Dispose();
            _sendGate.Dispose();
        }

        private async Task WriteRecordAsync(Guid recordId, DateTimeOffset started, VoiceSessionEndReason reason)
        {
            int turns;

            lock (_state)
            {
                turns = _state.Turns;
            }

            try
            {
                var cost = PriceConversation();

                await driver._store.SaveAsync(
                    new VoiceSessionRecord
                    {
                        Provider = ProviderLabel(),
                        Model = _transcriptionModel,
                        Cost = cost,
                        Id = recordId,
                        TenantId = request.TenantId,
                        SessionId = request.SessionId,
                        AgentName = _agentName.Length > 0 ? _agentName : "-",
                        StartedAt = started,
                        EndedAt = driver._timeProvider.GetUtcNow(),
                        Turns = turns,
                        InputSeconds = _inputSeconds > 0 ? _inputSeconds : null,
                        OutputChars = _outputChars > 0 ? _outputChars : null,
                        EndReason = reason,
                        CreatedBy = request.CreatedBy,
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TraconException or InvalidOperationException)
            {
                // Observability does not break functionality.
                driver._logger.LogWarning(exception, "The voice record could not be written.");
            }
        }

        /// <summary>The speech provider the connection ran on: one name, or both when the two directions differ.</summary>
        private string ProviderLabel()
            => string.Equals(transcriber.ProviderName, synthesizer.ProviderName, StringComparison.OrdinalIgnoreCase)
                ? synthesizer.ProviderName
                : $"{transcriber.ProviderName}+{synthesizer.ProviderName}";

        /// <summary>
        /// Prices what the connection used, once, as the record is written: the price is a snapshot, so a later change to the
        /// price table never re-prices a conversation that is over.
        /// </summary>
        /// <returns>
        /// The cost; <see langword="null"/> when nothing was priced. A direction with no price stays <see langword="null"/>
        /// and is never written as zero, which would claim it was free.
        /// </returns>
        private VoiceSessionCost? PriceConversation()
        {
            if (driver._pricing is not { } pricing)
            {
                return null;
            }

            decimal? characterCost;
            decimal? durationCost;

            try
            {
                characterCost = _outputChars > 0
                    ? pricing.ForSynthesis(synthesizer.ProviderName, modelId: null, _outputChars)
                    : null;

                durationCost = _inputSeconds > 0
                    ? pricing.ForTranscription(transcriber.ProviderName, _transcriptionModel, TimeSpan.FromSeconds((double)_inputSeconds))
                    : null;
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, CancellationToken.None))
            {
                // A price table is the host's code and may throw anything; the record is still written, unpriced.
                driver._logger.LogWarning(exception, "The voice conversation could not be priced.");

                return null;
            }

            return characterCost is null && durationCost is null
                ? null
                : new VoiceSessionCost
                {
                    CharacterCost = characterCost,
                    DurationCost = durationCost,
                    Currency = pricing.Currency,
                };
        }

        private async Task CloseSocketAsync(VoiceSessionEndReason reason)
        {
            if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
            {
                return;
            }

            var description = reason switch
            {
                VoiceSessionEndReason.DurationLimit when _hostLimitReached => "The conversation reached its time limit.",
                VoiceSessionEndReason.DurationLimit => "The connection reached the duration limit.",
                VoiceSessionEndReason.IdleTimeout => "The connection stayed idle.",
                VoiceSessionEndReason.ServerShutdown => "The server is shutting down.",
                VoiceSessionEndReason.Agent => "The conversation was ended by the agent.",
                VoiceSessionEndReason.Error => "The conversation closed with an error.",
                _ => "The conversation closed.",
            };

            // The close handshake waits for the client's answer, and a client that reads nothing never sends it.
            using var bound = new CancellationTokenSource(CloseTimeout, driver._timeProvider);

            try
            {
                await socket
                    .CloseAsync(WebSocketCloseStatus.NormalClosure, description, bound.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // The other side may be gone, or it did not answer within the bound: the connection is dropped, and the handler
                // returns, so the tenant's connection slot is released.
                driver._logger.LogDebug(exception, "The voice socket could not be closed cleanly.");
                socket.Abort();
            }
        }

        // --- transport ---

        private async Task<VoiceFrame> ReceiveAsync(CancellationToken cancellationToken)
        {
            using var payload = new MemoryStream();
            WebSocketMessageType type;

            while (true)
            {
                var result = await socket
                    .ReceiveAsync(_receiveBuffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return VoiceFrame.Close;
                }

                type = result.MessageType;
                payload.Write(_receiveBuffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    break;
                }

                if (payload.Length > _options.MaxUtteranceBytes)
                {
                    throw new TraconException("The voice frame exceeded the allowed size.");
                }
            }

            return new VoiceFrame(type, payload.ToArray());
        }

        private Task SendAsync(VoiceServerMessage message)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(
                message,
                VoiceConversationJsonContext.Default.VoiceServerMessage);

            // 🚨 The cancellation token is NOT PASSED: the error and `done` frames are
            // written AFTER the cancellation that interrupted the turn. A cancellable
            // send would leave the client without ever learning why the turn ended.
            return SendFrameAsync(json, WebSocketMessageType.Text, CancellationToken.None);
        }

        private Task SendBinaryAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
            => SendFrameAsync(data, WebSocketMessageType.Binary, cancellationToken);

        /// <summary>
        /// Sends one frame through a single gate, because a <see cref="WebSocket"/>
        /// supports only one send at a time.
        /// </summary>
        /// <remarks>
        /// The receive loop and the turn task both write; without the gate the frames
        /// interleave and the client reads broken JSON.
        /// </remarks>
        private async Task SendFrameAsync(
            ReadOnlyMemory<byte> payload,
            WebSocketMessageType type,
            CancellationToken cancellationToken)
        {
            try
            {
                await _sendGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                if (socket.State != WebSocketState.Open)
                {
                    return;
                }

                await socket
                    .SendAsync(payload, type, endOfMessage: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
            {
                driver._logger.LogDebug(exception, "The voice frame could not be sent.");
            }
            finally
            {
                _sendGate.Release();
            }
        }
    }
}
