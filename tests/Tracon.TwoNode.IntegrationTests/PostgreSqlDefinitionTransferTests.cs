using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The definition transfer on PostgreSQL.</summary>
/// <remarks>
/// The scenarios are in <see cref="DefinitionTransferAcceptanceContract"/>. The class joins the collection of the
/// two-node set so the PostgreSQL and SQL Server containers never run at once.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class PostgreSqlDefinitionTransferTests(PostgreSqlTwoNodeServer server)
    : DefinitionTransferAcceptanceContract, IClassFixture<PostgreSqlTwoNodeServer>
{
    /// <inheritdoc />
    protected override TwoNodeDatabase NewDatabase() => server.NewDatabase();
}
