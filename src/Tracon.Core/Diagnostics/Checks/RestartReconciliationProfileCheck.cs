using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether a run that a restart cuts off is closed by someone.
/// </summary>
/// <remarks>
/// <para>
/// A deploy stops the host. A run in flight gets the cancellation; a model that honours it ends the run as canceled,
/// and one that does not leaves the process to end first (the host waits its shutdown timeout, then exits), so the row
/// stays <c>Running</c>. A crash and a kill do the same. With the state in a database or a SQLite file the row is
/// still there when the next process starts, and only run reconciliation closes it. With it off (the default) the row
/// stays for ever: it is counted as running, and it dilutes the error rate.
/// </para>
/// <para>
/// Asked of a host whose state outlives the process and whose reconciliation is off, and not of a host that declares
/// more than one instance: <see cref="TraconProductionRisk.UnreconciledRuns"/> asks the same setting there and says the
/// same of a run whose instance died, so a host would be asked twice, and one that already answered or accepted that
/// decision would be stopped again. The in-memory stores lose their rows with the process, so no row is left to close.
/// The default is not changed: reconciliation writes a heartbeat per running run, which a development host does not want.
/// </para>
/// </remarks>
internal sealed class RestartReconciliationProfileCheck : IProductionProfileCheck
{
    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.OrphanedRunsAfterRestart;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var setting = $"{RunReconciliationOptions.SectionName}:{nameof(RunReconciliationOptions.Enabled)}";

        if (services.GetRequiredService<IOptions<TraconOptions>>().Value.Deployment.Instances > 1)
        {
            return ProductionProfileResult.NotApplicable(
                InstanceProfileCheck.DeclarationSetting,
                $"more than one instance is declared, and {nameof(TraconProductionRisk.UnreconciledRuns)} asks {setting} for that case");
        }

        var sharing = services.GetRequiredService<StateSharing>();

        if (!sharing.OutlivesProcess)
        {
            return ProductionProfileResult.NotApplicable(
                "the storage provider (UsePostgreSql, UseSqlServer or UseSqlite)",
                $"the state lives {sharing.Description}, so a restart takes every row with it and none is left Running");
        }

        return services.GetRequiredService<IOptions<RunReconciliationOptions>>().Value.Enabled
            ? ProductionProfileResult.Satisfied(setting)
            : ProductionProfileResult.Permissive(
                setting,
                $"off with the state {sharing.Description}, so a run that a restart or a crash cuts off keeps the status " +
                "Running for ever, counted as running and diluting the error rate",
                $"set {setting} to true, or accept the risk by name when a run left Running after a restart is closed by hand");
    }
}
