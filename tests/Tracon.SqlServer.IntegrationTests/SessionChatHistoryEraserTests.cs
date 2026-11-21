using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// What deleting a session, the retention period of the sessions and the erasure of a data subject do with the chat history
/// of the sessions, on SQL Server.
/// </summary>
/// <remarks>The scenarios live in <see cref="SessionChatHistoryEraserContract"/>, which the other two SQL providers run too.</remarks>
/// <param name="schema">The schema shared by the tests of this class.</param>
public sealed class SessionChatHistoryEraserTests(SqlServerSchemaFixture schema)
    : SessionChatHistoryEraserContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    internal override async ValueTask<SqlStoreContext> OpenAsync()
    {
        await schema.ResetAsync();

        return schema.Context.StoreContext;
    }
}
