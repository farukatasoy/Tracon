using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Builds the request headers every connection to an MCP server sends: the
/// stored plain headers plus the credential headers resolved from
/// configuration.
/// </summary>
/// <remarks>
/// <para>
/// The ONE place a server's headers are read. The background connection
/// (<see cref="McpTransportFactory"/>) and the interactive OAuth flow
/// (<see cref="McpOAuthAuthorizationCoordinator"/>) each copied
/// <c>server.Headers</c> on their own; a second copy would miss the resolved
/// credential headers on that path. An architecture test keeps
/// <c>server.Headers</c> to this class and the connection fingerprint.
/// </para>
/// <para>
/// The definition carries no secret, only configuration key NAMES.
/// Every name is checked against the tenant's key space before anything is
/// read; a violation throws, and the connection is logged as
/// unreachable. A log line names headers and keys, never a value.
/// </para>
/// </remarks>
internal static class McpHeaderBuilder
{
    /// <summary>The header OAuth manages while it is on.</summary>
    private const string AuthorizationHeader = "Authorization";

    /// <summary>Builds the headers for one connection.</summary>
    /// <param name="server">The server definition.</param>
    /// <param name="configuration">The configuration the credential values are read from.</param>
    /// <param name="keySpace">The key space every configuration key name must sit in.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>The headers, keyed case-insensitively.</returns>
    /// <exception cref="TraconException">A configuration key name sits outside the server's tenant.</exception>
    public static Dictionary<string, string> Build(
        McpServerDefinition server,
        IConfiguration configuration,
        McpKeySpace keySpace,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(keySpace);
        ArgumentNullException.ThrowIfNull(logger);

        // 🚨 Checked before a single value is read: a record written before
        // the rule existed, or by a third-party store, must not make Tracon
        // read another tenant's key.
        foreach (var (header, keyName) in server.HeaderConfigurationKeys)
        {
            keySpace.Require(keyName, server, $"headerConfigurationKeys[{header}]");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        AddPlainHeaders(headers, server, logger);
        AddLegacyAuthorization(headers, server, configuration, keySpace, logger);

        foreach (var (header, keyName) in server.HeaderConfigurationKeys)
        {
            // A header declared by key wins over a plain one of the same name,
            // even when the key resolves to nothing: the declaration is the
            // operator's intent, and the plain value may be a stale credential.
            headers.Remove(header);

            if (server.OAuthEnabled && string.Equals(header, AuthorizationHeader, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "MCP server '{ServerName}' declares '{Header}' in headerConfigurationKeys while OAuth is on; " +
                    "OAuth manages that header, so it is not sent.",
                    server.Name,
                    header);

                continue;
            }

            var value = configuration[keyName];

            if (string.IsNullOrWhiteSpace(value))
            {
                logger.LogWarning(
                    "Configuration key '{ConfigurationKey}' for header '{Header}' of MCP server '{ServerName}' is empty; " +
                    "the header is not sent.",
                    keyName,
                    header,
                    server.Name);

                continue;
            }

            if (!CanSend(header, value))
            {
                logger.LogWarning(
                    "Header '{Header}' of MCP server '{ServerName}' cannot be sent: the name is not a request header, " +
                    "or the value read from configuration key '{ConfigurationKey}' contains a line break.",
                    header,
                    server.Name,
                    keyName);

                continue;
            }

            headers[header] = value;
        }

        return headers;
    }

    /// <summary>
    /// Reports whether a header would reach the wire. The transport adds each
    /// header with <c>TryAddWithoutValidation</c> and, when that fails, throws
    /// an exception whose message carries the header VALUE — which the
    /// connection path then logs. Filtering here keeps a resolved credential
    /// out of that message.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value.</param>
    /// <returns><see langword="true"/> when the header can be added to a request.</returns>
    internal static bool CanSend(string name, string value)
    {
        if (!CredentialHeaderNames.IsValidName(name) || value.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            return false;
        }

        // A content header name (Content-Type, Content-Length) is refused on
        // a request's own headers; the probe asks the same collection the
        // transport writes to.
        using var probe = new HttpRequestMessage();

        return probe.Headers.TryAddWithoutValidation(name, value);
    }

    private static void AddPlainHeaders(Dictionary<string, string> headers, McpServerDefinition server, ILogger logger)
    {
        foreach (var (header, value) in server.Headers)
        {
            // 🚨 The indexer, not the copy constructor: a stored map is
            // case-sensitive, and 'X-A' next to 'x-a' made the copy throw, so
            // the server never connected. The last spelling wins.
            if (headers.ContainsKey(header))
            {
                logger.LogWarning(
                    "MCP server '{ServerName}' stores header '{Header}' more than once in different case; " +
                    "only the last one is sent.",
                    server.Name,
                    header);
            }

            // A row written before phase 190 may carry a credential in the
            // clear. It is still sent (the connection keeps working), but the
            // operator is told to move it.
            if (CredentialHeaderNames.IsCredential(header))
            {
                logger.LogWarning(
                    "MCP server '{ServerName}' stores the credential header '{Header}' in plain headers, in the " +
                    "clear. Move it to headerConfigurationKeys.",
                    server.Name,
                    header);
            }

            if (!CanSend(header, value))
            {
                logger.LogWarning(
                    "Header '{Header}' of MCP server '{ServerName}' cannot be sent: the name is not a request header, " +
                    "or the value contains a line break.",
                    header,
                    server.Name);

                continue;
            }

            headers[header] = value;
        }
    }

    /// <summary>Resolves the deprecated <c>AuthorizationConfigurationKey</c>.</summary>
    private static void AddLegacyAuthorization(
        Dictionary<string, string> headers,
        McpServerDefinition server,
        IConfiguration configuration,
        McpKeySpace keySpace,
        ILogger logger)
    {
#pragma warning disable CS0618 // The deprecated field is still resolved until 1.0.0 (phase 190).
        var key = server.AuthorizationConfigurationKey;
#pragma warning restore CS0618

        if (key is not { Length: > 0 })
        {
            return;
        }

        if (server.OAuthEnabled)
        {
            // While OAuth is enabled, ClientOAuthOptions manages the
            // Authorization header. A save rejects the combination with 400;
            // only a third-party store can produce it.
            logger.LogWarning(
                "MCP server '{ServerName}' sets authorizationConfigurationKey while OAuth is on; OAuth manages the " +
                "Authorization header, so the key is not read.",
                server.Name);

            return;
        }

        // 🚨 Checked here as well as where the server is saved. A definition
        // written before the rule existed must not silently read a
        // configuration key outside the prefix or outside its own tenant.
        keySpace.Require(key, server, "authorizationConfigurationKey");

        if (configuration[key] is { Length: > 0 } value && CanSend(AuthorizationHeader, value))
        {
            headers[AuthorizationHeader] = value;
        }
        else
        {
            logger.LogWarning(
                "Configuration key '{ConfigurationKey}' for MCP server '{ServerName}' is empty or holds a line break. " +
                "No authentication header will be sent.",
                key,
                server.Name);
        }
    }
}
