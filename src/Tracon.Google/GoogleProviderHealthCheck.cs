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
internal sealed class GoogleProviderHealthCheck(string providerName, GoogleProviderOptions options, ILogger? logger = null)
    : IModelProviderHealthCheck
{
    private static readonly Uri DefaultGoogleEndpoint = new("https://generativelanguage.googleapis.com/");
    private const string ModelResourcePrefix = "models/";

    /// <summary>API version used when no address is given.</summary>
    internal const string DefaultApiVersion = "v1beta";

    private readonly ProviderHealthCheckCore _core = new(providerName, logger);

    /// <inheritdoc />
    public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        => _core.CheckAsync(
            BuildModelsEndpoint(options.Endpoint, options.ApiVersion),
            options.Timeout,
            Authorize,
            ReadModelIdsAsync,
            cancellationToken);

    private ValueTask Authorize(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.Add("x-goog-api-key", options.ApiKey);
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
