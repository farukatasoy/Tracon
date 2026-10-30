using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The two-node acceptance set on SQL Server.</summary>
/// <remarks>
/// The scenarios are in <see cref="TwoNodeAcceptanceContract"/>; see <see cref="PostgreSqlTwoNodeAcceptanceTests"/> for
/// why the two classes share a collection.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class SqlServerTwoNodeAcceptanceTests(SqlServerTwoNodeServer server)
    : TwoNodeAcceptanceContract, IClassFixture<SqlServerTwoNodeServer>
{
    /// <inheritdoc />
    protected override TwoNodeDatabase NewDatabase() => server.NewDatabase();
}
