using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Branches a session's conversation from a given point, and opens a new
/// session that carries the branch.
/// </summary>
/// <remarks>
/// <para>
/// Branching <strong>copies</strong>: items are moved into the new
/// conversation with <c>INSERT … SELECT</c>; the pointer is only lineage
/// information. If a pointer chain had been chosen instead, every history
/// read would become recursive, and <c>SqlChatHistoryProvider</c> — the
/// hottest read path, run on every agent turn — would charge a cost to
/// consumers who never use branching. With this design, the read path
/// <strong>does not change by even a single line</strong>.
/// </para>
/// <para>
/// The conversation identity lives in the session's state bag
/// (<see cref="AgentPrismSessionStateKeys.ChatHistory"/>); the only way to
/// use a branched conversation is through a new session that carries that
/// identity. This is why the endpoint is the session, not the conversation.
/// </para>
/// </remarks>
internal sealed class ConversationBranchService
{
    private readonly ISessionStore _sessions;
    private readonly IAgentCatalog _catalog;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly IConversationBranchStore? _branchStore;
    private readonly ILogger<ConversationBranchService>? _logger;

    /// <summary>Creates a new branching service.</summary>
    /// <param name="sessions">The session store.</param>
    /// <param name="catalog">The agent catalog. Required to restore the session.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="branchStore">
    /// The conversation copier. Branching is not supported if this is
    /// <see langword="null"/> — in an in-memory setup, chat history lives in
    /// Microsoft Agent Framework's <c>InMemoryChatHistoryProvider</c> object,
    /// inside the session state's opaque block, and cannot be copied up to a
    /// given sequence number.
    /// </param>
    /// <param name="timeProvider">The time source. The system clock is used if not given.</param>
    /// <param name="logger">Records the full detail of a session restore failure, paired with the correlation id in <see cref="SessionBranchOutcome.Detail"/>.</param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    public ConversationBranchService(
        ISessionStore sessions,
        IAgentCatalog catalog,
        ITenantContext tenantContext,
        IConversationBranchStore? branchStore = null,
        TimeProvider? timeProvider = null,
        ILogger<ConversationBranchService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _sessions = sessions;
        _catalog = catalog;
        _tenantContext = tenantContext;
        _branchStore = branchStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>
    /// Whether branching is supported in this setup.
    /// </summary>
    /// <remarks>
    /// <see langword="true"/> only when a persistent SQL provider
    /// (<c>UsePostgreSql()</c>, <c>UseSqlServer()</c>, <c>UseSqlite()</c>) is enabled.
    /// </remarks>
    public bool IsSupported => _branchStore is not null;

