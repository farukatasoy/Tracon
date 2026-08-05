using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// ElevenLabs'in uc HTTP ucunu kullanan <see cref="ISpeechSynthesizer"/> ve
/// <see cref="ISpeechTranscriber"/> uygulamasi.
/// </summary>
/// <remarks>
/// <para>
/// SDK yerine ham <see cref="HttpClient"/>: kullanilan yuzey uc uctan ibarettir,
/// JSON sozlesmesi basittir ve kaynak ureteciyle AOT uyumlu kalir. Ayrica hata
/// detayinin sir ve adres tasimadigi burada tam olarak denetlenebilir.
/// </para>
/// <para>
/// Eszamanlilik bir <see cref="SemaphoreSlim"/> ile sinirlanir: ses istekleri
/// pahalidir ve yanlis yazilmis tek bir agent tanimi onlarca istegi ayni anda
/// baslatabilir.
/// </para>
/// </remarks>
internal sealed class ElevenLabsSpeechClient : ISpeechSynthesizer, ISpeechTranscriber, IVoiceHealthCheck, IDisposable
{
    /// <summary>Saglayicinin genel taban adresi.</summary>
    internal static readonly Uri DefaultEndpoint = new("https://api.elevenlabs.io/");

    /// <summary>Kimlik dogrulama basligi. 🚨 <c>Authorization: Bearer</c> DEGILDIR.</summary>
    internal const string ApiKeyHeader = "xi-api-key";

    /// <summary>Saglayicinin varsayilan sentez modeli.</summary>
    internal const string DefaultSynthesisModel = "eleven_multilingual_v2";

    /// <summary>Saglayicinin varsayilan cozum modeli.</summary>
    internal const string DefaultTranscriptionModel = "scribe_v2";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(100);
    private const int MaxReportedVoices = 500;

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly VoiceOptions _options;
    private readonly SemaphoreSlim _concurrency;

