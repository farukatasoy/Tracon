using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The date inputs of the HTTP surface on SQL Server.</summary>
/// <remarks>
/// The scenarios are in <see cref="DateInputSweepAcceptanceContract"/>; see
/// <see cref="PostgreSqlDefinitionTransferTests"/> for why the class shares a collection.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class SqlServerDateInputSweepTests(SqlServerTwoNodeServer server)
    : DateInputSweepAcceptanceContract, IClassFixture<SqlServerTwoNodeServer>
{
    /// <inheritdoc />
    protected override TwoNodeDatabase NewDatabase() => server.NewDatabase();
}
