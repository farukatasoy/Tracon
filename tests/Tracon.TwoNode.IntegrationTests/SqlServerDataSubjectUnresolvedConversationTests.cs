using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The conversation count of a data subject erasure on SQL Server.</summary>
/// <remarks>
/// The scenarios are in <see cref="DataSubjectUnresolvedConversationAcceptanceContract"/>; the class joins the collection of
/// the two-node set so the PostgreSQL and SQL Server containers never run at once.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class SqlServerDataSubjectUnresolvedConversationTests(SqlServerTwoNodeServer server)
    : DataSubjectUnresolvedConversationAcceptanceContract, IClassFixture<SqlServerTwoNodeServer>
{
    /// <inheritdoc />
    protected override TwoNodeDatabase NewDatabase() => server.NewDatabase();
}
