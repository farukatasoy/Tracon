namespace AgentPrism;

/// <summary>Options that constrain inbound triggers (phase 66).</summary>
/// <remarks>
/// 🚨 <see cref="AllowedConfigurationPrefix"/> is a security boundary, not a
/// convenience default — the same rationale as
/// <see cref="AgentPrismTenantProviderOptions.AllowedConfigurationPrefix"/>
/// (section 65.2). Without it, a trigger definition could reference an
/// unrelated configuration key as its "signing secret".
/// </remarks>
public sealed class AgentPrismInboundTriggerOptions
{
    /// <summary>Gets or sets how far the request's <c>X-AgentPrism-Timestamp</c> may drift. Default five minutes.</summary>
    public TimeSpan TimestampTolerance { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets the maximum accepted body size, in bytes. Default 256 KB.</summary>
    public int MaxBodyBytes { get; set; } = 256 * 1024;

    /// <summary>Gets or sets the maximum accepted requests per trigger per minute. Default 60.</summary>
    public int MaxRequestsPerMinute { get; set; } = 60;

    /// <summary>
    /// Gets or sets the only prefix under which a configuration key may be
    /// referenced as a trigger's signing secret. Default is
    /// <c>"AgentPrism:TriggerSecrets:"</c>.
    /// </summary>
    public string AllowedConfigurationPrefix { get; set; } = "AgentPrism:TriggerSecrets:";
}
