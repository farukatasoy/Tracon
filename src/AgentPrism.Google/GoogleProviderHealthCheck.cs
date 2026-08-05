using System.Diagnostics;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// <c>GET {endpoint}/{apiVersion}/models</c> ucuna giderek Gemini saglayicisinin
/// erisilebilirligini denetler.
/// </summary>
/// <remarks>
/// <para>
/// Bu uc model adlarini doner ve <strong>ucret uretmez</strong> — model cagrisi
/// yapilmaz. Desen <c>OpenAIProviderHealthCheck</c> ile aynidir
/// (<c>docs/08-SAGLAYICI-GENISLEMESI.md</c>, bolum 8.3).
/// </para>
/// <para>
/// Kimlik dogrulamasi <c>x-goog-api-key</c> basligi ile yapilir. Anahtarin sorgu
/// dizesine (<c>?key=</c>) konmasi da mumkundur ama <strong>bilerek
/// kullanilmaz</strong>: sorgu dizeleri vekil sunucu ve erisim gunluklerine duz
/// metin olarak yazilir.
/// </para>
/// <para>
/// Yanit bicimi OpenAI'dan farklidir: dizi <c>models</c> altindadir ve her ogenin
/// adi <c>models/gemini-3.6-flash</c> gibi bir kaynak yoludur. Onek temizlenir,
/// cunku <see cref="ModelBinding.Model"/> alani onegi tasimaz.
/// </para>
/// </remarks>
internal sealed class GoogleProviderHealthCheck(string providerName, GoogleProviderOptions options)
    : IModelProviderHealthCheck
{
    private static readonly Uri DefaultGoogleEndpoint = new("https://generativelanguage.googleapis.com/");
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private const int MaxReportedModels = 200;
    private const string ModelResourcePrefix = "models/";

    /// <summary>Adres verilmediginde kullanilan API surumu.</summary>
    internal const string DefaultApiVersion = "v1beta";

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
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                BuildModelsEndpoint(options.Endpoint, options.ApiVersion));

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                request.Headers.Add("x-goog-api-key", options.ApiKey);
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
    /// Taban adres, API surumu ve <c>models</c> parcasini birlestirir.
    /// </summary>
    /// <remarks><c>internal</c>: birim testleri ag cagrisi yapmadan bu birlestirmeyi dogrular.</remarks>
    internal static Uri BuildModelsEndpoint(Uri? baseEndpoint, string? apiVersion)
    {
        var effective = baseEndpoint ?? DefaultGoogleEndpoint;
        var text = effective.ToString();

        if (!text.EndsWith('/'))
        {
            text += "/";
        }

        var version = string.IsNullOrWhiteSpace(apiVersion) ? DefaultApiVersion : apiVersion.Trim('/');

        return new Uri(new Uri(text, UriKind.Absolute), $"{version}/models");
    }

    /// <summary>
    /// <c>{"models":[{"name":"models/gemini-..."}]}</c> govdesinden model adlarini
    /// okur ve <c>models/</c> onegini temizler.
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

            if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<string>();

            foreach (var entry in models.EnumerateArray())
            {
                if (result.Count >= MaxReportedModels)
                {
                    break;
                }

                if (entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("name", out var name)
                    && name.ValueKind == JsonValueKind.String
                    && name.GetString() is { Length: > 0 } resourceName)
                {
                    result.Add(resourceName.StartsWith(ModelResourcePrefix, StringComparison.Ordinal)
                        ? resourceName[ModelResourcePrefix.Length..]
                        : resourceName);
                }
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
