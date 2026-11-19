using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Checks the reachability of the Gemini provider by calling
/// <c>GET {endpoint}/{apiVersion}/models</c>.
/// </summary>
/// <remarks>
/// <para>
/// This endpoint returns model names and <strong>incurs no cost</strong> — no
/// model call is made. The shared body (timeout, failure mapping, body parsing)
/// is <c>ProviderHealthCheckCore</c>; this type supplies only the endpoint, the
/// auth header and the body shape.
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
internal sealed class GoogleProviderHealthCheck : IModelProviderHealthCheck
{
    private static readonly Uri DefaultGoogleEndpoint = new("https://generativelanguage.googleapis.com/");
    private const string ModelResourcePrefix = "models/";

    /// <summary>API version used when no address is given.</summary>
    internal const string DefaultApiVersion = "v1beta";

    private readonly ProviderHealthCheckCore _core;
    private readonly Func<HealthTarget> _target;

    /// <summary>Initializes a check that reads the key and the address from fixed options.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="options">The options the provider was set up with.</param>
    /// <param name="logger">The logger.</param>
    public GoogleProviderHealthCheck(string providerName, GoogleProviderOptions options, ILogger? logger = null)
        : this(providerName, () => new HealthTarget(options.ApiKey, options.Endpoint, options.ApiVersion, options.Timeout), logger)
    {
        ArgumentNullException.ThrowIfNull(options);
    }

    /// <summary>Initializes a check that reads the key and the address again every time it runs.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="target">Reads the key, the address, the API version and the time limit of one check.</param>
    /// <param name="logger">The logger.</param>
    public GoogleProviderHealthCheck(string providerName, Func<HealthTarget> target, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        _core = new ProviderHealthCheckCore(providerName, logger);
        _target = target;
    }

    /// <summary>The key, the address, the API version and the time limit one check uses.</summary>
    /// <param name="ApiKey">The key; <see langword="null"/> sends no <c>x-goog-api-key</c> header.</param>
    /// <param name="Endpoint">The base address; <see langword="null"/> is Google's own.</param>
    /// <param name="ApiVersion">The API version; <see langword="null"/> is the default one.</param>
    /// <param name="Timeout">The time limit; <see langword="null"/> is the default.</param>
    internal readonly record struct HealthTarget(string? ApiKey, Uri? Endpoint, string? ApiVersion, TimeSpan? Timeout);

    /// <inheritdoc />
    public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // One read: the address and the header of a check come from the same key and address, even when the
        // configuration changes while the check runs.
        var target = _target();

        return _core.CheckAsync(
            BuildModelsEndpoint(target.Endpoint, target.ApiVersion),
            target.Timeout,
            (request, token) => Authorize(request, target.ApiKey, token),
            ReadModelIdsAsync,
            cancellationToken);
    }

    private static ValueTask Authorize(HttpRequestMessage request, string? key, CancellationToken cancellationToken)
    {
        // The same trim and check the client gets: a key the header cannot carry is a credential failure of
        // this check, reported without the key, not an exception that quotes it.
        if (OutboundApiKey.Normalize(GoogleChatClientFactory.ApiKeyOptionName, key) is { } apiKey)
        {
            request.Headers.Add("x-goog-api-key", apiKey);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Joins the base address, API version, and the <c>models</c> segment.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify this join without making a network call.</remarks>
    internal static Uri BuildModelsEndpoint(Uri? baseEndpoint, string? apiVersion)
    {
        var version = string.IsNullOrWhiteSpace(apiVersion) ? DefaultApiVersion : apiVersion.Trim('/');

        return ProviderHealthCheckCore.JoinEndpoint(baseEndpoint ?? DefaultGoogleEndpoint, $"{version}/models");
    }

    /// <summary>
    /// Reads model names from a <c>{"models":[{"name":"models/gemini-..."}]}</c> body
    /// and strips the <c>models/</c> prefix.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify parsing against a fixed response body.</remarks>
    internal static ValueTask<IReadOnlyList<string>> ReadModelIdsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        => ProviderHealthCheckCore.ReadModelIdsAsync(response, "models", "name", ModelResourcePrefix, cancellationToken);
}
