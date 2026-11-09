using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The definition transfer on SQL Server.</summary>
/// <remarks>
/// The scenarios are in <see cref="DefinitionTransferAcceptanceContract"/>; see <see cref="TwoNodeAcceptanceSerial"/>
/// for why the class shares a collection and its server.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class SqlServerDefinitionTransferTests(SqlServerTwoNodeServer server)
    : DefinitionTransferAcceptanceContract
{
    /// <inheritdoc />
    protected override TwoNodeDatabase NewDatabase() => server.NewDatabase();
}
