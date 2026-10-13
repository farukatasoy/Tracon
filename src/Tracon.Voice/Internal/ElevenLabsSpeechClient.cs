using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// <see cref="ISpeechSynthesizer"/>, <see cref="ISpeechDialogueSynthesizer"/> and
/// <see cref="ISpeechTranscriber"/> implementation over ElevenLabs' HTTP endpoints.
/// </summary>
/// <remarks>
/// <para>
/// Raw <see cref="HttpClient"/> instead of an SDK: the surface used is a few
/// endpoints, the JSON contract is simple, and source generation keeps
/// it AOT compatible. This also lets the code fully control that the error
/// detail carries no secret or address.
/// </para>
/// <para>
/// Concurrency is bounded with a <see cref="SemaphoreSlim"/>: voice requests
/// are expensive, and a single misconfigured agent definition can fire dozens
/// of requests at once. Synthesis, dialogue and transcription share the one
/// counter.
/// </para>
/// <para>
/// The built-in client takes no tenant credential: a request that carries one
/// is refused, never sent on the configured key.
/// </para>
/// </remarks>
internal sealed class ElevenLabsSpeechClient : ISpeechSynthesizer, ISpeechDialogueSynthesizer, ISpeechTranscriber, IVoiceHealthCheck, IDisposable
{
    /// <summary>The provider's public base address.</summary>
    internal static readonly Uri DefaultEndpoint = new("https://api.elevenlabs.io/");

    /// <summary>Authentication header. It is NOT <c>Authorization: Bearer</c>.</summary>
    internal const string ApiKeyHeader = "xi-api-key";

    /// <summary>The provider's default synthesis model.</summary>
    internal const string DefaultSynthesisModel = "eleven_multilingual_v2";

    /// <summary>The provider's default transcription model.</summary>
    internal const string DefaultTranscriptionModel = "scribe_v2";

    /// <summary>The most distinct voices one dialogue request takes (the provider's limit).</summary>
    internal const int MaxDialogueVoices = 10;

    /// <summary>The response header that carries the provider's id for the generation.</summary>
    internal const string RequestIdHeader = "request-id";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(100);
    private const int MaxReportedVoices = 500;

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly VoiceOptions _options;
    private readonly SemaphoreSlim _concurrency;