    /// <summary>Yeni bir istemci kurar.</summary>
    /// <param name="options">Ayarlar.</param>
    /// <param name="httpClient">
    /// Kullanilacak istemci. <see langword="null"/> ise istemci burada kurulur ve
    /// bu nesneye ait olur. Testler sahte bir <c>HttpMessageHandler</c> gecirir.
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
            using var message = BuildSynthesisRequest(request, voiceId, format, streaming: false);
            using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);

            await EnsureSuccessAsync(response, "Ses uretilemedi", cancellationToken).ConfigureAwait(false);

            var data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var billed = ReadBilledCharacters(response);

            return new SpeechAudio
            {
                Data = data,
                MediaType = MediaTypeForFormat(format),
                CharactersBilled = billed ?? request.Text.Length,

                // Saglayici sayiyi bildirmediyse deger bir TAHMINDIR ve oyle
                // isaretlenir. Tahmini olcum gibi gostermek fiyat uydurmaktir.
                UsageSource = billed is null ? SpeechUsageSource.Estimated : SpeechUsageSource.Provider,
            };
        }
        finally
        {
            _concurrency.Release();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
        SpeechRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var voiceId = ResolveVoiceId(request);
        var format = request.OutputFormat ?? _options.OutputFormat;

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var message = BuildSynthesisRequest(request, voiceId, format, streaming: true);

            using var response = await _http
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            await EnsureSuccessAsync(response, "Ses akisi baslatilamadi", cancellationToken).ConfigureAwait(false);

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

                    // Tampon yeniden kullanilir; tuketiciye kopya verilir.
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

        await EnsureSuccessAsync(response, "Ses listesi alinamadi", cancellationToken).ConfigureAwait(false);

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
            // 🚨 Uc `multipart/form-data` ister; JSON govdesi KABUL ETMEZ.
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

            await EnsureSuccessAsync(response, "Ses cozulemedi", cancellationToken).ConfigureAwait(false);

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
            return Unhealthy(checkedAt, stopwatch.Elapsed, "Zaman asimi.");
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();

            // 🚨 exception.Message baglanti reddinde hedef adresi (host:port)
            // govdeye gomer. HttpRequestError adres tasimayan bir kategori adidir.
            return Unhealthy(checkedAt, stopwatch.Elapsed, $"Baglanti hatasi ({exception.HttpRequestError}).");
        }
        catch (JsonException)
        {
            stopwatch.Stop();
            return Unhealthy(checkedAt, stopwatch.Elapsed, "Yanit gecerli JSON degil.");
        }
        catch (AgentPrismException exception)
        {
            stopwatch.Stop();

            // AgentPrismException metni EnsureSuccessAsync tarafindan uretilir ve
            // yalnizca HTTP durum kodu tasir; sir ve adres icermez.
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
                "Ses kimligi verilmedi ve varsayilan ses tanimli degil. " +
                "`AgentPrism:Voice:DefaultVoiceId` ayarini verin veya istekte bir ses kimligi gecirin. " +
                "Kullanilabilir kimlikleri `list_voices` tool'u listeler.");
        }

        return voiceId;
    }

    private HttpRequestMessage BuildSynthesisRequest(
        SpeechRequest request,
        string voiceId,
        string format,
        bool streaming)
    {
        var path = streaming
            ? $"v1/text-to-speech/{Uri.EscapeDataString(voiceId)}/stream"
            : $"v1/text-to-speech/{Uri.EscapeDataString(voiceId)}";

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

    /// <summary>Taban adresi goreli bir yol ile birlestirir.</summary>
    /// <remarks>
    /// 🚨 Taban adres egik cizgi ile bitmiyorsa <see cref="Uri"/> son parcayi
    /// DEGISTIRIR (dosya gibi davranir); birlestirmeden once normalize edilir.
    /// <c>internal</c>: birim testleri ag cagrisi olmadan dogrular.
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

    /// <summary>Saglayicinin bicim adini bir MIME turune cevirir.</summary>
    /// <remarks>
    /// <c>internal</c>: birim testleri dogrular. Bilinmeyen bir bicim
    /// <c>application/octet-stream</c> dondurur ve ek deposu tarafindan
    /// reddedilir — sessizce yanlis bir tur yazmaktan iyidir.
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

    /// <summary>Yanit basliklarindan faturalanan karakter sayisini okur.</summary>
    /// <returns>Saglayici bildirmediyse <see langword="null"/>.</returns>
    /// <remarks>
    /// <c>internal</c>: birim testleri hazir bir yanitla dogrular. Baslik yoksa
    /// cagiran metnin uzunlugunu kullanir ve olcumu TAHMIN olarak isaretler.
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

    /// <summary>Ses listesi govdesini ayristirir.</summary>
    /// <remarks><c>internal</c>: birim testleri hazir bir govdeyle dogrular.</remarks>
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

    /// <summary>Cozum yanitini ayristirir.</summary>
    /// <remarks><c>internal</c>: birim testleri hazir bir govdeyle dogrular.</remarks>
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
    /// Basarisiz bir yaniti anlasilir bir hataya cevirir.
    /// </summary>
    /// <remarks>
    /// 🚨 Hata metni yalnizca durum kodu tasir. Saglayicinin govdesi istegi (yani
    /// seslendirilen metni) ve bazen anahtar parcasini yankilar; govdeyi hataya
    /// koymak onlari gunluge ve arayuze tasirdi.
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

        // Govde okunur ve ATILIR: baglantinin duzgun kapanmasi icin tuketilir.
        _ = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        var hint = (int)response.StatusCode switch
        {
            401 => " API anahtari gecersiz.",
            404 => " Ses veya model kimligi bulunamadi.",
            422 => " Istek saglayici tarafindan reddedildi; ses kimligini ve model adini denetleyin.",
            429 => " Saglayici hiz sinirina ulasildi.",
            _ => string.Empty,
        };

        throw new AgentPrismException($"{what}: HTTP {(int)response.StatusCode}.{hint}");
    }
}
