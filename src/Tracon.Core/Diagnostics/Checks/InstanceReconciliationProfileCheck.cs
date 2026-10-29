using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether run reconciliation is on, so a cancel and a dead instance reach every instance.
/// </summary>
/// <remarks>
/// The heartbeat is also how a run learns of a cancel request that landed on another instance. With
/// reconciliation off, <c>POST /api/runs/{runId}/cancel</c> answers <c>409</c> for a run that another instance
/// executes, and a run whose instance died stays <c>Running</c>.
/// </remarks>
internal sealed class InstanceReconciliationProfileCheck : InstanceProfileCheck
{
    /// <inheritdoc />
    public override TraconProductionRisk Risk => TraconProductionRisk.UnreconciledRuns;

    /// <inheritdoc />
    protected override ProductionProfileResult EvaluateShared(IServiceProvider services, int instances)
    {
        var setting = $"{RunReconciliationOptions.SectionName}:{nameof(RunReconciliationOptions.Enabled)}";

        return services.GetRequiredService<IOptions<RunReconciliationOptions>>().Value.Enabled
            ? ProductionProfileResult.Satisfied(setting)
            : ProductionProfileResult.Permissive(
                setting,
                $"off with {instances} instances declared, so a cancel that reaches the other instance answers 409 " +
                "and a run whose instance died stays Running",
                $"set {setting} to true; it needs a SQL provider that every instance shares");
    }
}
