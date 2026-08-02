using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// <c>GET {endpoint}/models</c> ucuna giderek bir OpenAI veya OpenAI uyumlu
/// saglayicinin erisilebilirligini denetler.
/// </summary>
/// <remarks>
/// <para>
/// Bu uc model adlarini doner ve <strong>ucret uretmez</strong> — model cagrisi
/// yapilmaz. Gerekce: <c>docs/08-SAGLAYICI-GENISLEMESI.md</c>, bolum 8.3.
/// </para>
/// <para>
/// Paylasilan bir statik <see cref="HttpClient"/> kullanilir. Yeni bir paket
/// (<c>Microsoft.Extensions.Http</c>) eklenmedi — K-007 gerekcesiyle ayni: kutuphane
/// tuketicinin bagimlilik grafigini kirletmemeli. Tek bir uzun omurlu istemci, dusuk
/// hacimli saglik denetimleri icin bilinen ve kabul edilebilir bir kaliptir.
/// </para>
/// </remarks>
internal sealed class OpenAIProviderHealthCheck(string providerName, OpenAIProviderOptions options)
    : IModelProviderHealthCheck
{
    private static readonly Uri DefaultOpenAiEndpoint = new("https://api.openai.com/v1/");
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private const int MaxReportedModels = 200;

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

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            }

            using var response = await SharedHttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedSource.Token)
                .ConfigureAwait(false);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return Unhealthy(
                    checkedAt,
                    stopwatch.Elapsed,
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
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
            // govdeye gomer — bu bir sir degildir ama uc adresi sizdirmama kuralini
            // (bkz. docs/08-SAGLAYICI-GENISLEMESI.md, DoD) ihlal eder. HttpRequestError
            // adres tasimayan bir kategori adidir (.NET 8+).
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
    /// Taban adresi <c>/models</c> ile birlestirir. Taban adres eğik cizgi ile
    /// bitmiyorsa <see cref="Uri"/> son parcayi DEGISTIRIR (dosya gibi davranir);
    /// bu yuzden birlestirmeden once normalize edilir.
    /// </summary>
    /// <remarks><c>internal</c>: birim testleri ag cagrisi yapmadan bu birlestirmeyi dogrular.</remarks>
    internal static Uri BuildModelsEndpoint(Uri? baseEndpoint)
    {
        var effective = baseEndpoint ?? DefaultOpenAiEndpoint;
        var text = effective.ToString();

        if (!text.EndsWith('/'))
        {
            text += "/";
        }

        return new Uri(new Uri(text, UriKind.Absolute), "models");
    }

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
