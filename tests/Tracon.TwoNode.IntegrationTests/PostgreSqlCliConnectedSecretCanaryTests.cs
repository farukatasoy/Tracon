using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The connected command-line canary on PostgreSQL.</summary>
/// <remarks>
/// The scenarios are in <see cref="CliConnectedSecretCanaryAcceptanceContract"/>. The class joins the collection of the
/// two-node set so the PostgreSQL and SQL Server containers never run at once.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class PostgreSqlCliConnectedSecretCanaryTests(PostgreSqlTwoNodeServer server)
    : CliConnectedSecretCanaryAcceptanceContract, IClassFixture<PostgreSqlTwoNodeServer>
{
    /// <inheritdoc />
    protected override string Provider => "postgres";

    /// <inheritdoc />
    protected override Task<TwoNodeDatabase> NewDatabaseAsync(string password) => server.NewDatabaseForLoginAsync(password);
}
