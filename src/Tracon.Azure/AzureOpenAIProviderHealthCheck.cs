using Azure.Core;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Checks whether an Azure OpenAI resource is reachable by calling
/// <c>GET {endpoint}/openai/models?api-version=...</c>.
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
/// Authentication happens one of two ways: an <c>api-key</c> header for the API
/// key (on Azure this is not <c>Authorization: Bearer</c>), or a <c>Bearer</c>
/// token obtained through <see cref="TokenCredential"/> for a managed credential.
/// </para>
/// </remarks>
internal sealed class AzureOpenAIProviderHealthCheck : IModelProviderHealthCheck
{
    /// <summary>
    /// The Azure OpenAI data-plane API version the check uses.
    /// </summary>
    /// <remarks>
    /// Kept identical to the version the <c>Azure.AI.OpenAI</c> version we use
    /// produces for a chat request (measured 2026-08-05:
    /// <c>?api-version=2024-10-21</c>). Upgrading it is a deliberate decision.
    /// </remarks>
    internal const string ApiVersion = "2024-10-21";

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

        return _core.CheckAsync(
            BuildModelsEndpoint(_options.Endpoint),
            _options.Timeout,
            AuthorizeAsync,
            ReadModelIdsAsync,
            cancellationToken);
    }

    private async ValueTask AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_credential is not null)
        {
            var scope = string.IsNullOrWhiteSpace(_options.Audience) ? DefaultAudience : _options.Audience;
            var token = await _credential
                .GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)
                .ConfigureAwait(false);

            request.Headers.Add("Authorization", $"Bearer {token.Token}");
        }
        else if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            // Azure OpenAI authenticates a key through the `api-key` header.
            request.Headers.Add("api-key", _options.ApiKey);
        }
    }

    /// <summary>
    /// Combines the resource address with <c>openai/models?api-version=...</c>.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify this combination without a network call.</remarks>
    internal static Uri BuildModelsEndpoint(Uri baseEndpoint)
        => ProviderHealthCheckCore.JoinEndpoint(baseEndpoint, $"openai/models?api-version={ApiVersion}");

    /// <summary>
    /// Reads model ids from a <c>{"data":[{"id":"gpt-..."}]}</c> body.
    /// </summary>
    /// <remarks><c>internal</c>: unit tests verify parsing against a prepared response body.</remarks>
    internal static ValueTask<IReadOnlyList<string>> ReadModelIdsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        => ProviderHealthCheckCore.ReadModelIdsAsync(response, "data", "id", stripPrefix: null, cancellationToken);
}
