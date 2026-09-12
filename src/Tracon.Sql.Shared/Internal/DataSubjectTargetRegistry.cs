namespace Tracon;

/// <summary>The fixed table/predicate map behind data subject export and erasure.</summary>
/// <remarks>
/// <para>
/// The mirror of <see cref="RetentionTargetRegistry"/> for identity-based (rather
/// than age-based) row selection: one table/predicate defined ONCE here, shared by
/// export (a <c>SELECT</c>) and erasure (a <c>DELETE</c>) alike, instead of four SQL
/// strings hand-copied per provider. All three parameters (<c>@tenant_id</c>,
/// <c>@session_ids</c>, <c>@run_ids</c>, <c>@conversation_ids</c>) are ALWAYS bound by
/// the caller, even when a predicate does not reference one of them — binding an
/// unused parameter is harmless and keeps every command's parameter list identical.
/// </para>
/// <para>
/// Export reads NINE targets; erasure deletes only SEVEN of them explicitly. Two
/// (<see cref="RunInputs"/>, <see cref="ConversationItems"/>) are read for export but
/// never deleted directly — they are removed by <c>ON DELETE CASCADE</c> when their
/// owning <see cref="Runs"/>/<see cref="Conversations"/> row is deleted, so an
/// explicit delete of them would always affect zero rows by the time it ran.
/// </para>
/// </remarks>
internal static class DataSubjectTargetRegistry
{
    /// <summary>Session state.</summary>
    public const string Sessions = "sessions";

    /// <summary>
    /// Run summaries. Deletion cascades to <c>run_events</c>, <c>tool_invocations</c>,
    /// <see cref="RunInputs"/>, and <c>traces</c>/<c>spans</c>.
    /// </summary>
    public const string Runs = "runs";

    /// <summary>A run's recorded input messages. Export only; see the type remarks.</summary>
    public const string RunInputs = "run_inputs";

    /// <summary>Uploaded attachment metadata and bytes.</summary>
    public const string Attachments = "attachments";

    /// <summary>Real-time voice connection summaries.</summary>
    public const string VoiceSessions = "voice_sessions";

    /// <summary>Run and message scores.</summary>
    public const string RunScores = "run_scores";

    /// <summary>
    /// Conversation headers. Deletion cascades to <see cref="ConversationItems"/> and
    /// the conversation-linked rows of <see cref="Responses"/>.
    /// </summary>
    public const string Conversations = "conversations";

    /// <summary>Conversation message history. Export only; see the type remarks.</summary>
    public const string ConversationItems = "conversation_items";

    /// <summary>Responses API records, linked by session, by conversation, or both.</summary>
    public const string Responses = "responses";

    /// <summary>The targets read for an export, in a stable order.</summary>
    public static IReadOnlyList<string> ExportTargets { get; } =
    [
        Sessions, Runs, RunInputs, Attachments, VoiceSessions, RunScores, Conversations, ConversationItems, Responses,
    ];

    /// <summary>
    /// The targets deleted for an erasure, in the order they must run:
    /// conversations first (so its cascade clears conversation-linked responses
    /// before the leftover, session-linked <see cref="Responses"/> step runs),
    /// runs next (so its cascade clears events/invocations/inputs/traces), then
    /// the remaining tenant-scoped tables, sessions last.
    /// </summary>
    public static IReadOnlyList<string> EraseTargets { get; } =
    [
        Conversations, Responses, Runs, Attachments, VoiceSessions, RunScores, Sessions,
    ];

    /// <summary>
    /// The export column list for <see cref="Attachments"/> — every column EXCEPT the
    /// file bytes; see <see cref="DataSubjectExport"/>.
    /// </summary>
    private const string AttachmentExportColumns =
        "id, tenant_id, session_id, run_id, file_name, media_type, byte_size, sha256, external_uri, created_by, created_at";

    /// <summary>Resolves the schema-qualified table, <c>WHERE</c> predicate, and export column list of a target.</summary>
    /// <param name="dialect">The provider dialect.</param>
    /// <param name="target">One of the constants on this type.</param>
    /// <returns>
    /// The table name, predicate, and the column list an export <c>SELECT</c> should
    /// use (<c>"*"</c> unless noted otherwise).
    /// </returns>
    /// <exception cref="ArgumentException">The target is not recognized.</exception>
    public static (string Table, string Predicate, string Columns) Resolve(SqlDialect dialect, string target)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        string Table(string name) => dialect.QualifyTable(name);

