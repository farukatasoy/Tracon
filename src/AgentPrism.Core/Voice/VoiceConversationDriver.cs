using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>The immutable facts of a conversation connection.</summary>
/// <remarks>
/// 🚨 The tenant and the session resolve while the connection is established and
/// stay <strong>constant</strong> for the whole connection (29.3). A tenant or
/// session value that arrives inside a frame is not accepted: to change the tenant
/// on a long-lived connection is to move authorization after the handshake.
/// </remarks>
public sealed record VoiceConversationRequest
{
    /// <summary>Gets the tenant that was resolved while the connection was established.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the identifier of the agent session that the conversation runs on.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the actor that opened the connection, for the audit trail.</summary>
    public string? CreatedBy { get; init; }
}

/// <summary>
/// Runs the real-time conversation pipeline: audio comes in, text is transcribed,
/// the <strong>existing run path</strong> processes it, and audio goes out.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <strong>Option A.</strong> Audio is not proxied to the real-time API of the
/// provider; the streaming run path of the agent itself is called. The price is
/// latency, and the return is <em>everything</em>: the run record, spans, cost,
/// tool approval, tenancy and quota all work in a voice turn exactly as they do
/// elsewhere. AgentPrism is a control plane; it cannot suspend those promises for
/// voice. Rationale: <c>docs/29-KONUSMA-KATMANI.md</c>, section 29.1.
/// </para>
/// <para>
/// The driver lives in <c>AgentPrism.Core</c> and is <strong>provider
/// independent</strong>: it knows only the <see cref="ISpeechTranscriber"/> and
/// <see cref="ISpeechSynthesizer"/> abstractions. ElevenLabs is one implementation
/// (K-215).
/// </para>
/// <para>
/// The concurrency model is three rules: a <em>single</em> receive loop reads the
/// frames; turn processing runs on a separate task (otherwise <c>cancel</c> could
/// not be read until the turn ended); and every send passes through one gate,
/// because a <see cref="WebSocket"/> supports only one send at a time.
/// </para>
/// </remarks>
public sealed class VoiceConversationDriver
{
    private const int ReceiveBufferSize = 16 * 1024;

    private readonly IAgentCatalog _catalog;
    private readonly AgentSessionManager _sessions;
    private readonly ISpeechTranscriber? _transcriber;
    private readonly ISpeechSynthesizer? _synthesizer;
    private readonly ChatHistoryProvider _chatHistory;
    private readonly IVoiceSessionStore _store;
    private readonly IAttachmentStore _attachments;
    private readonly AttachmentTypeGuard _guard;
    private readonly VoiceConversationOptions _options;
    private readonly ILogger<VoiceConversationDriver> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="VoiceConversationDriver"/> class.</summary>
    /// <param name="catalog">The agent catalog.</param>
    /// <param name="sessions">The session manager.</param>
    /// <param name="chatHistory">The chat history provider.</param>
    /// <param name="store">The conversation record store.</param>
    /// <param name="attachments">The attachment store.</param>
    /// <param name="guard">The attachment type guard.</param>
    /// <param name="options">The conversation options.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="transcriber">
    /// The transcription provider. When it is <see langword="null"/> a conversation
    /// cannot open (see <see cref="IsReady"/>).
    /// </param>
    /// <param name="synthesizer">The synthesis provider.</param>
    /// <param name="timeProvider">The time source.</param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    public VoiceConversationDriver(
        IAgentCatalog catalog,
        AgentSessionManager sessions,
        ChatHistoryProvider chatHistory,
        IVoiceSessionStore store,
        IAttachmentStore attachments,
        AttachmentTypeGuard guard,
        IOptions<VoiceConversationOptions> options,
        ILogger<VoiceConversationDriver> logger,
        ISpeechTranscriber? transcriber,
        ISpeechSynthesizer? synthesizer,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(chatHistory);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _catalog = catalog;
        _sessions = sessions;
        _chatHistory = chatHistory;
        _store = store;
        _attachments = attachments;
        _guard = guard;
        _options = options.Value;
        _logger = logger;
        _transcriber = transcriber;
        _synthesizer = synthesizer;
        _timeProvider = timeProvider ?? TimeProvider.System;

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

