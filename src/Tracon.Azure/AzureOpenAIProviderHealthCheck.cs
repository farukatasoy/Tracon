using System.Diagnostics;
using System.Text.Json;
using Azure.Core;

namespace Tracon;

/// <summary>
/// Checks whether an Azure OpenAI resource is reachable by calling
/// <c>GET {endpoint}/openai/models?api-version=...</c>.
/// </summary>
/// <remarks>
/// <para>
/// This endpoint returns the models the resource can reach and <strong>incurs
/// no cost</strong> — no model call is made. The pattern is identical to
/// <c>OpenAIProviderHealthCheck</c> and <c>AnthropicProviderHealthCheck</c>.
/// </para>
/// <para>
/// <strong>The returned list is a model list, not a deployment list.</strong>
/// The name used in agent definitions is the deployment name, and it does not
/// appear on this endpoint. What the check proves is: the address is correct,
/// the credential is valid, and the resource is up. Whether the deployment name
/// is correct is only found out on the first real call.
/// </para>
/// <para>
/// <see cref="HttpClient"/> is used directly instead of the SDK: this lets the
/// check fully control whether the error detail on the check path carries a
/// secret or an address, and lets the <c>internal static</c> helpers be tested
/// without a network call.
/// </para>
/// <para>
/// Authentication happens one of two ways: an <c>api-key</c> header for the API
/// key (on Azure this is not <c>Authorization: Bearer</c>), or a <c>Bearer</c>
/// token obtained through <see cref="TokenCredential"/> for a managed credential.
/// </para>
/// </remarks>
internal sealed class AzureOpenAIProviderHealthCheck : IModelProviderHealthCheck
{
    /// <summary>
    /// The Azure OpenAI data-plane API version the check uses.
    /// </summary>
    /// <remarks>
    /// Kept identical to the version the <c>Azure.AI.OpenAI</c> version we use
    /// produces for a chat request (measured 2026-08-05:
    /// <c>?api-version=2024-10-21</c>). Upgrading it is a deliberate decision.
    /// </remarks>
    internal const string ApiVersion = "2024-10-21";

    /// <summary>The Entra token scope on the Azure public cloud.</summary>
    internal const string DefaultAudience = "https://cognitiveservices.azure.com/.default";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private const int MaxReportedModels = 200;

    // PooledConnectionLifetime bounds how long a resolved address is reused: a
    // provider that fails over behind DNS would otherwise stay pinned to the old
    // address for the process lifetime. Two minutes matches
    // EgressSocketGuard.CreateHandler.
    private static readonly HttpClient SharedHttpClient = new(
        new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) },
        disposeHandler: true);

    private readonly string _providerName;
    private readonly AzureOpenAIProviderOptions _options;
    private readonly TokenCredential? _credential;

    /// <summary>Builds a new health check.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="options">The provider options.</param>
    /// <remarks>
    /// The credential factory is called <strong>once</strong> here; building a
    /// new credential object on every check would waste the token cache.
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
            return Unhealthy(checkedAt, TimeSpan.Zero, "The resource address is not defined.");
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
                // Azure OpenAI authenticates a key through the `api-key` header.
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
            return Unhealthy(checkedAt, stopwatch.Elapsed, "Timed out.");
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();

            // exception.Message embeds the target address (host:port) into the
            // body in cases like connection refused. HttpRequestError is a
            // category name that carries no address (.NET 8+).
            return Unhealthy(checkedAt, stopwatch.Elapsed, $"Connection error ({exception.HttpRequestError}).");
        }
        catch (JsonException)
        {
            stopwatch.Stop();
            return new ModelProviderHealth
            {
                ProviderName = _providerName,
                Status = ModelProviderHealthStatus.Degraded,
                Detail = "The response is not valid JSON.",
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
    /// Combines the resource address with <c>openai/models?api-version=...</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the base address does not end with a slash, <see cref="Uri"/>
    /// REPLACES the last segment (it behaves like a file); it is therefore
    /// normalized before combining.
    /// </para>
    /// <para><c>internal</c>: unit tests verify this combination without a network call.</para>
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
    /// Reads model ids from a <c>{"data":[{"id":"gpt-..."}]}</c> body.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify parsing against a prepared response body.</remarks>
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
