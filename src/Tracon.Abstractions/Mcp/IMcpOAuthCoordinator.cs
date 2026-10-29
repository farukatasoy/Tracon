namespace Tracon;

/// <summary>The result status of an OAuth authorization request.</summary>
internal enum McpOAuthOperationStatus
{
    /// <summary>The request succeeded.</summary>
    Ok = 0,

    /// <summary>No server is registered with this name.</summary>
    ServerNotFound = 1,

    /// <summary>
    /// The server is not set up for OAuth, the flow is not
    /// <see cref="McpOAuthAuthorizationMode.AuthorizationCode"/>, or
    /// <c>Tracon:Mcp:OAuthCallbackBaseUri</c> is not configured.
    /// </summary>
    NotConfigured = 2,

    /// <summary>Could not connect to the server to get the authorization address (including timeout).</summary>
    ConnectionFailed = 3,

    /// <summary>
    /// The <c>state</c> is unknown or has expired (CSRF protection), or no instance announced a flow under it.
    /// </summary>
    InvalidState = 4,

    /// <summary>The provider rejected the code exchange, or the user cancelled authorization.</summary>
    AuthorizationFailed = 5,
}

/// <summary>The result of an OAuth start request.</summary>
internal sealed record McpOAuthStartResult
{
    /// <summary>The result status.</summary>
    public required McpOAuthOperationStatus Status { get; init; }

    /// <summary>
    /// The authorization address the administrator is redirected to. Populated only for
    /// <see cref="McpOAuthOperationStatus.Ok"/>.
    /// </summary>
    public Uri? AuthorizationUri { get; init; }

    /// <summary>The single-use state value generated for CSRF protection.</summary>
    public string? State { get; init; }
}

/// <summary>The result of the OAuth callback.</summary>
internal sealed record McpOAuthCompleteResult
{
    /// <summary>The result status.</summary>
    public required McpOAuthOperationStatus Status { get; init; }

    /// <summary>
    /// The name of the authorized server. <see langword="null"/> for <see
    /// cref="McpOAuthOperationStatus.InvalidState"/>.
    /// </summary>
    public string? ServerName { get; init; }

    /// <summary>The failure reason (safe to show to the user, carries no secret).</summary>
    public string? Error { get; init; }
}

/// <summary>
/// The coordinator that manages the OAuth Mode 1 (authorization code) flow for MCP servers.
/// </summary>
/// <remarks>
/// <para>
/// The abstraction lives in <c>Tracon.Abstractions</c> because
/// <c>Tracon.AspNetCore</c> (the endpoints) does not depend on the
/// <c>Tracon.Mcp</c> package — the same rationale as <see cref="IMcpToolRefresher"/>.
/// </para>
/// <para>
/// <strong>Tokens are never written to the database.</strong> At the end of a
/// successful flow, the access and refresh tokens are kept where
/// <c>Tracon:Mcp:OAuthTokenStore</c> says. With <c>Memory</c> (the default) they stay in the
/// memory of the process that ran the flow, bounded by the process lifetime. With
/// <c>DistributedCache</c> they are kept, encrypted with <see cref="IContentProtector"/>,
/// in the application's <c>IDistributedCache</c>, and every instance that shares that cache uses them.
/// </para>
/// <para>
/// <strong>Two instances.</strong> The pending flow always lives in the process that ran
/// <see cref="StartAsync"/>. With <c>Memory</c>, only that instance completes it; a callback that
/// reaches another instance answers <see cref="McpOAuthOperationStatus.InvalidState"/>, with an
/// <see cref="McpOAuthCompleteResult.Error"/> that names the setting. With <c>DistributedCache</c>,
/// the instance that receives the callback leaves the code in the cache, the instance that ran
/// <see cref="StartAsync"/> takes it and exchanges it, and the instance that received the callback returns
/// the result.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>. This interface is plumbing between Tracon's own packages;
/// a consumer does not implement it. A singleton is required, not
/// incidental: the pending-flow state (matched by
/// <c>state</c> between <see cref="StartAsync"/> and <see cref="CompleteAsync"/>)
/// must survive across the two separate HTTP requests of one OAuth round trip,
/// which a scoped registration could not guarantee. An implementation must be
/// safe under concurrent flows from unrelated tenants.
/// </para>
/// </remarks>
internal interface IMcpOAuthCoordinator
{
    /// <summary>
    /// Starts the authorization flow for a server and returns the provider's
    /// authorization address.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="serverName">The server name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask<McpOAuthStartResult> StartAsync(
        string tenantId,
        string serverName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles the provider's callback request: forwards the code to the
    /// pending flow matched by <c>state</c> and waits for the token exchange result.
    /// </summary>
    /// <param name="state">The value generated by <see cref="McpOAuthStartResult.State"/>.</param>
    /// <param name="code">The authorization code returned by the provider.</param>
    /// <param name="iss">The RFC 9207 <c>iss</c> parameter (if present).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask<McpOAuthCompleteResult> CompleteAsync(
        string state,
        string? code,
        string? iss,
        CancellationToken cancellationToken = default);
}
