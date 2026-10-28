using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether a tenant nobody wrote an egress policy for is kept away from
/// every provider.
/// </summary>
/// <remarks>
/// Only asked when tenants are separated (<see cref="TenantSeparation"/>): with one
/// tenant there is no other tenant to protect, and the single tenant's own policy is
/// a choice, not a gap. Answered by the fail-closed switch
/// <see cref="TraconTenantProviderOptions.RequireEgressPolicy"/>; a deployment that
/// would rather keep it off accepts the risk by name.
/// </remarks>
internal sealed class TenantEgressProfileCheck : IProductionProfileCheck
{
    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.UnrestrictedTenantEgress;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        const string Setting = TenantProviderAccess.RequirePolicySetting;

        if (!TenantSeparation.IsSeparated(services))
        {
            return ProductionProfileResult.NotApplicable(
                Setting,
                "tenants are not separated in this composition, so no tenant is missing an egress policy");
        }

        return services.GetRequiredService<IOptions<TraconTenantProviderOptions>>().Value.RequireEgressPolicy
            ? ProductionProfileResult.Satisfied(Setting)
            : ProductionProfileResult.Permissive(
                Setting,
                "off, so a tenant with no egress policy may call every provider the deployment has",
                $"set {Setting} to true and save an egress policy for each tenant, the default tenant " +
                "included. A tenant without a policy then calls no provider");
    }
}
