namespace AgentPrism;

/// <summary>
/// The provider-independent surface of the SQL texts built for a schema name.
/// </summary>
/// <remarks>
/// <para>
/// This class declares only <em>which</em> queries exist; the provider subclasses
/// write the texts (<c>PostgresQueries</c>, <c>SqlServerQueries</c>). The shared
/// store code therefore depends on a single surface and the dialect difference
/// stays enclosed inside the SQL text.
/// </para>
/// <para>
/// <strong>When a new query is added here, its counterpart must be written in
/// every subclass.</strong> If it is not, the field stays <c>string.Empty</c> and
/// the error appears at run time only. The phase that adds a new query must verify
/// that the contract test runs on every provider.
/// </para>
/// <para>
/// The schema name is an identifier and cannot be sent as a parameter; it is placed
/// into the SQL text directly. The name is used only after
/// <see cref="SqlIdentifier.RequireSchemaName"/> validates it strictly.
/// </para>
/// <para>
/// Every query text is built once in the constructor and kept in a field; nothing
/// is concatenated again on each call.
/// </para>
/// </remarks>
internal abstract class SqlQueriesBase
{
    /// <summary>Creates a new query set for the given schema/prefix and builds the shared queries.</summary>
    /// <param name="schemaName">The schema name (SQL Server, PostgreSQL) or table prefix (SQLite) to validate.</param>
    /// <exception cref="AgentPrismException">The name is not a valid identifier.</exception>
    /// <remarks>
    /// <see cref="Schema"/> is assigned before the shared queries are built, since
    /// building them calls the (possibly overridden) <see cref="Table"/>, which
    /// reads <see cref="Schema"/>. A derived constructor must not assign
    /// <see cref="Schema"/> again; it is set here once.
    /// </remarks>
    protected SqlQueriesBase(string schemaName)
    {
        Schema = SqlIdentifier.RequireSchemaName(schemaName);
        BuildSharedQueries();
    }

    /// <summary>Qualifies a bare table name for this provider.</summary>
    /// <param name="name">The table name without schema or prefix.</param>
    /// <returns>The qualified name, ready to embed into runnable SQL.</returns>
    /// <remarks>
    /// PostgreSQL and SQL Server join with a dot; SQLite concatenates the prefix
    /// directly, since its object names share a single namespace across the
    /// database. The SQLite query set overrides this.
    /// </remarks>
    protected virtual string Table(string name) => $"{Schema}.{name}";

    /// <summary>Qualifies a bare table name for this provider.</summary>
    /// <param name="tableName">The table name without schema or prefix (for example <c>"sessions"</c>).</param>
    /// <returns>The qualified name, ready to embed into runnable SQL.</returns>
    /// <remarks>
    /// The public gateway <see cref="SqlDialect.QualifyTable"/> uses this for the
    /// provider-independent SQL text built outside this class (for example
    /// <see cref="RetentionTargetRegistry"/>).
    /// </remarks>
    public string QualifyTable(string tableName) => Table(tableName);

    /// <summary>
    /// The addends of a cost total, in order. A new cost term is added HERE and
    /// nowhere else: a cost total that reaches the SQL text but not the addend
    /// list here silently under-reports. <c>RunCost.Total()</c>, in
    /// <c>AgentPrism.Core</c>, is the C# twin of this list.
    /// </summary>
    protected static readonly string[] CostAddends = ["input_cost", "output_cost", "cached_input_cost"];

    /// <summary>Builds the null-preserving sum of <see cref="CostAddends"/> over the current aggregate.</summary>
    /// <param name="alias">The table alias to qualify each addend with, or <see langword="null"/> for none.</param>
    /// <returns>The sum expression, without the surrounding <c>CASE WHEN ... END</c> null guard.</returns>
    protected static string CostTotal(string? alias = null)
    {
        var prefix = alias is null ? string.Empty : $"{alias}.";

        return string.Join(" + ", CostAddends.Select(addend => $"COALESCE(SUM({prefix}{addend}), 0)"));
    }

    /// <summary>Counts the rows in which any of <paramref name="columns"/> is not null.</summary>
    /// <param name="columns">The columns to test.</param>
    /// <param name="alias">The table alias to qualify each column with, or <see langword="null"/> for none.</param>
    /// <returns>A scalar aggregate expression, for the <see cref="CostTotal"/> null guard.</returns>
    /// <remarks>SQL Server has no <c>FILTER</c> clause and overrides this.</remarks>
    protected virtual string CountWhereAnyNotNull(IReadOnlyList<string> columns, string? alias)
    {
        var prefix = alias is null ? string.Empty : $"{alias}.";
        var condition = string.Join(" OR ", columns.Select(column => $"{prefix}{column} IS NOT NULL"));

        return $"COUNT(*) FILTER (WHERE {condition})";
    }

    /// <summary>Builds the sum of a single token-tree column.</summary>
    /// <param name="column">The column to sum.</param>
    /// <param name="alias">The table alias to qualify the column with, or <see langword="null"/> for none.</param>
    /// <param name="coalesceToZero">
    /// Whether an empty group reads as <c>0</c>. <see langword="false"/> for a column whose
    /// zero and "not reported" states must stay distinguishable — a descendant tree in which
    /// nobody reported cache usage must read as "not measured", not "measured zero".
    /// </param>
    /// <returns>The sum expression.</returns>
    protected static string TreeSum(string column, string? alias, bool coalesceToZero)
    {
        var prefix = alias is null ? string.Empty : $"{alias}.";
        var sum = $"SUM({prefix}{column})";

        return coalesceToZero ? $"COALESCE({sum}, 0)" : sum;
    }

    /// <summary>
    /// The <c>runs</c> reader columns, in ordinal order. A new column is APPENDED,
    /// never inserted: <c>SqlRunStore</c> reads the <c>SelectRun</c>/<c>SelectRuns</c>
    /// result by ordinal, through the named constants in <c>RunOrdinals</c>, and a
    /// test cross-checks that every ordinal from <c>0</c> to <c>Length - 1</c> is
    /// named exactly once. Each entry names the value and where it comes from
    /// (<see cref="RunColumnSource.Own"/> — the run's own row, or
    /// <see cref="RunColumnSource.Tree"/> — an aggregate over its descendant tree).
    /// This list is the single record of the ordinal SEQUENCE; it does not generate
    /// the SQL text — each dialect still writes its own <c>runColumns</c>, because a
    /// <c>Tree</c> entry resolves through a join on PostgreSQL/SQL Server and through
    /// a correlated subquery on SQLite (no <c>treeJoin</c> there). Appending a
    /// column therefore still means updating four places (the three <c>runColumns</c>
    /// texts and this list); the difference is only that a mismatch is a build-time
    /// test failure instead of a run-time "wrong value" that reaches the API surface.
    /// </summary>
    protected static readonly RunColumn[] RunColumnOrder =
    [
        new("id", RunColumnSource.Own),
        new("tenant_id", RunColumnSource.Own),
        new("agent_name", RunColumnSource.Own),
        new("session_id", RunColumnSource.Own),
        new("status", RunColumnSource.Own),
        new("started_at", RunColumnSource.Own),
        new("completed_at", RunColumnSource.Own),
        new("is_streaming", RunColumnSource.Own),
        new("input_tokens", RunColumnSource.Own),
        new("output_tokens", RunColumnSource.Own),
        new("total_tokens", RunColumnSource.Own),
        new("event_count", RunColumnSource.Own),
        new("error_type", RunColumnSource.Own),
        new("error_message", RunColumnSource.Own),
        new("model_id", RunColumnSource.Own),
        new("parent_run_id", RunColumnSource.Own),
        new("root_run_id", RunColumnSource.Own),
        new("depth", RunColumnSource.Own),
        new("child_count", RunColumnSource.Tree),
        new("input_tokens", RunColumnSource.Tree),
        new("output_tokens", RunColumnSource.Tree),
        new("total_tokens", RunColumnSource.Tree),
        new("usage_rows", RunColumnSource.Tree),
        new("kind", RunColumnSource.Own),
        new("workflow_name", RunColumnSource.Own),
        new("agent_version", RunColumnSource.Own),
        new("experiment_id", RunColumnSource.Own),
        new("variant", RunColumnSource.Own),
        new("input_cost", RunColumnSource.Own),
        new("output_cost", RunColumnSource.Own),
        new("cost_currency", RunColumnSource.Own),
        new("pricing_source", RunColumnSource.Own),
        new("cost_input", RunColumnSource.Tree),
        new("cost_output", RunColumnSource.Tree),
        new("cost_currency", RunColumnSource.Tree),
        new("unknown_pricing_rows", RunColumnSource.Tree),
        new("pricing_rows", RunColumnSource.Tree),
        new("error_class", RunColumnSource.Own),
        new("error_fingerprint", RunColumnSource.Own),
        new("replay_of_run_id", RunColumnSource.Own),
        new("user_id", RunColumnSource.Own),
        new("labels", RunColumnSource.Own),
        new("cached_input_tokens", RunColumnSource.Own),
        new("reasoning_tokens", RunColumnSource.Own),
        new("audio_input_tokens", RunColumnSource.Own),
        new("audio_output_tokens", RunColumnSource.Own),
        new("cached_input_cost", RunColumnSource.Own),
        new("cached_input_tokens", RunColumnSource.Tree),
        new("reasoning_tokens", RunColumnSource.Tree),
        new("audio_input_tokens", RunColumnSource.Tree),
        new("audio_output_tokens", RunColumnSource.Tree),
        new("cost_cached_input", RunColumnSource.Tree),
        new("continued_from_run_id", RunColumnSource.Own),
        new("model_provider", RunColumnSource.Own),
        new("input_price_per_mtok", RunColumnSource.Own),
        new("output_price_per_mtok", RunColumnSource.Own),
        new("cached_input_price_per_mtok", RunColumnSource.Own),
    ];

