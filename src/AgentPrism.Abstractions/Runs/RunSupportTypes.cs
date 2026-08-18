using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Token usage of a run.</summary>
public sealed record RunUsage
{
    /// <summary>Gets the number of input tokens.</summary>
    public long? InputTokens { get; init; }

    /// <summary>Gets the number of output tokens.</summary>
    public long? OutputTokens { get; init; }

    /// <summary>Gets the total number of tokens.</summary>
    public long? TotalTokens { get; init; }
}

/// <summary>Error information for a failed run.</summary>
public sealed record RunError
{
    /// <summary>Gets the name of the exception type.</summary>
    public required string Type { get; init; }

    /// <summary>Gets the error message.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the class chosen by <see cref="IRunErrorClassifier"/>. Rows written
    /// BEFORE the error class was introduced hold <see langword="null"/>, which
    /// the user interface shows in the <c>Unknown</c> bucket.
    /// </summary>
    public RunErrorClass? Class { get; init; }

    /// <summary>
    /// Gets the digest of the normalized message, used to cluster repetitions of
    /// the same fault. <see langword="null"/> when no classifier ran, as for
    /// <see cref="Class"/>.
    /// </summary>
    public string? Fingerprint { get; init; }
}

/// <summary>
/// Cost of a run. It is computed and written once when the run ends (a price
/// snapshot) — a later change to the price list does not change past values
/// (see <c>docs/20-MALIYET-VE-GOSTERGE-PANELI.md</c>, section 20.2).
/// </summary>
public sealed record RunCost
{
    /// <summary>Gets the input token cost, or <see langword="null"/> when the price is unknown.</summary>
    public decimal? InputCost { get; init; }

    /// <summary>Gets the output token cost, or <see langword="null"/> when the price is unknown.</summary>
    public decimal? OutputCost { get; init; }

    /// <summary>Gets the currency, taken from <c>AgentPrism:Pricing:Currency</c>.</summary>
    public string? Currency { get; init; }

    /// <summary>Gets where the price came from.</summary>
    public required PricingSource Source { get; init; }
}

/// <summary>
/// Total cost of a run tree (the root plus every child run).
/// </summary>
/// <remarks>
/// This <strong>must not be added</strong> to <see cref="RunRecord.Cost"/>: the
/// value already includes the root's own cost, for the same reason as
/// <see cref="RunRecord.TreeUsage"/>. The user interface shows the two in
/// separate columns.
/// </remarks>
public sealed record RunTreeCost
{
    /// <summary>Gets the total input cost across the tree.</summary>
    public decimal? InputCost { get; init; }

    /// <summary>Gets the total output cost across the tree.</summary>
    public decimal? OutputCost { get; init; }

    /// <summary>Gets the currency.</summary>
    public string? Currency { get; init; }

    /// <summary>Gets how many runs in the tree have an unknown price.</summary>
    public long RunsWithUnknownPricing { get; init; }
}

/// <summary>Everything needed to open a new run.</summary>
public sealed record RunStartInfo
{
    /// <summary>Gets the run id. The caller produces it, so it knows the id straight away.</summary>
    public required Guid RunId { get; init; }

    /// <summary>
    /// Gets the name of the agent that runs. For workflow runs the workflow name
    /// is written instead; the rationale is on <see cref="RunRecord.AgentName"/>.
    /// </summary>
    public required string AgentName { get; init; }

    /// <summary>Gets whether this row records an agent or a workflow.</summary>
    public RunKind Kind { get; init; }

    /// <summary>Gets the name of the workflow, or <see langword="null"/> for agent runs.</summary>
    public string? WorkflowName { get; init; }

