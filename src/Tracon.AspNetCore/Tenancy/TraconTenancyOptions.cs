namespace Tracon;

/// <summary>
/// Settings that determine how the tenant is resolved from the request.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Off by default.</strong> A single-tenant setup needs no extra
/// configuration, and every request falls to the
/// <see cref="TraconOptions.DefaultTenantId"/> tenant.
/// </para>
/// <para>
/// <strong>A header can be spoofed.</strong> An HTTP header is not proof of
/// identity; the client can write any value it wants. Therefore:
/// </para>
/// <list type="bullet">
///   <item><description>
///   If <see cref="ClaimType"/> is set and the request passed authentication,
///   <strong>only the claim</strong> is used; the header is ignored.
///   </description></item>
///   <item><description>
///   The header path must be explicitly opened via <see cref="AllowHeaderResolution"/>
///   and used only inside a trusted network (or only during development).
///   </description></item>
///   <item><description>
///   If <see cref="AllowedTenants"/> is non-empty, an unresolved tenant is
///   rejected; a value not in the list does <em>not</em> fall back to the default
///   tenant.
///   </description></item>
/// </list>
/// <para>
/// This type is deliberately not a <c>record</c>; settings classes' generated
/// <c>ToString</c> method could leak values.
/// </para>
/// </remarks>
public sealed class TraconTenancyOptions
{
    /// <summary>
    /// Whether multi-tenancy is enabled. When disabled, every request falls to the
    /// default tenant.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The claim type the tenant is read from. Example: <c>tenant_id</c>.
    /// If set and the request passed authentication, it takes precedence over the header.
    /// </summary>
    public string? ClaimType { get; set; }

    /// <summary>The HTTP header the tenant is read from.</summary>
    public string HeaderName { get; set; } = "X-Tracon-Tenant";

    /// <summary>
    /// Whether the tenant can be resolved from the header. <strong>Off by default</strong>
    /// — a header can be spoofed.
    /// </summary>
    public bool AllowHeaderResolution { get; set; }

    /// <summary>
    /// The accepted tenant ids. If left empty, any value matching the format is accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The format</strong> is <c>^[a-zA-Z0-9_.-]+$</c>, at most 64
    /// characters. A value that does not match is never resolved.
    /// </para>
    /// <para>
    /// <strong>Letter case does not distinguish two tenants.</strong> Entries
    /// and incoming values are both folded with
    /// <see cref="AmbientTenantScope.Normalize"/> before they are compared, so
    /// <c>Acme</c> here admits a request that arrives as <c>acme</c>, and the
    /// reverse. The same fold is what every store persists, so one tenant can
    /// never split into two rows because someone typed a capital letter.
    /// </para>
    /// </remarks>
    public IList<string> AllowedTenants { get; } = [];
}
