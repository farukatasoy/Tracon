namespace Tracon;

/// <summary>Defines the table of a retention target and the condition that makes a row "old".</summary>
/// <param name="Table">The qualified (schema-prefixed) table name.</param>
/// <param name="WherePredicate">
/// The SQL condition that refers to the <c>@cutoff</c> parameter. It can be a
/// standalone column comparison (<c>created_at &lt; @cutoff</c>) or an
/// <c>EXISTS</c> clause that looks at a related table (for example
/// <c>workflow_checkpoints</c>, which looks at the completion time of a run).
/// </param>
/// <param name="OrderColumn">
/// The ordering column used for determinism in the archive read. When the target
/// has no time column of its own (<c>eval_case_results</c>), <c>id</c> is used,
/// because a UUID v7 identifier is time-ordered.
/// </param>
/// <param name="TenantPredicate">
/// The SQL condition that binds the row to a tenant; it refers to the
/// <c>@tenant_id</c> parameter. When the table has a <c>tenant_id</c> column of its
/// own, it is a direct comparison; otherwise (<c>run_events</c>,
/// <c>tool_invocations</c>, <c>eval_case_results</c>) it is an <c>EXISTS</c> clause
/// that looks at the owner.
/// Without this field, a policy defined for one tenant deleted the
/// rows of ALL tenants.
/// </param>
/// <param name="RowLimitOrderExpression">
/// For <c>MaxRows</c>: the SQL expression used to find the Nth row from
/// the newest. On most targets it is THE SAME COLUMN that
/// <see cref="WherePredicate"/> compares with <c>@cutoff</c> (the threshold can be
/// fed straight into the same condition). On targets that look at a related table
/// (<c>eval_case_results</c>) it is a correlated subquery. See 36.1.
/// </param>
/// <param name="KeyColumns">
/// The unquoted columns of the table's primary key, without its tenant column.
/// The archive mode deletes by these values: a row is deleted only when its key
/// was in the batch that was archived. The order is the key's own column order.
/// </param>
/// <param name="TenantInKey">
/// Whether the primary key also holds the tenant column (<c>sessions</c>,
/// <c>idempotency_keys</c>). Then <see cref="KeyColumns"/> is unique only within
/// one tenant, and a delete by key always runs with the tenant filter.
/// </param>
internal readonly record struct RetentionTargetDefinition(
    string Table,
    string WherePredicate,
    string TenantPredicate,
    string OrderColumn,
    string RowLimitOrderExpression,
    IReadOnlyList<string> KeyColumns,
    bool TenantInKey = false);

/// <summary>
/// The constant registry that holds the table/condition mapping of every target in
/// the <see cref="RetentionTargets"/> allow list.
/// </summary>
/// <remarks>
/// <para>
/// This registry is the SQL counterpart of the retention table in 25.1. To add a
/// new target, a constant is first added to <c>RetentionTargets</c> and then its
/// table/condition is written here — the SQL text is NOT copied
/// <strong>per provider</strong>; <see cref="SqlDialect"/> supplies only 3 template
/// methods (count/delete/read) and the table qualification
/// (<see cref="SqlDialect.QualifyTable"/>), while the table and condition are
/// defined in ONE place here. A single data table instead of 30
/// hand-written queries.
/// </para>
/// <para>
/// <c>audit_log</c> is DELIBERATELY absent here and must never be added.
/// </para>
/// </remarks>
internal static class RetentionTargetRegistry
{
    /// <summary>The key of a table whose primary key is a single <c>id</c> column.</summary>
    private static readonly string[] IdKey = ["id"];

    /// <summary>The key of <c>run_events</c>: <c>(run_id, seq)</c>.</summary>
    private static readonly string[] RunEventKey = ["run_id", "seq"];

    /// <summary>The key of <c>run_inputs</c>: one row per run.</summary>
    private static readonly string[] RunInputKey = ["run_id"];

    /// <summary>The key of <c>workflow_requests</c>: <c>(run_id, request_id)</c>.</summary>
    private static readonly string[] WorkflowRequestKey = ["run_id", "request_id"];

    /// <summary>The key of <c>idempotency_keys</c> within one tenant.</summary>
    private static readonly string[] IdempotencyKey = ["key"];

