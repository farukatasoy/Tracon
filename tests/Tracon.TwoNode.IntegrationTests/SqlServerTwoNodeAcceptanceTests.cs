using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The two-node acceptance set on SQL Server.</summary>
/// <remarks>
/// The scenarios are in <see cref="TwoNodeAcceptanceContract"/>; see <see cref="TwoNodeAcceptanceSerial"/>
/// for why the class shares a collection and its server.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class SqlServerTwoNodeAcceptanceTests(SqlServerTwoNodeServer server)
    : TwoNodeAcceptanceContract
{
    /// <inheritdoc />
    protected override TwoNodeDatabase NewDatabase() => server.NewDatabase();
}
