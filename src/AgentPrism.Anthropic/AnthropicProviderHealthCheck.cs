using System.Diagnostics;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// <c>GET {endpoint}/models</c> ucuna giderek Anthropic saglayicisinin
/// erisilebilirligini denetler.
/// </summary>
/// <remarks>
/// <para>
/// Bu uc model adlarini doner ve <strong>ucret uretmez</strong> — model cagrisi
/// yapilmaz. Desen <c>OpenAIProviderHealthCheck</c> ile birebir aynidir
/// (<c>docs/08-SAGLAYICI-GENISLEMESI.md</c>, bolum 8.3).
/// </para>
/// <para>
/// SDK yerine dogrudan <see cref="HttpClient"/> kullanilir: denetim yolunun hata
/// detayinin sir ve adres tasimadigi burada tam olarak denetlenebilir, ayrica
/// <c>internal static</c> yardimcilar ag cagrisi olmadan test edilebilir.
/// </para>
/// <para>
/// Anthropic kimlik dogrulamasi <c>Authorization: Bearer</c> degil, <c>x-api-key</c>
/// basligi ile yapilir ve <c>anthropic-version</c> basligi <strong>zorunludur</strong>;
/// eksikse uc <c>HTTP 400</c> doner.
/// </para>
/// </remarks>
internal sealed class AnthropicProviderHealthCheck(string providerName, AnthropicProviderOptions options)
    : IModelProviderHealthCheck
{
    private static readonly Uri DefaultAnthropicEndpoint = new("https://api.anthropic.com/v1/");
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private const int MaxReportedModels = 200;

    /// <summary>
    /// Anthropic'in istedigi API surum basligi. Tarihli bir surum kimligidir ve
    /// gunun tarihi degildir; yukseltmek bilincli bir karardir.
    /// </summary>
    internal const string AnthropicVersion = "2023-06-01";

    private static readonly HttpClient SharedHttpClient = new();

    /// <inheritdoc />
    public async ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var checkedAt = DateTimeOffset.UtcNow;

        using var timeoutSource = new CancellationTokenSource(options.Timeout ?? DefaultTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildModelsEndpoint(options.Endpoint));
            request.Headers.Add("anthropic-version", AnthropicVersion);

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                request.Headers.Add("x-api-key", options.ApiKey);
            }

            using var response = await SharedHttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedSource.Token)
                .ConfigureAwait(false);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return Unhealthy(checkedAt, stopwatch.Elapsed, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var models = await ReadModelIdsAsync(response, linkedSource.Token).ConfigureAwait(false);

            return new ModelProviderHealth
            {
                ProviderName = providerName,
                Status = ModelProviderHealthStatus.Healthy,
                Latency = stopwatch.Elapsed,
                CheckedAt = checkedAt,
                Models = models,
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

            // exception.Message baglanti reddi gibi durumlarda hedef adresi (host:port)
            // govdeye gomer. HttpRequestError adres tasimayan bir kategori adidir (.NET 8+).
            return Unhealthy(checkedAt, stopwatch.Elapsed, $"Baglanti hatasi ({exception.HttpRequestError}).");
        }
        catch (JsonException)
        {
            stopwatch.Stop();
            return new ModelProviderHealth
            {
                ProviderName = providerName,
                Status = ModelProviderHealthStatus.Degraded,
                Detail = "Yanit gecerli JSON degil.",
                Latency = stopwatch.Elapsed,
                CheckedAt = checkedAt,
            };
        }
    }

    private ModelProviderHealth Unhealthy(DateTimeOffset checkedAt, TimeSpan latency, string detail)
        => new()
        {
            ProviderName = providerName,
            Status = ModelProviderHealthStatus.Unhealthy,
            Detail = detail,
            Latency = latency,
            CheckedAt = checkedAt,
        };

    /// <summary>
    /// Taban adresi <c>/models</c> ile birlestirir. Taban adres egik cizgi ile
    /// bitmiyorsa <see cref="Uri"/> son parcayi DEGISTIRIR (dosya gibi davranir);
    /// bu yuzden birlestirmeden once normalize edilir.
    /// </summary>
    /// <remarks><c>internal</c>: birim testleri ag cagrisi yapmadan bu birlestirmeyi dogrular.</remarks>
    internal static Uri BuildModelsEndpoint(Uri? baseEndpoint)
    {
        var effective = baseEndpoint ?? DefaultAnthropicEndpoint;
        var text = effective.ToString();

        if (!text.EndsWith('/'))
        {
            text += "/";
        }

        return new Uri(new Uri(text, UriKind.Absolute), "models");
    }

    /// <summary>
    /// <c>{"data":[{"id":"claude-..."}]}</c> govdesinden model kimliklerini okur.
    /// </summary>
    /// <remarks><c>internal</c>: birim testleri hazir bir yanit govdesiyle ayristirmayi dogrular.</remarks>
    internal static async ValueTask<IReadOnlyList<string>> ReadModelIdsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var models = new List<string>();

            foreach (var entry in data.EnumerateArray())
            {
                if (models.Count >= MaxReportedModels)
                {
                    break;
                }

                if (entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("id", out var id)
                    && id.ValueKind == JsonValueKind.String
                    && id.GetString() is { Length: > 0 } modelId)
                {
                    models.Add(modelId);
                }
            }

            models.Sort(StringComparer.Ordinal);
            return models;
        }
    }
}
