using System.Diagnostics;
using System.Text.Json;
using Azure.Core;

namespace AgentPrism;

/// <summary>
/// <c>GET {endpoint}/openai/models?api-version=...</c> ucuna giderek Azure OpenAI
/// kaynaginin erisilebilirligini denetler.
/// </summary>
/// <remarks>
/// <para>
/// Bu uc kaynagin erisebildigi modelleri doner ve <strong>ucret uretmez</strong> —
/// model cagrisi yapilmaz. Desen <c>OpenAIProviderHealthCheck</c> ve
/// <c>AnthropicProviderHealthCheck</c> ile birebir aynidir.
/// </para>
/// <para>
/// 🚨 <strong>Donen liste model listesidir, deployment listesi degildir.</strong>
/// Agent tanimlarinda kullanilacak ad deployment adidir ve bu ucta gorunmez.
/// Denetimin kanitladigi sey sudur: adres dogru, kimlik gecerli ve kaynak ayakta.
/// Deployment adinin dogrulugu ilk gercek cagrida anlasilir.
/// </para>
/// <para>
/// SDK yerine dogrudan <see cref="HttpClient"/> kullanilir: denetim yolunun hata
/// detayinin sir ve adres tasimadigi burada tam olarak denetlenebilir, ayrica
/// <c>internal static</c> yardimcilar ag cagrisi olmadan test edilebilir.
/// </para>
/// <para>
/// Kimlik dogrulamasi iki yoldan biriyle yapilir: API anahtari icin <c>api-key</c>
/// basligi (Azure'da <c>Authorization: Bearer</c> degildir), yonetilen kimlik icin
/// <see cref="TokenCredential"/> uzerinden alinan bir <c>Bearer</c> token.
/// </para>
/// </remarks>
internal sealed class AzureOpenAIProviderHealthCheck : IModelProviderHealthCheck
{
    /// <summary>
    /// Denetimin kullandigi Azure OpenAI veri duzlemi API surumu.
    /// </summary>
    /// <remarks>
    /// Kullandigimiz <c>Azure.AI.OpenAI</c> surumunun sohbet istegi icin urettigi
    /// surumle ayni tutulur (olculdu, 2026-08-05:
    /// <c>?api-version=2024-10-21</c>). Yukseltmek bilincli bir karardir.
    /// </remarks>
    internal const string ApiVersion = "2024-10-21";

    /// <summary>Azure genel bulutunda Entra token kapsami.</summary>
    internal const string DefaultAudience = "https://cognitiveservices.azure.com/.default";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private const int MaxReportedModels = 200;

    private static readonly HttpClient SharedHttpClient = new();

    private readonly string _providerName;
    private readonly AzureOpenAIProviderOptions _options;
    private readonly TokenCredential? _credential;

    /// <summary>Yeni bir saglik denetimi kurar.</summary>
    /// <param name="providerName">Saglayici adi.</param>
    /// <param name="options">Saglayici ayarlari.</param>
    /// <remarks>
    /// Kimlik fabrikasi burada <strong>bir kez</strong> cagrilir; her denetimde
    /// yeni bir kimlik nesnesi kurmak token onbellegini bosa cikarirdi.
    /// </remarks>
    public AzureOpenAIProviderHealthCheck(string providerName, AzureOpenAIProviderOptions options)
    {
        _providerName = providerName;
        _options = options;
        _credential = options.CredentialFactory?.Invoke();
    }

    /// <inheritdoc />
    public async ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var checkedAt = DateTimeOffset.UtcNow;

        if (_options.Endpoint is null)
        {
            return Unhealthy(checkedAt, TimeSpan.Zero, "Kaynak adresi tanimli degil.");
        }

        using var timeoutSource = new CancellationTokenSource(_options.Timeout ?? DefaultTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildModelsEndpoint(_options.Endpoint));

            if (_credential is not null)
            {
                var scope = string.IsNullOrWhiteSpace(_options.Audience) ? DefaultAudience : _options.Audience;
                var token = await _credential
                    .GetTokenAsync(new TokenRequestContext([scope]), linkedSource.Token)
                    .ConfigureAwait(false);

                request.Headers.Add("Authorization", $"Bearer {token.Token}");
            }
            else if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                // Azure OpenAI anahtar dogrulamasini `api-key` basligi ile yapar.
                request.Headers.Add("api-key", _options.ApiKey);
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
                ProviderName = _providerName,
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
                ProviderName = _providerName,
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
            ProviderName = _providerName,
            Status = ModelProviderHealthStatus.Unhealthy,
            Detail = detail,
            Latency = latency,
            CheckedAt = checkedAt,
        };

    /// <summary>
    /// Kaynak adresini <c>openai/models?api-version=...</c> ile birlestirir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Taban adres egik cizgi ile bitmiyorsa <see cref="Uri"/> son parcayi DEGISTIRIR
    /// (dosya gibi davranir); bu yuzden birlestirmeden once normalize edilir.
    /// </para>
    /// <para><c>internal</c>: birim testleri ag cagrisi yapmadan bu birlestirmeyi dogrular.</para>
    /// </remarks>
    internal static Uri BuildModelsEndpoint(Uri baseEndpoint)
    {
        var text = baseEndpoint.ToString();

        if (!text.EndsWith('/'))
        {
            text += "/";
        }

        return new Uri(new Uri(text, UriKind.Absolute), $"openai/models?api-version={ApiVersion}");
    }

    /// <summary>
    /// <c>{"data":[{"id":"gpt-..."}]}</c> govdesinden model kimliklerini okur.
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
