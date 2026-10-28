using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// Says whether tenant resolution is switched on, for the one composition that
/// can switch it off while still binding its own <see cref="ITenantContext"/>.
/// </summary>
/// <remarks>
/// Implemented in <c>Tracon.AspNetCore</c> (<c>UseTenancy</c> registers it) over
/// <c>TraconTenancyOptions.Enabled</c>. The core cannot read that option itself,
/// so <see cref="TenantSeparation"/> asks through this seam.
/// </remarks>
internal interface ITenantResolutionSwitch
{
    /// <summary>Gets whether the request's tenant is read at all.</summary>
    bool Enabled { get; }
}

/// <summary>
/// Answers one question for the production profile checks that only matter when
/// tenants are separated: are they?
/// </summary>
/// <remarks>
/// <para>
/// Written once. Two checks ask (tenant egress, tenant workflow slots) and a second
/// copy would drift the way a hand-copied rule does.
/// </para>
/// <para>
/// The answer is "no" for two compositions only: the built-in
/// <c>SingleTenantContext</c> is bound, or <c>UseTenancy(o => o.Enabled = false)</c>
/// replaced it with a context that never reads the request. <c>UseTenancy</c>
/// binds <c>HttpTenantContext</c> either way, so asking which type is bound would
/// call the second host multi-tenant and stop it for a risk it cannot have.
/// </para>
/// </remarks>
internal static class TenantSeparation
{
    /// <summary>Says whether the composition separates tenants.</summary>
    /// <param name="services">The service provider the profile check received.</param>
    /// <returns>
    /// <see langword="false"/> when every call runs as the default tenant;
    /// <see langword="true"/> otherwise, including for an application's own
    /// <see cref="ITenantContext"/>, which is taken to separate tenants.
    /// </returns>
    public static bool IsSeparated(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var bound = services.GetRequiredService<ITenantContext>().GetType();

        if (bound == TraconExtensionPoints.BuiltInDefaultOf(typeof(ITenantContext)))
        {
            return false;
        }

        // No switch registered: the context is not the HTTP one, so nothing says
        // resolution is off. The tenant check treats it the same way.
        return services.GetService<ITenantResolutionSwitch>()?.Enabled ?? true;
    }
}
