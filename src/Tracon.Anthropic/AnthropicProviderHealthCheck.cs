using System.Net.Http.Headers;
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
/// The check sends what the SDK sends (Anthropic 13.8.0; the release before sent the same): <c>Authorization: Bearer</c> and the
/// <c>anthropic-version</c> header, which is <strong>required</strong>; the endpoint returns
/// <c>HTTP 400</c> when it is missing. Measured against the live API on 2026-10-28: the endpoint
/// also accepts <c>x-api-key</c> and the older <c>2023-06-01</c> version with an API key, so the choice
/// is not forced. The check follows the SDK so the two cannot drift apart: a test compares the version
/// with the one a chat request carries.
/// </para>
/// </remarks>
internal sealed class AnthropicProviderHealthCheck : IModelProviderHealthCheck
{
    private static readonly Uri DefaultAnthropicEndpoint = new("https://api.anthropic.com/v1/");

    /// <summary>
    /// The API version header Anthropic requires, the one the SDK sends. It is a dated version id, not
    /// today's date; bumping it is a deliberate decision, and it moves with the SDK.
    /// </summary>
    internal const string AnthropicVersion = "2026-10-21";

    private readonly ProviderHealthCheckCore _core;
    private readonly Func<HealthTarget> _target;

    /// <summary>Initializes a check that reads the key and the address from fixed options.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="options">The options the provider was set up with.</param>
    /// <param name="logger">The logger.</param>
    public AnthropicProviderHealthCheck(string providerName, AnthropicProviderOptions options, ILogger? logger = null)
        : this(providerName, () => new HealthTarget(options.ApiKey, options.Endpoint, options.Timeout), logger)
    {
        ArgumentNullException.ThrowIfNull(options);
    }

    /// <summary>Initializes a check that reads the key and the address again every time it runs.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="target">Reads the key, the address and the time limit of one check.</param>
    /// <param name="logger">The logger.</param>
    public AnthropicProviderHealthCheck(string providerName, Func<HealthTarget> target, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        _core = new ProviderHealthCheckCore(providerName, logger);
        _target = target;
    }

    /// <summary>The key, the address and the time limit one check uses.</summary>
    /// <param name="ApiKey">The key; <see langword="null"/> sends no <c>Authorization</c> header.</param>
    /// <param name="Endpoint">The base address; <see langword="null"/> is the official one.</param>
    /// <param name="Timeout">The time limit; <see langword="null"/> is the default.</param>
    internal readonly record struct HealthTarget(string? ApiKey, Uri? Endpoint, TimeSpan? Timeout);

    /// <inheritdoc />
    public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // One read: the address and the header of a check come from the same key and address, even when the
        // configuration changes while the check runs.
        var target = _target();

        return _core.CheckAsync(
            BuildModelsEndpoint(target.Endpoint),
            target.Timeout,
            (request, token) => Authorize(request, target.ApiKey, token),
            ReadModelIdsAsync,
            cancellationToken);
    }

    private static ValueTask Authorize(HttpRequestMessage request, string? key, CancellationToken cancellationToken)
    {
        request.Headers.Add("anthropic-version", AnthropicVersion);

        // The same trim and check the client gets: a key the header cannot carry is a credential failure of
        // this check, reported without the key, not an exception that quotes it.
        if (OutboundApiKey.Normalize(AnthropicChatClientFactory.ApiKeyOptionName, key) is { } apiKey)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
