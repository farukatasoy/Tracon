using Tracon.Tests.Workflows;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The workflow cancellation sweep on SQL Server.</summary>
/// <remarks>
/// <para>
/// The scenarios are in <see cref="WorkflowCancellationSweepAcceptanceContract"/>; see <see cref="TwoNodeAcceptanceSerial"/>
/// for why the class shares a collection and its server.
/// </para>
/// <para>
/// A plain run sweeps half of the combinations here (<see cref="Combinations"/>): a run on SQL Server costs several times
/// one on PostgreSQL, and PostgreSQL sweeps all of them on every run. CI sweeps all of them on both.
/// </para>
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class SqlServerWorkflowCancellationSweepTests(SqlServerTwoNodeServer server)
    : WorkflowCancellationSweepAcceptanceContract
{
    /// <summary>
    /// Gets the combinations the theory runs on SQL Server: half of them, or all of them when
    /// <see cref="WorkflowCancellationSweepAcceptanceContract.FullSweepVariable"/> is <c>1</c>.
    /// </summary>
    /// <returns>The combinations.</returns>
    public static new TheoryData<SweepGraph, SweepStart, bool> Combinations()
        => FullSweep ? WorkflowCancellationSweepAcceptanceContract.Combinations() : HalfOfCombinations();

    /// <inheritdoc />
    protected override TwoNodeDatabase NewDatabase() => server.NewDatabase();

    /// <inheritdoc />
    protected override string LockStatement => "EXEC sp_getapplock @Resource = N'sweep', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0";
}
