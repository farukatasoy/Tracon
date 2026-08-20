using System.Data.Common;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Microsoft Agent Framework chat history provider that stores history in the SQL database.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This provider's instance is shared across all sessions.</strong> This is
/// Microsoft Agent Framework's explicit warning: the provider is attached to the agent
/// and the same instance is used by every session. Nothing session-specific may
/// therefore be kept as a field. The conversation id is carried in the session's own
/// state via <see cref="ProviderSessionState{TState}"/>; the provider holds only the
/// data source reference.
/// </para>
/// <para>
/// Messages are stored in order in the <c>conversation_items</c> table. Keeping history
/// in a separate table instead of inside the session state gives two benefits: the
/// session row stays small and history can be queried with SQL.
/// </para>
/// </remarks>
internal sealed class SqlChatHistoryProvider : ChatHistoryProvider
{
    /// <summary>
    /// The state key under which the conversation id is stored in the session.
    /// </summary>
    /// <remarks>
    /// This value is <strong>stable</strong>; changing it breaks the history of existing
    /// sessions. The key is shared with <see cref="AgentPrismSessionStateKeys.ChatHistory"/>:
    /// conversation branching reads the same state and writes it into the new session.
    /// </remarks>
    public const string SessionStateKey = AgentPrismSessionStateKeys.ChatHistory;

    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;
    private readonly ProviderSessionState<ChatHistoryState> _sessionState;

    /// <summary>Creates a new chat history provider.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlChatHistoryProvider(
        SqlStoreContext context,
        ITenantContext tenantContext)
        : base(
            provideOutputMessageFilter: null,
            storeInputRequestMessageFilter: null,
            storeInputResponseMessageFilter: null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _context = context;
        _sql = context.Sql;
        _tenantContext = tenantContext;

        _sessionState = new ProviderSessionState<ChatHistoryState>(
            stateInitializer: static _ => new ChatHistoryState { ConversationId = AgentPrismId.NewId() },
            stateKey: SessionStateKey,
            jsonSerializerOptions: AgentPrismJsonContext.Default.Options);
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public override IReadOnlyList<string> StateKeys => [SessionStateKey];

    /// <inheritdoc />
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var conversationId = GetConversationId(context.Session);

        var command = CreateCommand(_sql.SelectConversationItems);
        DbHelpers.Add(command, "conversation_id", conversationId);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);

        var messages = await DbHelpers
            .ReadListAsync(command, static reader => reader.GetString(0), cancellationToken)
            .ConfigureAwait(false);

        var history = new List<ChatMessage>(messages.Count);

        foreach (var json in messages)
        {
            if (JsonSerializer.Deserialize(json, AgentPrismJsonContext.Default.ChatMessage) is { } message)
            {
                history.Add(message);
            }
        }

        return history;
    }

    /// <inheritdoc />
    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messages = new List<ChatMessage>(context.RequestMessages);

        if (context.ResponseMessages is { } responses)
        {
            messages.AddRange(responses);
        }

        if (messages.Count == 0)
        {
            return;
        }

        var conversationId = GetConversationId(context.Session);
        var agentName = context.Agent.Name ?? context.Agent.Id;
        var now = DateTimeOffset.UtcNow;

        var connection = await _context.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                // The upsert locks the conversation row; a second transaction
                // writing to the same conversation concurrently waits for this
                // transaction to finish before it can read the sequence number.
                var upsert = _context.CreateCommand(_sql.UpsertConversation, connection, transaction);

                DbHelpers.Add(upsert, "id", conversationId);
                DbHelpers.Add(upsert, "tenant_id", _tenantContext.TenantId);
                DbHelpers.Add(upsert, "agent_name", agentName);
                Dialect.AddTimestamp(upsert, "now", now);

                await DbHelpers.ExecuteAsync(upsert, cancellationToken).ConfigureAwait(false);

                var nextSequenceCommand = _context.CreateCommand(_sql.SelectNextConversationSequence, connection, transaction);

                DbHelpers.Add(nextSequenceCommand, "conversation_id", conversationId);

                var scalar = await DbHelpers
                    .ExecuteScalarAsync(nextSequenceCommand, cancellationToken)
                    .ConfigureAwait(false);

                var sequence = scalar as long? ?? 0L;

                foreach (var message in messages)
                {
                    var insert = _context.CreateCommand(_sql.InsertConversationItem, connection, transaction);

                    DbHelpers.Add(insert, "id", AgentPrismId.NewId());
                    DbHelpers.Add(insert, "conversation_id", conversationId);
                    DbHelpers.Add(insert, "seq", sequence++);
                    // `json`, not `jsonb`: ChatMessage contents are polymorphic and
                    // the `$type` discriminator must be the object's first property (decision K-027).
                    Dialect.AddJson(insert, "item", JsonSerializer.Serialize(message, AgentPrismJsonContext.Default.ChatMessage));
                    Dialect.AddTimestamp(insert, "created_at", now);

                    await DbHelpers.ExecuteAsync(insert, cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    /// <summary>
    /// Gets the session's conversation id, generating and writing it to the session if absent.
    /// </summary>
    /// <remarks>
    /// The state is written back on every access. This way the id persists once the
    /// session is serialized, and a restored session continues the same conversation.
    /// </remarks>
    private Guid GetConversationId(AgentSession? session)
    {
        if (session is null)
        {
            throw new AgentPrismException(
                "Chat history cannot be stored without a session. " +
                "Provide an AgentSession when running the agent.");
        }

        var state = _sessionState.GetOrInitializeState(session);
        _sessionState.SaveState(session, state);

        return state.ConversationId;
    }
}
