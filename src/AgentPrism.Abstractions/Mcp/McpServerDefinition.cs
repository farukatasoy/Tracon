using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// The MCP OAuth authorization flow.
/// </summary>
/// <remarks>
/// <strong>A single value:</strong> <c>ModelContextProtocol.Core</c> 2.2.0 supports only the Authorization Code (+PKCE) flow; <c>ClientOAuthOptions.RedirectUri</c> is a required field, and the library offers no non-interactive client-credentials flow. The value is still kept as an enum — if the SDK adds another flow later (for example, client_credentials), the extension point is ready.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<McpOAuthAuthorizationMode>))]
public enum McpOAuthAuthorizationMode
{
    /// <summary>
    /// The authorization code flow (Authorization Code + PKCE). Started from
    /// the admin UI with <c>/oauth/start</c>, redirected to the provider, and
    /// returns to <c>/oauth/callback</c>.
    /// </summary>
    AuthorizationCode = 0,
}

/// <summary>
/// The way a connection to an MCP server is made.
/// </summary>
/// <remarks>
/// <strong>Stdio is deliberately absent.</strong> The stdio transport starts a process on the server; this means anyone with access to the admin UI runs a program on the server, which fundamentally breaks the code-only tools rule. AgentPrism connects only to <em>remote</em> MCP servers.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<McpTransportMode>))]
public enum McpTransportMode
{
    /// <summary>Streamable HTTP. MCP's current remote transport.</summary>
    StreamableHttp = 0,

    /// <summary>Server-sent events (SSE). For legacy servers.</summary>
    Sse = 1,
}

/// <summary>
/// A registered remote MCP server. Its tools are discovered at connection time and
/// listed alongside the tools registered in code.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Carries no secret.</strong> The <em>value</em> of the authentication header is not stored in this record; only the name of the configuration key the value is read from (<c>AuthorizationConfigurationKey</c>) is stored. The value is resolved at run time through <c>IConfiguration</c>, so it stays in <c>dotnet user-secrets</c> or an environment variable. It never enters a database backup, an audit trail, or a UI response.
/// </para>
/// </remarks>
public sealed record McpServerDefinition
{
    /// <summary>The server identifier. A time-ordered UUID (v7).</summary>
    public required Guid Id { get; init; }

    /// <summary>The tenant the server belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// The server name. Discovered tools are named as <c>{name}.{tool}</c>, so
    /// tools with the same name on two servers never collide.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The description.</summary>
    public string? Description { get; init; }

    /// <summary>The server address. Only <c>http</c> and <c>https</c> are accepted.</summary>
    public required Uri Endpoint { get; init; }

    /// <summary>The transport format.</summary>
    public McpTransportMode Transport { get; init; }

    /// <summary>
    /// The configuration key the <c>Authorization</c> header's value is read
    /// from. Example: <c>AgentPrism:McpSecrets:GithubToken</c>. If left empty, the
    /// header is not sent.
    /// </summary>
    public string? AuthorizationConfigurationKey { get; init; }

    /// <summary>
    /// Extra request headers. <strong>Must not carry a secret</strong> — these
    /// values are stored as-is and shown in the UI.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Whether the server is enabled. Its tools are not discovered while disabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Whether OAuth authentication is on. When on, it cannot be used at the
    /// same time as <c>AuthorizationConfigurationKey</c> — both would
    /// try to manage the <c>Authorization</c> header.
    /// </summary>
    /// <remarks>
    /// <c>[JsonPropertyName]</c> is given DELIBERATELY: System.Text.Json's
    /// camelCase policy lowercases only the FIRST letter, and since "OAuth"
    /// starts with two uppercase letters, the default output would be
    /// <c>oAuthEnabled</c> (not the expected <c>oauthEnabled</c>). The same
    /// constraint applies to all four OAuth fields below.
    /// </remarks>
    [JsonPropertyName("oauthEnabled")]
    public bool OAuthEnabled { get; init; }

    /// <summary>The OAuth client identifier. Not a secret, stored as-is.</summary>
    [JsonPropertyName("oauthClientId")]
    public string? OAuthClientId { get; init; }

    /// <summary>
    /// The configuration key the OAuth client secret's value is read from.
    /// The value is never written to the database, under the same rule
    ///  as <c>AuthorizationConfigurationKey</c>.
    /// </summary>
    [JsonPropertyName("oauthClientSecretConfigurationKey")]
    public string? OAuthClientSecretConfigurationKey { get; init; }

    /// <summary>The space-separated OAuth scope list. Example: <c>"repo read:user"</c>.</summary>
    [JsonPropertyName("oauthScopes")]
    public string? OAuthScopes { get; init; }

    /// <summary>The OAuth authorization flow.</summary>
    [JsonPropertyName("oauthAuthorizationMode")]
    public McpOAuthAuthorizationMode OAuthAuthorizationMode { get; init; } = McpOAuthAuthorizationMode.AuthorizationCode;

    /// <summary>
    /// Whether this server's tools require explicit approval before each call.
    /// <strong>Defaults to <see langword="true"/></strong>: the tool definition
    /// comes from outside and is treated as untrusted.
    /// </summary>
    public bool RequiresApproval { get; init; } = true;

    /// <summary>The creation time (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The last-updated time (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>The store for registered MCP servers.</summary>
public interface IMcpServerStore
{
    /// <summary>Lists a tenant's servers, ordered by name.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The servers.</returns>
    ValueTask<IReadOnlyList<McpServerDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches a server by name.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The server name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The server; <see langword="null"/> if none exists.</returns>
    ValueTask<McpServerDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Adds or updates a server. The key is the <c>(tenant, name)</c> pair.</summary>
    /// <param name="server">The server to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The persisted record.</returns>
    ValueTask<McpServerDefinition> SaveAsync(
        McpServerDefinition server,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a server.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The server name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the record was deleted.</returns>
    ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);
}
