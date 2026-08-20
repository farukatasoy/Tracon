namespace AgentPrism;

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
internal readonly record struct RetentionTargetDefinition(
    string Table,
    string WherePredicate,
    string TenantPredicate,
    string OrderColumn,
    string RowLimitOrderExpression);

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

        return target switch
        {
            RetentionTargets.RunEvents => new RetentionTargetDefinition(
                Table("run_events"),
                "created_at < @cutoff",
                $"EXISTS (SELECT 1 FROM {Table("runs")} rt "
                    + $"WHERE rt.id = {Table("run_events")}.run_id AND rt.tenant_id = @tenant_id)",
                "created_at",
                "created_at"),

            RetentionTargets.ToolInvocations => new RetentionTargetDefinition(
                Table("tool_invocations"),
                "created_at < @cutoff",
                $"EXISTS (SELECT 1 FROM {Table("runs")} rt "
                    + $"WHERE rt.id = {Table("tool_invocations")}.run_id AND rt.tenant_id = @tenant_id)",
                "created_at",
                "created_at"),

            // traces is deleted; spans go with it through ON DELETE CASCADE.
            RetentionTargets.Traces => new RetentionTargetDefinition(
                Table("traces"),
                "started_at < @cutoff",
                "tenant_id = @tenant_id",
                "started_at",
                "started_at"),

            // Finished jobs only (Completed=3, Failed=4, Cancelled=5); job_items go
            // with them through ON DELETE CASCADE. The completed_at of an active job
            // is NULL; the row limit query filters the NULLs out
            // (see SqlDialect.BuildRetentionFindNthRowCutoffSql).
            RetentionTargets.Jobs => new RetentionTargetDefinition(
                Table("jobs"),
                "status IN (3, 4, 5) AND completed_at < @cutoff",
                "tenant_id = @tenant_id",
                "completed_at",
                "completed_at"),

            // Delivered records only (Delivered=1).
            RetentionTargets.WebhookDeliveries => new RetentionTargetDefinition(
                Table("webhook_deliveries"),
                "status = 1 AND delivered_at < @cutoff",
                "tenant_id = @tenant_id",
                "delivered_at",
                "delivered_at"),

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
                $"(SELECT er.completed_at FROM {Table("eval_runs")} er WHERE er.id = {Table("eval_case_results")}.eval_run_id)"),

            // The basis is not the created_at of the checkpoint itself but the
            // completion time of the ASSOCIATED RUN (25.1: "7 days after the run
            // completes"). For THE SAME REASON the MaxRows threshold is computed with
            // a correlated subquery over runs.completed_at (the same pattern as
            // eval_case_results). The correlation is the FULLY QUALIFIED name here as
            // well — the 🚨 note above applies.
            RetentionTargets.WorkflowCheckpoints => new RetentionTargetDefinition(
                Table("workflow_checkpoints"),
                $"EXISTS (SELECT 1 FROM {Table("runs")} r " +
                $"WHERE r.id = {Table("workflow_checkpoints")}.run_id AND r.completed_at < @cutoff)",
                "tenant_id = @tenant_id",
                "created_at",
                $"(SELECT r.completed_at FROM {Table("runs")} r WHERE r.id = {Table("workflow_checkpoints")}.run_id)"),

            // Expired OR revoked grants. The threshold is whichever of the two
            // columns is filled (COALESCE); when both are NULL the row limit query
            // filters the row out (it is not yet a candidate).
            RetentionTargets.SkillScriptGrants => new RetentionTargetDefinition(
                Table("skill_script_grants"),
                "(expires_at IS NOT NULL AND expires_at < @cutoff) " +
                "OR (revoked_at IS NOT NULL AND revoked_at < @cutoff)",
                "tenant_id = @tenant_id",
                "granted_at",
                "COALESCE(expires_at, revoked_at)"),

            // Orphaned: attachments that have no session at all OR whose session no
            // longer exists. The correlation is the FULLY QUALIFIED name here as well
            // — the 🚨 note above applies.
            RetentionTargets.Attachments => new RetentionTargetDefinition(
                Table("attachments"),
                "created_at < @cutoff AND (session_id IS NULL " +
                $"OR NOT EXISTS (SELECT 1 FROM {Table("sessions")} s WHERE s.id = {Table("attachments")}.session_id))",
                "tenant_id = @tenant_id",
                "created_at",
                "created_at"),

            // User data; OFF by default (see AgentPrismRetentionOptions).
            RetentionTargets.Sessions => new RetentionTargetDefinition(
                Table("sessions"),
                "updated_at < @cutoff",
                "tenant_id = @tenant_id",
                "updated_at",
                "updated_at"),

            // conversations is deleted; conversation_items and responses go with it
            // through ON DELETE CASCADE. User data; OFF by default.
            RetentionTargets.Conversations => new RetentionTargetDefinition(
                Table("conversations"),
                "updated_at < @cutoff",
                "tenant_id = @tenant_id",
                "updated_at",
                "updated_at"),

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
                "ended_at"),

            // The basis is the creation time of the score itself; because there is no
            // FK to runs (the same reason as the other event/summary tables) no
            // EXISTS is needed.
            RetentionTargets.RunScores => new RetentionTargetDefinition(
                Table("run_scores"),
                "created_at < @cutoff",
                "tenant_id = @tenant_id",
                "created_at",
                "created_at"),

            // Stored idempotency responses (phase 43). The basis is their own
            // creation time; because there is no FK to runs no EXISTS is needed.
            RetentionTargets.IdempotencyKeys => new RetentionTargetDefinition(
                Table("idempotency_keys"),
                "created_at < @cutoff",
                "tenant_id = @tenant_id",
                "created_at",
                "created_at"),

            // Run inputs (phase 47). They carry their own tenant_id and created_at
            // columns; because their FK to runs is CASCADE they already go when the
            // run is deleted, but their own lifetime must also be limitable.
            RetentionTargets.RunInputs => new RetentionTargetDefinition(
                Table("run_inputs"),
                "created_at < @cutoff",
                "tenant_id = @tenant_id",
                "created_at",
                "created_at"),

            // Knowledge base chunks (phase 51). 🚨 The table exists ONLY in the
            // PostgreSQL migration set; binding this target to a policy on SQL
            // Server/SQLite gives a "table does not exist" error at run time — that
            // is deliberate (see docs/51-VEKTOR-BELLEK-VE-RAG.md, 51.3).
            RetentionTargets.DocumentEmbeddings => new RetentionTargetDefinition(
                Table("document_embeddings"),
                "created_at < @cutoff",
                "tenant_id = @tenant_id",
                "created_at",
                "created_at"),

            _ => throw new ArgumentException($"Unknown retention target: '{target}'.", nameof(target)),
        };
    }
}
