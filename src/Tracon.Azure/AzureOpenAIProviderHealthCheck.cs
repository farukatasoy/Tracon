using Azure.Core;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Checks whether an Azure OpenAI resource is reachable by calling
/// <c>GET {endpoint}/openai/v1/models</c>.
/// </summary>
/// <remarks>
/// <para>
/// This endpoint returns the models the resource can reach and <strong>incurs
/// no cost</strong> — no model call is made. The shared body (timeout,
/// failure mapping, body parsing) is <c>ProviderHealthCheckCore</c>; this type
/// supplies only the endpoint, the credential and the body shape.
/// </para>
/// <para>
/// <strong>The returned list is a model list, not a deployment list.</strong>
/// The name used in agent definitions is the deployment name, and it does not
/// appear on this endpoint. What the check proves is: the address is correct,
/// the credential is valid, and the resource is up. Whether the deployment name
/// is correct is only found out on the first real call.
/// </para>
/// <para>
/// <see cref="HttpClient"/> is used directly instead of the SDK: this lets the
/// check fully control whether the error detail on the check path carries a
/// secret or an address, and lets the <c>internal static</c> helpers be tested
/// without a network call.
/// </para>
/// <para>
/// Authentication matches the chat call: the API key goes as
/// <c>Authorization: Bearer</c>, and a managed credential obtained through
/// <see cref="TokenCredential"/> goes as a <c>Bearer</c> token. An Entra token is
/// sent only over HTTPS: for a plain <c>http://</c> address the check reports
/// <em>unhealthy</em> without asking the credential for a token.
/// </para>
/// </remarks>
internal sealed class AzureOpenAIProviderHealthCheck : IModelProviderHealthCheck
{
    /// <summary>The Entra token scope on the Azure public cloud.</summary>
    internal const string DefaultAudience = "https://cognitiveservices.azure.com/.default";

    private readonly ProviderHealthCheckCore _core;
    private readonly AzureOpenAIProviderOptions _options;
    private readonly TokenCredential? _credential;

    /// <summary>Builds a new health check.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="options">The provider options.</param>
    /// <param name="logger">Receives a credential failure; the health result carries only its type name.</param>
    /// <remarks>
    /// The credential factory is called <strong>once</strong> here; building a
    /// new credential object on every check would waste the token cache.
    /// </remarks>
    public AzureOpenAIProviderHealthCheck(string providerName, AzureOpenAIProviderOptions options, ILogger? logger = null)
    {
        _core = new ProviderHealthCheckCore(providerName, logger);
        _options = options;
        _credential = options.CredentialFactory?.Invoke();
    }

    /// <inheritdoc />
    public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (_options.Endpoint is null)
        {
            return ValueTask.FromResult(
                _core.Unhealthy(DateTimeOffset.UtcNow, TimeSpan.Zero, "The resource address is not defined."));
        }

        if (_credential is not null && !string.Equals(_options.Endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(_core.Unhealthy(
                DateTimeOffset.UtcNow,
                TimeSpan.Zero,
                "An Entra credential is sent only over HTTPS; the resource address uses another scheme."));
        }

        return _core.CheckAsync(
            BuildModelsEndpoint(_options.Endpoint),
            _options.Timeout,
            AuthorizeAsync,
            ReadModelIdsAsync,
            cancellationToken);
    }

    internal async ValueTask AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_credential is not null)
        {
            var scope = string.IsNullOrWhiteSpace(_options.Audience) ? DefaultAudience : _options.Audience;
            var token = await _credential
                .GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)
                .ConfigureAwait(false);

            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
        }
        else if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            // The same header form the chat call sends for a key: the v1 API takes
            // `Authorization: Bearer <key>`, so one resource or gateway answers
            // both calls alike.
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    /// <summary>
    /// Combines the resource address with <c>openai/v1/models</c>; an address that
    /// already ends in <c>/openai/v1</c> is not extended twice.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify this combination without a network call.</remarks>
    internal static Uri BuildModelsEndpoint(Uri baseEndpoint)
        => ProviderHealthCheckCore.JoinEndpoint(AzureOpenAIChatClientFactory.V1Endpoint(baseEndpoint), "models");

    /// <summary>
    /// Reads model ids from a <c>{"data":[{"id":"gpt-..."}]}</c> body.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify parsing against a prepared response body.</remarks>
    internal static ValueTask<IReadOnlyList<string>> ReadModelIdsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        => ProviderHealthCheckCore.ReadModelIdsAsync(response, "data", "id", stripPrefix: null, cancellationToken);
}