    /// <summary>Branches a session's conversation.</summary>
    /// <param name="sessionId">The source session.</param>
    /// <param name="request">The branching request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The branching result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sessionId"/> is empty.</exception>
    public async ValueTask<SessionBranchOutcome> BranchAsync(
        string sessionId,
        SessionBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(request);

        if (_branchStore is null)
        {
            return SessionBranchOutcome.Failed(
                SessionBranchStatus.NotSupported,
                "Conversation branching only works when a persistent SQL provider is enabled. " +
                "In an in-memory setup, chat history lives in the opaque session state block and " +
                "cannot be copied up to a point; silently copying all of it would not produce the " +
                "requested branch.");
        }

        if (request.UpToSequence is < 0)
        {
            return SessionBranchOutcome.Failed(
                SessionBranchStatus.InvalidRequest,
                "'upToSequence' cannot be negative. Leave it empty to copy the entire conversation.");
        }

        var record = await _sessions.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        // "Does not exist" and "belongs to another tenant" produce the SAME result; existence is not leaked.
        if (record is null || !string.Equals(record.TenantId, _tenantContext.TenantId, StringComparison.Ordinal))
        {
            return SessionBranchOutcome.Failed(
                SessionBranchStatus.SessionNotFound,
                $"There is no session with id '{sessionId}'.");
        }

        var newSessionId = request.NewSessionId is { Length: > 0 } requested
            ? requested
            : AgentPrismId.NewId().ToString("D");

        if (await _sessions.GetAsync(newSessionId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return SessionBranchOutcome.Failed(
                SessionBranchStatus.SessionExists,
                $"A session with id '{newSessionId}' already exists. Branching does NOT overwrite " +
                "an existing session; give a different id or leave it empty to have one generated.");
        }

        var agent = await _catalog.ResolveAsync(record.AgentName, culture: null, cancellationToken).ConfigureAwait(false);

        if (agent is null)
        {
            return SessionBranchOutcome.Failed(
                SessionBranchStatus.AgentNotFound,
                $"There is no agent named '{record.AgentName}'; the session cannot be restored, " +
                "so its conversation cannot be branched.");
        }

        AgentSession session;

        try
        {
            session = await agent
                .DeserializeSessionAsync(record.State, jsonSerializerOptions: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            // 🚨 ex.Message describes AgentPrism's OWN previously-stored session state
            // failing to deserialize, not a provider secret - but it is still foreign text
            // (Phase 119, BL-027/BL-037) and this outcome's Detail reaches the HTTP response
            // verbatim (SessionEndpoints), so only the type name and a correlation id go out.
            var correlationId = SafeErrorText.NewCorrelationId();

            if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(ex, "Session {SessionId} could not be restored for branching. (ref: {CorrelationId})", sessionId, correlationId);
            }

            return SessionBranchOutcome.Failed(
                SessionBranchStatus.AgentNotFound,
                $"Session '{sessionId}' could not be restored with agent '{record.AgentName}': " +
                SafeErrorText.ForPersistence(ex, correlationId));
        }

        if (!session.StateBag.TryGetValue<ChatHistoryState>(
                AgentPrismSessionStateKeys.ChatHistory,
                out var state,
                AgentPrismCoreJsonContext.Default.Options) ||
            state is null ||
            state.ConversationId == Guid.Empty)
        {
            return SessionBranchOutcome.Failed(
                SessionBranchStatus.NoConversation,
                $"Session '{sessionId}' has no conversation yet. At least one turn must have run in " +
                "the session for there to be anything to branch.");
        }

        var branch = await _branchStore
            .BranchAsync(_tenantContext.TenantId, state.ConversationId, request.UpToSequence, cancellationToken)
            .ConfigureAwait(false);

        if (branch is not { } created)
        {
            return SessionBranchOutcome.Failed(
                SessionBranchStatus.NoConversation,
                $"Conversation '{state.ConversationId}' was not found; the session points to a " +
                "conversation, but the record may have been deleted.");
        }

        // The new session carries the source's state verbatim; only the
        // conversation identity and the AgentPrism identity stamp change.
        // This is the only way to use the branched conversation without
        // changing the read path at all.
        session.StateBag.SetValue(
            AgentPrismSessionStateKeys.ChatHistory,
            new ChatHistoryState { ConversationId = created.ConversationId },
            AgentPrismCoreJsonContext.Default.Options);

        AgentSessionIdentity.SetId(session, newSessionId);

        var now = _timeProvider.GetUtcNow();

        await _sessions.SaveAsync(
            new SessionRecord
            {
                Id = newSessionId,
                AgentName = record.AgentName,
                State = await agent
                    .SerializeSessionAsync(session, jsonSerializerOptions: null, cancellationToken)
                    .ConfigureAwait(false),
                CreatedAt = now,
                UpdatedAt = now,
                TenantId = _tenantContext.TenantId,
            },
            cancellationToken).ConfigureAwait(false);

        return SessionBranchOutcome.Succeeded(new SessionBranchResult
        {
            SessionId = newSessionId,
            ConversationId = created.ConversationId,
            ParentSessionId = sessionId,
            ParentConversationId = state.ConversationId,
            BranchFromSequence = created.BranchFromSequence,
            CopiedItemCount = created.CopiedItemCount,
        });
    }
}

/// <summary>The outcome of a branching attempt.</summary>
public enum SessionBranchStatus
{
    /// <summary>The branch was opened.</summary>
    Branched = 0,

    /// <summary>The source session does not exist, or belongs to another tenant.</summary>
    SessionNotFound = 1,

    /// <summary>The session has no conversation yet.</summary>
    NoConversation = 2,

    /// <summary>The requested new session identity is already in use.</summary>
    SessionExists = 3,

    /// <summary>The session's agent cannot be resolved.</summary>
    AgentNotFound = 4,

    /// <summary>The request is invalid.</summary>
    InvalidRequest = 5,

    /// <summary>Branching is not supported in this setup.</summary>
    NotSupported = 6,
}

/// <summary>The result of a branching attempt.</summary>
public sealed record SessionBranchOutcome
{
    private SessionBranchOutcome()
    {
    }

    /// <summary>The result.</summary>
    public required SessionBranchStatus Status { get; init; }

    /// <summary>The human-readable reason for a failure.</summary>
    public string? Detail { get; init; }

    /// <summary>The branch that was opened. Populated only when <see cref="SessionBranchStatus.Branched"/>.</summary>
    public SessionBranchResult? Result { get; init; }

    /// <summary>Produces a failed result.</summary>
    /// <param name="status">The result.</param>
    /// <param name="detail">The reason.</param>
    /// <returns>The result.</returns>
    public static SessionBranchOutcome Failed(SessionBranchStatus status, string detail)
        => new() { Status = status, Detail = detail };

    /// <summary>Produces a successful result.</summary>
    /// <param name="result">The branch that was opened.</param>
    /// <returns>The result.</returns>
    public static SessionBranchOutcome Succeeded(SessionBranchResult result)
        => new() { Status = SessionBranchStatus.Branched, Result = result };
}
