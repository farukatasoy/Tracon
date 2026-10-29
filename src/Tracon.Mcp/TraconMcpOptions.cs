namespace Tracon;

/// <summary>Options for connecting to remote MCP servers.</summary>
public sealed class TraconMcpOptions
{
    /// <summary>The default name of the configuration section.</summary>
    public const string SectionName = "Tracon:Mcp";

    /// <summary>
    /// Whether MCP tool discovery is on. When turned off, no server is
    /// connected to and only the tools registered in code are visible.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The refresh interval of the tool list. A remote server may change its
    /// tool definitions; discovery repeats at this interval.
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a server's last good tool list is kept when a refresh cannot list
    /// its tools. The default is 15 minutes, three refreshes at the default interval.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A network blip on one <c>tools/list</c> call no longer takes a server's
    /// tools away: while the server stays registered with the same definition, its
    /// connection keeps serving the tools it last listed for this long after that
    /// listing, and agents that use them keep running. A call that reaches a server
    /// that is really down fails as a tool error, as it would during the next refresh
    /// interval. Once the time has passed, the next failed refresh drops the tools,
    /// and an agent that names one is refused because the tool is not registered.
    /// </para>
    /// <para>
    /// A server whose definition changes, or that is deleted or disabled, drops its
    /// tools at once; this setting only covers a failed listing of an unchanged
    /// server. <see cref="TimeSpan.Zero"/> drops them at the first failed listing.
    /// Must not be negative; a negative value fails at startup.
    /// </para>
    /// </remarks>
    public TimeSpan StaleToolRetention { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>The upper time bound for connecting to a server and listing its tools.</summary>
    /// <remarks>Must be from one millisecond to about 49.7 days; any other value fails at startup.</remarks>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The maximum number of tools accepted from a single server. Tools
    /// beyond this limit are dropped and a warning is logged.
    /// </summary>
    /// <remarks>
    /// The remote server is treated as untrusted. An unbounded tool list
    /// would produce uncontrolled growth both in the model context and in
    /// the UI.
    /// </remarks>
    public int MaxToolsPerServer { get; set; } = 100;

    /// <summary>
    /// In Mode A (<see cref="AgentDefinition.McpResourceUris"/>), the maximum
    /// number of bytes added to the context from a single resource. Content
    /// beyond this limit is trimmed.
    /// </summary>
    public int MaxResourceBytesPerResource { get; set; } = 64 * 1024;

    /// <summary>
    /// In Mode A, the total byte limit across all resources of an agent
    /// definition. Resources beyond this limit are trimmed; resources beyond
    /// the total limit are skipped entirely.
    /// </summary>
    public int MaxResourceBytesTotal { get; set; } = 256 * 1024;

    /// <summary>
    /// The base URI of the OAuth Mode 1 (authorization code) callback
    /// addresses. Must already be registered with the provider. When
    /// <see langword="null"/>, no connection is made to a server with OAuth enabled, and
    /// <c>/oauth/start</c> answers that OAuth is not configured.
    /// </summary>
    /// <remarks>
    /// The callback address of a server is this address followed by
    /// <c>api/mcp-servers/{name}/oauth/callback</c>. Include the path prefix Tracon is mapped under
    /// and end the address with a slash: <c>https://myapp.example.com/tracon/</c> for the default prefix.
    /// Without the slash the last path segment is replaced, and the provider redirects to an address
    /// that has no <c>/tracon</c>.
    /// </remarks>
    public Uri? OAuthCallbackBaseUri { get; set; }

    /// <summary>
    /// Where the OAuth tokens of the servers that use OAuth are kept. The default is
    /// <see cref="McpOAuthTokenStore.Memory"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With <see cref="McpOAuthTokenStore.Memory"/> a token lives in the memory of one process.
    /// With two instances, the instance that did not start an authorization cannot finish it,
    /// and the instance that did not obtain a token cannot use it.
    /// </para>
    /// <para>
    /// With <see cref="McpOAuthTokenStore.DistributedCache"/> the token is kept in the
    /// application's <c>IDistributedCache</c>, encrypted with <c>IContentProtector</c>; the
    /// encryption key stays in configuration and the token is never written to the Tracon
    /// database. The application does not start when no <c>IDistributedCache</c> is registered or
    /// content protection is off. A cache that lives in one process
    /// (<c>AddDistributedMemoryCache</c>) starts with a warning: it is not shared between instances.
    /// </para>
    /// <para>
    /// Every time the MCP client needs the token it reads the cache; the process keeps no copy,
    /// because a copy could refresh with a refresh token the provider has already replaced.
    /// </para>
    /// </remarks>
    public McpOAuthTokenStore OAuthTokenStore { get; set; }

    /// <summary>
    /// How long a token stays in the distributed cache without being written. The default is 30
    /// days. It applies only to <see cref="McpOAuthTokenStore.DistributedCache"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every write of the token starts the time again. An entry for a server that was deleted, or
    /// that nobody uses, leaves the cache after this time. When the time passes for a server in use,
    /// the server asks for authorization again. The value is an estimate, not a measurement: a
    /// provider does not tell how long its refresh token lives. Must be greater than zero.
    /// </para>
    /// </remarks>
    public TimeSpan OAuthTokenSlidingLifetime { get; set; } = TimeSpan.FromDays(30);
}
