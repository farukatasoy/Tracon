using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The connected command-line canary on SQL Server.</summary>
/// <remarks>
/// The scenarios are in <see cref="CliConnectedSecretCanaryAcceptanceContract"/>; see <see cref="TwoNodeAcceptanceSerial"/>
/// for why the class shares a collection and its server.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class SqlServerCliConnectedSecretCanaryTests(SqlServerTwoNodeServer server)
    : CliConnectedSecretCanaryAcceptanceContract
{
    /// <inheritdoc />
    protected override string Provider => "sqlserver";

    /// <inheritdoc />
    protected override Task<TwoNodeDatabase> NewDatabaseAsync(string password) => server.NewDatabaseForLoginAsync(password);
}