    /// <summary>Runs one conversation connection from start to end.</summary>
    /// <param name="socket">The upgraded socket.</param>
    /// <param name="request">The immutable facts of the connection.</param>
    /// <param name="cancellationToken">The server shutdown token.</param>
    /// <returns>A task that completes when the connection closes.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><see cref="IsReady"/> is <see langword="false"/>.</exception>
    public async Task RunAsync(
        WebSocket socket,
        VoiceConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(request);

        if (_transcriber is null || _synthesizer is null)
        {
            throw new InvalidOperationException(
                "The voice layer is not enabled: an ISpeechTranscriber and an ISpeechSynthesizer must be registered.");
        }

        using var connection = new VoiceConnection(this, socket, request, _transcriber, _synthesizer);

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
        /// separate <c>object</c> field would trigger MA0158, which recommends
        /// <c>System.Threading.Lock</c> — a type we cannot use because net8.0 is also
        /// a target.
        /// </summary>
        private readonly VoiceConversationStateMachine _state = new();
        private readonly VoiceConversationOptions _options = driver._options;
        private readonly byte[] _receiveBuffer = new byte[ReceiveBufferSize];

        private VoiceUtteranceBuffer? _buffer;
        private AIAgent? _agent;
        private AgentSession? _session;
        private string _agentName = string.Empty;
        private string? _voiceId;

        private decimal _inputSeconds;
        private long _outputChars;

        private Task _turn = Task.CompletedTask;
        private CancellationTokenSource? _turnCancellation;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var started = driver._timeProvider.GetUtcNow();
            var recordId = AgentPrismId.NewId();
            var reason = VoiceSessionEndReason.Client;

            using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                reason = await PumpAsync(started, connectionCancellation, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                reason = VoiceSessionEndReason.ServerShutdown;
            }
            catch (WebSocketException exception)
            {
                // The client cut the connection abruptly. This is not a server error;
                // the record is still written.
                reason = VoiceSessionEndReason.Client;
                driver._logger.LogDebug(exception, "The voice socket closed unexpectedly.");
            }
            catch (Exception exception) when (exception is AgentPrismException or InvalidOperationException or JsonException)
            {
                reason = VoiceSessionEndReason.Error;
                driver._logger.LogError(exception, "The voice connection closed with an error.");
            }
            finally
            {
                await ShutdownAsync(recordId, started, reason).ConfigureAwait(false);
            }
        }

