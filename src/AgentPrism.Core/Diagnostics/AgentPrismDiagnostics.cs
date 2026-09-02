namespace AgentPrism;

/// <summary>Defines AgentPrism telemetry source and measurement names.</summary>
/// <remarks>
/// <para>
/// These names are <strong>stable</strong>. Consumers use them in OpenTelemetry
/// configuration, so changing them is a breaking change.
/// </para>
/// <para>
/// Consumers continue to configure their own OTLP exporter. AgentPrism does not
/// take over the pipeline. Example setup:
/// </para>
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t => t.AddSource(AgentPrismDiagnostics.ActivitySourceName))
///     .WithMetrics(m => m.AddMeter(AgentPrismDiagnostics.MeterName));
/// </code>
/// </remarks>
public static class AgentPrismDiagnostics
{
    /// <summary>Gets the AgentPrism <c>ActivitySource</c> name.</summary>
    public const string ActivitySourceName = "AgentPrism";

    /// <summary>Gets the AgentPrism <c>Meter</c> name.</summary>
    public const string MeterName = "AgentPrism";

    /// <summary>Gets the root span name that represents a run.</summary>
    public const string RunActivityName = "agentprism.run";

    /// <summary>Gets the span name that represents a skill script execution.</summary>
    public const string SkillScriptActivityName = "execute_skill_script";

    /// <summary>Gets the span name that represents a context-compaction summarization call.</summary>
    public const string CompactHistoryActivityName = "compact_history";

    /// <summary>Gets the tool name for skill script calls in metrics.</summary>
    public const string SkillScriptToolName = "skill_script";

    /// <summary>Gets the completed-run counter name.</summary>
    public const string RunCounterName = "agentprism.runs";

    /// <summary>Gets the run-duration histogram name in seconds.</summary>
    public const string RunDurationName = "agentprism.run.duration";

    /// <summary>Gets the token counter name.</summary>
    public const string TokenCounterName = "agentprism.tokens";

    /// <summary>Gets the tool-invocation counter name.</summary>
    public const string ToolCounterName = "agentprism.tool.invocations";

    /// <summary>Gets the tool-call duration histogram name in seconds.</summary>
    public const string ToolDurationName = "agentprism.tool.duration";

    /// <summary>Gets the monetary cost counter name per run.</summary>
    public const string RunCostCounterName = "agentprism.run.cost";

    /// <summary>Gets the observable gauge that shows quota-scope consumption in the current period.</summary>
    public const string QuotaUsageGaugeName = "agentprism.quota.usage";

    /// <summary>Gets the observable gauge that shows the configured quota-scope limit.</summary>
    public const string QuotaLimitGaugeName = "agentprism.quota.limit";

    /// <summary>Gets the counter name for the judge's own cost.</summary>
    public const string JudgeCostCounterName = "agentprism.judge.cost";

    /// <summary>Gets the histogram name for judge scores from 0 to 100.</summary>
    public const string JudgeScoreHistogramName = "agentprism.judge.score";

    /// <summary>Gets the response-cache lookup counter name.</summary>
    public const string ModelCacheLookupCounterName = "agentprism.model.cache";

    /// <summary>Gets the counter name for agent-source failures.</summary>
    public const string AgentSourceFailureCounterName = "agentprism.agent_source.failures";

    /// <summary>Gets the counter name for finished background jobs.</summary>
    public const string JobCounterName = "agentprism.job.executions";

    /// <summary>Gets the job-attempt duration histogram name in seconds.</summary>
    public const string JobDurationName = "agentprism.job.duration";

    /// <summary>Gets the observable gauge that shows outstanding jobs per lane and status.</summary>
    public const string JobQueueDepthGaugeName = "agentprism.job.queue.depth";

    /// <summary>Defines span and metric tag names. Changing them breaks dashboards.</summary>
    public static class Tags
    {
        /// <summary>Gets the run identifier tag name.</summary>
        public const string RunId = "agentprism.run.id";

        /// <summary>Gets the agent name tag name.</summary>
        public const string AgentName = "agentprism.agent.name";

        /// <summary>Gets the tenant identifier tag name.</summary>
        public const string TenantId = "agentprism.tenant.id";

        /// <summary>Gets the session identifier tag name.</summary>
        public const string SessionId = "agentprism.session.id";