    /// <summary>Builds a new client.</summary>
    /// <param name="options">The settings.</param>
    /// <param name="httpClient">
    /// The client to use. When <see langword="null"/>, the client is built here
    /// and owned by this instance. Tests pass a fake <c>HttpMessageHandler</c>.
    /// </param>
    public ElevenLabsSpeechClient(VoiceOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _ownsHttpClient = httpClient is null;

        // PooledConnectionLifetime bounds how long a resolved address is
        // reused; two minutes matches EgressSocketGuard.CreateHandler.
        _http = httpClient ?? new HttpClient(
            new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) },
            disposeHandler: true);
        _http.Timeout = options.Timeout ?? DefaultTimeout;
        _concurrency = new SemaphoreSlim(options.MaxConcurrentRequests, options.MaxConcurrentRequests);
    }

    /// <inheritdoc />
    public string ProviderName => VoiceProviderNames.ElevenLabs;

    /// <inheritdoc />
    public int MaxCharactersPerRequest => _options.MaxCharactersPerRequest;

    /// <inheritdoc />
    public SpeechDialogueCapabilities DialogueCapabilities => new()
    {
        MaxVoices = MaxDialogueVoices,
        MaxCharactersPerRequest = _options.MaxDialogueCharactersPerRequest,
        SupportsSegmentTimestamps = true,
    };

    private Uri BaseEndpoint => _options.Endpoint ?? DefaultEndpoint;

    /// <inheritdoc />
    public async ValueTask<SpeechAudio> SynthesizeAsync(
        SpeechRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RefuseTenantCredential(request.Credential);

        var voiceId = ResolveVoiceId(request);
        var format = request.OutputFormat ?? _options.OutputFormat;
        var settings = ElevenLabsRequestSettings.ReadSpeech(request.ProviderSettings);

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return request.IncludeTimestamps
                ? await SynthesizeWithTimestampsAsync(request, voiceId, format, settings, cancellationToken).ConfigureAwait(false)
                : await SynthesizePlainAsync(request, voiceId, format, settings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private async ValueTask<SpeechAudio> SynthesizePlainAsync(
        SpeechRequest request,
        string voiceId,
        string format,
        ElevenLabsRequestSettings settings,
        CancellationToken cancellationToken)
    {
        using var message = BuildSynthesisRequest(request, voiceId, format, settings, streaming: false, withTimestamps: false);
        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, "Speech could not be generated", cancellationToken).ConfigureAwait(false);

        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var billed = ReadBilledCharacters(response);

        return new SpeechAudio
        {
            Data = data,
            MediaType = MediaTypeForFormat(format),
            ModelId = SynthesisModel(request),
            CharactersBilled = billed ?? request.Text.Length,

            // When the provider does not report the count, the value is an
            // ESTIMATE and is marked as such. Presenting an estimate as a
            // measurement would be inventing a price.
            UsageSource = billed is null ? SpeechUsageSource.Estimated : SpeechUsageSource.Provider,
            ProviderRequestId = ReadRequestId(response),
        };
    }

    /// <remarks>
    /// The <c>.../with-timestamps</c> endpoint returns a JSON body
    /// (<c>audio_base64</c> + <c>alignment</c>), not raw audio bytes - a
    /// different response shape from the plain synthesis endpoint. Verified
    /// against the provider's published OpenAPI document, 2026-08-19
    /// (<c>AudioWithTimestampsResponseModel</c>).
    /// </remarks>
    private async ValueTask<SpeechAudio> SynthesizeWithTimestampsAsync(
        SpeechRequest request,
        string voiceId,
        string format,
        ElevenLabsRequestSettings settings,
        CancellationToken cancellationToken)
    {
        using var message = BuildSynthesisRequest(request, voiceId, format, settings, streaming: false, withTimestamps: true);
        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, "Speech could not be generated", cancellationToken).ConfigureAwait(false);

        var billed = ReadBilledCharacters(response);
        var payload = await ReadTimestampedAudioAsync(response, cancellationToken).ConfigureAwait(false);

        return new SpeechAudio
        {
            Data = payload.Data,
            MediaType = MediaTypeForFormat(format),
            ModelId = SynthesisModel(request),
            CharactersBilled = billed ?? request.Text.Length,
            UsageSource = billed is null ? SpeechUsageSource.Estimated : SpeechUsageSource.Provider,
            Alignment = payload.Alignment,
            ProviderRequestId = ReadRequestId(response),
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
        SpeechRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 🚨 This method returns raw audio chunks only; there is no channel to
        // carry alignment data back to the caller. Silently dropping the
        // alignment would be a surprise (K1) - the combination is rejected
        // explicitly instead. SynthesizeAsync supports timestamps.
        if (request.IncludeTimestamps)
        {
            throw new TraconException(
                $"{nameof(SpeechRequest.IncludeTimestamps)} is not supported together with streaming synthesis: " +
                $"{nameof(SynthesizeStreamingAsync)} returns raw audio chunks only and has no channel for " +
                $"alignment data. Use {nameof(SynthesizeAsync)} for timestamped speech.");
        }

        RefuseTenantCredential(request.Credential);

        var voiceId = ResolveVoiceId(request);
        var format = request.OutputFormat ?? _options.OutputFormat;
        var settings = ElevenLabsRequestSettings.ReadSpeech(request.ProviderSettings);

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var message = BuildSynthesisRequest(request, voiceId, format, settings, streaming: true, withTimestamps: false);

            using var response = await _http
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            await EnsureSuccessAsync(response, "Speech stream could not be started", cancellationToken).ConfigureAwait(false);

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            await using (stream.ConfigureAwait(false))
            {
                var buffer = new byte[8192];

                while (true)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

                    if (read == 0)
                    {
                        break;
                    }

                    // The buffer is reused; the consumer gets a copy.
                    yield return buffer.AsMemory(0, read).ToArray();
                }
            }
        }
        finally
        {
            _concurrency.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>POST v1/text-to-dialogue</c> returns raw audio;
    /// <c>v1/text-to-dialogue/with-timestamps</c> returns JSON with the audio, the
    /// character timing and <c>voice_segments</c> (verified against the provider's
    /// OpenAPI document and a live request, 2026-10-13).
    /// </remarks>
    public async ValueTask<SpeechDialogueAudio> SynthesizeDialogueAsync(
        SpeechDialogueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RefuseTenantCredential(request.Credential);

        // Every refusal comes before the semaphore and the network: a dialogue over a
        // limit is the caller's to split, never this client's to cut (K1).
        var characters = CheckDialogue(request);
        var settings = ElevenLabsRequestSettings.ReadDialogue(request.ProviderSettings);
        var format = request.OutputFormat ?? _options.OutputFormat;

        // The single-voice model is not a dialogue model; with neither set, the
        // provider's default applies and the result names no model.
        var model = request.ModelId ?? _options.DialogueModelId;

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var message = BuildDialogueRequest(request, settings, model, format);
            using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);

            await EnsureSuccessAsync(response, "Dialogue could not be generated", cancellationToken).ConfigureAwait(false);

            var billed = ReadBilledCharacters(response);
            var requestId = ReadRequestId(response);
            byte[] data;
            IReadOnlyList<SpeechAlignment>? alignment = null;
            IReadOnlyList<SpeechDialogueSegment>? segments = null;

            if (request.IncludeTimestamps)
            {
                var payload = await ReadDialogueWithTimestampsAsync(response, cancellationToken).ConfigureAwait(false);

                data = payload?.AudioBase64 is { Length: > 0 } base64 ? Convert.FromBase64String(base64) : [];
                alignment = ToAlignment(payload?.Alignment);
                segments = ToSegments(payload?.VoiceSegments, request.Turns);
            }
            else
            {
                data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }

            return new SpeechDialogueAudio
            {
                Audio = new SpeechAudio
                {
                    Data = data,
                    MediaType = MediaTypeForFormat(format),
                    ModelId = model,
                    CharactersBilled = billed ?? characters,
                    UsageSource = billed is null ? SpeechUsageSource.Estimated : SpeechUsageSource.Provider,
                    Alignment = alignment,
                    ProviderRequestId = requestId,
                },
                Segments = segments,
            };
        }
        finally
        {
            _concurrency.Release();
        }
    }

    /// <summary>The most voices requested per <c>/v2/voices</c> call - the provider's own ceiling.</summary>
    private const int VoicePageSize = 100;

    /// <inheritdoc />
    /// <remarks>
    /// Follows the provider's <c>has_more</c>/<c>next_page_token</c> pagination
    /// until it reports no more pages or the result reaches the reported cap,
    /// requesting the largest page size the provider allows on each call.
    /// </remarks>
    public async ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
        CancellationToken cancellationToken = default)
    {
        var collected = new List<ElevenLabsVoice>();
        string? pageToken = null;

        do
        {
            var path = pageToken is null
                ? $"v2/voices?page_size={VoicePageSize}"
                : $"v2/voices?page_size={VoicePageSize}&next_page_token={Uri.EscapeDataString(pageToken)}";

            using var message = new HttpRequestMessage(HttpMethod.Get, BuildUri(path));
            AddApiKey(message);

            using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);

            await EnsureSuccessAsync(response, "Voice list could not be retrieved", cancellationToken).ConfigureAwait(false);

            var page = await ReadVoicesPageAsync(response, cancellationToken).ConfigureAwait(false);

            if (page?.Voices is { Count: > 0 } voices)
            {
                collected.AddRange(voices);
            }

            pageToken = page is { HasMore: true } && page.NextPageToken is { Length: > 0 } token
                ? token
                : null;
        }
        while (pageToken is not null && collected.Count < MaxReportedVoices);

        return MapVoices(collected);
    }

    /// <inheritdoc />
    public async ValueTask<SpeechTranscript> TranscribeAsync(
        Stream audio,
        string mediaType,
        SpeechTranscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        RefuseTenantCredential(options?.Credential);

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 🚨 The endpoint requires `multipart/form-data`; it does NOT accept a JSON body.
            using var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(audio);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            content.Add(fileContent, "file", "audio");

            var model = options?.ModelId
                        ?? _options.TranscriptionModelId
                        ?? DefaultTranscriptionModel;

            content.Add(new StringContent(model), "model_id");

            if (options?.LanguageCode is { Length: > 0 } language)
            {
                content.Add(new StringContent(language), "language_code");
            }

            using var message = new HttpRequestMessage(HttpMethod.Post, BuildUri("v1/speech-to-text"))
            {
                Content = content,
            };

            AddApiKey(message);

            using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);

            await EnsureSuccessAsync(response, "Speech could not be transcribed", cancellationToken).ConfigureAwait(false);

            var transcript = await ReadTranscriptAsync(response, cancellationToken).ConfigureAwait(false);

            // The model the request was sent with: the price follows what did the work.
            return transcript with { ModelId = model };
        }
        finally
        {
            _concurrency.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<VoiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var checkedAt = DateTimeOffset.UtcNow;

        try
        {
            var voices = await ListVoicesAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            return new VoiceHealth
            {
                ProviderName = ProviderName,
                IsHealthy = true,
                Latency = stopwatch.Elapsed,
                CheckedAt = checkedAt,
                VoiceCount = voices.Count,
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return Unhealthy(checkedAt, stopwatch.Elapsed, "Timed out.");
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();

            // 🚨 exception.Message embeds the target address (host:port) in a
            // connection refusal. HttpRequestError is a category name that
            // carries no address.
            return Unhealthy(checkedAt, stopwatch.Elapsed, $"Connection error ({exception.HttpRequestError}).");
        }
        catch (JsonException)
        {
            stopwatch.Stop();
            return Unhealthy(checkedAt, stopwatch.Elapsed, "Response is not valid JSON.");
        }
        catch (TraconException exception)
        {
            stopwatch.Stop();

            // The TraconException text is produced by EnsureSuccessAsync and
            // carries only the HTTP status code; it contains no secret or address.
            return Unhealthy(checkedAt, stopwatch.Elapsed, exception.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _concurrency.Dispose();

        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private VoiceHealth Unhealthy(DateTimeOffset checkedAt, TimeSpan latency, string detail)
        => new()
        {
            ProviderName = ProviderName,
            IsHealthy = false,
            Latency = latency,
            CheckedAt = checkedAt,
            Detail = detail,
        };

    private string ResolveVoiceId(SpeechRequest request)
    {
        var voiceId = request.VoiceId ?? _options.DefaultVoiceId;

        if (string.IsNullOrWhiteSpace(voiceId))
        {
            throw new TraconException(
                "No voice id was given and no default voice is configured. " +
                "Set `Tracon:Voice:DefaultVoiceId` or pass a voice id in the request. " +
                "The `list_voices` tool lists the available ids.");
        }

        return voiceId;
    }

    private HttpRequestMessage BuildSynthesisRequest(
        SpeechRequest request,
        string voiceId,
        string format,
        ElevenLabsRequestSettings settings,
        bool streaming,
        bool withTimestamps)
    {
        var escapedVoiceId = Uri.EscapeDataString(voiceId);

        // withTimestamps and streaming are never both true: SynthesizeStreamingAsync
        // rejects IncludeTimestamps before reaching here.
        var path = (streaming, withTimestamps) switch
        {
            (true, false) => $"v1/text-to-speech/{escapedVoiceId}/stream",
            (false, true) => $"v1/text-to-speech/{escapedVoiceId}/with-timestamps",
            (false, false) => $"v1/text-to-speech/{escapedVoiceId}",
            (true, true) => throw new UnreachableException(),
        };

        var uri = BuildUri($"{path}?output_format={Uri.EscapeDataString(format)}");

        var body = JsonSerializer.Serialize(
            new ElevenLabsSynthesisRequest
            {
                Text = request.Text,
                ModelId = SynthesisModel(request),
                LanguageCode = request.LanguageCode,

                // Every field below is null when the request sets no provider
                // setting, and a null field is not written: the body of a request
                // without settings is the same as before they existed.
                VoiceSettings = settings.ToVoiceSettings(),
                Seed = settings.Seed,
                PreviousText = settings.PreviousText,
                NextText = settings.NextText,
                PreviousRequestIds = settings.PreviousRequestIds?.ToList(),
            },
            ElevenLabsJsonContext.Default.ElevenLabsSynthesisRequest);

        var message = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        AddApiKey(message);

        return message;
    }

    private HttpRequestMessage BuildDialogueRequest(
        SpeechDialogueRequest request,
        ElevenLabsRequestSettings settings,
        string? model,
        string format)
    {
        var path = request.IncludeTimestamps ? "v1/text-to-dialogue/with-timestamps" : "v1/text-to-dialogue";
        var uri = BuildUri($"{path}?output_format={Uri.EscapeDataString(format)}");

        var body = JsonSerializer.Serialize(
            new ElevenLabsDialogueRequest
            {
                Inputs = [.. request.Turns.Select(static turn => new ElevenLabsDialogueInput { Text = turn.Text, VoiceId = turn.VoiceId })],
                ModelId = model,
                LanguageCode = request.LanguageCode,
                Settings = settings.ToDialogueSettings(),
                Seed = settings.Seed,
                PreviousText = settings.PreviousText,
                FutureText = settings.NextText,
                PreviousRequestIds = settings.PreviousRequestIds?.ToList(),
            },
            ElevenLabsJsonContext.Default.ElevenLabsDialogueRequest);

        var message = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        AddApiKey(message);

        return message;
    }

    /// <summary>Checks a dialogue against this client's limits.</summary>
    /// <returns>The characters of all turns: the estimate when the provider reports no count.</returns>
    /// <exception cref="TraconException">The request breaks a limit. The message never carries a turn's text.</exception>
    /// <remarks><c>internal</c>: unit tests verify each refusal without a network call.</remarks>
    internal int CheckDialogue(SpeechDialogueRequest request)
    {
        if (request.Turns is not { Count: > 0 } turns)
        {
            throw new TraconException("A dialogue request needs at least one turn.");
        }

        var characters = 0;
        var voices = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < turns.Count; index++)
        {
            var turn = turns[index] ?? throw new TraconException($"Dialogue turn {index} is missing.");

            if (string.IsNullOrWhiteSpace(turn.Text))
            {
                throw new TraconException($"Dialogue turn {index} has no text.");
            }

            if (string.IsNullOrWhiteSpace(turn.VoiceId))
            {
                throw new TraconException($"Dialogue turn {index} has no voice id. A dialogue turn has no default voice.");
            }

            characters += turn.Text.Length;
            voices.Add(turn.VoiceId);
        }

        if (voices.Count > MaxDialogueVoices)
        {
            throw new TraconException(
                $"The dialogue uses {voices.Count} voices; one '{ProviderName}' dialogue request takes at most " +
                $"{MaxDialogueVoices}. Split it into requests of at most {MaxDialogueVoices} voices.");
        }

        var limit = _options.MaxDialogueCharactersPerRequest;

        if (characters > limit)
        {
            throw new TraconException(
                $"The dialogue has {characters} characters; one '{ProviderName}' dialogue request takes at most " +
                $"{limit} ({nameof(VoiceOptions)}.{nameof(VoiceOptions.MaxDialogueCharactersPerRequest)}). " +
                "Split it into smaller requests: Tracon does not split or cut a dialogue.");
        }

        return characters;
    }

    /// <summary>Refuses a tenant credential: the built-in client calls with its configured key only.</summary>
    /// <exception cref="NotSupportedException"><paramref name="credential"/> is set.</exception>
    /// <remarks>
    /// The resolver never sends one here (<c>SupportsTenantCredentials</c> is
    /// <see langword="false"/>); a caller that calls the client directly with one would
    /// otherwise bill the tenant's call to the host's account without a word.
    /// </remarks>
    private void RefuseTenantCredential(ModelProviderCredential? credential)
    {
        if (credential is not null)
        {
            throw new NotSupportedException(
                $"The built-in '{ProviderName}' client cannot use a tenant credential. The call is not made on the " +
                "configured credential.");
        }
    }

    /// <summary>The model a synthesis request is sent with: the request's, the setting, then the built-in default.</summary>
    private string SynthesisModel(SpeechRequest request)
        => request.ModelId ?? _options.SynthesisModelId ?? DefaultSynthesisModel;

    private void AddApiKey(HttpRequestMessage message)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            message.Headers.TryAddWithoutValidation(ApiKeyHeader, _options.ApiKey);
        }
    }

    /// <summary>Combines the base address with a relative path.</summary>
    /// <remarks>
    /// When the base address does not end with a slash, <see cref="Uri"/>
    /// REPLACES the last segment (treats it like a file); it is normalized
    /// before combining. <c>internal</c>: unit tests verify this without a
    /// network call.
    /// </remarks>
    internal Uri BuildUri(string relativePath) => Combine(BaseEndpoint, relativePath);

    /// <inheritdoc cref="BuildUri"/>
    internal static Uri Combine(Uri baseEndpoint, string relativePath)
    {
        var text = baseEndpoint.ToString();

        if (!text.EndsWith('/'))
        {
            text += "/";
        }

        return new Uri(new Uri(text, UriKind.Absolute), relativePath);
    }

    /// <summary>Converts the provider's format name to a MIME type.</summary>
    /// <remarks>
    /// <c>internal</c>: unit tests verify this. An unknown format returns
    /// <c>application/octet-stream</c> and is rejected by the attachment
    /// store — better than silently writing a wrong type.
    /// </remarks>
    internal static string MediaTypeForFormat(string outputFormat)
    {
        if (outputFormat.StartsWith("mp3", StringComparison.OrdinalIgnoreCase))
        {
            return "audio/mpeg";
        }

        if (outputFormat.StartsWith("opus", StringComparison.OrdinalIgnoreCase) ||
            outputFormat.StartsWith("ogg", StringComparison.OrdinalIgnoreCase))
        {
            return "audio/ogg";
        }

        if (outputFormat.StartsWith("wav", StringComparison.OrdinalIgnoreCase))
        {
            return "audio/wav";
        }

        return "application/octet-stream";
    }

    /// <summary>Reads the billed character count from the response headers.</summary>
    /// <returns><see langword="null"/> when the provider does not report it.</returns>
    /// <remarks>
    /// <c>internal</c>: unit tests verify this with a prepared response. When
    /// the header is absent, the caller uses the input text's length and
    /// marks the measurement as an ESTIMATE.
    /// </remarks>
    internal static int? ReadBilledCharacters(HttpResponseMessage response)
    {
        foreach (var name in (ReadOnlySpan<string>)["character-cost", "x-character-cost"])
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                foreach (var value in values)
                {
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return parsed;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Reads the provider's id for the generation from the response headers.</summary>
    /// <returns><see langword="null"/> when the provider does not report it.</returns>
    /// <remarks><c>internal</c>: unit tests verify this with a prepared response.</remarks>
    internal static string? ReadRequestId(HttpResponseMessage response)
        => response.Headers.TryGetValues(RequestIdHeader, out var values)
           && values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) is { } id
            ? id
            : null;

    /// <summary>Parses the dialogue <c>.../with-timestamps</c> response body.</summary>
    /// <remarks><c>internal</c>: unit tests verify this with a prepared body.</remarks>
    internal static async ValueTask<ElevenLabsDialogueWithTimestampsResponse?> ReadDialogueWithTimestampsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer
                .DeserializeAsync(
                    stream,
                    ElevenLabsJsonContext.Default.ElevenLabsDialogueWithTimestampsResponse,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Maps the provider's <c>voice_segments</c> to the request's turns.</summary>
    /// <returns>The segments in order; <see langword="null"/> when the report is missing or does not match.</returns>
    /// <remarks>
    /// <para>
    /// A segment must name a turn the request had and the voice that turn used, with a
    /// finite, ordered time range. One segment that does not fit drops the whole list: a
    /// shifted index would put one speaker's time on another's turn, and the audio itself
    /// is still valid (the <see cref="ToAlignment"/> rule).
    /// </para>
    /// <para>
    /// The count is not compared with the turns: the provider documents a segment as a
    /// part of a turn. <c>internal</c>: unit tests verify this directly.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<SpeechDialogueSegment>? ToSegments(
        IReadOnlyList<ElevenLabsVoiceSegment>? segments,
        IReadOnlyList<SpeechDialogueTurn> turns)
    {
        if (segments is not { Count: > 0 })
        {
            return null;
        }

        var result = new List<SpeechDialogueSegment>(segments.Count);

        foreach (var segment in segments)
        {
            if (segment is not { DialogueInputIndex: { } index, VoiceId: { } voiceId, StartTimeSeconds: { } start, EndTimeSeconds: { } end } ||
                index < 0 ||
                index >= turns.Count ||
                !string.Equals(voiceId, turns[index].VoiceId, StringComparison.Ordinal) ||
                !double.IsFinite(start) ||
                !double.IsFinite(end) ||
                start < 0 ||
                end < start)
            {
                return null;
            }

            result.Add(new SpeechDialogueSegment
            {
                TurnIndex = index,
                VoiceId = voiceId,
                Start = TimeSpan.FromSeconds(start),
                End = TimeSpan.FromSeconds(end),
            });
        }

        return result;
    }

    /// <summary>Parses the <c>.../with-timestamps</c> response body.</summary>
    /// <remarks>
    /// <c>internal</c>: unit tests verify this with a prepared body. A missing
    /// <c>alignment</c> object (the provider does not always report it) is not
    /// an error - the caller gets <see langword="null"/> for
    /// <see cref="SpeechAudio.Alignment"/>, exactly like a provider that never
    /// supported timestamps at all (the no-surprises rule: same input, same behavior everywhere).
    /// </remarks>
    internal static async ValueTask<(byte[] Data, IReadOnlyList<SpeechAlignment>? Alignment)> ReadTimestampedAudioAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            var payload = await JsonSerializer
                .DeserializeAsync(
                    stream,
                    ElevenLabsJsonContext.Default.ElevenLabsAudioWithTimestampsResponse,
                    cancellationToken)
                .ConfigureAwait(false);

            var data = payload?.AudioBase64 is { Length: > 0 } base64
                ? Convert.FromBase64String(base64)
                : [];

            return (data, ToAlignment(payload?.Alignment));
        }
    }

    /// <summary>
    /// Converts the provider's parallel-array shape into
    /// <see cref="SpeechAlignment"/> entries.
    /// </summary>
    /// <remarks>
    /// The three arrays are contractually the same length; a malformed body
    /// (mismatched lengths) is treated as "no alignment" rather than throwing -
    /// the audio itself is still valid and usable.
    /// </remarks>
    internal static IReadOnlyList<SpeechAlignment>? ToAlignment(ElevenLabsCharacterAlignment? alignment)
    {
        if (alignment is not { Characters.Count: > 0 } ||
            alignment.CharacterStartTimesSeconds is not { } starts ||
            alignment.CharacterEndTimesSeconds is not { } ends ||
            starts.Count != alignment.Characters.Count ||
            ends.Count != alignment.Characters.Count)
        {
            return null;
        }

        var result = new List<SpeechAlignment>(alignment.Characters.Count);

        for (var index = 0; index < alignment.Characters.Count; index++)
        {
            result.Add(new SpeechAlignment
            {
                Character = alignment.Characters[index],
                Start = TimeSpan.FromSeconds(starts[index]),
                End = TimeSpan.FromSeconds(ends[index]),
            });
        }

        return result;
    }

    /// <summary>Parses one page of the voice list body.</summary>
    /// <remarks><c>internal</c>: unit tests verify this with a prepared body.</remarks>
    internal static async ValueTask<ElevenLabsVoicesResponse?> ReadVoicesPageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer
                .DeserializeAsync(stream, ElevenLabsJsonContext.Default.ElevenLabsVoicesResponse, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Maps, bounds and sorts raw provider voices into the public model.</summary>
    /// <remarks><c>internal</c>: unit tests verify this directly, without HTTP.</remarks>
    internal static IReadOnlyList<VoiceDescriptor> MapVoices(IEnumerable<ElevenLabsVoice> voices)
    {
        var result = new List<VoiceDescriptor>();

        foreach (var voice in voices)
        {
            if (result.Count >= MaxReportedVoices)
            {
                break;
            }

            if (voice.VoiceId is { Length: > 0 } id)
            {
                result.Add(new VoiceDescriptor
                {
                    VoiceId = id,
                    Name = voice.Name ?? id,
                    Category = voice.Category,
                    Attributes = VoiceAttributeMapper.Map(voice.Labels, voice.VerifiedLanguages),
                });
            }
        }

        result.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return result;
    }

    /// <summary>Parses the transcription response.</summary>
    /// <remarks><c>internal</c>: unit tests verify this with a prepared body.</remarks>
    internal static async ValueTask<SpeechTranscript> ReadTranscriptAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            var payload = await JsonSerializer
                .DeserializeAsync(
                    stream,
                    ElevenLabsJsonContext.Default.ElevenLabsTranscriptionResponse,
                    cancellationToken)
                .ConfigureAwait(false);

            return new SpeechTranscript
            {
                Text = payload?.Text ?? string.Empty,
                LanguageCode = payload?.LanguageCode,
                LanguageProbability = payload?.LanguageProbability,
                AudioDuration = payload?.AudioDurationSeconds is { } seconds
                    ? TimeSpan.FromSeconds(seconds)
                    : null,
            };
        }
    }

    /// <summary>
    /// Turns a failed response into an understandable error.
    /// </summary>
    /// <remarks>
    /// The error text carries only the status code. The provider's response
    /// body echoes the request (i.e. the spoken text) and sometimes a key
    /// fragment; putting the body in the error would carry them into logs and
    /// the UI.
    /// </remarks>
    private static async ValueTask EnsureSuccessAsync(
        HttpResponseMessage response,
        string what,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // The body is read and DISCARDED: consumed so the connection closes cleanly.
        _ = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        var hint = (int)response.StatusCode switch
        {
            401 => " The API key is invalid.",
            404 => " The voice or model id was not found.",
            422 => " The request was rejected by the provider; check the voice id and model name.",
            429 => " The provider's rate limit was reached.",
            _ => string.Empty,
        };

        throw new TraconException($"{what}: HTTP {(int)response.StatusCode}.{hint}");
    }
}