    /// <summary>Gets the query that inserts a tool invocation record.</summary>
    public string InsertToolInvocation { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the tool invocations of a run.</summary>
    public string SelectToolInvocations { get; protected set; } = string.Empty;

    /// <summary>Gets the query that produces the usage summary per tool.</summary>
    public string SelectToolUsage { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists every experiment of the tenant.</summary>
    public string SelectExperiments { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the named experiment.</summary>
    public string SelectExperiment { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the Running experiment of an agent.</summary>
    public string SelectRunningExperiment { get; protected set; } = string.Empty;

    /// <summary>Gets the query that creates an experiment or updates it (only while it is Draft).</summary>
    public string UpsertExperiment { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes an experiment (only when it is not Running).</summary>
    public string DeleteExperiment { get; protected set; } = string.Empty;

    /// <summary>Gets the query that moves an experiment into the Running state.</summary>
    public string StartExperiment { get; protected set; } = string.Empty;

    /// <summary>Gets the query that moves an experiment into the Stopped state.</summary>
    public string StopExperiment { get; protected set; } = string.Empty;

    /// <summary>Gets the query that produces the per-arm run results of an experiment.</summary>
    public string SelectExperimentResults { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the experiments across all tenants that are Running AND have a canary policy.</summary>
    public string SelectRunningExperimentsWithCanary { get; protected set; } = string.Empty;

    /// <summary>Gets the query that sets or clears the canary policy of an experiment.</summary>
    public string SetExperimentCanaryPolicy { get; protected set; } = string.Empty;

    /// <summary>Gets the query that applies a canary ramp step (weight only, the experiment stays Running).</summary>
    public string AdvanceExperimentCanaryRamp { get; protected set; } = string.Empty;

    /// <summary>Gets the query that applies an automatic canary rollback (weight + Stopped + reason).</summary>
    public string RollbackExperimentCanary { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts or updates the trace header and returns its identifier.</summary>
    public string UpsertTrace { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts or updates a span.</summary>
    public string UpsertSpan { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the trace header of a run.</summary>
    public string SelectTraceByRun { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the spans of a trace.</summary>
    public string SelectSpans { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the approval rules of a tenant.</summary>
    public string SelectToolApprovalRules { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that inserts an approval rule; it returns the existing record
    /// when the same scope is present.
    /// </summary>
    public string InsertToolApprovalRule { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes an approval rule.</summary>
    public string DeleteToolApprovalRule { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the MCP servers of a tenant.</summary>
    public string SelectMcpServers { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a single MCP server.</summary>
    public string SelectMcpServer { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts or updates an MCP server.</summary>
    public string UpsertMcpServer { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes an MCP server.</summary>
    public string DeleteMcpServer { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the registered tenants.</summary>
    public string SelectTenants { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts or updates a tenant record.</summary>
    public string UpsertTenantDescriptor { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes a tenant record.</summary>
    public string DeleteTenant { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts a new attachment.</summary>
    public string InsertAttachment { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the metadata of an attachment.</summary>
    public string SelectAttachment { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the raw content of an attachment.</summary>
    public string SelectAttachmentContent { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the attachments with a filter.</summary>
    public string SelectAttachments { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes an attachment and returns its external store location.</summary>
    public string DeleteAttachment { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes every attachment of a session.</summary>
    public string DeleteAttachmentsBySession { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the content of a persistent agent file.</summary>
    public string SelectAgentFile { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts or updates a persistent agent file.</summary>
    public string UpsertAgentFile { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes a persistent agent file.</summary>
    public string DeleteAgentFile { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that reads the files of an agent, narrowed down in SQL by a
    /// path prefix, an optional depth limit, an optional glob filter and (on
    /// PostgreSQL only) an optional regex prefilter (item A). It replaces
    /// the earlier <c>SelectAgentFiles</c> (which loaded every file into memory);
    /// the prefix is always supplied (<c>"/"</c> for the root directory), so no
    /// separate "fetch everything" query was needed.
    /// </summary>
    public string SelectAgentFilesFiltered { get; protected set; } = string.Empty;

    /// <summary>Gets the query that saves a workflow definition and increments its version.</summary>
    public string UpsertWorkflow { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a single workflow definition.</summary>
    public string SelectWorkflow { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the workflow definitions of a tenant.</summary>
    public string SelectWorkflows { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes a workflow definition.</summary>
    public string DeleteWorkflow { get; protected set; } = string.Empty;

    /// <summary>Gets the query that writes a checkpoint.</summary>
    public string InsertWorkflowCheckpoint { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the state of a single checkpoint.</summary>
    public string SelectWorkflowCheckpoint { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the checkpoint metadata of a session.</summary>
    public string SelectWorkflowCheckpoints { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the checkpoint metadata of a run.</summary>
    public string SelectWorkflowCheckpointsByRun { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes every checkpoint of a session.</summary>
    public string DeleteWorkflowCheckpoints { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts an audit log record.</summary>
    public string InsertAuditEntry { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the audit log records with a filter.</summary>
    public string SelectAuditLog { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the hash of a tenant's newest audit log record.</summary>
    public string SelectLastAuditHash { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a tenant's audit log records, oldest first, for hash chain verification.</summary>
    public string SelectAuditChain { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts or updates a schedule.</summary>
    public string UpsertJobSchedule { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a single schedule.</summary>
    public string SelectJobSchedule { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the schedules of a tenant.</summary>
    public string SelectJobSchedules { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the due schedules of every tenant.</summary>
    public string SelectDueJobSchedules { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes a schedule.</summary>
    public string DeleteJobSchedule { get; protected set; } = string.Empty;

    /// <summary>Gets the query that tries to advance the next run time of a schedule atomically.</summary>
    public string TryClaimJobScheduleNextRun { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts or updates an inbound trigger.</summary>
    public string UpsertInboundTrigger { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a single inbound trigger.</summary>
    public string SelectInboundTrigger { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the inbound triggers of a tenant.</summary>
    public string SelectInboundTriggers { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes an inbound trigger.</summary>
    public string DeleteInboundTrigger { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts a new job.</summary>
    public string InsertJob { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts the items of a job in bulk.</summary>
    public string InsertJobItems { get; protected set; } = string.Empty;

    /// <summary>Gets the query that leases the oldest job ready to run, with <c>FOR UPDATE SKIP LOCKED</c>.</summary>
    public string LeaseJob { get; protected set; } = string.Empty;

    /// <summary>Gets the query that extends the lease of a running job.</summary>
    public string RenewJobLease { get; protected set; } = string.Empty;

    /// <summary>Gets the query that moves a leased job into the running state.</summary>
    public string MarkJobRunning { get; protected set; } = string.Empty;

    /// <summary>Gets the query that finishes a job.</summary>
    public string CompleteJob { get; protected set; } = string.Empty;

    /// <summary>Gets the query that puts a job back into the queue for a retry.</summary>
    public string ReleaseJobForRetry { get; protected set; } = string.Empty;

    /// <summary>Gets the query that tries to cancel a job.</summary>
    public string CancelJob { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a single job record.</summary>
    public string SelectJob { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the jobs with a filter.</summary>
    public string SelectJobs { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the items of a job.</summary>
    public string SelectJobItems { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reports the outcome of a job item and updates the job counters.</summary>
    public string ReportJobItem { get; protected set; } = string.Empty;

    /// <summary>Gets the query that counts the OPEN jobs, grouped by lane and status.</summary>
    public string SelectJobQueueDepth { get; protected set; } = string.Empty;

    /// <summary>Gets the validated schema name.</summary>
    public string Schema { get; private set; } = string.Empty;

    /// <summary>Gets the statement that creates the schema.</summary>
    public string CreateSchema { get; protected set; } = string.Empty;

    /// <summary>Gets the statement that creates the migration ledger.</summary>
    public string CreateMigrationsTable { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the statement that upgrades a pre-phase-67 ledger (no <c>set_name</c>
    /// column) to the current shape. Empty when the provider does this in code
    /// instead (see <see cref="SqlDialect.UpgradeMigrationsTableAsync"/>).
    /// </summary>
    public string UpgradeMigrationsTable { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the applied migrations.</summary>
    public string SelectAppliedMigrations { get; protected set; } = string.Empty;

    /// <summary>Gets the query that writes an applied migration into the ledger.</summary>
    public string InsertMigration { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts the tenant record when it is absent.</summary>
    public string UpsertTenant { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts an agent definition or increments its version.</summary>
    public string UpsertAgentDefinition { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts the immutable version record of a definition.</summary>
    public string InsertAgentDefinitionVersion { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the current version of a definition.</summary>
    public string SelectAgentDefinition { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads every definition of the tenant.</summary>
    public string SelectAgentDefinitions { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes a definition and its history.</summary>
    public string DeleteAgentDefinition { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads every version of a definition.</summary>
    public string SelectAgentDefinitionVersions { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a specific version of a definition.</summary>
    public string SelectAgentDefinitionVersion { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the skills of the tenant.</summary>
    public string SelectAgentSkills { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a single skill.</summary>
    public string SelectAgentSkill { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts or updates a skill.</summary>
    public string UpsertAgentSkill { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes a skill.</summary>
    public string DeleteAgentSkill { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes the resources of a skill.</summary>
    public string DeleteAgentSkillResources { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts a skill resource.</summary>
    public string InsertAgentSkillResource { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the skill resources.</summary>
    public string SelectAgentSkillResources { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes every script of a skill.</summary>
    public string DeleteAgentSkillScripts { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts a skill script.</summary>
    public string InsertAgentSkillScript { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the scripts of a skill.</summary>
    public string SelectAgentSkillScripts { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads every script execution grant of a tenant.</summary>
    public string SelectSkillScriptGrants { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the active grant for a specific script.</summary>
    public string SelectActiveSkillScriptGrant { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts or renews a script execution grant.</summary>
    public string UpsertSkillScriptGrant { get; protected set; } = string.Empty;

    /// <summary>Gets the query that revokes a script execution grant.</summary>
    public string RevokeSkillScriptGrant { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts or updates a session.</summary>
    public string UpsertSession { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that inserts a new session ONLY when it is absent. It is a
    /// plain <c>INSERT</c>; a concurrent second call with the same (tenant_id, id)
    /// hits a unique violation and is caught with
    /// <see cref="SqlDialect.IsUniqueViolation"/> — the same pattern as
    /// <see cref="InsertIdempotencyKey"/>.
    /// the unconditional overwrite of <see cref="UpsertSession"/> made two
    /// concurrent first requests to the same NEW session produce a different
    /// conversation identifier each, and the messages of the loser stayed silently
    /// unreachable.
    /// </summary>
    public string InsertSession { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a session.</summary>
    public string SelectSession { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that replaces a session row only when its stored
    /// <c>version</c> still matches the caller's. The sibling of
    /// <see cref="InsertSession"/>: that one makes the FIRST write safe
    /// against a concurrent writer, this one makes every LATER write safe.
    /// <see cref="UpsertSession"/> is unconditional and does neither.
    /// </summary>
    public string UpdateSessionIfVersionMatches { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that reads the tenant owning a session identifier, WITHOUT
    /// applying the tenant filter.
    /// the cross-tenant ownership check cannot use
    /// <see cref="SelectSession"/> — that one is already filtered by tenant and never
    /// sees the record of another tenant.
    /// </summary>
    public string SelectSessionOwner { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes a session.</summary>
    public string DeleteSession { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the sessions with a filter.</summary>
    public string SelectSessions { get; protected set; } = string.Empty;

    /// <summary>Gets the query that opens a new run record.</summary>
    public string InsertRun { get; protected set; } = string.Empty;

    /// <summary>Gets the query that finishes a run.</summary>
    public string UpdateRunCompletion { get; protected set; } = string.Empty;

    /// <summary>Gets the query that updates the cost of a run (the maintenance path only).</summary>
    public string UpdateRunCost { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that writes the heartbeat mark of a running run. It
    /// affects <c>Running</c> rows only.
    /// </summary>
    public string TouchRunHeartbeat { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that closes the top N <c>Running</c> rows past the heartbeat
    /// threshold as <c>Failed</c> and returns the closed rows.
    /// </summary>
    public string ClaimOrphanedRuns { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that inserts the <c>RunFailed</c> event reporting the closure
    /// of an orphaned run; the sequence number is one more than the current maximum.
    /// </summary>
    public string InsertOrphanRunEvent { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a run.</summary>
    public string SelectRun { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the runs with a filter.</summary>
    public string SelectRuns { get; protected set; } = string.Empty;

    /// <summary>Gets the query that returns the run summary and the per-agent breakdown as two result sets.</summary>
    public string SelectRunStatistics { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query for the per-bucket run, error, token and cost time series. Empty
    /// buckets are returned as well.
    /// </summary>
    public string SelectRunTimeSeries { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts a run event.</summary>
    public string InsertRunEvent { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the run events by sequence number.</summary>
    public string SelectRunEvents { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts or updates a conversation.</summary>
    public string UpsertConversation { get; protected set; } = string.Empty;

    /// <summary>Gets the query that returns the next sequence number in a conversation.</summary>
    public string SelectNextConversationSequence { get; protected set; } = string.Empty;

    /// <summary>Gets the query that adds a message to a conversation.</summary>
    public string InsertConversationItem { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the messages of a conversation in order.</summary>
    public string SelectConversationItems { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that measures the branch point: the last sequence number to
    /// copy and the item count. When there is no item at all the sequence
    /// number is <c>-1</c>.
    /// </summary>
    public string SelectConversationBranchPoint { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that opens a new branch conversation by copying the metadata of
    /// the source conversation. When the source is absent or belongs to
    /// another tenant, no row is written.
    /// </summary>
    public string InsertBranchConversation { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that reads the items to copy while branching, in order.
    /// </summary>
    public string SelectConversationItemsForBranch { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that writes the input messages of a run. A second
    /// write for the same run is <strong>ignored</strong>.
    /// </summary>
    public string InsertRunInput { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the stored input of a run.</summary>
    public string SelectRunInput { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the eval suites of a tenant.</summary>
    public string SelectEvalSuites { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a single eval suite.</summary>
    public string SelectEvalSuite { get; protected set; } = string.Empty;

    /// <summary>Gets the query that creates or updates an eval suite.</summary>
    public string UpsertEvalSuite { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes an eval suite (the cases and runs are deleted by cascade).</summary>
    public string DeleteEvalSuite { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the cases of a suite by sequence number.</summary>
    public string SelectEvalCases { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes every case of a suite (before the replacements are written).</summary>
    public string DeleteEvalCases { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts an eval case.</summary>
    public string InsertEvalCase { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that inserts a SINGLE eval case into a suite, computing
    /// <c>seq</c> atomically (promotion from production).
    /// </summary>
    public string InsertEvalCaseWithComputedSeq { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the case promoted from a given source run in a suite.</summary>
    public string SelectEvalCaseBySourceRun { get; protected set; } = string.Empty;

    /// <summary>Gets the query that opens a new eval run record.</summary>
    public string InsertEvalRun { get; protected set; } = string.Empty;

    /// <summary>Gets the query that moves the run into the running state and writes the measured version and model.</summary>
    public string MarkEvalRunRunning { get; protected set; } = string.Empty;

    /// <summary>Gets the query that finishes the run and writes the summary counters.</summary>
    public string CompleteEvalRun { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads an eval run.</summary>
    public string SelectEvalRun { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the eval run produced by a job record.</summary>
    public string SelectEvalRunByJobId { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the eval runs with a filter.</summary>
    public string SelectEvalRuns { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts an eval case result.</summary>
    public string InsertEvalCaseResult { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the case results of a run.</summary>
    public string SelectEvalCaseResults { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts or updates a quota rule (on a scope conflict).</summary>
    public string UpsertQuota { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the quota rules of a tenant.</summary>
    public string SelectQuotas { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a single quota rule.</summary>
    public string SelectQuota { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes a quota rule.</summary>
    public string DeleteQuota { get; protected set; } = string.Empty;

    /// <summary>Gets the query that increments the quota consumption atomically.</summary>
    public string AddQuotaUsage { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the quota consumption counters.</summary>
    public string SelectQuotaUsage { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that atomically claims a quota threshold notification —
    /// appends the threshold key to <c>notified_thresholds</c> only if it is
    /// not already present, and only if the usage row exists. The affected
    /// row count (0 or 1) reports whether this call won the claim.
    /// </summary>
    public string TryClaimQuotaThresholdNotification { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts or updates a webhook subscription.</summary>
    public string UpsertWebhookSubscription { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the webhook subscriptions of a tenant.</summary>
    public string SelectWebhookSubscriptions { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a single webhook subscription by name.</summary>
    public string SelectWebhookSubscription { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the enabled subscriptions of a specific event.</summary>
    public string SelectWebhookSubscriptionsForEvent { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes a webhook subscription.</summary>
    public string DeleteWebhookSubscription { get; protected set; } = string.Empty;

    /// <summary>Gets the query that updates the failure counter of a subscription and disables it at the threshold.</summary>
    public string UpdateWebhookSubscriptionOutcome { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts a webhook delivery record.</summary>
    public string InsertWebhookDelivery { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a single delivery record.</summary>
    public string SelectWebhookDelivery { get; protected set; } = string.Empty;

    /// <summary>Gets the query that writes the outcome of a delivery attempt.</summary>
    public string UpdateWebhookDeliveryResult { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the delivery history with a filter.</summary>
    public string SelectWebhookDeliveries { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts a new API key.</summary>
    public string InsertApiKey { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the API keys of a tenant.</summary>
    public string SelectApiKeys { get; protected set; } = string.Empty;

    /// <summary>Gets the query that looks an API key up by its hash. There is NO tenant filter.</summary>
    public string SelectApiKeyByHash { get; protected set; } = string.Empty;

    /// <summary>Gets the query that revokes an API key within the tenant boundary.</summary>
    public string RevokeApiKey { get; protected set; } = string.Empty;

    /// <summary>Gets the query that updates the last-used stamp of an API key.</summary>
    public string TouchApiKeyLastUsed { get; protected set; } = string.Empty;

    /// <summary>Gets the query that determines whether the system holds a valid key carrying the given scope.</summary>
    public string HasApiKeyWithScope { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts or updates a retention policy (on a scope conflict).</summary>
    public string UpsertRetentionPolicy { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the retention policies of a tenant.</summary>
    public string SelectRetentionPolicies { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the policy of a single target inside the tenant.</summary>
    public string SelectRetentionPolicy { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes a retention policy.</summary>
    public string DeleteRetentionPolicy { get; protected set; } = string.Empty;

    /// <summary>Gets the query that opens a new cleanup run.</summary>
    public string InsertRetentionRun { get; protected set; } = string.Empty;

    /// <summary>Gets the query that increments the counters of a running cleanup run atomically.</summary>
    public string UpdateRetentionRunProgress { get; protected set; } = string.Empty;

    /// <summary>Gets the query that finishes a cleanup run.</summary>
    public string CompleteRetentionRun { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the cleanup run history with a filter.</summary>
    public string SelectRetentionRuns { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts or updates the summary record of a voice connection.</summary>
    public string UpsertVoiceSession { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the voice records from newest to oldest.</summary>
    public string SelectVoiceSessions { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that inserts a run/message score or updates it (when the author
    /// and target are the same).
    /// </summary>
    public string UpsertRunScore { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists every score of a run.</summary>
    public string SelectRunScores { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes a score.</summary>
    public string DeleteRunScore { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that acquires the singleton lease (it inserts when the row is
    /// absent, and updates when the owner or the expiry allows it).
    /// </summary>
    public string AcquireSingletonLease { get; protected set; } = string.Empty;

    /// <summary>Gets the query that extends a held singleton lease.</summary>
    public string RenewSingletonLease { get; protected set; } = string.Empty;

    /// <summary>Gets the query that releases the singleton lease.</summary>
    public string ReleaseSingletonLease { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that tries to insert an idempotency key as <c>Reserved</c>.
    /// When the key already exists it throws a violation that is caught with
    /// <see cref="SqlDialect.IsUniqueViolation"/>; the caller then reads the existing
    /// record with <see cref="SelectIdempotencyKey"/>.
    /// </summary>
    public string InsertIdempotencyKey { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads an idempotency key by tenant and key.</summary>
    public string SelectIdempotencyKey { get; protected set; } = string.Empty;

    /// <summary>Gets the query that turns a reserved idempotency key into <c>Completed</c> and writes the response.</summary>
    public string CompleteIdempotencyKey { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes an idempotency key (releasing it after a failed request).</summary>
    public string DeleteIdempotencyKey { get; protected set; } = string.Empty;

    /// <summary>Gets the query that inserts a new pending approval request.</summary>
    public string InsertPendingApproval { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists the pending requests of the caller tenant from oldest to newest.</summary>
    public string SelectPendingApprovals { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a single pending approval request, restricted to the tenant.</summary>
    public string SelectPendingApproval { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that writes a decision on a pending request. The <c>WHERE</c>
    /// targets only the row that is still <c>Pending</c>; a second decision affects 0
    /// rows.
    /// </summary>
    public string DecidePendingApproval { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that closes the expired requests that are still <c>Pending</c>
    /// as <c>Expired</c> and returns the closed rows (THE SAME pattern as
    /// <c>ClaimOrphanedRuns</c>). It carries NO tenant filter — it is a maintenance
    /// operation.
    /// </summary>
    public string ExpirePendingApprovals { get; protected set; } = string.Empty;

    /// <summary>Gets the query that creates or replaces a tenant's provider binding (BYOK).</summary>
    public string UpsertTenantProviderBinding { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a single tenant provider binding.</summary>
    public string SelectTenantProviderBinding { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists every provider binding of a tenant.</summary>
    public string SelectTenantProviderBindings { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes a tenant provider binding.</summary>
    public string DeleteTenantProviderBinding { get; protected set; } = string.Empty;

    /// <summary>Gets the query that creates or replaces a tenant's egress policy.</summary>
    public string UpsertTenantEgressPolicy { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a tenant's egress policy.</summary>
    public string SelectTenantEgressPolicy { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes a tenant's egress policy.</summary>
    public string DeleteTenantEgressPolicy { get; protected set; } = string.Empty;

    /// <summary>Replaces the schema placeholder in the embedded migration text with the real name.</summary>
    /// <param name="sql">The raw migration text.</param>
    /// <returns>Runnable SQL.</returns>
    public string ApplySchema(string sql)
        => sql.Replace(SchemaPlaceholder, Schema, StringComparison.Ordinal);

    /// <summary>The marker that stands in for the schema name in the embedded SQL files.</summary>
    public const string SchemaPlaceholder = "{schema}";

    // --- Shared column lists (phase 94): identical text across all three
    //     providers, used by both the queries built here and the dialect's own.
    protected const string McpServerColumns = """
            id, tenant_id, name, description, endpoint, transport,
            authorization_configuration_key, headers, enabled, requires_approval, created_at, updated_at,
            oauth_enabled, oauth_client_id, oauth_client_secret_configuration_key, oauth_scopes, oauth_authorization_mode
            """;

    protected const string ScheduleColumns = """
            id, tenant_id, name, handler_key, target_name, cron, time_zone, payload, enabled,
            next_run_at, last_run_at, created_by, created_at, updated_at, lane
            """;

    protected const string InboundTriggerColumns = """
            id, tenant_id, name, target_kind, target_name, signing_secret_configuration_name,
            payload_mode, payload_path, enabled, created_at, updated_at
            """;

    protected const string JobColumns = """
            id, tenant_id, schedule_id, handler_key, target_name, status, payload, total_items, done_items,
            failed_items, attempt, lease_owner, lease_until, scheduled_for, started_at, completed_at,
            error_message, created_at, max_attempts, lane
            """;

    // 🚨 Derived, never retyped. SQL Server's lease query spelled this list out
    // by hand, so renaming a column in JobColumns left its OUTPUT clause
    // pointing at the old name -- a drift no compiler, and no SQL snapshot of
    // the OTHER two providers, could see. The reader maps by ORDINAL, so the
    // two lists must also stay in the same ORDER, which deriving guarantees.
    /// <summary>
    /// <see cref="JobColumns"/> with every name qualified by <c>inserted.</c>,
    /// for T-SQL's <c>OUTPUT</c> clause.
    /// </summary>
    protected static string InsertedJobColumns { get; } = Qualify(JobColumns, "inserted.");

    /// <summary>Prefixes every comma-separated name in a column list.</summary>
    /// <param name="columns">The column list.</param>
    /// <param name="prefix">The qualifier to prepend, including its dot.</param>
    /// <returns>The qualified list, line breaks preserved.</returns>
    private static string Qualify(string columns, string prefix)
        => string.Join(
            ",",
            columns.Split(',').Select(part =>
            {
                var trimmed = part.TrimStart();
                var leading = part[..^trimmed.Length];

                return leading + prefix + trimmed;
            }));

    protected const string SuiteColumns = """
            id, tenant_id, name, description, agent_name, checks, created_at, updated_at
            """;

    protected const string EvalCaseColumns = """
            id, suite_id, seq, query, expected_output, expected_tools, context,
            source_run_id, source_kind, promoted_at, parameters
            """;

    protected const string EvalRunColumns = """
            id, tenant_id, suite_id, job_id, agent_version, model_id, status, total, passed, failed,
            input_tokens, output_tokens, started_at, completed_at
            """;

    protected const string QuotaColumns = """
            id, tenant_id, agent_name, period, max_runs, max_tokens, max_cost, enabled,
            created_at, updated_at
            """;

    protected const string WebhookSubscriptionColumns = """
            id, tenant_id, name, url, events, secret_configuration_key, headers, enabled,
            consecutive_failures, created_at, updated_at
            """;

    protected const string WebhookDeliveryColumns = """
            id, subscription_id, tenant_id, event_type, payload, status, attempt, response_code,
            error, created_at, delivered_at
            """;

    protected const string ApiKeyColumns = """
            id, tenant_id, name, key_hash, key_prefix, scopes, expires_at, revoked_at,
            last_used_at, created_at
            """;

    protected const string RetentionPolicyColumns =
         "id, tenant_id, target, max_age_days, max_rows, archive, enabled, created_at, updated_at";

    protected const string RetentionRunColumns =
         "id, tenant_id, target, deleted_rows, archived_rows, started_at, completed_at, error";

    protected const string RunScoreColumns =
         "id, tenant_id, run_id, message_id, kind, value, comment, source, author, created_at";

    protected const string TenantProviderBindingColumns =
         "tenant_id, provider_name, api_key_configuration_name, endpoint, updated_at";

    /// <summary>
    /// Builds the queries whose resolved text is identical across all three
    /// providers once the schema qualifier is abstracted through
    /// <see cref="Table"/>. The queries that genuinely differ (including a few
    /// that look identical in source but differ only in whitespace once
    /// resolved) stay in the provider's own constructor, unchanged.
    /// </summary>
    private void BuildSharedQueries()
    {
        SelectAppliedMigrations = $"SELECT set_name, id, name, checksum FROM {Table("__migrations")} ORDER BY set_name, id;";

        InsertMigration = $"""
            INSERT INTO {Table("__migrations")} (set_name, id, name, checksum, applied_at)
            VALUES (@set_name, @id, @name, @checksum, @applied_at);
            """;

        InsertAgentDefinitionVersion = $"""
            INSERT INTO {Table("agent_definition_versions")} (id, agent_id, version, definition, created_by, created_at)
            VALUES (@id, @agent_id, @version, @definition, @created_by, @created_at);
            """;

        SelectAgentDefinition = $"""
            SELECT definition, version, updated_at
            FROM {Table("agent_definitions")}
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        SelectAgentDefinitions = $"""
            SELECT name, definition, version, updated_at
            FROM {Table("agent_definitions")}
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        DeleteAgentDefinition = $"DELETE FROM {Table("agent_definitions")} WHERE tenant_id = @tenant_id AND name = @name;";

        SelectAgentDefinitionVersions = $"""
            SELECT v.definition, v.version, v.created_at
            FROM {Table("agent_definition_versions")} v
            JOIN {Table("agent_definitions")} d ON d.id = v.agent_id
            WHERE d.tenant_id = @tenant_id AND d.name = @name
            ORDER BY v.version DESC;
            """;

        SelectAgentDefinitionVersion = $"""
            SELECT v.definition, v.created_at
            FROM {Table("agent_definition_versions")} v
            JOIN {Table("agent_definitions")} d ON d.id = v.agent_id
            WHERE d.tenant_id = @tenant_id AND d.name = @name AND v.version = @version;
            """;

        DeleteAgentSkill = $"DELETE FROM {Table("agent_skills")} WHERE tenant_id = @tenant_id AND name = @name;";

        DeleteAgentSkillResources = $"DELETE FROM {Table("agent_skill_resources")} WHERE skill_id = @skill_id;";

        InsertAgentSkillResource = $"""
            INSERT INTO {Table("agent_skill_resources")}
                (id, skill_id, name, description, media_type, content, created_at)
            VALUES
                (@id, @skill_id, @name, @description, @media_type, @content, @created_at);
            """;

        SelectAgentSkillResources = $"""
            SELECT name, description, media_type, content
            FROM {Table("agent_skill_resources")}
            WHERE skill_id = @skill_id
            ORDER BY name;
            """;

        DeleteAgentSkillScripts = $"DELETE FROM {Table("agent_skill_scripts")} WHERE skill_id = @skill_id;";

        InsertAgentSkillScript = $"""
            INSERT INTO {Table("agent_skill_scripts")}
                (id, skill_id, name, description, extension, content, parameters_schema, created_at)
            VALUES
                (@id, @skill_id, @name, @description, @extension, @content, @parameters_schema, @created_at);
            """;

        SelectAgentSkillScripts = $"""
            SELECT name, description, extension, content, parameters_schema
            FROM {Table("agent_skill_scripts")}
            WHERE skill_id = @skill_id
            ORDER BY name;
            """;

        InsertSession = $"""
            INSERT INTO {Table("sessions")} (id, tenant_id, agent_name, state, state_schema_version, created_at, updated_at, version, state_maf_version)
            VALUES (@id, @tenant_id, @agent_name, @state, @state_schema_version, @created_at, @updated_at, 1, @state_maf_version);
            """;

        // `version` and `state_maf_version` are appended LAST so every
        // existing ordinal keeps its index (same rule as the `version`
        // column itself, decision K-648).
        SelectSession = $"""
            SELECT agent_name, state, state_schema_version, created_at, updated_at, tenant_id, version, state_maf_version
            FROM {Table("sessions")}
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        // Optimistic concurrency for every write after the first one. Plain
        // ANSI SQL, so no dialect needs its own copy: the predicate carries
        // the expected generation and the SET advances it in the SAME
        // statement, which is what makes the check and the write one atomic
        // step. Zero affected rows means another writer got there first.
        UpdateSessionIfVersionMatches = $"""
            UPDATE {Table("sessions")}
               SET agent_name           = @agent_name,
                   state                = @state,
                   state_schema_version = @state_schema_version,
                   state_maf_version    = @state_maf_version,
                   updated_at           = @updated_at,
                   version              = version + 1
             WHERE id = @id AND tenant_id = @tenant_id AND version = @expected_version;
            """;

        SelectSessionOwner = $"SELECT tenant_id FROM {Table("sessions")} WHERE id = @id;";

        DeleteSession = $"DELETE FROM {Table("sessions")} WHERE id = @id AND tenant_id = @tenant_id;";

        // RunEventWriter no longer exists in that process; the reconciler
        // writes the event. The sequence number is the current maximum plus
        // one -- there is no race risk because the run was just closed
        // (SingletonGuard already guarantees a single reconciler).
        InsertOrphanRunEvent = $"""
            INSERT INTO {Table("run_events")} (run_id, seq, type, text, created_at)
            VALUES (@run_id,
                    COALESCE((SELECT MAX(seq) FROM {Table("run_events")} WHERE run_id = @run_id), -1) + 1,
                    @type, @text, @created_at);
            """;

        // 🚨 SELECT ... WHERE EXISTS, not VALUES: the write applies only if
        // the target run belongs to the EXPECTED tenant (K-355). No check is
        // made when @tenant_id is NULL. The subquery is a primary-key lookup;
        // its added cost on the hot write path is a single index read.
        InsertRunEvent = $"""
            INSERT INTO {Table("run_events")} (run_id, seq, type, text, tool_name, tool_call_id, payload, custom_type, created_at)
            SELECT @run_id, @seq, @type, @text, @tool_name, @tool_call_id, @payload, @custom_type, @created_at
            WHERE EXISTS (
                SELECT 1 FROM {Table("runs")} r
                WHERE r.id = @run_id AND (@tenant_id IS NULL OR r.tenant_id = @tenant_id));
            """;

        SelectRunEvents = $"""
            SELECT e.run_id, e.seq, e.type, e.text, e.tool_name, e.tool_call_id, e.payload, e.created_at, e.custom_type
            FROM {Table("run_events")} e
            JOIN {Table("runs")} r ON r.id = e.run_id
            WHERE e.run_id = @run_id AND e.seq >= @from_sequence AND r.tenant_id = @tenant_id
            ORDER BY e.seq;
            """;

        SelectNextConversationSequence = $"""
            SELECT COALESCE(MAX(seq), -1) + 1
            FROM {Table("conversation_items")}
            WHERE conversation_id = @conversation_id;
            """;

        InsertConversationItem = $"""
            INSERT INTO {Table("conversation_items")} (id, conversation_id, seq, item, created_at)
            VALUES (@id, @conversation_id, @seq, @item, @created_at);
            """;

        SelectConversationItems = $"""
            SELECT i.item
            FROM {Table("conversation_items")} i
            JOIN {Table("conversations")} c ON c.id = i.conversation_id
            WHERE i.conversation_id = @conversation_id AND c.tenant_id = @tenant_id
            ORDER BY i.seq;
            """;

        // COUNT(*) is CAST to bigint because SQL Server's COUNT returns int
        // while PostgreSQL's and SQLite's return bigint, and the shared reader
        // takes every dialect through GetInt64.
        SelectConversationBranchPoint = $"""
            SELECT COALESCE(MAX(seq), -1), CAST(COUNT(*) AS bigint)
            FROM {Table("conversation_items")}
            WHERE conversation_id = @conversation_id
              AND (@up_to_sequence IS NULL OR seq <= @up_to_sequence);
            """;

        // Metadata (agent_name, metadata) is COPIED from the source: a branch
        // is a conversation of the same agent. The tenant filter is on the
        // SELECT side; for another tenant's conversation no row is written
        // and the caller sees 0 affected rows.
        InsertBranchConversation = $"""
            INSERT INTO {Table("conversations")}
                (id, tenant_id, agent_name, metadata, created_at, updated_at,
                 parent_conversation_id, branch_from_seq)
            SELECT @id, c.tenant_id, c.agent_name, c.metadata, @now, @now,
                   c.id, @branch_from_seq
            FROM {Table("conversations")} c
            WHERE c.id = @parent_conversation_id AND c.tenant_id = @tenant_id;
            """;

        SelectConversationItemsForBranch = $"""
            SELECT i.seq, i.item, i.created_at
            FROM {Table("conversation_items")} i
            WHERE i.conversation_id = @conversation_id
              AND (@up_to_sequence IS NULL OR i.seq <= @up_to_sequence)
            ORDER BY i.seq;
            """;

        SelectRunInput = $"""
            SELECT messages, created_at
            FROM {Table("run_inputs")}
            WHERE run_id = @run_id AND tenant_id = @tenant_id;
            """;

        // Same tenant guard as InsertRunEvent (K-355).
        InsertToolInvocation = $"""
            INSERT INTO {Table("tool_invocations")}
                (id, run_id, tool_name, tool_call_id, source, arguments, result, duration_ms, error, created_at,
                 usage_unit, usage_quantity, usage_estimated, cost, cost_currency,
                 authorization_denied, timed_out)
            SELECT @id, @run_id, @tool_name, @tool_call_id, @source, @arguments, @result, @duration_ms, @error, @created_at,
                   @usage_unit, @usage_quantity, @usage_estimated, @cost, @cost_currency,
                   @authorization_denied, @timed_out
            WHERE EXISTS (
                SELECT 1 FROM {Table("runs")} r
                WHERE r.id = @run_id AND (@tenant_id IS NULL OR r.tenant_id = @tenant_id));
            """;

        // 🚨 New columns are ALWAYS appended at the end; existing fixed-index
        // readers (ReadToolInvocation) are never renumbered. Lesson from
        // Phase 20.
        SelectToolInvocations = $"""
            SELECT t.id, t.run_id, t.tool_name, t.tool_call_id, t.source, t.arguments, t.result,
                   t.duration_ms, t.error, t.created_at,
                   t.usage_unit, t.usage_quantity, t.usage_estimated, t.cost, t.cost_currency,
                   t.authorization_denied, t.timed_out
            FROM {Table("tool_invocations")} t
            JOIN {Table("runs")} r ON r.id = t.run_id
            WHERE t.run_id = @run_id AND r.tenant_id = @tenant_id
            ORDER BY t.created_at, t.id;
            """;

        DeleteExperiment = $"""
            DELETE FROM {Table("experiments")}
            WHERE tenant_id = @tenant_id AND name = @name AND status <> 1;
            """;

        SelectTraceByRun = $"""
            SELECT id, trace_id, run_id, tenant_id, started_at, ended_at
            FROM {Table("traces")}
            WHERE run_id = @run_id AND tenant_id = @tenant_id;
            """;

        SelectSpans = $"""
            SELECT id, parent_span_id, span_id, name, kind, started_at, ended_at, attributes, status
            FROM {Table("spans")}
            WHERE trace_id = @trace_id
            ORDER BY started_at, id;
            """;

        // 🚨 New columns are ALWAYS appended at the end: SqlToolApprovalRuleStore.ReadRule
        // reads argument_conditions by fixed ordinal 7 (docs/hafiza/postgresql.md).
        SelectToolApprovalRules = $"""
            SELECT id, tenant_id, agent_name, tool_name, arguments_hash, created_by, created_at, argument_conditions
            FROM {Table("tool_approval_rules")}
            WHERE tenant_id = @tenant_id
            ORDER BY created_at DESC;
            """;

        DeleteToolApprovalRule = $"""
            DELETE FROM {Table("tool_approval_rules")}
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        SelectMcpServers = $"""
            SELECT {McpServerColumns}
            FROM {Table("mcp_servers")}
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectMcpServer = $"""
            SELECT {McpServerColumns}
            FROM {Table("mcp_servers")}
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        DeleteMcpServer = $"DELETE FROM {Table("mcp_servers")} WHERE tenant_id = @tenant_id AND name = @name;";

        SelectTenants = $"""
            SELECT id, slug, display_name, created_at
            FROM {Table("tenants")}
            ORDER BY slug;
            """;

        DeleteTenant = $"DELETE FROM {Table("tenants")} WHERE slug = @slug;";

        InsertAttachment = $"""
            INSERT INTO {Table("attachments")}
                (id, tenant_id, session_id, run_id, file_name, media_type, byte_size, sha256,
                 content, external_uri, created_by, created_at)
            VALUES
                (@id, @tenant_id, @session_id, @run_id, @file_name, @media_type, @byte_size, @sha256,
                 @content, @external_uri, @created_by, @created_at);
            """;

        SelectAttachment = $"""
            SELECT id, tenant_id, session_id, run_id, file_name, media_type, byte_size, sha256, created_by, created_at
            FROM {Table("attachments")}
            WHERE tenant_id = @tenant_id AND id = @id;
            """;

        // content is read only when requested (docs/arsiv/fazlar/14-COK-MODLULUK.md, section 14.2).
        SelectAttachmentContent = $"""
            SELECT content, external_uri, media_type
            FROM {Table("attachments")}
            WHERE tenant_id = @tenant_id AND id = @id;
            """;

        SelectAgentFile = $"""
            SELECT content
            FROM {Table("agent_files")}
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name AND path = @path;
            """;

        DeleteAgentFile = $"""
            DELETE FROM {Table("agent_files")}
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name AND path = @path;
            """;

        SelectWorkflow = $"""
            SELECT definition, version, updated_at
            FROM {Table("workflows")}
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        SelectWorkflows = $"""
            SELECT definition, version, updated_at, name
            FROM {Table("workflows")}
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        DeleteWorkflow = $"DELETE FROM {Table("workflows")} WHERE tenant_id = @tenant_id AND name = @name;";

        // AgentPrism generates the checkpoint id; a conflict means only that
        // the same id was written twice, and that is an error -- it is not
        // silently skipped, so there is NO ON CONFLICT clause.
        InsertWorkflowCheckpoint = $"""
            INSERT INTO {Table("workflow_checkpoints")}
                (id, tenant_id, session_id, checkpoint_id, parent_id, run_id, state, state_schema_version, state_maf_version, created_at)
            VALUES (@id, @tenant_id, @session_id, @checkpoint_id, @parent_id, @run_id, @state, @state_schema_version, @state_maf_version, @created_at);
            """;

        SelectWorkflowCheckpoint = $"""
            SELECT state
            FROM {Table("workflow_checkpoints")}
            WHERE tenant_id = @tenant_id AND session_id = @session_id AND checkpoint_id = @checkpoint_id;
            """;

        // The state payload is DELIBERATELY not selected: this is a list of
        // metadata, and carrying kilobytes of opaque JSON next to every row
        // would make the UI's checkpoint list unopenable.
        SelectWorkflowCheckpoints = $"""
            SELECT id, tenant_id, session_id, checkpoint_id, parent_id, run_id, created_at, state_schema_version, state_maf_version
            FROM {Table("workflow_checkpoints")}
            WHERE tenant_id = @tenant_id AND session_id = @session_id
            ORDER BY created_at, checkpoint_id;
            """;

        SelectWorkflowCheckpointsByRun = $"""
            SELECT id, tenant_id, session_id, checkpoint_id, parent_id, run_id, created_at, state_schema_version, state_maf_version
            FROM {Table("workflow_checkpoints")}
            WHERE tenant_id = @tenant_id AND run_id = @run_id
            ORDER BY created_at, checkpoint_id;
            """;

        DeleteWorkflowCheckpoints = $"""
            DELETE FROM {Table("workflow_checkpoints")}
            WHERE tenant_id = @tenant_id AND session_id = @session_id;
            """;

        InsertAuditEntry = $"""
            INSERT INTO {Table("audit_log")} (id, tenant_id, actor, action, entity, before, after, created_at, prev_hash, hash)
            VALUES (@id, @tenant_id, @actor, @action, @entity, @before, @after, @created_at, @prev_hash, @hash);
            """;

        SelectJobSchedule = $"""
            SELECT {ScheduleColumns}
            FROM {Table("job_schedules")}
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        SelectJobSchedules = $"""
            SELECT {ScheduleColumns}
            FROM {Table("job_schedules")}
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        DeleteJobSchedule = $"DELETE FROM {Table("job_schedules")} WHERE tenant_id = @tenant_id AND name = @name;";

        // CAS (compare-and-swap): advances only if the expected `next_run_at`
        // is still current. If another app instance already advanced the same
        // schedule concurrently, the match fails and the affected row count
        // is zero.
        TryClaimJobScheduleNextRun = $"""
            UPDATE {Table("job_schedules")}
               SET next_run_at = @new_next_run_at, last_run_at = @ran_at
             WHERE id = @id AND next_run_at = @expected_next_run_at;
            """;

        SelectInboundTrigger = $"""
            SELECT {InboundTriggerColumns}
            FROM {Table("inbound_triggers")}
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        SelectInboundTriggers = $"""
            SELECT {InboundTriggerColumns}
            FROM {Table("inbound_triggers")}
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        DeleteInboundTrigger = $"DELETE FROM {Table("inbound_triggers")} WHERE tenant_id = @tenant_id AND name = @name;";

        InsertJob = $"""
            INSERT INTO {Table("jobs")}
                (id, tenant_id, schedule_id, handler_key, target_name, status, payload, total_items,
                 done_items, failed_items, attempt, scheduled_for, created_at, max_attempts, lane)
            VALUES (@id, @tenant_id, @schedule_id, @handler_key, @target_name, 0, @payload, @total_items,
                    0, 0, 0, @scheduled_for, @created_at, @max_attempts, @lane);
            """;

        RenewJobLease = $"""
            UPDATE {Table("jobs")} SET lease_until = @lease_until WHERE id = @id AND lease_owner = @owner;
            """;

        MarkJobRunning = $"""
            UPDATE {Table("jobs")} SET status = 2 WHERE id = @id AND lease_owner = @owner AND status = 1;
            """;

        CompleteJob = $"""
            UPDATE {Table("jobs")}
               SET status = @status, completed_at = @completed_at, error_message = @error_message,
                   lease_owner = NULL, lease_until = NULL
             WHERE id = @id;
            """;

        // If @retry_at is NULL, scheduled_for is left untouched (the old
        // behavior: the job can be re-leased immediately). If it is given,
        // backoff is applied; this makes writing a second queue for webhook
        // delivery unnecessary (K-160).
        ReleaseJobForRetry = $"""
            UPDATE {Table("jobs")}
               SET status = 0, lease_owner = NULL, lease_until = NULL, error_message = @error_message,
                   scheduled_for = COALESCE(@retry_at, scheduled_for)
             WHERE id = @id;
            """;

        CancelJob = $"""
            UPDATE {Table("jobs")}
               SET status = 5, completed_at = @completed_at, lease_owner = NULL, lease_until = NULL
             WHERE id = @id AND tenant_id = @tenant_id AND status IN (0, 1, 2);
            """;

        SelectJob = $"""
            SELECT {JobColumns}
            FROM {Table("jobs")}
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        SelectJobItems = $"""
            SELECT id, job_id, seq, input, run_id, status, error
            FROM {Table("job_items")}
            WHERE job_id = @job_id
            ORDER BY seq;
            """;

        // Counts ONLY the open statuses (Pending, Leased, Running). That set is
        // exactly the partial condition of `jobs_claim_idx (lane, status,
        // scheduled_for) WHERE status IN (0, 1, 2)`, so this needs no index of
        // its own. COUNT(*) is CAST to bigint because SQL Server's COUNT
        // returns int, and the reader takes every dialect through GetInt64.
        SelectJobQueueDepth = $"""
            SELECT lane, status, CAST(COUNT(*) AS bigint)
            FROM {Table("jobs")}
            WHERE status IN (0, 1, 2)
            GROUP BY lane, status;
            """;

        SelectEvalSuites = $"""
            SELECT {SuiteColumns}
            FROM {Table("eval_suites")}
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectEvalSuite = $"""
            SELECT {SuiteColumns}
            FROM {Table("eval_suites")}
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        DeleteEvalSuite = $"DELETE FROM {Table("eval_suites")} WHERE tenant_id = @tenant_id AND name = @name;";

        SelectEvalCases = $"""
            SELECT {EvalCaseColumns}
            FROM {Table("eval_cases")}
            WHERE suite_id = @suite_id
            ORDER BY seq;
            """;

        DeleteEvalCases = $"DELETE FROM {Table("eval_cases")} WHERE suite_id = @suite_id;";

        InsertEvalCase = $"""
            INSERT INTO {Table("eval_cases")}
                (id, suite_id, seq, query, expected_output, expected_tools, context, parameters)
            VALUES
                (@id, @suite_id, @seq, @query, @expected_output, @expected_tools, @context, @parameters);
            """;

        SelectEvalCaseBySourceRun = $"""
            SELECT {EvalCaseColumns}
            FROM {Table("eval_cases")}
            WHERE suite_id = @suite_id AND source_run_id = @source_run_id;
            """;

        MarkEvalRunRunning = $"""
            UPDATE {Table("eval_runs")}
               SET status = 1, agent_version = @agent_version, model_id = @model_id
             WHERE id = @id;
            """;

        CompleteEvalRun = $"""
            UPDATE {Table("eval_runs")}
               SET status = @status, completed_at = @completed_at, total = @total,
                   passed = @passed, failed = @failed, input_tokens = @input_tokens,
                   output_tokens = @output_tokens
             WHERE id = @id;
            """;

        SelectEvalRun = $"""
            SELECT {EvalRunColumns}
            FROM {Table("eval_runs")}
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        SelectEvalRunByJobId = $"""
            SELECT {EvalRunColumns}
            FROM {Table("eval_runs")}
            WHERE tenant_id = @tenant_id AND job_id = @job_id;
            """;

        InsertEvalCaseResult = $"""
            INSERT INTO {Table("eval_case_results")}
                (id, eval_run_id, case_id, run_id, passed, output, scores, failure_reason)
            VALUES
                (@id, @eval_run_id, @case_id, @run_id, @passed, @output, @scores, @failure_reason);
            """;

        SelectEvalCaseResults = $"""
            SELECT ecr.id, ecr.eval_run_id, ecr.case_id, ecr.run_id, ecr.passed, ecr.output,
                   ecr.scores, ecr.failure_reason
            FROM {Table("eval_case_results")} ecr
            JOIN {Table("eval_runs")} er ON er.id = ecr.eval_run_id
            WHERE er.tenant_id = @tenant_id AND ecr.eval_run_id = @eval_run_id
            ORDER BY ecr.id;
            """;

        SelectQuota = $"""
            SELECT {QuotaColumns}
            FROM {Table("quotas")}
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        DeleteQuota = $"""
            DELETE FROM {Table("quotas")} WHERE id = @id AND tenant_id = @tenant_id;
            """;

        SelectQuotaUsage = $"""
            SELECT tenant_id, agent_name, period, period_start, runs, tokens, cost, updated_at
            FROM {Table("quota_usage")}
            WHERE tenant_id = @tenant_id
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@period     IS NULL OR period     = @period)
            ORDER BY agent_name, period, period_start DESC;
            """;

        SelectWebhookSubscriptions = $"""
            SELECT {WebhookSubscriptionColumns}
            FROM {Table("webhook_subscriptions")}
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectWebhookSubscription = $"""
            SELECT {WebhookSubscriptionColumns}
            FROM {Table("webhook_subscriptions")}
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        DeleteWebhookSubscription = $"""
            DELETE FROM {Table("webhook_subscriptions")} WHERE tenant_id = @tenant_id AND name = @name;
            """;

        InsertWebhookDelivery = $"""
            INSERT INTO {Table("webhook_deliveries")}
                ({WebhookDeliveryColumns})
            VALUES
                (@id, @subscription_id, @tenant_id, @event_type, @payload, @status, @attempt,
                 @response_code, @error, @created_at, @delivered_at);
            """;

        SelectWebhookDelivery = $"""
            SELECT {WebhookDeliveryColumns}
            FROM {Table("webhook_deliveries")}
            WHERE id = @id;
            """;

        UpdateWebhookDeliveryResult = $"""
            UPDATE {Table("webhook_deliveries")}
               SET status        = @status,
                   attempt       = @attempt,
                   response_code = @response_code,
                   error         = @error,
                   delivered_at  = CASE WHEN @status = 1 THEN @recorded_at ELSE delivered_at END
             WHERE id = @id;
            """;

        SelectApiKeys = $"""
            SELECT {ApiKeyColumns}
            FROM {Table("api_keys")}
            WHERE tenant_id = @tenant_id
            ORDER BY created_at;
            """;

        // The tenant filter is DELIBERATELY absent (section 53.5): the tenant
        // is the output of this query, not its input.
        SelectApiKeyByHash = $"""
            SELECT {ApiKeyColumns}
            FROM {Table("api_keys")}
            WHERE key_hash = @key_hash;
            """;

        RevokeApiKey = $"""
            UPDATE {Table("api_keys")}
               SET revoked_at = @revoked_at
             WHERE tenant_id = @tenant_id AND id = @id AND revoked_at IS NULL;
            """;

        TouchApiKeyLastUsed = $"""
            UPDATE {Table("api_keys")}
               SET last_used_at = @last_used_at
             WHERE id = @id;
            """;

        SelectRetentionPolicies = $"""
            SELECT {RetentionPolicyColumns}
            FROM {Table("retention_policies")}
            WHERE tenant_id = @tenant_id
            ORDER BY target;
            """;

        SelectRetentionPolicy = $"""
            SELECT {RetentionPolicyColumns}
            FROM {Table("retention_policies")}
            WHERE tenant_id = @tenant_id
              AND target    = @target;
            """;

        DeleteRetentionPolicy = $"""
            DELETE FROM {Table("retention_policies")}
             WHERE tenant_id = @tenant_id
               AND target    = @target;
            """;

        InsertRetentionRun = $"""
            INSERT INTO {Table("retention_runs")}
                ({RetentionRunColumns})
            VALUES
                (@id, @tenant_id, @target, 0, 0, @started_at, NULL, NULL);
            """;

        UpdateRetentionRunProgress = $"""
            UPDATE {Table("retention_runs")}
               SET deleted_rows  = deleted_rows + @deleted_delta,
                   archived_rows = archived_rows + @archived_delta
             WHERE id = @id;
            """;

        CompleteRetentionRun = $"""
            UPDATE {Table("retention_runs")}
               SET completed_at = @completed_at,
                   error        = @error
             WHERE id = @id;
            """;

        SelectRunScores = $"""
            SELECT {RunScoreColumns}
            FROM {Table("run_scores")}
            WHERE tenant_id = @tenant_id AND run_id = @run_id;
            """;

        DeleteRunScore = $"""
            DELETE FROM {Table("run_scores")} WHERE id = @id AND tenant_id = @tenant_id;
            """;

        RenewSingletonLease = $"""
            UPDATE {Table("singleton_leases")}
               SET expires_at = @expires_at, updated_at = @now
             WHERE name = @name AND owner_id = @owner_id;
            """;

        ReleaseSingletonLease = $"""
            DELETE FROM {Table("singleton_leases")} WHERE name = @name AND owner_id = @owner_id;
            """;

        // 🚨 New columns are ALWAYS appended at the end (docs/hafiza/postgresql.md):
        // presentation reads by fixed ordinal 12, after the original twelve columns.
        InsertPendingApproval = $"""
            INSERT INTO {Table("pending_approvals")}
                (id, tenant_id, run_id, session_id, request_id, tool_name, arguments, status,
                 decided_by, decided_at, expires_at, created_at, presentation)
            VALUES
                (@id, @tenant_id, @run_id, @session_id, @request_id, @tool_name, @arguments, @status,
                 @decided_by, @decided_at, @expires_at, @created_at, @presentation);
            """;

        SelectPendingApprovals = $"""
            SELECT id, tenant_id, run_id, session_id, request_id, tool_name, arguments, status,
                   decided_by, decided_at, expires_at, created_at, presentation
              FROM {Table("pending_approvals")}
             WHERE tenant_id = @tenant_id AND status = @status
             ORDER BY created_at ASC;
            """;

        SelectPendingApproval = $"""
            SELECT id, tenant_id, run_id, session_id, request_id, tool_name, arguments, status,
                   decided_by, decided_at, expires_at, created_at, presentation
              FROM {Table("pending_approvals")}
             WHERE id = @id AND tenant_id = @tenant_id;
            """;

        // WHERE status = @status_pending: a second decision affects 0 rows,
        // DecideAsync interprets that as false.
        DecidePendingApproval = $"""
            UPDATE {Table("pending_approvals")}
               SET status = @status, decided_by = @decided_by, decided_at = @decided_at
             WHERE id = @id AND tenant_id = @tenant_id AND status = @status_pending;
            """;

        SelectTenantProviderBinding = $"""
            SELECT {TenantProviderBindingColumns}
            FROM {Table("tenant_provider_bindings")}
            WHERE tenant_id = @tenant_id AND provider_name = @provider_name;
            """;

        SelectTenantProviderBindings = $"""
            SELECT {TenantProviderBindingColumns}
            FROM {Table("tenant_provider_bindings")}
            WHERE tenant_id = @tenant_id
            ORDER BY provider_name;
            """;

        DeleteTenantProviderBinding = $"""
            DELETE FROM {Table("tenant_provider_bindings")}
            WHERE tenant_id = @tenant_id AND provider_name = @provider_name;
            """;

        SelectTenantEgressPolicy = $"""
            SELECT tenant_id, allowed_providers, updated_at
            FROM {Table("tenant_egress_policies")}
            WHERE tenant_id = @tenant_id;
            """;

        DeleteTenantEgressPolicy = $"""
            DELETE FROM {Table("tenant_egress_policies")}
            WHERE tenant_id = @tenant_id;
            """;
    }
}