    /// <summary>Gets the start time (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Gets the initial status of the opened row. Defaults to <see cref="RunStatus.Running"/>.
    /// </summary>
    /// <remarks>
    /// Phase 46: a queued run opens as <see cref="RunStatus.Queued"/>. When
    /// <c>StartRunAsync</c> is called a SECOND time with the same
    /// <see cref="RunId"/> — once the worker actually runs the job — the store
    /// treats it as an upsert: no new row is opened, the existing row is updated
    /// with this field, normally to <see cref="RunStatus.Running"/>.
    /// </remarks>
    public RunStatus Status { get; init; } = RunStatus.Running;

    /// <summary>Gets the tenant id.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the session id.</summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the model to use, resolved from the agent definition, or
    /// <see langword="null"/> when it is unknown.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>Gets whether the run streams.</summary>
    public bool IsStreaming { get; init; }

    /// <summary>Gets the id of the run that started this one, or <see langword="null"/> at the root.</summary>
    public Guid? ParentRunId { get; init; }

    /// <summary>Gets the id of the run at the root of the tree, or <see langword="null"/> at the root.</summary>
    public Guid? RootRunId { get; init; }

    /// <summary>Gets the depth in the tree. The root run is 0.</summary>
    public int Depth { get; init; }

    /// <summary>Gets the definition version this run measured, or <see langword="null"/> when unknown.</summary>
    public int? AgentVersion { get; init; }

    /// <summary>Gets the experiment this run belongs to, or <see langword="null"/> outside an experiment.</summary>
    public Guid? ExperimentId { get; init; }

    /// <summary>Gets the experiment variant this run was assigned to, or <see langword="null"/> outside an experiment.</summary>
    public string? Variant { get; init; }

    /// <summary>
    /// Gets the source run id when this run is a replay (phase 47), or
    /// <see langword="null"/> for a normal run.
    /// </summary>
    public Guid? ReplayOfRunId { get; init; }
}

/// <summary>Everything needed to close a run.</summary>
public sealed record RunCompletion
{
    /// <summary>Gets the run id.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the final status.</summary>
    public required RunStatus Status { get; init; }

    /// <summary>Gets the completion time (UTC).</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>Gets the total number of events written.</summary>
    public long EventCount { get; init; }

    /// <summary>Gets the token usage.</summary>
    public RunUsage? Usage { get; init; }

    /// <summary>Gets the error information. Only populated for <see cref="RunStatus.Failed"/>.</summary>
    public RunError? Error { get; init; }

    /// <summary>
    /// Gets the computed cost. <see langword="null"/> when the model is unknown
    /// (a code agent, for example); when the model is known the value is populated
    /// even if the price is not (see <see cref="RunCost"/>).
    /// </summary>
    public RunCost? Cost { get; init; }

    /// <summary>
    /// Gets the model that actually answered, overriding <c>runs.model_id</c>
    /// when it differs from the value written at <c>RunStarted</c>.
    /// <see langword="null"/> leaves the stored value unchanged.
    /// </summary>
    /// <remarks>
    /// The row is written once, at run start, from the agent's primary
    /// <see cref="ModelBinding"/> — before it is known whether a
    /// <see cref="ModelBinding.Fallbacks"/> link will answer instead. This
    /// field lets completion correct the record so
    /// <c>IRunStore.GetStatisticsAsync</c>'s <c>ByModel</c> breakdown groups by
    /// the model that really ran (phase 62). <see langword="null"/> is the
    /// overwhelmingly common case (no fallback happened) and is a deliberate
    /// no-op, not an omission.
    /// </remarks>
    public string? ModelId { get; init; }

    /// <summary>
    /// Gets the EXPECTED tenant of the run being closed. Defence in depth; when
    /// <see langword="null"/> no tenant check is made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the rationale, and why the ambient tenant is not used, see
    /// <see cref="RunEvent.TenantId"/>. Decision K-355.
    /// </para>
    /// <para>
    /// 🚨 The field is WRITE-side only and is removed from the transport contract
    /// with <see cref="JsonIgnoreAttribute"/>.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public string? TenantId { get; init; }
}

