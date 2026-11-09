using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The secret canary on SQL Server.</summary>
/// <remarks>
/// The scenarios are in <see cref="SecretCanaryAcceptanceContract"/>; see <see cref="TwoNodeAcceptanceSerial"/>
/// for why the class shares a collection and its server.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class SqlServerSecretCanaryTests(SqlServerTwoNodeServer server)
    : SecretCanaryAcceptanceContract
{
    /// <inheritdoc />
    protected override Task<TwoNodeDatabase> NewDatabaseAsync(string password) => server.NewDatabaseForLoginAsync(password);
}