    /// <summary>
    /// The columns of a target that content protection may have stored as an
    /// envelope. An archive hands them to the consumer's sink as plaintext, the way
    /// every store read does: the envelope is Tracon's own format, and nothing outside
    /// Tracon can open it.
    /// </summary>
    /// <param name="target">A <see cref="RetentionTargets"/> value.</param>
    /// <returns>The column names; empty when the target holds no protected column.</returns>
    /// <remarks>
    /// <c>DataSubjectCoverageTests</c> fails when a <see cref="ProtectedColumn"/> of a
    /// retention target is missing here.
    /// </remarks>
    public static IReadOnlyList<string> ProtectedArchiveColumns(string target) => target switch
    {
        RetentionTargets.RunEvents => ["text", "payload"],
        RetentionTargets.ToolInvocations => ["arguments", "result"],
        RetentionTargets.WorkflowCheckpoints => ["state"],
        RetentionTargets.Attachments => ["content"],
        RetentionTargets.Sessions => ["state"],
        RetentionTargets.RunInputs => ["messages", "parameters"],
        _ => [],
    };

    /// <summary>Resolves the table/condition definition of a target.</summary>
    /// <param name="dialect">
    /// The provider dialect used to qualify the table names
    /// (see <see cref="SqlDialect.QualifyTable"/> — PostgreSQL/SQL Server qualify
    /// with a dot, SQLite by concatenating a prefix).
    /// </param>
    /// <param name="target">See <see cref="RetentionTargets"/>.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="ArgumentException">The target is not in the allow list.</exception>
    public static RetentionTargetDefinition Resolve(SqlDialect dialect, string target)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        string Table(string name) => dialect.QualifyTable(name);

        // 🚨 A workflow run waiting for a human keeps what answering it needs
        // (F-327, K-128, K-130): its pending request lives ONLY in its
        // WorkflowRequest event, and the answer resumes from its checkpoints.
        // Both were deleted by age like any other row — the run has a
        // completed_at once it pauses — and the request became permanently
        // unanswerable. An answered request leaves its run AwaitingInput too
        // (a second answer opens a new branch, K-130), so these rows are kept
        // for as long as the run waits. The way out is the run's RequestTimeout (phase 209): when
        // it passes, the run is closed, no longer AwaitingInput, and the next round deletes
        // these rows by age like any other.
        var awaitingInput = (int)RunStatus.AwaitingInput;
        var workflowRequest = (int)RunEventType.WorkflowRequest;
        var workflowKind = (int)RunKind.Workflow;
        var workflowRequestClosed = (int)WorkflowRequestStatus.Closed;