/// <summary>Query used to filter the run list.</summary>
public sealed record RunQuery
{
    /// <summary>Gets the agent whose runs are returned.</summary>
    public string? AgentName { get; init; }

    /// <summary>Gets the status whose runs are returned.</summary>
    public RunStatus? Status { get; init; }

    /// <summary>Gets the tenant whose runs are returned.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the session whose runs are returned.</summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the error type (<see cref="RunError.Type"/>) whose runs are returned.
    /// <see langword="null"/> includes every error type.
    /// </summary>
    public string? ErrorType { get; init; }

    /// <summary>Gets the lower bound on start time (UTC).</summary>
    public DateTimeOffset? StartedAfter { get; init; }

    /// <summary>
    /// Gets whether only root runs are returned. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// The default is deliberately <see langword="true"/>: when an agent calls other
    /// agents, every child call produces its own <c>runs</c> row and the list fills
    /// with runs the user never started. Child runs appear as a tree inside their
    /// own root's detail view; set this to <see langword="false"/> for the full list.
    /// </remarks>
    public bool OnlyRootRuns { get; init; } = true;

    /// <summary>
    /// Gets the run whose direct children are returned.
    /// </summary>
    /// <remarks>
    /// When a value is given, <see cref="OnlyRootRuns"/> is ignored: asking for
    /// child runs and filtering to roots contradict each other, and silently
    /// returning an empty list is hard to debug.
    /// </remarks>
    public Guid? ParentRunId { get; init; }

    /// <summary>Gets the tree whose runs are returned, including the root.</summary>
    public Guid? RootRunId { get; init; }

    /// <summary>Gets the number of records to skip.</summary>
    public int Skip { get; init; }

    /// <summary>Gets the maximum number of records to return.</summary>
    public int Take { get; init; } = 50;

    /// <summary>
    /// Gets the kind whose runs are returned. <see langword="null"/> includes
    /// every kind.
    /// </summary>
    /// <remarks>
    /// Phase 49: judge runs are <see cref="RunKind.Eval"/> and do not appear in
    /// the default list (see <see cref="IRunStore.GetStatisticsAsync"/>); to see
    /// their cost, call <c>GET /api/runs?kind=Eval</c>.
    /// </remarks>
    public RunKind? Kind { get; init; }
}

/// <summary>Filter for a time-series query.</summary>
public sealed record RunTimeSeriesQuery
{
    /// <summary>Gets the start of the range (UTC, inclusive).</summary>
    public required DateTimeOffset From { get; init; }

    /// <summary>Gets the end of the range (UTC, exclusive).</summary>
    public required DateTimeOffset To { get; init; }

    /// <summary>Gets the bucket width. Defaults to hourly.</summary>
    public TimeSeriesBucket Bucket { get; init; } = TimeSeriesBucket.Hour;

    /// <summary>Gets the agent whose runs are counted.</summary>
    public string? AgentName { get; init; }

    /// <summary>Gets the model whose runs are counted.</summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Gets the kind whose runs are counted. <see langword="null"/> includes every
    /// kind — unlike <see cref="IRunStore.GetStatisticsAsync"/>, this query does
    /// not exclude eval and workflow runs by default (see <c>docs/KARARLAR.md</c>,
    /// K-152).
    /// </summary>
    public RunKind? Kind { get; init; }

    /// <summary>Gets the tenant filter. When empty, the current tenant is used.</summary>
    public string? TenantId { get; init; }
}

/// <summary>Result of a cost recalculation.</summary>
public sealed record RunCostRecalculationResult
{
    /// <summary>Gets the number of runs considered, that is those with a model and usage.</summary>
    public required long RunsConsidered { get; init; }

    /// <summary>Gets the number of runs whose price resolved and was updated.</summary>
    public required long RunsUpdated { get; init; }

    /// <summary>Gets the number of runs whose price is still unknown afterwards.</summary>
    public required long RunsStillUnknown { get; init; }
}
