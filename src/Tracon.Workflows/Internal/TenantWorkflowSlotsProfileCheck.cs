using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether one tenant can take every workflow slot.
/// </summary>
/// <remarks>
/// <para>
/// Registered by <c>UseWorkflows(...)</c>, the one composition in which the question exists. It
/// asks nothing when tenants are not separated (one tenant owning every slot is the whole
/// deployment) and nothing when execution is switched off
/// (<see cref="TraconWorkflowOptions.Enabled"/>), because no run takes a slot then.
/// </para>
/// <para>
/// The answer is the setting, not the sizing: any value that passes the options validator limits a
/// tenant, and whether it limits it enough is the deployment's call.
/// </para>
/// </remarks>
internal sealed class TenantWorkflowSlotsProfileCheck : IProductionProfileCheck
{
    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.UnlimitedTenantWorkflows;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var setting = $"{TraconWorkflowOptions.SectionName}:{nameof(TraconWorkflowOptions.MaxConcurrentRunsPerTenant)}";
        var options = services.GetRequiredService<IOptions<TraconWorkflowOptions>>().Value;

        if (!options.Enabled)
        {
            return ProductionProfileResult.NotApplicable(
                setting,
                "workflow execution is switched off, so no run takes a slot");
        }

        if (!TenantSeparation.IsSeparated(services))
        {
            return ProductionProfileResult.NotApplicable(
                setting,
                "tenants are not separated in this composition, so no tenant can crowd out another");
        }

        return options.MaxConcurrentRunsPerTenant is not null
            ? ProductionProfileResult.Satisfied(setting)
            : ProductionProfileResult.Permissive(
                setting,
                "not set, so one tenant that starts many workflow runs holds every slot and the other " +
                "tenants wait",
                $"set {setting} to the most runs one tenant may have going at once, from 1 to " +
                $"{TraconWorkflowOptions.SectionName}:{nameof(TraconWorkflowOptions.MaxConcurrentRuns)}");
    }
}
