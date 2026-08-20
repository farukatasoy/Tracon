using System.Diagnostics;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Checks the reachability of the Gemini provider by calling
/// <c>GET {endpoint}/{apiVersion}/models</c>.
/// </summary>
/// <remarks>
/// <para>
/// This endpoint returns model names and <strong>incurs no cost</strong> — no
/// model call is made. The pattern matches <c>OpenAIProviderHealthCheck</c>
/// </para>
/// <para>
/// Authentication uses the <c>x-goog-api-key</c> header. Putting the key in the
/// query string (<c>?key=</c>) is also possible but is <strong>deliberately
/// not used</strong>: query strings are written as plain text into proxy and
/// access logs.
/// </para>
/// <para>
/// The response shape differs from OpenAI's: the array is under <c>models</c>,
/// and each entry's name is a resource path like <c>models/gemini-3.6-flash</c>.
/// The prefix is stripped, because the <see cref="ModelBinding.Model"/> field does
/// not carry it.
/// </para>
/// </remarks>
internal sealed class GoogleProviderHealthCheck(string providerName, GoogleProviderOptions options)
    : IModelProviderHealthCheck
{
    private static readonly Uri DefaultGoogleEndpoint = new("https://generativelanguage.googleapis.com/");
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private const int MaxReportedModels = 200;
    private const string ModelResourcePrefix = "models/";

    /// <summary>API version used when no address is given.</summary>
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
            return Unhealthy(checkedAt, stopwatch.Elapsed, "Timed out.");
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();

            // exception.Message embeds the target address (host:port) in the body in
            // cases like a connection refusal. HttpRequestError is a category name
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
                Detail = "Response is not valid JSON.",
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
    /// Joins the base address, API version, and the <c>models</c> segment.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify this join without making a network call.</remarks>
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
    /// Reads model names from a <c>{"models":[{"name":"models/gemini-..."}]}</c> body
    /// and strips the <c>models/</c> prefix.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify parsing against a fixed response body.</remarks>
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
