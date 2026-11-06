using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The workflow cancellation sweep on PostgreSQL.</summary>
/// <remarks>
/// The scenarios are in <see cref="WorkflowCancellationSweepAcceptanceContract"/>; see
/// <see cref="PostgreSqlDefinitionTransferTests"/> for why the class shares a collection.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class PostgreSqlWorkflowCancellationSweepTests(PostgreSqlTwoNodeServer server)
    : WorkflowCancellationSweepAcceptanceContract, IClassFixture<PostgreSqlTwoNodeServer>
{
    /// <inheritdoc />
    protected override TwoNodeDatabase NewDatabase() => server.NewDatabase();

    /// <inheritdoc />
    protected override string LockStatement => "SELECT pg_advisory_xact_lock(7)";
}
