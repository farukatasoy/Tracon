using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The secret canary on PostgreSQL.</summary>
/// <remarks>
/// The scenarios are in <see cref="SecretCanaryAcceptanceContract"/>; see <see cref="TwoNodeAcceptanceSerial"/>
/// for why the class shares a collection and its server.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class PostgreSqlSecretCanaryTests(PostgreSqlTwoNodeServer server)
    : SecretCanaryAcceptanceContract
{
    /// <inheritdoc />
    protected override Task<TwoNodeDatabase> NewDatabaseAsync(string password) => server.NewDatabaseForLoginAsync(password);
}
