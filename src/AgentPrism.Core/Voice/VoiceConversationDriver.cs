using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Bir konusma baglantisinin degismeyen bilgileri.</summary>
/// <remarks>
/// 🚨 Kiraci ve oturum baglanti kurulurken cozulur ve baglanti boyunca
/// <strong>sabittir</strong> (29.3). Cerceve icinde gelen bir kiraci/oturum
/// degeri kabul edilmez: uzun omurlu bir baglantida kiraci degistirmek,
/// yetkilendirmeyi el sikismadan sonraya tasimak demektir.
/// </remarks>
public sealed record VoiceConversationRequest
{
    /// <summary>Baglanti kurulurken cozulen kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>Konusmanin yurudugu agent oturumunun kimligi.</summary>
    public required string SessionId { get; init; }

    /// <summary>Baglantiyi acan aktor (denetim izi icin).</summary>
    public string? CreatedBy { get; init; }
}

/// <summary>
/// Gercek zamanli konusmayi yuruten boru hatti: ses girer, metin cozulur,
/// <strong>mevcut calistirma yolu</strong> isler, ses cikar.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <strong>Secenek A.</strong> Ses, saglayicinin gercek zamanli API'sine
/// vekillenmez; agent'in kendi akisli calistirma yolu cagrilir. Bunun bedeli
/// gecikmedir, karsiligi ise <em>her sey</em>dir: calistirma kaydi, span,
/// maliyet, tool onayi, kiraci ve kota ses turunda da aynen isler. AgentPrism
/// bir kontrol duzlemidir; bu vaatleri ses icin askiya alamaz. Gerekce:
/// <c>docs/29-KONUSMA-KATMANI.md</c>, bolum 29.1.
/// </para>
/// <para>
/// Surucu <c>AgentPrism.Core</c>'dadir ve <strong>saglayicidan bagimsizdir</strong>:
/// yalnizca <see cref="ISpeechTranscriber"/> ve <see cref="ISpeechSynthesizer"/>
/// soyutlamalarini bilir. ElevenLabs bir uygulamadir (K-215).
/// </para>
/// <para>
/// Es zamanlilik modeli uc kuraldan ibarettir: <em>tek</em> bir alma dongusu
/// cerceveleri okur; tur isleme ayri bir gorevde yurur (aksi halde <c>cancel</c>
/// tur bitene kadar okunamazdi); gonderme tek bir kilitten gecer — bir
/// <see cref="WebSocket"/> ayni anda yalniz bir gonderme kaldirir.
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

    /// <summary>Yeni bir surucu kurar.</summary>
    /// <param name="catalog">Agent katalogu.</param>
    /// <param name="sessions">Oturum yoneticisi.</param>
    /// <param name="chatHistory">Sohbet gecmisi saglayicisi.</param>
    /// <param name="store">Konusma kaydi deposu.</param>
    /// <param name="attachments">Ek deposu.</param>
    /// <param name="guard">Ek tur denetleyicisi.</param>
    /// <param name="options">Konusma ayarlari.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <param name="transcriber">
    /// Cozum saglayicisi. <see langword="null"/> ise konusma acilamaz
    /// (bkz. <see cref="IsReady"/>).
    /// </param>
    /// <param name="synthesizer">Sentez saglayicisi.</param>
    /// <param name="timeProvider">Zaman kaynagi.</param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
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

    /// <summary>Kiraci basina es zamanli baglanti sinirlayicisi.</summary>
    /// <remarks>
    /// Uc, soketi <strong>yukseltmeden once</strong> yer ayirir: sinir dolduysa
    /// istemci bir HTTP hatasi gorur. Yukselttikten sonra kapatmak, istemciye
    /// nedeni anlatmanin cok daha kotu bir yoludur.
    /// </remarks>
    public VoiceConnectionLimiter Limiter { get; }

    /// <summary>
    /// Konusma icin gereken iki saglayici da kayitli mi.
    /// </summary>
    /// <remarks>
    /// Cozum <em>ve</em> sentez ikisi de gerekir: yalniz biriyle konusma tek
    /// yonlu olurdu ve bu bir konusma degildir.
    /// </remarks>
    public bool IsReady => _transcriber is not null && _synthesizer is not null;

    /// <summary>Ses saklaniyor mu.</summary>
    /// <remarks>
    /// Arayuz bu degeri kullaniciya <strong>gosterir</strong>; kayit sessizce
    /// yapilmaz.
    /// </remarks>
    public bool PersistAudio => _options.PersistAudio;

    /// <summary>Bir konusma baglantisini bastan sona yurutur.</summary>
    /// <param name="socket">Yukseltilmis soket.</param>
    /// <param name="request">Baglantinin degismeyen bilgileri.</param>
    /// <param name="cancellationToken">Sunucu kapanisi belirteci.</param>
    /// <returns>Baglanti kapandiginda tamamlanir.</returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    /// <exception cref="InvalidOperationException"><see cref="IsReady"/> yanlissa.</exception>
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
                "Konusma katmani acik degil: bir ISpeechTranscriber ve bir ISpeechSynthesizer kayitli olmalidir.");
        }

        using var connection = new VoiceConnection(this, socket, request, _transcriber, _synthesizer);

        await connection.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Alinmis bir WebSocket cercevesi.</summary>
    private readonly record struct VoiceFrame(WebSocketMessageType Type, ReadOnlyMemory<byte> Payload)
    {
        public static VoiceFrame Close => new(WebSocketMessageType.Close, ReadOnlyMemory<byte>.Empty);

        public bool IsClose => Type == WebSocketMessageType.Close;

        public bool IsBinary => Type == WebSocketMessageType.Binary;
    }

    /// <summary>Tek bir baglantinin durumu ve dongusu.</summary>
    /// <remarks>
    /// Ayri bir sinif olmasinin sebebi: baglanti basina durum (tampon, oturum,
    /// sayaclar) surucunun alanlarina yazilamaz — surucu singleton'dir ve tum
    /// baglantilar onu paylasir.
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
        /// Durum makinesinin kilidi. Kilit <em>nesnenin kendisidir</em>: ayri bir
        /// <c>object</c> alani, net8.0 da hedeflendigi icin kullanamadigimiz
        /// <c>System.Threading.Lock</c>'u oneren MA0158'i tetiklerdi.
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
                // Istemci baglantiyi kaba bicimde kesti. Bu bir sunucu hatasi
                // degildir; kayit yine yazilir.
                reason = VoiceSessionEndReason.Client;
                driver._logger.LogDebug(exception, "Konusma soketi beklenmedik bicimde kapandi.");
            }
            catch (Exception exception) when (exception is AgentPrismException or InvalidOperationException or JsonException)
            {
                reason = VoiceSessionEndReason.Error;
                driver._logger.LogError(exception, "Konusma baglantisi hata ile kapandi.");
            }
            finally
            {
                await ShutdownAsync(recordId, started, reason).ConfigureAwait(false);
            }
        }

        /// <summary>Cerceveleri okuyan tek dongu.</summary>
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
                    // Hangi sinirin doldugu bekledigimiz sureden anlasilir; iki
                    // sinir farkli bir kapanis nedeni uretir.
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

        /// <summary>Bir ikili cerceveyi konusma parcasina ekler.</summary>
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

            // 🚨 Guvenlik agi: istemcinin VAD'i hic tetiklenmedi. Parca
            // kendiliginden kapatilir; yoksa tampon dolar, sonraki ses sessizce
            // atilir ve konusma hic yanitlanmazdi.
            driver._logger.LogInformation(
                "Konusma parcasi sinira ulasti ve kendiliginden kapatildi ({Bytes} bayt).",
                _buffer.Length);

            Commit();
        }

        /// <summary>Bir metin cercevesini isler.</summary>
        /// <returns>Baglanti kapanacaksa <see langword="true"/>.</returns>
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
                await SendAsync(Error("Denetim mesaji gecerli JSON degil.")).ConfigureAwait(false);
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
                    await SendAsync(Error($"Bilinmeyen mesaj: '{message?.Type}'.")).ConfigureAwait(false);
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
                // Ikinci bir `start` agent'i degistirmek anlamina gelirdi.
                await FailAsync("Konusma zaten baslatildi; agent baglanti boyunca degismez.").ConfigureAwait(false);
                return;
            }

            if (!VoiceAudioFormats.IsKnown(message.InputFormat))
            {
                await FailAsync($"Bilinmeyen ses bicimi: '{message.InputFormat}'.").ConfigureAwait(false);
                return;
            }

            if (message.Agent is not { Length: > 0 } agentName)
            {
                await FailAsync("'agent' alani zorunludur.").ConfigureAwait(false);
                return;
            }

            try
            {
                _agent = await driver._catalog.ResolveAsync(agentName, cancellationToken).ConfigureAwait(false);
            }
            catch (AgentPrismException exception)
            {
                await FailAsync($"Agent derlenemedi: {exception.Message}").ConfigureAwait(false);
                return;
            }

            if (_agent is null)
            {
                await FailAsync($"'{agentName}' adinda bir agent yok.").ConfigureAwait(false);
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

        /// <summary>Konusma parcasini kapatir ve turu baslatir.</summary>
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
                // Ses gelmeden `commit` geldi. Bu bir hata degildir (kisa bir
                // oksuruk de istemcinin VAD'ini tetikleyebilir); dinlemeye donulur.
                FinishTurn(counted: false);
                return;
            }

            // 🚨 Onceki turun belirteci BURADA bertaraf edilir, tur gorevinin
            // kendi icinde degil: gorev `done` cercevesini FinishTurn'den SONRA
            // yazar ve o sirada bertaraf edilmis bir belirteci iptal etmek
            // ObjectDisposedException uretirdi. Commit, Cancel ve kapanis ayni
            // alma dongusunden calisir; bu yuzden yaris yoktur.
            _turnCancellation?.Dispose();
            _turnCancellation = new CancellationTokenSource();
            _turn = ProcessTurnAsync(utterance, _turnCancellation.Token);
        }

        /// <summary>Kesinti (barge-in).</summary>
        /// <remarks>
        /// 🚨 Durum burada <strong>degismez</strong>. Dinlemeye donusu yalnizca
        /// tur gorevi yapar (<see cref="FinishTurn"/>); aksi halde istemci hemen
        /// yeni bir <c>commit</c> gonderebilir ve iki tur ayni anda calisirdi.
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

        /// <summary>Bir konusma turunu bastan sona isler.</summary>
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
                driver._logger.LogError(exception, "Konusma turu basarisiz oldu.");
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
        /// Agent'i calistirir, metni altyazi olarak akitir ve cumle cumle
        /// seslendirir.
        /// </summary>
        /// <returns>Ses saklandiysa ekin kimligi.</returns>
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

        /// <summary>Bir cumleyi seslendirir ve parcalarini akitir.</summary>
        /// <returns>Guncellenen seslendirilmis karakter sayaci.</returns>
        private async Task<int> SpeakAsync(
            string segment,
            int spokenCharacters,
            MemoryStream? audio,
            CancellationToken cancellationToken)
        {
            if (spokenCharacters >= _options.MaxSpokenCharactersPerTurn)
            {
                // Sinir doldu: kalan metin SESLENDIRILMEZ ama altyazi olarak
                // akmaya devam eder. Kullanici cevabin tamamini gorur.
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
        /// Kesilen turun yarim yanitini oturum gecmisine yazar.
        /// </summary>
        /// <remarks>
        /// 🚨 Bu adim atlanamaz. Akisli calistirma iptal edildiginde Microsoft
        /// Agent Framework gecmisi yazmaz; model bir sonraki turda kendi yarim
        /// cumlesini GORMEZ ve kullanici "az once soyledigin" dedigi anda konusma
        /// kopar. Kayit, kesildigini <strong>acikca</strong> belirtir.
        /// </remarks>
        private async Task RecordInterruptionAsync(string partial)
        {
            if (_agent is null || _session is null)
            {
                return;
            }

            var trimmed = partial.Trim();

            var text = trimmed.Length > 0
                ? trimmed + "\n\n[Yanit kullanici tarafindan kesildi.]"
                : "[Yanit baslamadan kullanici tarafindan kesildi.]";

            try
            {
                // MAAI001 gerekcesi ChatHistoryReader ile aynidir: gecmise yazmanin
                // baska public yolu yoktur (StoreChatHistoryAsync protected'tir).
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
                // Gozlemlenebilirlik islevselligi bozmaz: gecmis yazilamadiysa
                // konusma yine surer.
                driver._logger.LogWarning(exception, "Kesilen yanit oturum gecmisine yazilamadi.");
            }
        }

        /// <summary>Seslendirilen yaniti ek olarak saklar.</summary>
        /// <remarks>
        /// 🚨 Yalnizca <strong>agent'in urettigi ses</strong> saklanir.
        /// Kullanicinin sesi hicbir zaman yazilmaz: ses biyometrik veridir ve
        /// soylenenin kaydi zaten oturum gecmisindeki transkripttir. Gerekce:
        /// <c>docs/29-KONUSMA-KATMANI.md</c>, bolum 29.3.
        /// </remarks>
        /// <returns>Ekin kimligi; saklanamadiysa <see langword="null"/>.</returns>
        private async Task<string?> PersistAudioAsync(MemoryStream audio, CancellationToken cancellationToken)
        {
            var data = audio.ToArray();

            if (data.Length == 0)
            {
                return null;
            }

            // Ek deposu dogrulama YAPMAZ (Faz 28/G2); denetleyici burada acikca
            // cagrilir.
            var validation = driver._guard.Validate(data);

            if (!validation.IsValid)
            {
                driver._logger.LogWarning("Konusma sesi ek olarak saklanamadi: {Error}", validation.Error);
                return null;
            }

            try
            {
                var descriptor = await driver._attachments.SaveAsync(
                    new AttachmentContent
                    {
                        TenantId = request.TenantId,

                        // Oturum kimligi ZORUNLUDUR: bos birakilirsa saklama
                        // politikasi eki sahipsiz sayar ve siler (Faz 28/G1).
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
                driver._logger.LogWarning(exception, "Konusma sesi ek olarak saklanamadi.");
                return null;
            }
        }

        // --- durum makinesi kapilari ---

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

        /// <summary>Baglanti kapanisi: bekleyen turu bitirir ve kaydi yazar.</summary>
        private async Task ShutdownAsync(Guid recordId, DateTimeOffset started, VoiceSessionEndReason reason)
        {
            _turnCancellation?.Cancel();

            try
            {
                await _turn.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or AgentPrismException or InvalidOperationException)
            {
                // Kapanista bekleyen turun hatasi kaydi engellememelidir.
                driver._logger.LogDebug(exception, "Bekleyen konusma turu kapanista sonlandi.");
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
                // Gozlemlenebilirlik islevselligi bozmaz.
                driver._logger.LogWarning(exception, "Konusma kaydi yazilamadi.");
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
                VoiceSessionEndReason.DurationLimit => "Baglanti sure sinirina ulasti.",
                VoiceSessionEndReason.IdleTimeout => "Baglanti boste kaldi.",
                VoiceSessionEndReason.ServerShutdown => "Sunucu kapaniyor.",
                VoiceSessionEndReason.Error => "Konusma hata ile kapandi.",
                _ => "Konusma kapandi.",
            };

            try
            {
                await socket
                    .CloseAsync(WebSocketCloseStatus.NormalClosure, description, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Karsi taraf gitmis olabilir; kapanis en iyi cabadir.
                driver._logger.LogDebug(exception, "Konusma soketi duzgun kapatilamadi.");
            }
        }

        // --- tasima ---

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
                    throw new AgentPrismException("Konusma cercevesi izin verilen boyutu asti.");
                }
            }

            return new VoiceFrame(type, payload.ToArray());
        }

        private Task SendAsync(VoiceServerMessage message)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(
                message,
                VoiceConversationJsonContext.Default.VoiceServerMessage);

            // 🚨 Iptal belirteci GECILMEZ: hata ve `done` cerceveleri, turu kesen
            // iptalden SONRA yazilir. Iptal edilebilir bir gonderme, istemciyi
            // turun neden bittigini hic ogrenemeden birakirdi.
            return SendFrameAsync(json, WebSocketMessageType.Text, CancellationToken.None);
        }

        private Task SendBinaryAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
            => SendFrameAsync(data, WebSocketMessageType.Binary, cancellationToken);

        /// <summary>
        /// Tek bir gonderme kilidi: bir <see cref="WebSocket"/> ayni anda yalniz
        /// bir gonderme kaldirir.
        /// </summary>
        /// <remarks>
        /// Alma dongusu ve tur gorevi ikisi de yazar; kilit olmadan cerceveler
        /// birbirine girer ve istemci bozuk JSON okur.
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
                driver._logger.LogDebug(exception, "Konusma cercevesi gonderilemedi.");
            }
            finally
            {
                _sendGate.Release();
            }
        }
    }
}
