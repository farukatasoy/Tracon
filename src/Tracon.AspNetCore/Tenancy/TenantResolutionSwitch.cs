using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Tells the core's profile checks whether <c>UseTenancy</c> left tenant resolution on.
/// </summary>
/// <remarks>
/// <c>UseTenancy(o =&gt; o.Enabled = false)</c> still binds <see cref="HttpTenantContext"/>, so the
/// bound type cannot say whether tenants are separated; this value can. Registered by
/// <c>UseTenancy</c> and read through the core's <c>TenantSeparation</c>.
/// </remarks>
/// <param name="options">The tenant resolution settings.</param>
internal sealed class TenantResolutionSwitch(IOptions<TraconTenancyOptions> options) : ITenantResolutionSwitch
{
    /// <inheritdoc />
    public bool Enabled => options.Value.Enabled;
}
