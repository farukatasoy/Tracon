using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>Tenant of the current request.</summary>
public sealed record CurrentTenantResponse
{
    /// <summary>Tenant identifier.</summary>
    public required string TenantId { get; init; }
}

/// <summary>Request to create/update a tenant record.</summary>
public sealed record TenantRequest
{
    /// <summary>Name shown in the UI. If left empty, the key is used.</summary>
    public string? DisplayName { get; init; }
}

/// <summary>
/// Request to create/update an MCP server.
/// </summary>
/// <remarks>
/// <strong>There is no secret field.</strong> A credential header's value is
/// not sent; only the name of the configuration key the value is to be read
/// from is sent (<c>HeaderConfigurationKeys</c>). The value is resolved
/// through <c>IConfiguration</c> at runtime and is never written to the
/// database.
/// </remarks>
public sealed record McpServerRequest
{
    /// <summary>Description.</summary>
    public string? Description { get; init; }

    /// <summary>Server address. Only <c>http</c> and <c>https</c> are accepted.</summary>
    public required string Endpoint { get; init; }

    /// <summary>Transport mode.</summary>
    public McpTransportMode Transport { get; init; }

    /// <summary>
    /// Configuration key the <c>Authorization</c> header's value is read from.
    /// Example: <c>Tracon:McpSecrets:GithubToken</c>. Deprecated: use
    /// <c>headerConfigurationKeys["Authorization"]</c>; the field is removed in
    /// the first stable release. Naming <c>Authorization</c> in both fields is rejected
    /// with <c>400</c>.
    /// </summary>
    [Obsolete(ObsoleteMessages.AuthorizationConfigurationKey, UrlFormat = ObsoleteMessages.UrlFormat)]
    public string? AuthorizationConfigurationKey { get; init; }

    /// <summary>
    /// Extra request headers, sent as given with every request to the server.
    /// The values are stored as plain text, so a header whose name looks like
    /// a credential (<c>Authorization</c>, <c>X-Api-Key</c>, <c>Cookie</c>, any
    /// name ending in <c>-key</c>) is rejected with <c>400</c>: declare it in
    /// <c>HeaderConfigurationKeys</c>. Send every header with its real
    /// value on each save: a response masks the values as <c>***</c>, and a
    /// value of <c>***</c> is rejected with <c>400</c>. Absent or
    /// <see langword="null"/> keeps the stored headers; <c>{}</c> removes them.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Credential request headers, as a map from the header name to the NAME
    /// of the configuration key its value is read from, for example
    /// <c>{"X-Api-Key": "Tracon:McpSecrets:acme:SearchKey"}</c>. Every name must
    /// sit inside the caller's tenant key space. A response returns the map
    /// unmasked: it carries no value. Absent or <see langword="null"/> keeps the
    /// stored map; <c>{}</c> removes it.
    /// </summary>
    public IReadOnlyDictionary<string, string>? HeaderConfigurationKeys { get; init; }

    /// <summary>Whether the server is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Whether this server's tools require approval. Default <see langword="true"/>.</summary>
    public bool RequiresApproval { get; init; } = true;

    /// <summary>
    /// Whether OAuth authentication is enabled. While on,
    /// <c>AuthorizationConfigurationKey</c> must be empty and
    /// <c>HeaderConfigurationKeys</c> may not name <c>Authorization</c>.
    /// </summary>
    /// <remarks>
    /// <c>[JsonPropertyName]</c> is given DELIBERATELY — same rationale as
    /// <see cref="McpServerDefinition.OAuthEnabled"/>: the camelCase policy
    /// produces an unexpected result for a name like "OAuth" that starts with
    /// two capital letters.
    /// </remarks>
    [JsonPropertyName("oauthEnabled")]
    public bool OAuthEnabled { get; init; }

    /// <summary>OAuth client identifier.</summary>
    [JsonPropertyName("oauthClientId")]
    public string? OAuthClientId { get; init; }

    /// <summary>
    /// Configuration key the OAuth client secret's value is read from. The
    /// value itself is not sent; only the key's name is.
    /// </summary>
    [JsonPropertyName("oauthClientSecretConfigurationKey")]
    public string? OAuthClientSecretConfigurationKey { get; init; }

    /// <summary>Space-separated list of OAuth scopes.</summary>
    [JsonPropertyName("oauthScopes")]
    public string? OAuthScopes { get; init; }

    /// <summary>OAuth authorization flow.</summary>
    [JsonPropertyName("oauthAuthorizationMode")]
    public McpOAuthAuthorizationMode OAuthAuthorizationMode { get; init; } = McpOAuthAuthorizationMode.AuthorizationCode;
}

/// <summary>Result of an MCP tool refresh.</summary>
public sealed record McpRefreshResponse
{
    /// <summary>Total number of tools available after the refresh.</summary>
    public required int ToolCount { get; init; }
}

/// <summary>Request to resolve an MCP prompt with arguments.</summary>
public sealed record McpPromptArgumentsRequest
{
    /// <summary>Prompt arguments.</summary>
    public IReadOnlyDictionary<string, string>? Arguments { get; init; }
}

/// <summary>Response for starting OAuth Mode 1.</summary>
public sealed record McpOAuthStartResponse
{
    /// <summary>Authorization address the admin is redirected to.</summary>
    public required string AuthorizationUri { get; init; }

    /// <summary>Single-use state value generated for CSRF protection.</summary>
    public required string State { get; init; }
}

/// <summary>
/// Tool approval decision sent from the UI.
/// </summary>
/// <remarks>
/// The decision is carried in the body of the next run request. Microsoft
/// Agent Framework expects the approval response as a <c>ChatMessage</c>
/// content item; there is no separate "continue" endpoint, because approval
/// is the input to the next turn.
/// </remarks>
public sealed record ToolApprovalDecision
{
    /// <summary>
    /// Identifier of the approved request. This is the
    /// <c>ToolApprovalRequestContent.RequestId</c> value received in the stream.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>Whether the call is approved.</summary>
    public required bool Approved { get; init; }

    /// <summary>Reason for the decision. Passed on to the model.</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Whether the decision should be saved as a permanent rule ("don't ask
    /// again"). Meaningful only while <c>Approved</c> is <see langword="true"/>.
    /// </summary>
    public bool Remember { get; init; }

    /// <summary>
    /// Whether the permanent rule covers only a call with the same arguments.
    /// If <see langword="false"/>, it covers every call to the tool.
    /// </summary>
    public bool RememberArgumentsOnly { get; init; }
}

/// <summary>
/// Request to create a persistent, argument-conditioned approval rule.
/// </summary>
/// <remarks>
/// This is a separate creation path from the "remember this decision" rule
/// <see cref="ToolApprovalResolver"/> writes from the approve/reject flow — that
/// path fingerprints an exact call (<c>ArgumentsHash</c>); this one writes a
/// standing, admin-authored comparison instead. There is no free-text expression
/// field here — the code-only tools rule applies to the HTTP surface too.
/// </remarks>
public sealed record ToolApprovalRuleRequest
{
    /// <summary>The agent the rule holds for. When empty it covers every agent of the tenant.</summary>
    public string? AgentName { get; init; }

    /// <summary>The tool the rule holds for.</summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// All conditions must match for the rule to apply (<c>AND</c>). An empty list
    /// (the default) matches every call of the tool.
    /// </summary>
    public IReadOnlyList<ToolArgumentCondition> ArgumentConditions { get; init; } = [];
}
