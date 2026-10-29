using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// Answers whether the store is one that every declared instance reaches.
/// </summary>
/// <remarks>
/// The in-memory stores and a SQLite file serve one process. Two instances on them do not fail at start:
/// each runs against its own state, and the first sign is a run that one instance cannot find.
/// </remarks>
internal sealed class InstanceStateProfileCheck : InstanceProfileCheck
{
    /// <inheritdoc />
    public override TraconProductionRisk Risk => TraconProductionRisk.InstanceLocalState;

    /// <inheritdoc />
    protected override ProductionProfileResult EvaluateShared(IServiceProvider services, int instances)
    {
        var sharing = services.GetRequiredService<StateSharing>();
        const string Setting = "the storage provider (UsePostgreSql or UseSqlServer)";

        return sharing.AcrossProcesses
            ? ProductionProfileResult.Satisfied(Setting)
            : ProductionProfileResult.Permissive(
                Setting,
                $"{instances} instances are declared, and the state lives {sharing.Description}, so each instance " +
                "sees its own runs, sessions and job queue",
                "register UsePostgreSql or UseSqlServer, with one database that every instance reaches. " +
                "SQLite and the in-memory stores serve one process");
    }
}
