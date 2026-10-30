using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The two-node acceptance set on PostgreSQL.</summary>
/// <remarks>
/// The scenarios are in <see cref="TwoNodeAcceptanceContract"/>. The PostgreSQL and SQL Server classes share a
/// collection so the two containers never run at once: each test starts two hosts, and four hosts and two database
/// servers on one machine stretch the very clocks the scenarios wait on.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class PostgreSqlTwoNodeAcceptanceTests(PostgreSqlTwoNodeServer server)
    : TwoNodeAcceptanceContract, IClassFixture<PostgreSqlTwoNodeServer>
{
    /// <inheritdoc />
    protected override TwoNodeDatabase NewDatabase() => server.NewDatabase();
}
