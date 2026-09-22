using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

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
/// The shared body (the static <see cref="HttpClient"/>, timeout, failure mapping,
/// body parsing) is <c>ProviderHealthCheckCore</c>; this type supplies only the
/// endpoint and the <c>Bearer</c> header.
/// </para>
/// </remarks>
internal sealed class OpenAIProviderHealthCheck(string providerName, OpenAIProviderOptions options, ILogger? logger = null)
    : IModelProviderHealthCheck
{
    private static readonly Uri DefaultOpenAiEndpoint = new("https://api.openai.com/v1/");

    private readonly ProviderHealthCheckCore _core = new(providerName, logger);

    /// <inheritdoc />
    public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        => _core.CheckAsync(
            BuildModelsEndpoint(options.Endpoint),
            options.Timeout,
            Authorize,
            ReadModelIdsAsync,
            cancellationToken);

    private ValueTask Authorize(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Joins the base address with <c>/models</c>; the official endpoint is used
    /// when no address is given.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify this join without a network call.</remarks>
    internal static Uri BuildModelsEndpoint(Uri? baseEndpoint)
        => ProviderHealthCheckCore.JoinEndpoint(baseEndpoint ?? DefaultOpenAiEndpoint, "models");

    /// <summary>
    /// Reads model ids from a <c>{"data":[{"id":"gpt-..."}]}</c> body.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify the parsing against a canned response body.</remarks>
    internal static ValueTask<IReadOnlyList<string>> ReadModelIdsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        => ProviderHealthCheckCore.ReadModelIdsAsync(response, "data", "id", stripPrefix: null, cancellationToken);
}
