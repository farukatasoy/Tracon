using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The connected command-line canary on PostgreSQL.</summary>
/// <remarks>
/// The scenarios are in <see cref="CliConnectedSecretCanaryAcceptanceContract"/>; see <see cref="TwoNodeAcceptanceSerial"/>
/// for why the class shares a collection and its server.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class PostgreSqlCliConnectedSecretCanaryTests(PostgreSqlTwoNodeServer server)
    : CliConnectedSecretCanaryAcceptanceContract
{
    /// <inheritdoc />
    protected override string Provider => "postgres";

    /// <inheritdoc />
    protected override Task<TwoNodeDatabase> NewDatabaseAsync(string password) => server.NewDatabaseForLoginAsync(password);
}
