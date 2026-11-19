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
    private readonly Func<AzureOpenAIProviderOptions> _options;
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
        : this(providerName, options, () => options, logger)
    {
    }

    /// <summary>Builds a health check that reads the key and the address again every time it runs.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="options">The options the provider was set up with; the credential factory is taken from them.</param>
    /// <param name="current">Reads the options of one check: the key and the address that are current now.</param>
    /// <param name="logger">Receives a credential failure; the health result carries only its type name.</param>
    public AzureOpenAIProviderHealthCheck(
        string providerName,
        AzureOpenAIProviderOptions options,
        Func<AzureOpenAIProviderOptions> current,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(current);

        _core = new ProviderHealthCheckCore(providerName, logger);
        _options = current;
        _credential = options.CredentialFactory?.Invoke();
    }

    /// <inheritdoc />
    public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // One read: the address and the key of a check come from the same snapshot.
        var options = _options();

        if (options.Endpoint is null)
        {
            return ValueTask.FromResult(
                _core.Unhealthy(DateTimeOffset.UtcNow, TimeSpan.Zero, "The resource address is not defined."));
        }

        if (_credential is not null && !string.Equals(options.Endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(_core.Unhealthy(
                DateTimeOffset.UtcNow,
                TimeSpan.Zero,
                "An Entra credential is sent only over HTTPS; the resource address uses another scheme."));
        }

        return _core.CheckAsync(
            BuildModelsEndpoint(options.Endpoint),
            options.Timeout,
            (request, token) => AuthorizeAsync(request, options, token),
            ReadModelIdsAsync,
            cancellationToken);
    }

    internal ValueTask AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => AuthorizeAsync(request, _options(), cancellationToken);

    private async ValueTask AuthorizeAsync(HttpRequestMessage request, AzureOpenAIProviderOptions options, CancellationToken cancellationToken)
    {
        if (_credential is not null)
        {
            var scope = string.IsNullOrWhiteSpace(options.Audience) ? DefaultAudience : options.Audience;
            var token = await _credential
                .GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)
                .ConfigureAwait(false);

            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
        }
        else if (OutboundApiKey.Normalize(AzureOpenAIChatClientFactory.ApiKeyOptionName, options.ApiKey) is { } apiKey)
        {
            // The same header form the chat call sends for a key: the v1 API takes
            // `Authorization: Bearer <key>`, so one resource or gateway answers
            // both calls alike. The key is trimmed and checked like the client's: a key the header
            // cannot carry is a credential failure of this check, reported without the key.
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
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
