using System.Diagnostics;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Checks the Anthropic provider's reachability by calling <c>GET {endpoint}/models</c>.
/// </summary>
/// <remarks>
/// <para>
/// This endpoint returns model names and <strong>incurs no cost</strong> — no model
/// call is made. The pattern is identical to <c>OpenAIProviderHealthCheck</c>
/// </para>
/// <para>
/// <see cref="HttpClient"/> is used directly instead of the SDK: this lets the
/// check path be fully verified to carry neither secret nor address in its error
/// detail, and lets the <c>internal static</c> helpers be tested without a
/// network call.
/// </para>
/// <para>
/// Anthropic authentication uses the <c>x-api-key</c> header, not
/// <c>Authorization: Bearer</c>, and the <c>anthropic-version</c> header is
/// <strong>required</strong>; the endpoint returns <c>HTTP 400</c> when it is missing.
/// </para>
/// </remarks>
internal sealed class AnthropicProviderHealthCheck(string providerName, AnthropicProviderOptions options)
    : IModelProviderHealthCheck
{
    private static readonly Uri DefaultAnthropicEndpoint = new("https://api.anthropic.com/v1/");
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private const int MaxReportedModels = 200;

    /// <summary>
    /// The API version header Anthropic requires. It is a dated version id, not
    /// today's date; bumping it is a deliberate decision.
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
            return Unhealthy(checkedAt, stopwatch.Elapsed, "Timed out.");
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();

            // exception.Message embeds the target address (host:port) in the body in
            // cases such as a refused connection. HttpRequestError is a category name
            // that carries no address (.NET 8+).
            return Unhealthy(checkedAt, stopwatch.Elapsed, $"Connection error ({exception.HttpRequestError}).");
        }
        catch (JsonException)
        {
            stopwatch.Stop();
            return new ModelProviderHealth
            {
                ProviderName = providerName,
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
            ProviderName = providerName,
            Status = ModelProviderHealthStatus.Unhealthy,
            Detail = detail,
            Latency = latency,
            CheckedAt = checkedAt,
        };

    /// <summary>
    /// Joins the base address with <c>/models</c>. When the base address does not
    /// end with a slash, <see cref="Uri"/> REPLACES its last segment (treating it
    /// like a file); the address is therefore normalized before joining.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify this join without a network call.</remarks>
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
    /// Reads model ids from a <c>{"data":[{"id":"claude-..."}]}</c> body.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify parsing against a canned response body.</remarks>
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
