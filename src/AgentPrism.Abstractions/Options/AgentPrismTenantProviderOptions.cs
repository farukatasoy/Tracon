namespace AgentPrism;

/// <summary>Options that constrain per-tenant model provider bindings (BYOK).</summary>
/// <remarks>
/// <see cref="AllowedConfigurationPrefix"/> is a security boundary, not a
/// convenience default. Without it, a tenant manager could bind
/// <c>ApiKeyConfigurationName</c> to an unrelated key such as
/// <c>ConnectionStrings:Default</c> — unable to read its value, but able to
/// make calls billed to it.
/// </remarks>
public sealed class AgentPrismTenantProviderOptions
{
    /// <summary>
    /// Gets or sets the only prefix under which a configuration key may be
    /// referenced by a tenant provider binding. Default is
    /// <c>"AgentPrism:ProviderKeys:"</c>.
    /// </summary>
    public string AllowedConfigurationPrefix { get; set; } = "AgentPrism:ProviderKeys:";
}
