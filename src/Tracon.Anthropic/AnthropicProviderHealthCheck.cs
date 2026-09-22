using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Checks the Anthropic provider's reachability by calling <c>GET {endpoint}/models</c>.
/// </summary>
/// <remarks>
/// <para>
/// This endpoint returns model names and <strong>incurs no cost</strong> — no model
/// call is made. The shared body (timeout, failure mapping, body parsing) is
/// <c>ProviderHealthCheckCore</c>; this type supplies only the endpoint, the
/// auth headers and the body shape.
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
internal sealed class AnthropicProviderHealthCheck(string providerName, AnthropicProviderOptions options, ILogger? logger = null)
    : IModelProviderHealthCheck
{
    private static readonly Uri DefaultAnthropicEndpoint = new("https://api.anthropic.com/v1/");

    /// <summary>
    /// The API version header Anthropic requires. It is a dated version id, not
    /// today's date; bumping it is a deliberate decision.
    /// </summary>
    internal const string AnthropicVersion = "2023-06-01";

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
        request.Headers.Add("anthropic-version", AnthropicVersion);

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.Add("x-api-key", options.ApiKey);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Joins the base address with <c>/models</c>; the official endpoint is used
    /// when no address is given.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify this join without a network call.</remarks>
    internal static Uri BuildModelsEndpoint(Uri? baseEndpoint)
        => ProviderHealthCheckCore.JoinEndpoint(baseEndpoint ?? DefaultAnthropicEndpoint, "models");

    /// <summary>
    /// Reads model ids from a <c>{"data":[{"id":"claude-..."}]}</c> body.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify parsing against a canned response body.</remarks>
    internal static ValueTask<IReadOnlyList<string>> ReadModelIdsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        => ProviderHealthCheckCore.ReadModelIdsAsync(response, "data", "id", stripPrefix: null, cancellationToken);
}
