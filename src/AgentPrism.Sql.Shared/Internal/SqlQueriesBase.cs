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
/// 🚨 <strong>When a new query is added here, its counterpart must be written in
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

    /// <summary>Gets the query that inserts an approval rule; it returns the existing record when the same scope is present.</summary>
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
    /// PostgreSQL only) an optional regex prefilter (phase 51, item A). It replaces
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

    /// <summary>Gets the query that reads the hash of a tenant's newest audit log record (phase 64).</summary>
    public string SelectLastAuditHash { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a tenant's audit log records, oldest first, for hash chain verification (phase 64).</summary>
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

    /// <summary>Gets the validated schema name.</summary>
    public string Schema { get; protected set; } = string.Empty;

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
    /// HATA-004: the unconditional overwrite of <see cref="UpsertSession"/> made two
    /// concurrent first requests to the same NEW session produce a different
    /// conversation identifier each, and the messages of the loser stayed silently
    /// unreachable.
    /// </summary>
    public string InsertSession { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a session.</summary>
    public string SelectSession { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that reads the tenant owning a session identifier, WITHOUT
    /// applying the tenant filter.
    /// HATA-S2-005: the cross-tenant ownership check cannot use
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
    /// Gets the query that writes the heartbeat mark of a running run (phase 54). It
    /// affects <c>Running</c> rows only.
    /// </summary>
    public string TouchRunHeartbeat { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that closes the top N <c>Running</c> rows past the heartbeat
    /// threshold as <c>Failed</c> and returns the closed rows (phase 54).
    /// </summary>
    public string ClaimOrphanedRuns { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that inserts the <c>RunFailed</c> event reporting the closure
    /// of an orphaned run; the sequence number is one more than the current maximum
    /// (phase 54).
    /// </summary>
    public string InsertOrphanRunEvent { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a run.</summary>
    public string SelectRun { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the runs with a filter.</summary>
    public string SelectRuns { get; protected set; } = string.Empty;

    /// <summary>Gets the query that returns the run summary and the per-agent breakdown as two result sets.</summary>
    public string SelectRunStatistics { get; protected set; } = string.Empty;

    /// <summary>Gets the query for the per-bucket run, error, token and cost time series. Empty buckets are returned as well.</summary>
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
    /// copy and the item count (phase 47). When there is no item at all the sequence
    /// number is <c>-1</c>.
    /// </summary>
    public string SelectConversationBranchPoint { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that opens a new branch conversation by copying the metadata of
    /// the source conversation (phase 47). When the source is absent or belongs to
    /// another tenant, no row is written.
    /// </summary>
    public string InsertBranchConversation { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that reads the items to copy while branching, in order (phase 47).
    /// </summary>
    public string SelectConversationItemsForBranch { get; protected set; } = string.Empty;

    /// <summary>
    /// Gets the query that writes the input messages of a run (phase 47). A second
    /// write for the same run is <strong>ignored</strong>.
    /// </summary>
    public string InsertRunInput { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads the stored input of a run (phase 47).</summary>
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
    /// <c>seq</c> atomically (promotion from production, phase 45).
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

    /// <summary>Gets the query that looks an API key up by its hash. There is NO tenant filter (section 53.5).</summary>
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

    /// <summary>Gets the query that inserts a run/message score or updates it (when the author and target are the same).</summary>
    public string UpsertRunScore { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists every score of a run.</summary>
    public string SelectRunScores { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes a score.</summary>
    public string DeleteRunScore { get; protected set; } = string.Empty;

    /// <summary>Gets the query that acquires the singleton lease (it inserts when the row is absent, and updates when the owner or the expiry allows it).</summary>
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

    /// <summary>Gets the query that inserts a new pending approval request (phase 55).</summary>
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

    /// <summary>Gets the query that creates or replaces a tenant's provider binding (phase 65, BYOK).</summary>
    public string UpsertTenantProviderBinding { get; protected set; } = string.Empty;

    /// <summary>Gets the query that reads a single tenant provider binding.</summary>
    public string SelectTenantProviderBinding { get; protected set; } = string.Empty;

    /// <summary>Gets the query that lists every provider binding of a tenant.</summary>
    public string SelectTenantProviderBindings { get; protected set; } = string.Empty;

    /// <summary>Gets the query that deletes a tenant provider binding.</summary>
    public string DeleteTenantProviderBinding { get; protected set; } = string.Empty;

    /// <summary>Gets the query that creates or replaces a tenant's egress policy (phase 65, F-119).</summary>
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
}
