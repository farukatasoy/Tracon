namespace Tracon;

/// <summary>Where the OAuth tokens of the MCP servers that use OAuth are kept.</summary>
/// <remarks>
/// Set it with <see cref="TraconMcpOptions.OAuthTokenStore"/>. A token is never written to the
/// Tracon database in either mode.
/// </remarks>
public enum McpOAuthTokenStore
{
    /// <summary>
    /// The token stays in the memory of the process that obtained it, and is lost when the process
    /// stops. Only the instance that started an authorization can finish it. This is the default.
    /// </summary>
    Memory = 0,

    /// <summary>
    /// The token is kept, encrypted, in the application's <c>IDistributedCache</c>. Every instance
    /// that shares that cache can use it, and the provider's redirect can reach any instance.
    /// </summary>
    /// <remarks>
    /// Needs an <c>IDistributedCache</c> that all instances share, and content protection
    /// (<c>AddContentProtection</c>): the application does not start without them.
    /// </remarks>
    DistributedCache = 1,
}
