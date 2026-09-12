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

    /// <summary>The upper time bound for connecting to a server and listing its tools.</summary>
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
    /// <c>/oauth/start</c> returns <see cref="McpOAuthOperationStatus.NotConfigured"/>.
    /// </summary>
    public Uri? OAuthCallbackBaseUri { get; set; }
}
