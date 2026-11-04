using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The audit entry of an operation on a resource with a 200-character name, on SQL Server.</summary>
/// <remarks>
/// The scenarios are in <see cref="AuditLabelLimitAcceptanceContract"/>; see
/// <see cref="PostgreSqlDefinitionTransferTests"/> for why the class shares a collection.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class SqlServerAuditLabelLimitTests(SqlServerTwoNodeServer server)
    : AuditLabelLimitAcceptanceContract, IClassFixture<SqlServerTwoNodeServer>
{
    /// <inheritdoc />
    protected override TwoNodeDatabase NewDatabase() => server.NewDatabase();
}
