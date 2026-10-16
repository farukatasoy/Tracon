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
    /// addresses. Example: <c>https://myapp.example.com/</c>. Must already be
    /// registered with the provider. When <see langword="null"/>, no
    /// connection is made to a server with OAuth enabled, and
    /// <c>/oauth/start</c> answers that OAuth is not configured.
    /// </summary>
    public Uri? OAuthCallbackBaseUri { get; set; }
}
