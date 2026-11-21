using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon;

/// <summary>
/// Counts and deletes the chat history of sessions: the conversation <see cref="SqlChatHistoryProvider"/> keeps for each
/// of them, and with it the items of that conversation.
/// </summary>
/// <remarks>
/// <para>
/// The conversation of a session cannot be found from SQL. The <c>conversations</c> table has no session column, and the id
/// of the conversation lives only inside the state of the session. Once the session record is deleted, nothing names its
/// history any more, and no later call can reach it. So the history goes first, while the record can still say which
/// conversation is the session's: the callers run an eraser before they delete the record, and a failure leaves the record in
/// place, so a repeat call finds the session again and finishes.
/// </para>
/// <para>
/// The id is read with <see cref="SessionConversationResolver"/> from the state the session store returns, which has already
/// been unprotected when content protection is on. A session whose agent is no longer registered, or whose agent the
/// catalog cannot compile, is read from its stored state; a session whose state cannot be read in either way is counted as
/// unresolved by the resolver and logged here, and its history stays. The conversations are then deleted by tenant and id,
/// so a conversation of another tenant is never touched, and their items go with them through the foreign key.
/// </para>
/// <para>
/// <strong>Who calls it.</strong> Deleting a session and the retention period of the <c>sessions</c> target run it. The
/// erasure of a data subject does not: the HTTP endpoint already adds the conversation of every named session to the scope,
/// and the erasure deletes those conversations in its own transaction, together with the audit entry. Running this eraser
/// there as well would delete the same rows before that transaction, outside it, and report them under two names.
/// </para>
/// <para>
/// <strong>Limits.</strong> A run that is still writing the history of a session when the session is deleted creates the
/// conversation again: a first turn writes its history before the session record exists, so the write cannot be refused for
/// want of a record. A run that has no session leaves a conversation of its own that no session names, and this eraser
/// does not look for it.
/// </para>
/// </remarks>
internal sealed class SessionChatHistoryEraser : ISubjectContentEraser
{
    private readonly SqlStoreContext _context;
    private readonly Func<SessionConversationResolver> _resolver;
    private readonly ILogger<SessionChatHistoryEraser> _logger;

    /// <summary>Creates the eraser.</summary>
    /// <param name="context">The store context the conversations are deleted through.</param>
    /// <param name="resolver">
    /// Gives the resolver of the session conversations. It is asked when the eraser first has work, not when it is built: the
    /// eraser is built with the stores, before the agent catalog the resolver needs can be.
    /// </param>
    /// <param name="logger">The logger for sessions whose history cannot be found; optional.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="resolver"/> is <see langword="null"/>.</exception>
    public SessionChatHistoryEraser(
        SqlStoreContext context,
        Func<SessionConversationResolver> resolver,
        ILogger<SessionChatHistoryEraser>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resolver);

        _context = context;
        _resolver = resolver;
        _logger = logger ?? NullLogger<SessionChatHistoryEraser>.Instance;
    }

    /// <inheritdoc />
    public string Target => "session_chat_history";

    /// <inheritdoc />
    /// <remarks>Counts conversations, not their items.</remarks>
    public async ValueTask<int> CountAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken)
    {
        var conversationIds = await FindConversationsAsync(tenantId, sessionIds, cancellationToken).ConfigureAwait(false);

        if (conversationIds.Count == 0)
        {
            return 0;
        }

        var (table, predicate, _) = DataSubjectTargetRegistry.Resolve(_context.Dialect, DataSubjectTargetRegistry.Conversations);

        var count = await _context.ExecuteStatementAsync(
            _context.Dialect.BuildDataSubjectSelectSql(table, "COUNT(*)", predicate),
            command => Bind(command, tenantId, conversationIds),
            DbHelpers.ExecuteScalarAsync,
            cancellationToken).ConfigureAwait(false);

        // COUNT(*) is an int on SQL Server and a bigint on the other two.
        return count is null ? 0 : Convert.ToInt32(count, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns the number of conversations deleted; the items that went with them through the foreign key are not counted.
    /// </remarks>
    public async ValueTask<int> EraseAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken)
    {
        var conversationIds = await FindConversationsAsync(tenantId, sessionIds, cancellationToken).ConfigureAwait(false);

        if (conversationIds.Count == 0)
        {
            return 0;
        }

        var (table, predicate, _) = DataSubjectTargetRegistry.Resolve(_context.Dialect, DataSubjectTargetRegistry.Conversations);

        return await _context.ExecuteStatementAsync(
            _context.Dialect.BuildDataSubjectDeleteSql(table, predicate),
            command => Bind(command, tenantId, conversationIds),
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Writes an empty array. The export of a data subject lists the conversations and their items from the tables, by the
    /// conversation ids the HTTP endpoint adds to the scope, and this eraser is not part of that export.
    /// </remarks>
    public ValueTask WriteExportAsync(
        string tenantId,
        IReadOnlyList<string> sessionIds,
        Utf8JsonWriter writer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartArray(Target);
        writer.WriteEndArray();

        return ValueTask.CompletedTask;
    }

    private void Bind(System.Data.Common.DbCommand command, string tenantId, IReadOnlyList<Guid> conversationIds)
    {
        DbHelpers.AddTenant(command, tenantId);
        _context.Dialect.AddUuidArray(command, "conversation_ids", conversationIds);
    }

    /// <summary>Finds the conversations of the sessions of <paramref name="tenantId"/>, from the state their records hold.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="sessionIds">The sessions.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The distinct conversation ids; empty when no session has a history or none can be read.</returns>
    private async ValueTask<IReadOnlyList<Guid>> FindConversationsAsync(
        string tenantId,
        IReadOnlyList<string> sessionIds,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(sessionIds);

        if (sessionIds.Count == 0)
        {
            return [];
        }

        SessionConversationLookup lookup;

        // The session store answers for the ambient tenant, and a caller that is not a request (a retention run) has none
        // that is the tenant of the sessions. The scope is opened and ended in this method: nothing outside it needs the value.
        using (AmbientTenantScope.Begin(tenantId))
        {
            lookup = await _resolver()
                .ResolveForDeletionAsync(sessionIds, cancellationToken)
                .ConfigureAwait(false);
        }

        if (lookup.Unresolved > 0 && _logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(
                "The chat history of {Unresolved} of {Sessions} session(s) of tenant '{TenantId}' could not be found, " +
                "because their stored state does not have the shape the conversation id is read from. " +
                "A conversation may stay behind.",
                lookup.Unresolved,
                sessionIds.Count,
                tenantId);
        }

        return [.. lookup.ConversationIds.Distinct()];
    }
}
