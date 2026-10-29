using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether single-executor election is on, so periodic background work runs on one instance.
/// </summary>
/// <remarks>
/// Election is off by default so a single instance never queries the lease table. With more than one instance
/// every instance runs the periodic work (approval expiry, canary rollback, workflow request expiry, the
/// orphaned-run scan, the model health probe) unless one of them holds the lease.
/// </remarks>
internal sealed class InstanceBackgroundWorkProfileCheck : InstanceProfileCheck
{
    /// <inheritdoc />
    public override TraconProductionRisk Risk => TraconProductionRisk.UnelectedBackgroundWork;

    /// <inheritdoc />
    protected override ProductionProfileResult EvaluateShared(IServiceProvider services, int instances)
    {
        var setting = $"{SingletonExecutionOptions.SectionName}:{nameof(SingletonExecutionOptions.Enabled)}";

        return services.GetRequiredService<IOptions<SingletonExecutionOptions>>().Value.Enabled
            ? ProductionProfileResult.Satisfied(setting)
            : ProductionProfileResult.Permissive(
                setting,
                $"off with {instances} instances declared, so every instance runs the periodic background work at the same time",
                $"set {setting} to true; it needs a SQL provider that every instance shares");
    }
}
