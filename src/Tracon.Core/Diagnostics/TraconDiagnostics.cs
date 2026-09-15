namespace Tracon;

/// <summary>Defines Tracon telemetry source and measurement names.</summary>
/// <remarks>
/// <para>
/// These names are <strong>stable</strong>. Consumers use them in OpenTelemetry
/// configuration, so changing them is a breaking change.
/// </para>
/// <para>
/// Consumers continue to configure their own OTLP exporter. Tracon does not
/// take over the pipeline. Example setup:
/// </para>
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t => t.AddSource(TraconDiagnostics.ActivitySourceName))
///     .WithMetrics(m => m.AddMeter(TraconDiagnostics.MeterName));
/// </code>
/// </remarks>
public static class TraconDiagnostics
{
    /// <summary>Gets the Tracon <c>ActivitySource</c> name.</summary>
    public const string ActivitySourceName = "Tracon";

    /// <summary>Gets the Tracon <c>Meter</c> name.</summary>
    public const string MeterName = "Tracon";

    /// <summary>Gets the root span name that represents a run.</summary>
    public const string RunActivityName = "tracon.run";

    /// <summary>Gets the span name that represents a skill script execution.</summary>
    public const string SkillScriptActivityName = "execute_skill_script";

    /// <summary>Gets the span name that represents a context-compaction summarization call.</summary>
    public const string CompactHistoryActivityName = "compact_history";

    /// <summary>Gets the tool name for skill script calls in metrics.</summary>
    public const string SkillScriptToolName = "skill_script";

    /// <summary>Gets the completed-run counter name.</summary>
    public const string RunCounterName = "tracon.runs";

    /// <summary>Gets the run-duration histogram name in seconds.</summary>
    public const string RunDurationName = "tracon.run.duration";

    /// <summary>Gets the token counter name.</summary>
    public const string TokenCounterName = "tracon.tokens";

    /// <summary>Gets the tool-invocation counter name.</summary>
    public const string ToolCounterName = "tracon.tool.invocations";

    /// <summary>Gets the tool-call duration histogram name in seconds.</summary>
    public const string ToolDurationName = "tracon.tool.duration";

    /// <summary>Gets the monetary cost counter name per run.</summary>
    public const string RunCostCounterName = "tracon.run.cost";

    /// <summary>Gets the observable gauge that shows quota-scope consumption in the current period.</summary>
    public const string QuotaUsageGaugeName = "tracon.quota.usage";

    /// <summary>Gets the observable gauge that shows the configured quota-scope limit.</summary>
    public const string QuotaLimitGaugeName = "tracon.quota.limit";

    /// <summary>Gets the counter name for the judge's own cost.</summary>
    public const string JudgeCostCounterName = "tracon.judge.cost";

    /// <summary>Gets the histogram name for judge scores from 0 to 100.</summary>
    public const string JudgeScoreHistogramName = "tracon.judge.score";

    /// <summary>Gets the response-cache lookup counter name.</summary>
    public const string ModelCacheLookupCounterName = "tracon.model.cache";

    /// <summary>Gets the counter name for agent-source failures.</summary>
    public const string AgentSourceFailureCounterName = "tracon.agent_source.failures";

    /// <summary>Gets the counter name for finished background jobs.</summary>
    public const string JobCounterName = "tracon.job.executions";

    /// <summary>Gets the job-attempt duration histogram name in seconds.</summary>
    public const string JobDurationName = "tracon.job.duration";

    /// <summary>Gets the observable gauge that shows outstanding jobs per lane and status.</summary>
    public const string JobQueueDepthGaugeName = "tracon.job.queue.depth";

    /// <summary>Gets the counter name for audit-trail entries that could not be written.</summary>
    /// <remarks>
    /// Counted on BOTH audit write paths and separated by <see cref="Tags.AuditOutcome"/>:
    /// a <c>swallowed</c> failure left the operation to continue, a <c>refused</c> one
    /// stopped it. A non-zero value on either means the audit trail has a hole in it.
    /// </remarks>
    public const string AuditWriteFailureCounterName = "tracon.audit.write_failures";

    /// <summary>Gets the counter name for run records that could not be written.</summary>
    /// <remarks>
    /// Run recording is best-effort by design: a store failure never interrupts the
    /// run. This counter is how that loss becomes visible. One measurement is one
    /// write attempt whose record was lost, separated by
    /// <see cref="Tags.RecordingStage"/>. A non-zero value means a run happened whose
    /// evidence is incomplete or missing.
    /// </remarks>
    public const string RunRecordingFailureCounterName = "tracon.run.recording_failures";

    /// <summary>Defines span and metric tag names. Changing them breaks dashboards.</summary>
    public static class Tags
    {
        /// <summary>Gets the run identifier tag name.</summary>
        public const string RunId = "tracon.run.id";

        /// <summary>Gets the agent name tag name.</summary>
        public const string AgentName = "tracon.agent.name";

        /// <summary>Gets the tenant identifier tag name.</summary>
        public const string TenantId = "tracon.tenant.id";

        /// <summary>Gets the session identifier tag name.</summary>
        public const string SessionId = "tracon.session.id";

        /// <summary>Gets the run-status tag name.</summary>
        public const string Status = "tracon.run.status";

