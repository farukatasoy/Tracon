using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Checks whether an OpenAI or OpenAI compatible provider is reachable by calling the
/// <c>GET {endpoint}/models</c> endpoint.
/// </summary>
/// <remarks>
/// <para>
/// This endpoint returns model names and <strong>costs nothing</strong> — it makes no
/// model call.
/// </para>
/// <para>
/// It uses a shared static <see cref="HttpClient"/>. No new package
/// (<c>Microsoft.Extensions.Http</c>) was added — the same reason: a library
/// must not pollute the dependency graph of its consumer. A single long lived client is
/// a known and acceptable pattern for low volume health checks.
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
            return Unhealthy(checkedAt, stopwatch.Elapsed, "Timed out.");
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();

            // On failures such as a refused connection, exception.Message embeds the
            // target address (host:port) in its text — that is not a secret, but it
            // breaks the rule that the endpoint address must not leak (see
            // docs/arsiv/fazlar/08-SAGLAYICI-GENISLEMESI.md, DoD). HttpRequestError is a category
            // name that carries no address (.NET 8+).
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
    /// Joins the base address with <c>/models</c>. When the base address does not end
    /// with a slash, <see cref="Uri"/> REPLACES the last segment (it treats it like a
    /// file); the address is therefore normalized before the join.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify this join without a network call.</remarks>
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

    /// <remarks><c>internal</c>: unit tests verify the parsing against a canned response body.</remarks>
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