        /// <summary>Gets the run-status tag name.</summary>
        public const string Status = "agentprism.run.status";

        /// <summary>Gets the parent run identifier tag name. It is written only for child runs.</summary>
        public const string ParentRunId = "agentprism.run.parent_id";

        /// <summary>Gets the call-tree depth tag name. It is written only for child runs.</summary>
        public const string Depth = "agentprism.run.depth";

        /// <summary>Gets the tag name that indicates streaming runs.</summary>
        public const string Streaming = "agentprism.run.streaming";

        /// <summary>Gets the model name tag name.</summary>
        public const string ModelId = "agentprism.model.id";

        /// <summary>Gets the tool name tag name.</summary>
        public const string ToolName = "agentprism.tool.name";

        /// <summary>Gets the token direction tag name: <c>input</c> or <c>output</c>.</summary>
        public const string Direction = "agentprism.token.direction";

        /// <summary>Gets the skill name tag name.</summary>
        public const string SkillName = "agentprism.skill.name";

        /// <summary>Gets the script name tag name.</summary>
        public const string ScriptName = "agentprism.script.name";

        /// <summary>Gets the script process exit-code tag name.</summary>
        public const string ExitCode = "agentprism.script.exit_code";

        /// <summary>Gets the script execution duration tag name in milliseconds.</summary>
        public const string DurationMs = "agentprism.script.duration_ms";

        /// <summary>Gets the summarization-call input-token tag name.</summary>
        public const string CompactionInputTokens = "agentprism.compaction.input_tokens";

        /// <summary>Gets the summarization-call output-token tag name.</summary>
        public const string CompactionOutputTokens = "agentprism.compaction.output_tokens";

        /// <summary>Gets the evaluated definition version tag name.</summary>
        public const string AgentVersion = "agentprism.agent.version";

        /// <summary>Gets the cost currency tag name.</summary>
        public const string Currency = "agentprism.cost.currency";

        /// <summary>Gets the quota scope tag name: agent name or an empty string for tenant-wide scope.</summary>
        public const string QuotaScope = "agentprism.quota.scope";

        /// <summary>Gets the quota counter reset-period tag name.</summary>
        public const string QuotaPeriod = "agentprism.quota.period";

        /// <summary>Gets the applied quota metric tag name: <c>Runs</c>, <c>Tokens</c>, or <c>Cost</c>.</summary>
        public const string QuotaMetric = "agentprism.quota.metric";

        /// <summary>Gets the judge name tag name from <see cref="IRunJudge.Name"/>.</summary>
        public const string JudgeName = "agentprism.judge.name";

        /// <summary>Gets the model provider name tag name.</summary>
        public const string Provider = "agentprism.model.provider";

        /// <summary>Gets the response-cache lookup result tag name: <c>hit</c> or <c>miss</c>.</summary>
        public const string CacheResult = "agentprism.model.cache.result";

        /// <summary>Gets the agent-source name tag.</summary>
        public const string AgentSourceName = "agentprism.agent_source.name";

        /// <summary>Gets the agent-source operation tag.</summary>
        public const string AgentSourceOperation = "agentprism.agent_source.operation";

        /// <summary>Gets the job lane tag name. See <see cref="JobLanes"/>.</summary>
        /// <remarks>
        /// A lane name is chosen by the consumer, so its cardinality is not
        /// bounded by AgentPrism. Once a process has seen
        /// <see cref="AgentPrismObservabilityOptions.MaxJobLaneCardinality"/>
        /// distinct lanes, every further lane is written as <c>other</c>.
        /// </remarks>
        public const string Lane = "agentprism.job.lane";

        /// <summary>Gets the job-kind tag name.</summary>
        public const string JobKind = "agentprism.job.kind";

        /// <summary>Gets the job-status tag name.</summary>
        /// <remarks>
        /// The counter and the duration histogram carry a TERMINAL status
        /// (<c>Completed</c>, <c>Failed</c>, <c>Cancelled</c>) — they record a job
        /// that finished. The queue-depth gauge carries an OPEN one
        /// (<c>Pending</c>, <c>Leased</c>, <c>Running</c>) — it reports work that
        /// has not finished. The two sets never overlap.
        /// </remarks>
        public const string JobStatus = "agentprism.job.status";
    }
}
