using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace Tracon;

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
    /// <summary>
    /// The guard used when a caller supplies none. It rejects every private
    /// network address.
    /// </summary>
    /// <remarks>
    /// Deliberately fail-closed. A construction site that forgets to pass
    /// the configured guard then <em>over</em>-rejects, which an operator
    /// sees immediately, instead of silently connecting unguarded.
    /// </remarks>
    private static readonly EgressSocketGuard StrictGuard = new(static () => EgressAddressPolicy.Deny);

    /// <summary>
    /// The response timeout the MCP transport applies to its own
    /// <see cref="HttpClient"/>. Measured against ModelContextProtocol.Core 2.2.0.
    /// </summary>
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(100);

    /// <summary>Builds a transport whose every connection passes through the egress guard.</summary>
    /// <param name="transportOptions">The transport options.</param>
    /// <param name="guard">The configured guard, or <see langword="null"/> to use the strict one.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <returns>The transport. It owns the <see cref="HttpClient"/> and disposes it.</returns>
    /// <remarks>
    /// <para>
    /// Every MCP connection is built here. Constructing an
    /// <c>HttpClientTransport</c> without this method leaves that path
    /// unguarded, which is why all three call sites go through it.
    /// </para>
    /// <para>
    /// Supplying the client also replaces the one the transport would have
    /// built, so its response timeout has to be restated here.
    /// <see cref="ResponseTimeout"/> keeps the transport's own value; without
    /// it, a server that accepts a connection and never answers would hang the
    /// prompt and resource paths forever — those bound only the connect step.
    /// </para>
    /// </remarks>
    public static HttpClientTransport CreateTransport(
        HttpClientTransportOptions transportOptions,
        EgressSocketGuard? guard,
        ILoggerFactory loggerFactory)
        => new(
            transportOptions,
            (guard ?? StrictGuard).CreateHttpClient(ResponseTimeout),
            loggerFactory,
            ownsHttpClient: true);

    /// <summary>Only remote http/https addresses are accepted; there is no stdio.</summary>
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
    /// derived from the fixed <see cref="TraconMcpOptions.OAuthCallbackBaseUri"/>
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
        TraconMcpOptions mcpOptions,
        string allowedConfigurationPrefix,
        ITokenCache tokenCache,
        ILogger logger)
        => new()
        {
            Name = server.Name,
            Endpoint = server.Endpoint,
            TransportMode = server.Transport == McpTransportMode.Sse
                ? HttpTransportMode.Sse
                : HttpTransportMode.StreamableHttp,
            AdditionalHeaders = BuildHeaders(server, configuration, allowedConfigurationPrefix, logger),
            OAuth = BuildNonInteractiveOAuthOptions(
                server,
                configuration,
                mcpOptions,
                allowedConfigurationPrefix,
                tokenCache,
                logger),
        };

    /// <summary>
    /// When OAuth is enabled but <see cref="TraconMcpOptions.OAuthCallbackBaseUri"/>
    /// is not set, the server must be skipped; this prevents an attempt to
    /// connect with a callback address that is not registered with the provider.
    /// </summary>
    public static bool RequiresUnconfiguredCallback(McpServerDefinition server, TraconMcpOptions mcpOptions)
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
        string allowedConfigurationPrefix,
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

        // 🚨 Checked here as well as where the server is saved. A definition
        // written before the prefix was configured must not silently read an
        // out-of-prefix configuration key.
        ConfigurationKeyGuard.RequirePrefix(
            key,
            allowedConfigurationPrefix,
            "authorizationConfigurationKey");

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
        TraconMcpOptions mcpOptions,
        string allowedConfigurationPrefix,
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
            ClientSecret = ResolveClientSecret(server, configuration, allowedConfigurationPrefix, logger),
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

    /// <summary>Resolves the OAuth client secret from configuration.</summary>
    public static string? ResolveClientSecret(
        McpServerDefinition server,
        IConfiguration configuration,
        string allowedConfigurationPrefix,
        ILogger logger)
    {
        if (server.OAuthClientSecretConfigurationKey is not { Length: > 0 } key)
        {
            return null;
        }

        // 🚨 Same second layer as BuildHeaders.
        ConfigurationKeyGuard.RequirePrefix(
            key,
            allowedConfigurationPrefix,
            "oauthClientSecretConfigurationKey");

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
