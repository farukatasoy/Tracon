namespace AgentPrism;

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

    /// <summary>Orphaned (sessionless) uploaded attachments.</summary>
    public const string Attachments = "attachments";

    /// <summary>Serialized session state. User data; OFF by default.</summary>
    public const string Sessions = "sessions";

    /// <summary>Conversation history. Deletion cascades to its messages. User data; OFF by default.</summary>
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
    ];

    /// <summary>The targets that carry user data and are OFF by default.</summary>
    /// <remarks>
    /// A policy can be created for these targets, but no row is deleted
    /// unless <see cref="RetentionPolicy.Enabled"/> is explicitly set to
    /// <see langword="true"/>. Configuration-based defaults
    /// (<c>AgentPrismRetentionOptions</c>) also do not apply to these
    /// targets — only an explicit database policy takes effect.
    /// </remarks>
    public static IReadOnlyList<string> UserDataTargets { get; } = [Sessions, Conversations];

    /// <summary>Reports whether a target name is recognized.</summary>
    /// <param name="target">The target name.</param>
    /// <returns><see langword="true"/> if the name is recognized.</returns>
    public static bool IsKnown(string? target)
        => target is not null && All.Contains(target, StringComparer.Ordinal);
}
