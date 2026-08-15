using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace AgentPrism;

/// <summary>
/// Builds transport options from a <see cref="McpServerDefinition"/>.
/// </summary>
/// <remarks>
/// <see cref="McpConnection"/> (the long-lived, cached connection) and the
/// short-lived prompt/resource clients share the same setup logic; this
/// class collects it in one place to avoid duplication.
/// </remarks>
internal static class McpTransportFactory
{
    /// <summary>Only remote http/https addresses are accepted; there is no stdio (K-058).</summary>
    [SuppressMessage(
        "Design",
        "MA0089:Optimize string method usage",
        Justification = "The scheme comparison must be case-insensitive.")]
    public static bool IsRemoteHttp(Uri endpoint)
        => endpoint.IsAbsoluteUri
            && (string.Equals(endpoint.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                || string.Equals(endpoint.Scheme, "http", StringComparison.OrdinalIgnoreCase));

    /// <summary>Builds a server's OAuth callback address.</summary>
    /// <remarks>
    /// Must already be registered with the provider; this is why it is
    /// derived from the fixed <see cref="AgentPrismMcpOptions.OAuthCallbackBaseUri"/>
    /// setting, not from the request.
    /// </remarks>
    public static Uri BuildCallbackUri(Uri baseUri, string serverName)
        => new(baseUri, $"api/mcp-servers/{Uri.EscapeDataString(serverName)}/oauth/callback");

    /// <summary>
    /// Builds transport options from a server definition for the (non-interactive)
    /// connection reused in the background.
    /// </summary>
    /// <remarks>
    /// While OAuth is enabled, <see cref="ClientOAuthOptions.AuthorizationCallbackHandler"/>
    /// deliberately <strong>fails immediately</strong>: this is the background
    /// refresh loop, and there is no administrator present to complete an
    /// interactive authorization. Valid tokens are reused through
    /// <paramref name="tokenCache"/>; otherwise the connection is logged as
    /// "unreachable" and that server's tools drop out of the list —
    /// re-authorization is expected via <c>/oauth/start</c>.
    /// </remarks>
    public static HttpClientTransportOptions BuildTransportOptions(
        McpServerDefinition server,
        IConfiguration configuration,
        AgentPrismMcpOptions mcpOptions,
        ITokenCache tokenCache,
        ILogger logger)
        => new()
        {
            Name = server.Name,
            Endpoint = server.Endpoint,
            TransportMode = server.Transport == McpTransportMode.Sse
                ? HttpTransportMode.Sse
                : HttpTransportMode.StreamableHttp,
            AdditionalHeaders = BuildHeaders(server, configuration, logger),
            OAuth = BuildNonInteractiveOAuthOptions(server, configuration, mcpOptions, tokenCache, logger),
        };

    /// <summary>
    /// When OAuth is enabled but <see cref="AgentPrismMcpOptions.OAuthCallbackBaseUri"/>
    /// is not set, the server must be skipped; this prevents an attempt to
    /// connect with a callback address that is not registered with the provider.
    /// </summary>
    public static bool RequiresUnconfiguredCallback(McpServerDefinition server, AgentPrismMcpOptions mcpOptions)
        => server.OAuthEnabled && mcpOptions.OAuthCallbackBaseUri is null;

    /// <summary>
    /// Resolves the authentication header from configuration and merges it
    /// with additional headers.
    /// </summary>
    /// <remarks>
    /// The server definition <strong>carries no secret</strong>; it only
    /// carries the name of the configuration key the value is read from. The
    /// value is resolved here, at run time, and stays in <c>dotnet
    /// user-secrets</c> or an environment variable.
    /// </remarks>
    private static Dictionary<string, string> BuildHeaders(
        McpServerDefinition server,
        IConfiguration configuration,
        ILogger logger)
    {
        var headers = new Dictionary<string, string>(server.Headers, StringComparer.OrdinalIgnoreCase);

        if (server.OAuthEnabled)
        {
            // While OAuth is enabled, ClientOAuthOptions manages the
            // Authorization header; if both tried to write the same header,
            // which one wins would be an internal detail of the server SDK.
            // GovernanceEndpoints.Validate already rejects this combination
            // with 400; this is a last line of defense.
            return headers;
        }

        if (server.AuthorizationConfigurationKey is not { Length: > 0 } key)
        {
            return headers;
        }

        if (configuration[key] is { Length: > 0 } value)
        {
            headers["Authorization"] = value;
        }
        else
        {
            logger.LogWarning(
                "Configuration key '{ConfigurationKey}' for MCP server '{ServerName}' is empty. " +
                "No authentication header will be sent.",
                key,
                server.Name);
        }

        return headers;
    }

    private static ClientOAuthOptions? BuildNonInteractiveOAuthOptions(
        McpServerDefinition server,
        IConfiguration configuration,
        AgentPrismMcpOptions mcpOptions,
        ITokenCache tokenCache,
        ILogger logger)
    {
        if (!server.OAuthEnabled || mcpOptions.OAuthCallbackBaseUri is not { } baseUri)
        {
            return null;
        }

        return new ClientOAuthOptions
        {
            ClientId = server.OAuthClientId,
            ClientSecret = ResolveClientSecret(server, configuration, logger),
            Scopes = ParseScopes(server.OAuthScopes),
            RedirectUri = BuildCallbackUri(baseUri, server.Name),
            TokenCache = tokenCache,
            // Non-interactive path: if there is no valid token, this fails
            // immediately, falls into McpConnection.ConnectAsync's general
            // "could not connect to server" catch, and that server's tools
            // are not listed in this refresh. Real authorization only happens
            // through McpOAuthAuthorizationCoordinator (the admin UI,
            // /oauth/start).
            AuthorizationCallbackHandler = (_, _) => Task.FromException<AuthorizationResult?>(
                new InvalidOperationException(
                    $"MCP server '{server.Name}' requires OAuth authorization. " +
                    "Start the /oauth/start flow from the admin UI with 'Authorize'.")),
        };
    }

    /// <summary>Resolves the OAuth client secret from configuration (K-059).</summary>
    public static string? ResolveClientSecret(McpServerDefinition server, IConfiguration configuration, ILogger logger)
    {
        if (server.OAuthClientSecretConfigurationKey is not { Length: > 0 } key)
        {
            return null;
        }

        if (configuration[key] is { Length: > 0 } value)
        {
            return value;
        }

        logger.LogWarning(
            "OAuth configuration key '{ConfigurationKey}' for MCP server '{ServerName}' is empty.",
            key,
            server.Name);

        return null;
    }

    /// <summary>Converts a space-separated scope string into a list.</summary>
    public static IEnumerable<string>? ParseScopes(string? scopes)
        => string.IsNullOrWhiteSpace(scopes)
            ? null
            : scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
