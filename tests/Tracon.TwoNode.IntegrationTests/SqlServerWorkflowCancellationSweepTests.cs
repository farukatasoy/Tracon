using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The workflow cancellation sweep on SQL Server.</summary>
/// <remarks>
/// The scenarios are in <see cref="WorkflowCancellationSweepAcceptanceContract"/>; see
/// <see cref="PostgreSqlDefinitionTransferTests"/> for why the class shares a collection.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class SqlServerWorkflowCancellationSweepTests(SqlServerTwoNodeServer server)
    : WorkflowCancellationSweepAcceptanceContract, IClassFixture<SqlServerTwoNodeServer>
{
    /// <inheritdoc />
    protected override TwoNodeDatabase NewDatabase() => server.NewDatabase();

    /// <inheritdoc />
    protected override string LockStatement => "EXEC sp_getapplock @Resource = N'sweep', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0";
}
