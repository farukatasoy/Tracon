using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The secret canary on PostgreSQL.</summary>
/// <remarks>
/// The scenarios are in <see cref="SecretCanaryAcceptanceContract"/>. The class joins the collection of the two-node set
/// so the PostgreSQL and SQL Server containers never run at once.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class PostgreSqlSecretCanaryTests(PostgreSqlTwoNodeServer server)
    : SecretCanaryAcceptanceContract, IClassFixture<PostgreSqlTwoNodeServer>
{
    /// <inheritdoc />
    protected override Task<TwoNodeDatabase> NewDatabaseAsync(string password) => server.NewDatabaseForLoginAsync(password);
}
