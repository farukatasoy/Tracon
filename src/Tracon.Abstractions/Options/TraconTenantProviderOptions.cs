namespace Tracon;

/// <summary>Options that constrain per-tenant model provider bindings (BYOK).</summary>
/// <remarks>
/// <see cref="AllowedConfigurationPrefix"/> is a security boundary, not a
/// convenience default. Without it, a tenant manager could bind
/// <c>ApiKeyConfigurationName</c> to an unrelated key such as
/// <c>ConnectionStrings:Default</c> — unable to read its value, but able to
/// make calls billed to it.
/// </remarks>
public sealed class TraconTenantProviderOptions
{
    /// <summary>
    /// Gets or sets the only prefix under which a configuration key may be
    /// referenced by a tenant provider binding. Default is
    /// <c>"Tracon:ProviderKeys:"</c>.
    /// </summary>
    public string AllowedConfigurationPrefix { get; set; } = "Tracon:ProviderKeys:";

    /// <summary>
    /// Gets or sets whether a tenant with no egress policy may call no provider
    /// at all. Default <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Off</strong>, a tenant with no egress policy row is not restricted:
    /// it may call every provider the deployment has. That is the right default
    /// for a single-tenant setup, and it is the risk that
    /// <see cref="TraconProductionRisk.UnrestrictedTenantEgress"/> names for a
    /// deployment that separates tenants.
    /// </para>
    /// <para>
    /// <strong>On</strong>, the call is refused for every provider kind (model,
    /// fallback, image, speech, live voice and embedding) until the tenant has a
    /// policy, and a tenant provider binding cannot be saved without one. The
    /// default tenant is a tenant too: with this on, it needs its own policy row.
    /// The refusal is the same <c>TraconTenantProviderRefusedException</c> a
    /// policy that forbids the provider raises, and its message names this
    /// setting. An empty allow-list is a policy that forbids everything, so it is
    /// not the same as having no policy.
    /// </para>
    /// <para>
    /// Configuration key: <c>Tracon:TenantProviders:RequireEgressPolicy</c>. It is read
    /// when the host starts; changing it afterwards does not apply to the running process.
    /// </para>
    /// </remarks>
    public bool RequireEgressPolicy { get; set; }
}
