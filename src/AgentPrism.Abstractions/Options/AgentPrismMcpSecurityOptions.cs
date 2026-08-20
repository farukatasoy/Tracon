namespace AgentPrism;

/// <summary>The security boundary applied to stored MCP server definitions.</summary>
/// <remarks>
/// <para>
/// Read from the <c>AgentPrism:Mcp</c> configuration section, the same
/// section as the MCP package's own connection settings.
/// </para>
/// <para>
/// This type lives in the abstractions package on purpose: the rule is
/// enforced in two places that do not see each other — the endpoint that
/// saves a server definition (<c>AgentPrism.AspNetCore</c>) and the transport
/// that resolves the key at connection time (<c>AgentPrism.Mcp</c>). One
/// shared type keeps the two from drifting apart.
/// </para>
/// </remarks>
public sealed class AgentPrismMcpSecurityOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "AgentPrism:Mcp";

    /// <summary>
    /// Gets or sets the only prefix under which a configuration key may be
    /// referenced by an MCP server definition. Default is
    /// <c>"AgentPrism:McpSecrets:"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Covers both key-name fields a definition carries:
    /// <c>authorizationConfigurationKey</c> and
    /// <c>oauthClientSecretConfigurationKey</c>.
    /// </para>
    /// <para>
    /// A security boundary, not a convenience default — the same rationale as
    /// <see cref="AgentPrismTenantProviderOptions.AllowedConfigurationPrefix"/>.
    /// A definition never carries a secret value, only the name of the key the
    /// value is read from. Without this restriction that name could
    /// point at any configuration key in the application, and its value would
    /// be sent to the remote MCP server as an <c>Authorization</c> header.
    /// </para>
    /// </remarks>
    public string AllowedConfigurationPrefix { get; set; } = "AgentPrism:McpSecrets:";
}