        // 🚨 The compared column is ALWAYS fully qualified (`{table}.{column}`),
        // never bare — measured against real SQLite: `json_each()` itself
        // exposes a column literally named `id` (part of its fixed output
        // shape: key/value/type/atom/id/parent/fullkey/path), which SILENTLY
        // SHADOWS an unqualified `id` reference to the correlated outer table.
        // `WHERE value = id` inside the EXISTS then compares against
        // json_each's OWN row id, never the outer table's — every row fails to
        // match and the predicate is always false, with no error at all.
        // PostgreSQL's `= ANY(...)` and SQL Server's `OPENJSON` (key/value/type
        // only, no `id` column) do not have this trap; the fix is still applied
        // unconditionally here for every column, not just `id`, since the
        // qualification is harmless on all three providers and protects
        // against the same shadowing on a future column named `key`/`value`/
        // `type`/`atom`/`parent`/`fullkey`/`path`.
        string InSessions(string qualifiedColumn) => dialect.ArrayContains(qualifiedColumn, "session_ids");
        string InRuns(string qualifiedColumn) => dialect.ArrayContains(qualifiedColumn, "run_ids");
        string InConversations(string qualifiedColumn) => dialect.ArrayContains(qualifiedColumn, "conversation_ids");

        var sessions = Table("sessions");
        var runs = Table("runs");
        var attachments = Table("attachments");
        var voiceSessions = Table("voice_sessions");
        var runScores = Table("run_scores");
        var conversations = Table("conversations");
        var conversationItems = Table("conversation_items");
        var responses = Table("responses");
        var runInputs = Table("run_inputs");

        return target switch
        {
            Sessions => (sessions, $"tenant_id = @tenant_id AND {InSessions($"{sessions}.id")}", "*"),

            Runs => (
                runs,
                $"tenant_id = @tenant_id AND ({InSessions($"{runs}.session_id")} OR {InRuns($"{runs}.id")})",
                "*"),

            RunInputs => (
                runInputs,
                $"tenant_id = @tenant_id AND run_id IN (SELECT id FROM {runs} " +
                $"WHERE tenant_id = @tenant_id AND ({InSessions($"{runs}.session_id")} OR {InRuns($"{runs}.id")}))",
                "*"),

            // 🚨 The file BYTES ('content') are deliberately excluded; see
            // DataSubjectExport's remarks and AttachmentExportColumns.
            Attachments => (
                attachments,
                $"tenant_id = @tenant_id AND ({InSessions($"{attachments}.session_id")} OR {InRuns($"{attachments}.run_id")})",
                AttachmentExportColumns),

            VoiceSessions => (
                voiceSessions,
                $"tenant_id = @tenant_id AND {InSessions($"{voiceSessions}.session_id")}",
                "*"),

            RunScores => (runScores, $"tenant_id = @tenant_id AND {InRuns($"{runScores}.run_id")}", "*"),

            Conversations => (
                conversations,
                $"tenant_id = @tenant_id AND {InConversations($"{conversations}.id")}",
                "*"),

            ConversationItems => (
                conversationItems,
                $"conversation_id IN (SELECT id FROM {conversations} " +
                $"WHERE tenant_id = @tenant_id AND {InConversations($"{conversations}.id")})",
                "*"),

            // 🚨 Responses carries no tenant_id column of its own (0001_initial.sql):
            // the session-linked branch is therefore scoped through an EXISTS
            // correlation against sessions, which DOES carry tenant_id — without it
            // a session id string that happens to collide across two tenants
            // (sessions' key is (tenant_id, id), K-278) could touch another
            // tenant's row.
            Responses => (
                responses,
                $"(conversation_id IS NOT NULL AND conversation_id IN (SELECT id FROM {conversations} " +
                $"WHERE tenant_id = @tenant_id AND {InConversations($"{conversations}.id")}))" +
                $" OR (session_id IS NOT NULL AND {InSessions($"{responses}.session_id")} AND EXISTS (SELECT 1 FROM " +
                $"{sessions} s WHERE s.id = {responses}.session_id AND s.tenant_id = @tenant_id))",
                "*"),

            _ => throw new ArgumentException($"Unknown data subject target: '{target}'.", nameof(target)),
        };
    }
}