        /// <summary>Gets the parent run identifier tag name. It is written only for child runs.</summary>
        public const string ParentRunId = "tracon.run.parent_id";

        /// <summary>Gets the call-tree depth tag name. It is written only for child runs.</summary>
        public const string Depth = "tracon.run.depth";

        /// <summary>Gets the tag name that indicates streaming runs.</summary>
        public const string Streaming = "tracon.run.streaming";

        /// <summary>Gets the model name tag name.</summary>
        public const string ModelId = "tracon.model.id";

        /// <summary>Gets the tool name tag name.</summary>
        public const string ToolName = "tracon.tool.name";

        /// <summary>Gets the token direction tag name: <c>input</c> or <c>output</c>.</summary>
        public const string Direction = "tracon.token.direction";

        /// <summary>Gets the skill name tag name.</summary>
        public const string SkillName = "tracon.skill.name";

        /// <summary>Gets the script name tag name.</summary>
        public const string ScriptName = "tracon.script.name";

        /// <summary>Gets the script process exit-code tag name.</summary>
        public const string ExitCode = "tracon.script.exit_code";

        /// <summary>Gets the script execution duration tag name in milliseconds.</summary>
        public const string DurationMs = "tracon.script.duration_ms";

        /// <summary>Gets the summarization-call input-token tag name.</summary>
        public const string CompactionInputTokens = "tracon.compaction.input_tokens";

        /// <summary>Gets the summarization-call output-token tag name.</summary>
        public const string CompactionOutputTokens = "tracon.compaction.output_tokens";

        /// <summary>Gets the evaluated definition version tag name.</summary>
        public const string AgentVersion = "tracon.agent.version";

        /// <summary>Gets the cost currency tag name.</summary>
        public const string Currency = "tracon.cost.currency";

        /// <summary>Gets the quota scope tag name: agent name or an empty string for tenant-wide scope.</summary>
        public const string QuotaScope = "tracon.quota.scope";

        /// <summary>Gets the quota counter reset-period tag name.</summary>
        public const string QuotaPeriod = "tracon.quota.period";

        /// <summary>Gets the applied quota metric tag name: <c>Runs</c>, <c>Tokens</c>, or <c>Cost</c>.</summary>
        public const string QuotaMetric = "tracon.quota.metric";

        /// <summary>Gets the judge name tag name from <see cref="IRunJudge.Name"/>.</summary>
        public const string JudgeName = "tracon.judge.name";

        /// <summary>Gets the model provider name tag name.</summary>
        public const string Provider = "tracon.model.provider";

        /// <summary>Gets the response-cache lookup result tag name: <c>hit</c> or <c>miss</c>.</summary>
        public const string CacheResult = "tracon.model.cache.result";

        /// <summary>Gets the agent-source name tag.</summary>
        public const string AgentSourceName = "tracon.agent_source.name";

        /// <summary>Gets the agent-source operation tag.</summary>
        public const string AgentSourceOperation = "tracon.agent_source.operation";

        /// <summary>Gets the job lane tag name. See <see cref="JobLanes"/>.</summary>
        /// <remarks>
        /// A lane name is chosen by the consumer, so its cardinality is not
        /// bounded by Tracon. Once a process has seen
        /// <see cref="TraconObservabilityOptions.MaxJobLaneCardinality"/>
        /// distinct lanes, every further lane is written as <c>other</c>.
        /// </remarks>
        public const string Lane = "tracon.job.lane";

        /// <summary>Gets the job-kind tag name.</summary>
        public const string JobHandlerKey = "tracon.job.handler_key";

        /// <summary>Gets the audit-action tag name, such as <c>approval.decision</c>.</summary>
        /// <remarks>
        /// The action is a fixed name chosen in Tracon's own code, never consumer input,
        /// so its cardinality is bounded. The affected entity is NOT a tag: an entity name
        /// is consumer data and would give every agent, trigger and session a series of
        /// its own.
        /// </remarks>
        public const string AuditAction = "tracon.audit.action";

        /// <summary>Gets the audit write-failure outcome tag name: <c>swallowed</c> or <c>refused</c>.</summary>
        public const string AuditOutcome = "tracon.audit.outcome";

        /// <summary>Gets the tag name for the stage a run recording failed at.</summary>
        /// <remarks>
        /// A closed set: <c>start</c>, <c>event</c>, <c>tool_invocation</c>,
        /// <c>completion</c>, <c>sink</c>, <c>input</c>. The run identity is NOT a tag —
        /// it is unbounded — and neither is the sink type, which is consumer code; the
        /// failing sink is named in the accompanying log entry instead.
        /// </remarks>
        public const string RecordingStage = "tracon.recording.stage";

        /// <summary>Gets the job-status tag name.</summary>
        /// <remarks>
        /// The counter and the duration histogram carry a TERMINAL status
        /// (<c>Completed</c>, <c>Failed</c>, <c>Cancelled</c>) — they record a job
        /// that finished. The queue-depth gauge carries an OPEN one
        /// (<c>Pending</c>, <c>Leased</c>, <c>Running</c>) — it reports work that
        /// has not finished. The two sets never overlap.
        /// </remarks>
        public const string JobStatus = "tracon.job.status";
    }
}
