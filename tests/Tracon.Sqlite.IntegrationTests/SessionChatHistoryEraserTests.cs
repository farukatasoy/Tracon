using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// What deleting a session, the retention period of the sessions and the erasure of a data subject do with the chat history
/// of the sessions, on SQLite.
/// </summary>
/// <remarks>The scenarios live in <see cref="SessionChatHistoryEraserContract"/>, which the other two SQL providers run too.</remarks>
/// <param name="schema">The table prefix shared by the tests of this class.</param>
public sealed class SessionChatHistoryEraserTests(SqliteSchemaFixture schema)
    : SessionChatHistoryEraserContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    internal override async ValueTask<SqlStoreContext> OpenAsync()
    {
        await schema.ResetAsync();

        return schema.Context.StoreContext;
    }
}

/// <summary>
/// The same scenarios with content protection on: the state of a session and the items of its history are encrypted, and the
/// eraser still finds the conversation, because it reads the state through the session store, which decrypts it.
/// </summary>
/// <param name="schema">The table prefix shared by the tests of this class.</param>
public sealed class SessionChatHistoryEraserWithContentProtectionTests(SqliteSchemaFixture schema)
    : SessionChatHistoryEraserContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    internal override bool ContentIsProtected => true;

    /// <inheritdoc />
    internal override async ValueTask<SqlStoreContext> OpenAsync()
    {
        await schema.ResetAsync();

        return ProtectingStoreContext.Build(
            schema.Context,
            "k1",
            new HashSet<ProtectedColumn> { ProtectedColumn.SessionState, ProtectedColumn.ConversationItem });
    }
}
