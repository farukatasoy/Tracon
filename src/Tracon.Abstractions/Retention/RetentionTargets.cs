namespace Tracon;

/// <summary>The fixed table list a retention policy may target.</summary>
/// <remarks>
/// <para>
/// The target name is <strong>not</strong> free-form text: the allowed
/// targets are this fixed list. Otherwise this would be a table-name
/// injection surface.
/// </para>
/// <para>
/// <c>audit_log</c> is DELIBERATELY <strong>absent</strong> from this list.
/// The audit trail is never automatically deleted.
/// </para>
/// </remarks>
public static class RetentionTargets
{
    /// <summary>The run event stream (append-only).</summary>
    public const string RunEvents = "run_events";

    /// <summary>Tool call summaries.</summary>
    public const string ToolInvocations = "tool_invocations";

    /// <summary>OpenTelemetry trace headers. Deletion cascades to the spans.</summary>
    public const string Traces = "traces";

    /// <summary>Completed/failed/cancelled queue jobs. Deletion cascades to their items.</summary>
    public const string Jobs = "jobs";

    /// <summary>Delivered webhook delivery-history records.</summary>
    public const string WebhookDeliveries = "webhook_deliveries";

    /// <summary>Evaluation (eval) case results.</summary>
    public const string EvalCaseResults = "eval_case_results";

    /// <summary>Workflow checkpoints of completed runs.</summary>
    public const string WorkflowCheckpoints = "workflow_checkpoints";

    /// <summary>Expired or cancelled skill script execution grants.</summary>
    public const string SkillScriptGrants = "skill_script_grants";

    /// <summary>Orphaned attachments and, when they are stored externally, their bytes.</summary>
    /// <remarks>
    /// An attachment is orphaned when it has no session id, or when nothing in
    /// the same tenant owns its session id. An owner is a session, or a workflow
    /// run with that session id that waits for input, has not finished, or
    /// finished after the cutoff. With <see cref="IAttachmentStorage"/>
    /// registered, the bytes are deleted before the row; a row whose bytes
    /// cannot be deleted is kept for the next run.
    /// </remarks>
    public const string Attachments = "attachments";

    /// <summary>Serialized session state. User data; OFF by default.</summary>
    /// <remarks>
    /// An expired session goes with the copies of its content that live outside its record: its chat history (the
    /// conversation a SQL provider keeps for it, with its messages), the files of its workspace and its conversation memory
    /// records. They go whatever the rule for <see cref="Conversations"/> says.
    /// </remarks>
    public const string Sessions = "sessions";

    /// <summary>Conversation history. Deletion cascades to its messages. User data; OFF by default.</summary>
    /// <remarks>
    /// The conversation of a session goes with the session when the <see cref="Sessions"/> target expires it. This target
    /// removes every conversation whose last write is older than its age, whether or not a session still names it, and it
    /// is the only target that reaches the conversations of runs that have no session.
    /// </remarks>
    public const string Conversations = "conversations";

    /// <summary>
    /// The summary record of closed real-time voice connections.
    /// </summary>
    /// <remarks>
    /// The record carries <strong>no audio</strong>; it only carries
    /// duration, turn count, and measurements. If the conversation's audio
    /// was stored (default: no), the bytes fall under the
    /// <see cref="Attachments"/> target's scope.
    /// </remarks>
    public const string VoiceSessions = "voice_sessions";

    /// <summary>Run and message scores.</summary>
    public const string RunScores = "run_scores";

    /// <summary>Stored idempotency responses.</summary>
    public const string IdempotencyKeys = "idempotency_keys";

    /// <summary>
    /// Runs' recorded input messages. The source of replay;
    /// deleting an input makes that run unable to be replayed.
    /// </summary>
    /// <remarks>
    /// The content is in the same information class as <c>conversation_items</c>,
    /// but the target is <strong>not off by default</strong>:
    /// <c>run_inputs</c> is not the user's own conversation history but a
    /// derived record of the run, and its lifetime should be limitable
    /// through the retention policy.
    /// </remarks>
    public const string RunInputs = "run_inputs";

    /// <summary>
    /// Knowledge-base chunks and their embeddings. The table
    /// exists only in the PostgreSQL migration set; binding this target to a
    /// policy on SQL Server or SQLite fails at run time (no such table).
    /// </summary>
    public const string DocumentEmbeddings = "document_embeddings";

    /// <summary>
    /// The inbox rows of workflow requests whose run was closed because the request expired
    /// (<see cref="WorkflowDefinition.RequestTimeout"/>).
    /// </summary>
    /// <remarks>
    /// Only a closed row is deleted: a row of a run that still waits for an answer is the
    /// request's entry in the inbox. The row carries a session id, a request id and a workflow
    /// name, no prompt and no answer; it is not user data, so the configuration-based default applies.
    /// </remarks>
    public const string WorkflowRequests = "workflow_requests";

    /// <summary>All recognized target names.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        RunEvents,
        ToolInvocations,
        Traces,
        Jobs,
        WebhookDeliveries,
        EvalCaseResults,
        WorkflowCheckpoints,
        SkillScriptGrants,
        Attachments,
        Sessions,
        Conversations,
        VoiceSessions,
        RunScores,
        IdempotencyKeys,
        RunInputs,
        DocumentEmbeddings,
        WorkflowRequests,
    ];

    /// <summary>The targets that carry user data and are OFF by default.</summary>
    /// <remarks>
    /// <para>
    /// Off by default means that no age is set for them, not that configuration is ignored. The configuration-based default
    /// of each of these targets (<c>TraconRetentionOptions</c>) carries no <c>MaxAgeDays</c>, so nothing is deleted until an
    /// operator sets one: either in configuration, which applies once <c>Enabled</c> is on and no database policy covers the
    /// target, or in a database policy whose <see cref="RetentionPolicy.Enabled"/> is <see langword="true"/>.
    /// </para>
    /// <para>
    /// The list is a classification, and nothing in the resolver reads it: these targets resolve like every other, a database
    /// policy first and configuration only when there is none.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> UserDataTargets { get; } = [Sessions, Conversations];

    /// <summary>Reports whether a target name is recognized.</summary>
    /// <param name="target">The target name.</param>
    /// <returns><see langword="true"/> if the name is recognized.</returns>
    public static bool IsKnown(string? target)
        => target is not null && All.Contains(target, StringComparer.Ordinal);
}
