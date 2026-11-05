using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The conversation count of a data subject erasure on PostgreSQL.</summary>
/// <remarks>
/// The scenarios are in <see cref="DataSubjectUnresolvedConversationAcceptanceContract"/>; the class joins the collection of
/// the two-node set so the PostgreSQL and SQL Server containers never run at once.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class PostgreSqlDataSubjectUnresolvedConversationTests(PostgreSqlTwoNodeServer server)
    : DataSubjectUnresolvedConversationAcceptanceContract, IClassFixture<PostgreSqlTwoNodeServer>
{
    /// <inheritdoc />
    protected override TwoNodeDatabase NewDatabase() => server.NewDatabase();
}
