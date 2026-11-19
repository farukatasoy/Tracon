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
internal sealed class OpenAIProviderHealthCheck : IModelProviderHealthCheck
{
    private static readonly Uri DefaultOpenAiEndpoint = new("https://api.openai.com/v1/");

    private readonly ProviderHealthCheckCore _core;
    private readonly Func<HealthTarget> _target;

    /// <summary>Initializes a check that reads the key and the address from fixed options.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="options">The options the provider was set up with.</param>
    /// <param name="logger">The logger.</param>
    public OpenAIProviderHealthCheck(string providerName, OpenAIProviderOptions options, ILogger? logger = null)
        : this(providerName, () => new HealthTarget(options.ApiKey, options.Endpoint, options.Timeout), logger)
    {
        ArgumentNullException.ThrowIfNull(options);
    }

    /// <summary>Initializes a check that reads the key and the address again every time it runs.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="target">Reads the key, the address and the time limit of one check.</param>
    /// <param name="logger">The logger.</param>
    public OpenAIProviderHealthCheck(string providerName, Func<HealthTarget> target, ILogger? logger = null)
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
        // The same trim and check the client gets: a key the header cannot carry is a credential failure of
        // this check, reported without the key, not an exception that quotes it.
        if (OutboundApiKey.Normalize(OpenAIChatClientFactory.ApiKeyOptionName, key) is { } apiKey)
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
