using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// <see cref="ISpeechSynthesizer"/> and <see cref="ISpeechTranscriber"/>
/// implementation that uses ElevenLabs' three HTTP endpoints.
/// </summary>
/// <remarks>
/// <para>
/// Raw <see cref="HttpClient"/> instead of an SDK: the surface used is only
/// three endpoints, the JSON contract is simple, and source generation keeps
/// it AOT compatible. This also lets the code fully control that the error
/// detail carries no secret or address.
/// </para>
/// <para>
/// Concurrency is bounded with a <see cref="SemaphoreSlim"/>: voice requests
/// are expensive, and a single misconfigured agent definition can fire dozens
/// of requests at once.
/// </para>
/// </remarks>
internal sealed class ElevenLabsSpeechClient : ISpeechSynthesizer, ISpeechTranscriber, IVoiceHealthCheck, IDisposable
{
    /// <summary>The provider's public base address.</summary>
    internal static readonly Uri DefaultEndpoint = new("https://api.elevenlabs.io/");

    /// <summary>Authentication header. It is NOT <c>Authorization: Bearer</c>.</summary>
    internal const string ApiKeyHeader = "xi-api-key";

    /// <summary>The provider's default synthesis model.</summary>
    internal const string DefaultSynthesisModel = "eleven_multilingual_v2";

    /// <summary>The provider's default transcription model.</summary>
    internal const string DefaultTranscriptionModel = "scribe_v2";

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
        _http = httpClient ?? new HttpClient();
        _http.Timeout = options.Timeout ?? DefaultTimeout;
        _concurrency = new SemaphoreSlim(options.MaxConcurrentRequests, options.MaxConcurrentRequests);
    }

    /// <inheritdoc />
    public string ProviderName => VoiceProviderNames.ElevenLabs;

    /// <inheritdoc />
    public int MaxCharactersPerRequest => _options.MaxCharactersPerRequest;

    private Uri BaseEndpoint => _options.Endpoint ?? DefaultEndpoint;

    /// <inheritdoc />
    public async ValueTask<SpeechAudio> SynthesizeAsync(
        SpeechRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var voiceId = ResolveVoiceId(request);
        var format = request.OutputFormat ?? _options.OutputFormat;

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return request.IncludeTimestamps
                ? await SynthesizeWithTimestampsAsync(request, voiceId, format, cancellationToken).ConfigureAwait(false)
                : await SynthesizePlainAsync(request, voiceId, format, cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        using var message = BuildSynthesisRequest(request, voiceId, format, streaming: false, withTimestamps: false);
        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, "Speech could not be generated", cancellationToken).ConfigureAwait(false);

        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var billed = ReadBilledCharacters(response);

        return new SpeechAudio
        {
            Data = data,
            MediaType = MediaTypeForFormat(format),
            CharactersBilled = billed ?? request.Text.Length,

            // When the provider does not report the count, the value is an
            // ESTIMATE and is marked as such. Presenting an estimate as a
            // measurement would be inventing a price.
            UsageSource = billed is null ? SpeechUsageSource.Estimated : SpeechUsageSource.Provider,
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
        CancellationToken cancellationToken)
    {
        using var message = BuildSynthesisRequest(request, voiceId, format, streaming: false, withTimestamps: true);
        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, "Speech could not be generated", cancellationToken).ConfigureAwait(false);

        var billed = ReadBilledCharacters(response);
        var payload = await ReadTimestampedAudioAsync(response, cancellationToken).ConfigureAwait(false);

        return new SpeechAudio
        {
            Data = payload.Data,
            MediaType = MediaTypeForFormat(format),
            CharactersBilled = billed ?? request.Text.Length,
            UsageSource = billed is null ? SpeechUsageSource.Estimated : SpeechUsageSource.Provider,
            Alignment = payload.Alignment,
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
            throw new AgentPrismException(
                $"{nameof(SpeechRequest.IncludeTimestamps)} is not supported together with streaming synthesis: " +
                $"{nameof(SynthesizeStreamingAsync)} returns raw audio chunks only and has no channel for " +
                $"alignment data. Use {nameof(SynthesizeAsync)} for timestamped speech.");
        }

        var voiceId = ResolveVoiceId(request);
        var format = request.OutputFormat ?? _options.OutputFormat;

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var message = BuildSynthesisRequest(request, voiceId, format, streaming: true, withTimestamps: false);

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
    public async ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, BuildUri("v2/voices"));
        AddApiKey(message);

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, "Voice list could not be retrieved", cancellationToken).ConfigureAwait(false);

        return await ReadVoicesAsync(response, cancellationToken).ConfigureAwait(false);
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

            return await ReadTranscriptAsync(response, cancellationToken).ConfigureAwait(false);
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
        catch (AgentPrismException exception)
        {
            stopwatch.Stop();

            // The AgentPrismException text is produced by EnsureSuccessAsync and
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
            throw new AgentPrismException(
                "No voice id was given and no default voice is configured. " +
                "Set `AgentPrism:Voice:DefaultVoiceId` or pass a voice id in the request. " +
                "The `list_voices` tool lists the available ids.");
        }

        return voiceId;
    }

    private HttpRequestMessage BuildSynthesisRequest(
        SpeechRequest request,
        string voiceId,
        string format,
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
                ModelId = request.ModelId ?? _options.SynthesisModelId ?? DefaultSynthesisModel,
                LanguageCode = request.LanguageCode,
            },
            ElevenLabsJsonContext.Default.ElevenLabsSynthesisRequest);

        var message = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        AddApiKey(message);

        return message;
    }

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

    /// <summary>Parses the voice list body.</summary>
    /// <remarks><c>internal</c>: unit tests verify this with a prepared body.</remarks>
    internal static async ValueTask<IReadOnlyList<VoiceDescriptor>> ReadVoicesAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            var payload = await JsonSerializer
                .DeserializeAsync(stream, ElevenLabsJsonContext.Default.ElevenLabsVoicesResponse, cancellationToken)
                .ConfigureAwait(false);

            if (payload?.Voices is not { Count: > 0 } voices)
            {
                return [];
            }

            var result = new List<VoiceDescriptor>(voices.Count);

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
                    });
                }
            }

            result.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

            return result;
        }
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

        throw new AgentPrismException($"{what}: HTTP {(int)response.StatusCode}.{hint}");
    }
}
