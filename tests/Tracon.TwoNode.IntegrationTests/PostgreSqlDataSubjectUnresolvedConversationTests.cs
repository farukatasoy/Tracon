using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>The conversation count of a data subject erasure on PostgreSQL.</summary>
/// <remarks>
/// The scenarios are in <see cref="DataSubjectUnresolvedConversationAcceptanceContract"/>; see <see cref="TwoNodeAcceptanceSerial"/>
/// for why the class shares a collection and its server.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class PostgreSqlDataSubjectUnresolvedConversationTests(PostgreSqlTwoNodeServer server)
    : DataSubjectUnresolvedConversationAcceptanceContract
{
    /// <inheritdoc />
    protected override TwoNodeDatabase NewDatabase() => server.NewDatabase();
}
