using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// The body every provider health check shares: one <c>GET</c> to a model-list
/// endpoint, a timeout, and the mapping of each failure to a
/// <see cref="ModelProviderHealth"/> whose <c>Detail</c> carries neither the key
/// nor the address.
/// </summary>
/// <remarks>
/// <para>
/// What stays in each provider's health check class: the endpoint
/// (<c>BuildModelsEndpoint</c>), the auth headers (the <c>authorize</c> delegate)
/// and the response body shape (the <c>readModelIds</c> delegate). Everything
/// else lives here once; it used to be copied into four packages.
/// </para>
/// <para>
/// Shared source: this type compiles into each provider assembly and stays
/// <see langword="internal"/> (<c>src/Tracon.Providers.Shared/README.md</c>).
/// </para>
/// </remarks>
internal sealed class ProviderHealthCheckCore(string providerName, ILogger? logger = null)
{
    /// <summary>The timeout used when the provider options leave <c>Timeout</c> unset.</summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The upper bound on the model ids a health result reports.</summary>
    internal const int MaxReportedModels = 200;

    // PooledConnectionLifetime bounds how long a resolved address is reused: a
    // provider that fails over behind DNS would otherwise stay pinned to the old
    // address for the process lifetime. Two minutes matches
    // EgressSocketGuard.CreateHandler.
    //
    // A shared static client, not IHttpClientFactory: Microsoft.Extensions.Http
    // would join the consumer's dependency graph (K-007). One long-lived client
    // is an accepted pattern for low-volume health checks. The source is linked
    // into each provider assembly, so each package still owns its own client.
    private static readonly HttpClient SharedHttpClient = new(
        new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) },
        disposeHandler: true);

    /// <summary>Calls <paramref name="modelsEndpoint"/> and maps the outcome.</summary>
    /// <param name="modelsEndpoint">The model-list endpoint.</param>
    /// <param name="timeout">The provider's timeout; <see cref="DefaultTimeout"/> when <see langword="null"/>.</param>
    /// <param name="authorize">Writes the provider's auth headers onto the request.</param>
    /// <param name="readModelIds">Reads the model ids from a successful response.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The health result. A credential, network, timeout or JSON failure is a result, not an exception.</returns>
    /// <remarks>
    /// <paramref name="authorize"/> runs <strong>inside</strong> the timeout: an
    /// Azure token request that hangs is reported as <c>Timed out.</c>, the same
    /// as a hanging HTTP call. Caller cancellation is not a timeout and propagates.
    /// </remarks>
    internal async ValueTask<ModelProviderHealth> CheckAsync(
        Uri modelsEndpoint,
        TimeSpan? timeout,
        Func<HttpRequestMessage, CancellationToken, ValueTask> authorize,
        Func<HttpResponseMessage, CancellationToken, ValueTask<IReadOnlyList<string>>> readModelIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modelsEndpoint);
        ArgumentNullException.ThrowIfNull(authorize);
        ArgumentNullException.ThrowIfNull(readModelIds);

        var stopwatch = Stopwatch.StartNew();
        var checkedAt = DateTimeOffset.UtcNow;

        using var timeoutSource = new CancellationTokenSource(timeout ?? DefaultTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, modelsEndpoint);

            try
            {
                await authorize(request, linkedSource.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A credential that throws (Azure.Identity's
                // CredentialUnavailableException without `az login`) is this
                // provider's status, not an exception for the caller: before
                // phase 181 it escaped and failed /api/models/health for every
                // provider. Only the type name is reported - a credential
                // library's message can carry tenant and client identifiers;
                // the operator gets the message and stack trace in the log.
                stopwatch.Stop();

                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(
                        exception,
                        "The '{Provider}' health check could not authorize its request; the provider is reported Unhealthy.",
                        providerName);
                }

                return Unhealthy(checkedAt, stopwatch.Elapsed, $"Credential error ({exception.GetType().Name}).");
            }

            using var response = await SharedHttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedSource.Token)
                .ConfigureAwait(false);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return Unhealthy(checkedAt, stopwatch.Elapsed, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var models = await readModelIds(response, linkedSource.Token).ConfigureAwait(false);

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

            // On failures such as a refused connection, exception.Message embeds
            // the target address (host:port) in its text. That is not a secret,
            // but it breaks the rule that the endpoint address must not leak
            // (docs/arsiv/fazlar/08-SAGLAYICI-GENISLEMESI.md, DoD).
            // HttpRequestError is a category name that carries no address.
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

    /// <summary>
    /// Builds an <see cref="ModelProviderHealthStatus.Unhealthy"/> result. A check
    /// that ends before the HTTP call (Azure without a resource address) uses it too.
    /// </summary>
    /// <param name="checkedAt">When the check started.</param>
    /// <param name="latency">The measured latency.</param>
    /// <param name="detail">A detail that carries neither a secret nor an address.</param>
    /// <returns>The health result.</returns>
    internal ModelProviderHealth Unhealthy(DateTimeOffset checkedAt, TimeSpan latency, string detail)
        => new()
        {
            ProviderName = providerName,
            Status = ModelProviderHealthStatus.Unhealthy,
            Detail = detail,
            Latency = latency,
            CheckedAt = checkedAt,
        };

    /// <summary>
    /// Joins a base address with a relative path. When the base address does not
    /// end with a slash, <see cref="Uri"/> REPLACES its last segment (it treats it
    /// like a file), so the address is normalized before the join.
    /// </summary>
    /// <param name="baseEndpoint">The absolute base address.</param>
    /// <param name="relativePath">The path (and query) to append.</param>
    /// <returns>The joined address.</returns>
    internal static Uri JoinEndpoint(Uri baseEndpoint, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(baseEndpoint);

        var text = baseEndpoint.ToString();

        if (!text.EndsWith('/'))
        {
            text += "/";
        }

        return new Uri(new Uri(text, UriKind.Absolute), relativePath);
    }

    /// <summary>
    /// Reads model ids from a body shaped <c>{"array":[{"id":"..."}]}</c>, sorted
    /// ordinally and capped at <see cref="MaxReportedModels"/>. Entries without a
    /// string id are skipped; a body without the array yields an empty list.
    /// </summary>
    /// <param name="response">The successful response.</param>
    /// <param name="arrayProperty">The name of the array property.</param>
    /// <param name="idProperty">The name of the id property inside each entry.</param>
    /// <param name="stripPrefix">A resource-path prefix to remove (Gemini: <c>models/</c>), or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sorted model ids.</returns>
    /// <exception cref="JsonException">The body is not valid JSON.</exception>
    internal static async ValueTask<IReadOnlyList<string>> ReadModelIdsAsync(
        HttpResponseMessage response,
        string arrayProperty,
        string idProperty,
        string? stripPrefix,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty(arrayProperty, out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var models = new List<string>();

            foreach (var entry in entries.EnumerateArray())
            {
                if (models.Count >= MaxReportedModels)
                {
                    break;
                }

                if (entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty(idProperty, out var id)
                    && id.ValueKind == JsonValueKind.String
                    && id.GetString() is { Length: > 0 } modelId)
                {
                    models.Add(stripPrefix is not null && modelId.StartsWith(stripPrefix, StringComparison.Ordinal)
                        ? modelId[stripPrefix.Length..]
                        : modelId);
                }
            }

            models.Sort(StringComparer.Ordinal);
            return models;
        }
    }
}
