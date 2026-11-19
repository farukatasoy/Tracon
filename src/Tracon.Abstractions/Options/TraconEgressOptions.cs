namespace Tracon;

/// <summary>Shared rules for the target address of outbound network requests.</summary>
/// <remarks>
/// <para>
/// Read from the <c>Tracon:Egress</c> configuration section. Tracon
/// sends outbound requests to addresses that a user or a remote service supplied —
/// webhook delivery, MCP server connections, the address a tenant's provider binding
/// overrides, the URL of a generated image and the OpenAI live voice endpoint — and this
/// option governs all of them, so an operator reasons about one setting instead of
/// several.
/// </para>
/// <para>
/// The endpoint that a <c>Use...()</c> call receives (<c>UseOpenAI</c>,
/// <c>UseOpenAICompatible</c>, <c>UseAnthropic</c>, <c>UseGoogle</c>,
/// <c>UseAzureOpenAI</c>) is not covered. It is the operator's own decision and is
/// written in code or configuration, and guarding it would break sovereign cloud,
/// internal proxy and local model setups that are private on purpose.
/// </para>
/// <para>
/// <see cref="AllowPrivateNetworkTargets"/> is a security boundary, not a
/// convenience default — the same rationale as
/// <see cref="TraconTenantProviderOptions.AllowedConfigurationPrefix"/>.
/// Without it, an administrator could point an MCP server or a tenant's
/// provider endpoint at the cloud metadata address
/// (<c>169.254.169.254</c>), which often hands out unauthenticated temporary
/// credentials.
/// </para>
/// </remarks>
public sealed class TraconEgressOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "Tracon:Egress";

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