        return target switch
        {
            RetentionTargets.RunEvents => new RetentionTargetDefinition(
                Table("run_events"),
                $"created_at < @cutoff AND NOT (type = {workflowRequest} AND EXISTS (SELECT 1 FROM {Table("runs")} rw "
                    + $"WHERE rw.id = {Table("run_events")}.run_id AND rw.status = {awaitingInput}))",
                $"EXISTS (SELECT 1 FROM {Table("runs")} rt "
                    + $"WHERE rt.id = {Table("run_events")}.run_id AND rt.tenant_id = @tenant_id)",
                "created_at",
                "created_at",
                RunEventKey),

            RetentionTargets.ToolInvocations => new RetentionTargetDefinition(
                Table("tool_invocations"),
                "created_at < @cutoff",
                $"EXISTS (SELECT 1 FROM {Table("runs")} rt "
                    + $"WHERE rt.id = {Table("tool_invocations")}.run_id AND rt.tenant_id = @tenant_id)",
                "created_at",
                "created_at",
                IdKey),

            // traces is deleted; spans go with it through ON DELETE CASCADE.
            RetentionTargets.Traces => new RetentionTargetDefinition(
                Table("traces"),
                "started_at < @cutoff",
                "tenant_id = @tenant_id",
                "started_at",
                "started_at",
                IdKey),

            // Finished jobs only (Completed=3, Failed=4, Cancelled=5); job_items go
            // with them through ON DELETE CASCADE. The completed_at of an active job
            // is NULL; the row limit query filters the NULLs out
            // (see SqlDialect.BuildRetentionFindNthRowCutoffSql).
            RetentionTargets.Jobs => new RetentionTargetDefinition(
                Table("jobs"),
                "status IN (3, 4, 5) AND completed_at < @cutoff",
                "tenant_id = @tenant_id",
                "completed_at",
                "completed_at",
                IdKey),

            // Delivered records only (Delivered=1).
            RetentionTargets.WebhookDeliveries => new RetentionTargetDefinition(
                Table("webhook_deliveries"),
                "status = 1 AND delivered_at < @cutoff",
                "tenant_id = @tenant_id",
                "delivered_at",
                "delivered_at",
                IdKey),

            // eval_case_results has no time column of its own; the completion time
            // of the run is looked at with EXISTS. The uuid v7 identifier is used for
            // the archive ordering (OrderColumn), but the MaxRows threshold must be
            // THE SAME as the column the WherePredicate REALLY compares
            // (er.completed_at) — RowLimitOrderExpression is therefore a standalone
            // correlated subquery. Open Question 2 (phase 36 plan) was resolved this
            // way: id is NOT a timestamp and cannot be a threshold directly.
            //
            // 🚨 The correlation is written against the FULLY QUALIFIED name
            // (Table(...)), NOT against the BARE target name: on SQLite QualifyTable
            // CONCATENATES the prefix and the name (no dot, K-193) — a bare
            // "eval_case_results" does NOT MATCH the real object in the FROM clause
            // (for example "t_ab12cd34eval_case_results") and blows up at run time
            // with "no such column". In phase 36 the MaxRows tests CAUGHT this trap
            // while running against SQLite; the original WherePredicate of phase 25
            // carried the SAME defect and was fixed here at the same time.
            RetentionTargets.EvalCaseResults => new RetentionTargetDefinition(
                Table("eval_case_results"),
                $"EXISTS (SELECT 1 FROM {Table("eval_runs")} er " +
                $"WHERE er.id = {Table("eval_case_results")}.eval_run_id AND er.completed_at < @cutoff)",
                $"EXISTS (SELECT 1 FROM {Table("eval_runs")} ert " +
                $"WHERE ert.id = {Table("eval_case_results")}.eval_run_id AND ert.tenant_id = @tenant_id)",
                "id",
                $"(SELECT er.completed_at FROM {Table("eval_runs")} er WHERE er.id = {Table("eval_case_results")}.eval_run_id)",
                IdKey),

            // The basis is not the created_at of the checkpoint itself but the
            // completion time of the ASSOCIATED RUN (25.1: "7 days after the run
            // completes"). For THE SAME REASON the MaxRows threshold is computed with
            // a correlated subquery over runs.completed_at (the same pattern as
            // eval_case_results). The correlation is the FULLY QUALIFIED name here as
            // well — the 🚨 note above applies.
            //
            // A run AwaitingInput keeps its checkpoints (see the note at the top):
            // the run's status filter is in BOTH expressions, so such a row is
            // neither deleted nor counted toward MaxRows (its threshold is NULL).
            RetentionTargets.WorkflowCheckpoints => new RetentionTargetDefinition(
                Table("workflow_checkpoints"),
                $"EXISTS (SELECT 1 FROM {Table("runs")} r " +
                $"WHERE r.id = {Table("workflow_checkpoints")}.run_id AND r.completed_at < @cutoff " +
                $"AND r.status <> {awaitingInput})",
                "tenant_id = @tenant_id",
                "created_at",
                $"(SELECT r.completed_at FROM {Table("runs")} r WHERE r.id = {Table("workflow_checkpoints")}.run_id " +
                $"AND r.status <> {awaitingInput})",
                IdKey),

            // Expired OR revoked grants. The threshold is whichever of the two
            // columns is filled (COALESCE); when both are NULL the row limit query
            // filters the row out (it is not yet a candidate).
            RetentionTargets.SkillScriptGrants => new RetentionTargetDefinition(
                Table("skill_script_grants"),
                "(expires_at IS NOT NULL AND expires_at < @cutoff) " +
                "OR (revoked_at IS NOT NULL AND revoked_at < @cutoff)",
                "tenant_id = @tenant_id",
                "granted_at",
                "COALESCE(expires_at, revoked_at)",
                IdKey),

            // Orphaned: attachments that have no session at all, OR whose session id
            // has no owner in the SAME tenant. An owner is a sessions row, or a
            // workflow run with that session id that can still use the attachment:
            // it waits for a human, it has not finished, or it finished after the
            // cutoff. A workflow's session id never gets a sessions row (F-332):
            // without the run branch, an image an agent step made was deleted after
            // seven days while the run waited in AwaitingInput. The tenant is in
            // both lookups because the session key is (tenant_id, id) (K-278): a
            // session id another tenant uses owns nothing here.
            //
            // The correlation is the FULLY QUALIFIED name here as well — the 🚨
            // note above applies. The row limit expression stays created_at: an
            // owned attachment counts toward MaxRows but is never deleted by it.
            RetentionTargets.Attachments => new RetentionTargetDefinition(
                Table("attachments"),
                "created_at < @cutoff AND (session_id IS NULL OR ("
                    + $"NOT EXISTS (SELECT 1 FROM {Table("sessions")} s "
                    + $"WHERE s.tenant_id = {Table("attachments")}.tenant_id AND s.id = {Table("attachments")}.session_id) "
                    + $"AND NOT EXISTS (SELECT 1 FROM {Table("runs")} wr "
                    + $"WHERE wr.tenant_id = {Table("attachments")}.tenant_id AND wr.session_id = {Table("attachments")}.session_id "
                    + $"AND wr.kind = {workflowKind} "
                    + $"AND (wr.status = {awaitingInput} OR wr.completed_at IS NULL OR wr.completed_at >= @cutoff))))",
                "tenant_id = @tenant_id",
                "created_at",
                "created_at",
                IdKey),

            // User data; OFF by default (see TraconRetentionOptions).
            RetentionTargets.Sessions => new RetentionTargetDefinition(
                Table("sessions"),
                "updated_at < @cutoff",
                "tenant_id = @tenant_id",
                "updated_at",
                "updated_at",
                IdKey,
                TenantInKey: true),

            // conversations is deleted; conversation_items and responses go with it
            // through ON DELETE CASCADE. User data; OFF by default.
            RetentionTargets.Conversations => new RetentionTargetDefinition(
                Table("conversations"),
                "updated_at < @cutoff",
                "tenant_id = @tenant_id",
                "updated_at",
                "updated_at",
                IdKey),

            // CLOSED connections only. An open connection (ended_at IS NULL) cannot
            // be deleted; when the server crashes the remaining NULL is the trace of
            // a connection that never closed, and the retention policy must not
            // destroy it silently. The row limit query filters out a NULL ended_at
            // (an open connection cannot be a candidate).
            RetentionTargets.VoiceSessions => new RetentionTargetDefinition(
                Table("voice_sessions"),
                "ended_at IS NOT NULL AND ended_at < @cutoff",
                "tenant_id = @tenant_id",
                "started_at",
                "ended_at",
                IdKey),

            // The basis is the creation time of the score itself; because there is no
            // FK to runs (the same reason as the other event/summary tables) no
            // EXISTS is needed.
            RetentionTargets.RunScores => new RetentionTargetDefinition(
                Table("run_scores"),
                "created_at < @cutoff",
                "tenant_id = @tenant_id",
                "created_at",
                "created_at",
                IdKey),

            // Stored idempotency responses (phase 43). The basis is their own
            // creation time; because there is no FK to runs no EXISTS is needed.
            RetentionTargets.IdempotencyKeys => new RetentionTargetDefinition(
                Table("idempotency_keys"),
                "created_at < @cutoff",
                "tenant_id = @tenant_id",
                "created_at",
                "created_at",
                IdempotencyKey,
                TenantInKey: true),

            // Run inputs (phase 47). They carry their own tenant_id and created_at
            // columns; because their FK to runs is CASCADE they already go when the
            // run is deleted, but their own lifetime must also be limitable.
            //
            // 🚨 A run AwaitingInput keeps its row (F-294, K-888): the parameter
            // values it was started with live ONLY here, and answering the request
            // resumes the workflow with them. Deleted by age, the row left a run
            // whose answer is refused (the resume cannot tell "values deleted"
            // from "never had values"). Same rule as the checkpoints: such a row
            // is neither deleted nor counted toward MaxRows.
            RetentionTargets.RunInputs => new RetentionTargetDefinition(
                Table("run_inputs"),
                $"created_at < @cutoff AND NOT EXISTS (SELECT 1 FROM {Table("runs")} ri "
                    + $"WHERE ri.id = {Table("run_inputs")}.run_id AND ri.status = {awaitingInput})",
                "tenant_id = @tenant_id",
                "created_at",
                $"CASE WHEN EXISTS (SELECT 1 FROM {Table("runs")} rr "
                    + $"WHERE rr.id = {Table("run_inputs")}.run_id AND rr.status = {awaitingInput}) "
                    + "THEN NULL ELSE created_at END",
                RunInputKey),

            // The inbox rows of workflow requests (phase 209). A row is OPEN while its run waits for
            // an answer and is the request's entry in the inbox, so it is never old; it is CLOSED when
            // the request expired, and then only its age counts. The row carries a session id, a
            // request id and a workflow name: no prompt and no answer.
            RetentionTargets.WorkflowRequests => new RetentionTargetDefinition(
                Table("workflow_requests"),
                $"status = {workflowRequestClosed} AND closed_at < @cutoff",
                "tenant_id = @tenant_id",
                "closed_at",
                $"CASE WHEN status = {workflowRequestClosed} THEN closed_at ELSE NULL END",
                WorkflowRequestKey),

            // Knowledge base chunks (phase 51). 🚨 The table exists ONLY in the
            // PostgreSQL migration set; binding this target to a policy on SQL
            // Server/SQLite gives a "table does not exist" error at run time — that
            // is deliberate (see docs/arsiv/fazlar/51-VEKTOR-BELLEK-VE-RAG.md, 51.3).
            RetentionTargets.DocumentEmbeddings => new RetentionTargetDefinition(
                Table("document_embeddings"),
                "created_at < @cutoff",
                "tenant_id = @tenant_id",
                "created_at",
                "created_at",
                IdKey),

            _ => throw new ArgumentException($"Unknown retention target: '{target}'.", nameof(target)),
        };
    }
}
