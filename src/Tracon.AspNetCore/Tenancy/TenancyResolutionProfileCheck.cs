using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Adds what the core's tenant check cannot see: whether tenant resolution from
/// the request is actually switched on.
/// </summary>
/// <remarks>
/// <para>
/// Registered by <c>UseTenancy(...)</c>, which is the only composition in which
/// it has anything to add. A host that never calls <c>UseTenancy</c> keeps the
/// built-in single-tenant context, and the core's own check already reports
/// that correctly.
/// </para>
/// <para>
/// The case this one exists for is narrow and otherwise invisible:
/// <c>UseTenancy(options =&gt; options.Enabled = false)</c> replaces the tenant
/// context, so the core sees a custom binding and would call the decision
/// answered - while every request still falls to the default tenant. Two checks
/// carry the same risk and the strictest answer wins, so this one's permissive
/// verdict stands.
/// </para>
/// </remarks>
internal sealed class TenancyResolutionProfileCheck : IProductionProfileCheck
{
    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.SingleTenant;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Named as the call, not as a configuration key: Tracon binds no
        // configuration section to TraconTenancyOptions, so the only surface
        // that changes this value is the UseTenancy delegate itself. Pointing a
        // reader at an appsettings key here would send them to a setting
        // nothing reads.
        const string Setting = "UseTenancy(options => options.Enabled)";

        return services.GetRequiredService<IOptions<TraconTenancyOptions>>().Value.Enabled
            ? ProductionProfileResult.Satisfied(Setting)
            : ProductionProfileResult.Permissive(
                Setting,
                "false although UseTenancy was called, so the request's tenant is never read and every " +
                "request falls to the default tenant",
                "set options.Enabled to true inside UseTenancy(...) and choose a claim type, or remove " +
                "the UseTenancy call if this deployment is single-tenant on purpose");
    }
}
