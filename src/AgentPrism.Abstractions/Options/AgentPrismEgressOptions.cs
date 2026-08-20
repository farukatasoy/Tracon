namespace AgentPrism;

/// <summary>Shared rules for the target address of outbound network requests.</summary>
/// <remarks>
/// <para>
/// Read from the <c>AgentPrism:Egress</c> configuration section. AgentPrism
/// sends outbound requests from three surfaces — webhook delivery, MCP server
/// connections and model provider calls — and this option governs all three,
/// so an operator reasons about one setting instead of three.
/// </para>
/// <para>
/// <see cref="AllowPrivateNetworkTargets"/> is a security boundary, not a
/// convenience default — the same rationale as
/// <see cref="AgentPrismTenantProviderOptions.AllowedConfigurationPrefix"/>.
/// Without it, an administrator could point an MCP server or a tenant's
/// provider endpoint at the cloud metadata address
/// (<c>169.254.169.254</c>), which often hands out unauthenticated temporary
/// credentials.
/// </para>
/// </remarks>
public sealed class AgentPrismEgressOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "AgentPrism:Egress";

    /// <summary>
    /// Gets or sets whether outbound requests to private network addresses
    /// are allowed. <strong>Disabled by default.</strong>
    /// </summary>
    /// <remarks>
    /// Enabling this opens an SSRF surface: the server becomes able to reach
    /// any service on the internal network, including the cloud metadata
    /// endpoint. Enable it only deliberately, on a closed network — for
    /// example when the MCP servers really do run inside the private network.
    /// </remarks>
    public bool AllowPrivateNetworkTargets { get; set; }
}