        /// <summary>The single loop that reads the frames.</summary>
        private async Task<VoiceSessionEndReason> PumpAsync(
            DateTimeOffset started,
            CancellationTokenSource connectionCancellation,
            CancellationToken cancellationToken)
        {
            var deadline = started + _options.MaxConnectionDuration;

            while (!IsClosed() && socket.State == WebSocketState.Open)
            {
                var untilDeadline = deadline - driver._timeProvider.GetUtcNow();

                if (untilDeadline <= TimeSpan.Zero)
                {
                    return VoiceSessionEndReason.DurationLimit;
                }

                var wait = untilDeadline < _options.IdleTimeout ? untilDeadline : _options.IdleTimeout;

                using var receiveCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(connectionCancellation.Token);

                receiveCancellation.CancelAfter(wait);

                VoiceFrame frame;

                try
                {
                    frame = await ReceiveAsync(receiveCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // The wait we used tells which limit was reached; the two limits
                    // produce a different close reason.
                    return wait == untilDeadline
                        ? VoiceSessionEndReason.DurationLimit
                        : VoiceSessionEndReason.IdleTimeout;
                }

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

                if (await HandleControlAsync(frame.Payload, connectionCancellation.Token).ConfigureAwait(false))
                {
                    return VoiceSessionEndReason.Client;
                }
            }

            return VoiceSessionEndReason.Client;
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
            driver._logger.LogInformation(
                "The voice utterance reached the limit and closed on its own ({Bytes} bytes).",
                _buffer.Length);

            Commit();
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
                    Commit();
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

            try
            {
                _agent = await driver._catalog.ResolveAsync(agentName, culture: null, cancellationToken).ConfigureAwait(false);
            }
            catch (AgentPrismException exception)
            {
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
            catch (AgentPrismException exception)
            {
                await FailAsync(exception.Message).ConfigureAwait(false);
                return;
            }

            _agentName = agentName;
            _voiceId = message.VoiceId ?? _options.VoiceId;

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
        }

        /// <summary>Closes the current utterance and starts the turn.</summary>
        private void Commit()
        {
            lock (_state)
            {
                if (_state.Commit() != VoiceTransitionOutcome.Accepted)
                {
                    return;
                }
            }

            if (_buffer?.Take() is not { } utterance)
            {
                // `commit` arrived before any audio. This is not an error (a short
                // cough can also trigger the VAD of the client); go back to listening.
                FinishTurn(counted: false);
                return;
            }

            // 🚨 The token of the previous turn is disposed HERE, not inside the turn
            // task: the task writes the `done` frame AFTER FinishTurn, and to cancel a
            // token that was already disposed at that moment produced an
            // ObjectDisposedException. Commit, Cancel and shutdown all run from the
            // same receive loop, so there is no race.
            _turnCancellation?.Dispose();
            _turnCancellation = new CancellationTokenSource();
            _turn = ProcessTurnAsync(utterance, _turnCancellation.Token);
        }

        /// <summary>Handles an interruption (barge-in).</summary>
        /// <remarks>
        /// 🚨 The state <strong>does not change</strong> here. Only the turn task
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
            string? attachmentId = null;

            try
            {
                var transcript = await TranscribeAsync(utterance, cancellationToken).ConfigureAwait(false);
                var text = transcript?.Text?.Trim() ?? string.Empty;

                await SendAsync(new VoiceServerMessage
                {
                    Type = VoiceConversationProtocol.ServerTranscript,
                    Text = text,
                    Final = true,
                }).ConfigureAwait(false);

                if (text.Length == 0)
                {
                    FinishTurn(counted: false);
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
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception exception) when (exception is AgentPrismException or HttpRequestException or InvalidOperationException)
            {
                driver._logger.LogError(exception, "The voice turn failed.");
                await SendAsync(Error(exception.Message)).ConfigureAwait(false);
            }

            if (cancelled)
            {
                await RecordInterruptionAsync(spoken.ToString()).ConfigureAwait(false);
            }

            FinishTurn(counted: true);

            await SendAsync(new VoiceServerMessage
            {
                Type = VoiceConversationProtocol.ServerDone,
                Cancelled = cancelled,
                Turn = _state.Turns,
                AttachmentId = attachmentId,
            }).ConfigureAwait(false);
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
            var runId = AgentPrismId.NewId();

            await SendAsync(new VoiceServerMessage
            {
                Type = VoiceConversationProtocol.ServerRunStarted,
                RunId = runId.ToString("D"),
            }).ConfigureAwait(false);

            var segmenter = new VoiceSpeechSegmenter();
            var spokenCharacters = 0;

            using var audio = _options.PersistAudio ? new MemoryStream() : null;

            var updates = _agent!.RunStreamingAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                _session,
                new AgentPrismRunOptions { RunId = runId, SessionId = request.SessionId },
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

            _outputChars += spokenCharacters;

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

        /// <summary>
        /// Writes the partial answer of an interrupted turn to the session history.
        /// </summary>
        /// <remarks>
        /// 🚨 This step cannot be skipped. When a streaming run is cancelled the
        /// Microsoft Agent Framework does not write the history; on the next turn the
        /// model DOES NOT SEE its own half sentence, and the conversation breaks the
        /// moment the user says "what you said a moment ago". The record states
        /// <strong>explicitly</strong> that the answer was interrupted.
        /// </remarks>
        private async Task RecordInterruptionAsync(string partial)
        {
            if (_agent is null || _session is null)
            {
                return;
            }

            var trimmed = partial.Trim();

            var text = trimmed.Length > 0
                ? trimmed + "\n\n[The response was interrupted by the user.]"
                : "[The response was interrupted by the user before it started.]";

            try
            {
                // The MAAI001 rationale is the same as in ChatHistoryReader: there is
                // no other public way to write to the history (StoreChatHistoryAsync
                // is protected).
#pragma warning disable MAAI001
                var context = new ChatHistoryProvider.InvokedContext(
                    _agent,
                    _session,
                    [],
                    [new ChatMessage(ChatRole.Assistant, text)]);
#pragma warning restore MAAI001

                await driver._chatHistory.InvokedAsync(context, CancellationToken.None).ConfigureAwait(false);

                await driver._sessions
                    .SaveSessionAsync(_agent, _session, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is AgentPrismException or InvalidOperationException or NotSupportedException or JsonException)
            {
                // Observability does not break functionality: when the history cannot
                // be written the conversation still continues.
                driver._logger.LogWarning(exception, "The interrupted response could not be written to the session history.");
            }
        }

        /// <summary>Stores the spoken response as an attachment.</summary>
        /// <remarks>
        /// 🚨 Only the <strong>audio that the agent produced</strong> is stored. The
        /// audio of the user is never written: voice is biometric data, and the record
        /// of what was said is already the transcript in the session history.
        /// Rationale: <c>docs/29-KONUSMA-KATMANI.md</c>, section 29.3.
        /// </remarks>
        /// <returns>The attachment identifier, or <see langword="null"/> when it could not be stored.</returns>
        private async Task<string?> PersistAudioAsync(MemoryStream audio, CancellationToken cancellationToken)
        {
            var data = audio.ToArray();

            if (data.Length == 0)
            {
                return null;
            }

            // The attachment store DOES NOT validate (phase 28/G2); the guard is
            // called explicitly here.
            var validation = driver._guard.Validate(data);

            if (!validation.IsValid)
            {
                driver._logger.LogWarning("The voice audio could not be stored as an attachment: {Error}", validation.Error);
                return null;
            }

            try
            {
                var descriptor = await driver._attachments.SaveAsync(
                    new AttachmentContent
                    {
                        TenantId = request.TenantId,

                        // The session identifier is REQUIRED: when it is left empty the
                        // retention policy treats the attachment as orphaned and
                        // deletes it (phase 28/G1).
                        SessionId = request.SessionId,
                        FileName = $"voice-{driver._timeProvider.GetUtcNow():yyyyMMdd-HHmmss-fff}.bin",
                        MediaType = validation.MediaType!,
                        Data = data,
                        CreatedBy = request.CreatedBy,
                    },
                    cancellationToken).ConfigureAwait(false);

                return descriptor.Id.ToString("D");
            }
            catch (Exception exception) when (exception is AgentPrismException or InvalidOperationException)
            {
                driver._logger.LogWarning(exception, "The voice audio could not be stored as an attachment.");
                return null;
            }
        }

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

        /// <summary>Closes the connection: it ends the pending turn and writes the record.</summary>
        private async Task ShutdownAsync(Guid recordId, DateTimeOffset started, VoiceSessionEndReason reason)
        {
            _turnCancellation?.Cancel();

            try
            {
                await _turn.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or AgentPrismException or InvalidOperationException)
            {
                // At shutdown a failure of the pending turn must not block the record.
                driver._logger.LogDebug(exception, "The pending voice turn ended during shutdown.");
            }

            await WriteRecordAsync(recordId, started, reason).ConfigureAwait(false);
            await CloseSocketAsync(reason).ConfigureAwait(false);
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
                await driver._store.SaveAsync(
                    new VoiceSessionRecord
                    {
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
            catch (Exception exception) when (exception is AgentPrismException or InvalidOperationException)
            {
                // Observability does not break functionality.
                driver._logger.LogWarning(exception, "The voice record could not be written.");
            }
        }

        private async Task CloseSocketAsync(VoiceSessionEndReason reason)
        {
            if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
            {
                return;
            }

            var description = reason switch
            {
                VoiceSessionEndReason.DurationLimit => "The connection reached the duration limit.",
                VoiceSessionEndReason.IdleTimeout => "The connection stayed idle.",
                VoiceSessionEndReason.ServerShutdown => "The server is shutting down.",
                VoiceSessionEndReason.Error => "The conversation closed with an error.",
                _ => "The conversation closed.",
            };

            try
            {
                await socket
                    .CloseAsync(WebSocketCloseStatus.NormalClosure, description, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // The other side may be gone; the close is best effort.
                driver._logger.LogDebug(exception, "The voice socket could not be closed cleanly.");
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
                    throw new AgentPrismException("The voice frame exceeded the allowed size.");
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
