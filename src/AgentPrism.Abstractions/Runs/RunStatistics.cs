namespace AgentPrism;

/// <summary>
/// The filter for a run statistics query.
/// </summary>
/// <remarks>
/// There is no pagination: the result is always a single summary row. Use
/// <see cref="StartedAfter"/> to limit the volume.
/// </remarks>
public sealed record RunStatisticsQuery
{
    /// <summary>Count only this agent's runs.</summary>
    public string? AgentName { get; init; }

    /// <summary>The tenant filter. The current tenant is used if left empty.</summary>
    public string? TenantId { get; init; }

    /// <summary>Count only runs that started after this moment.</summary>
    public DateTimeOffset? StartedAfter { get; init; }

    /// <summary>
    /// The maximum number of rows to return in the agent breakdown. Agents
    /// with the highest run count are returned first.
    /// </summary>
    public int MaxAgents { get; init; } = 20;
}

/// <summary>
/// A summary of the runs in a time range.
/// </summary>
/// <remarks>
/// <para>
/// Token totals cover only runs that reported usage information. If a
/// provider does not return usage, that run is counted but contributes
/// nothing to the token total.
/// </para>
/// <para>
/// Cost (Phase 20) is populated only when pricing is configured — see
/// <see cref="TotalCost"/>. Pricing is not embedded in AgentPrism (decision
/// K-032): it comes from the model catalog or the <c>AgentPrism:Pricing</c> configuration.
/// </para>
/// </remarks>
public sealed record RunStatistics
{
    /// <summary>The total number of runs matching the filter.</summary>
    public required long TotalRuns { get; init; }

    /// <summary>The number of runs that completed successfully.</summary>
    public required long CompletedRuns { get; init; }

    /// <summary>The number of runs that ended in an error.</summary>
    public required long FailedRuns { get; init; }

    /// <summary>The number of cancelled runs.</summary>
    public required long CanceledRuns { get; init; }

    /// <summary>The number of runs still running.</summary>
    public required long RunningRuns { get; init; }

    /// <summary>The number of runs awaiting human input.</summary>
    /// <remarks>
    /// Occurs only in workflow runs. Counted separately because such a run is
    /// neither running nor settled; forcing it into one bucket would make the
    /// subtotals not add up to <see cref="TotalRuns"/>.
    /// </remarks>
    public long AwaitingInputRuns { get; init; }

    /// <summary>The total input tokens.</summary>
    public long InputTokens { get; init; }

    /// <summary>The total output tokens.</summary>
    public long OutputTokens { get; init; }

    /// <summary>The total tokens.</summary>
    public long TotalTokens { get; init; }

    /// <summary>The breakdown by agent.</summary>
    public IReadOnlyList<RunAgentStatistics> ByAgent { get; init; } = [];

    /// <summary>
    /// The breakdown by model. Runs with an unknown model name do not appear
    /// in this list; they are still counted in the totals.
    /// </summary>
    public IReadOnlyList<RunModelStatistics> ByModel { get; init; } = [];

    /// <summary>The breakdown by definition version. Runs with an unknown version do not appear in this list.</summary>
    public IReadOnlyList<RunVersionStatistics> ByVersion { get; init; } = [];

    /// <summary>
    /// The breakdown by error class. Only runs that ended in an error are
    /// counted. Rows written before the error class was added appear in the
    /// <c>Unknown</c> bucket (K-014 — past rows are not backfilled).
    /// </summary>
    public IReadOnlyList<RunErrorStatistics> ByErrorClass { get; init; } = [];

    /// <summary>
    /// The total cost. If a model has undefined pricing, that model's run
    /// costs are <strong>not included</strong> in this total (only runs with
    /// known pricing are summed); the number of runs excluded appears in
    /// <see cref="RunsWithUnknownPricing"/>. <see langword="null"/> if no run
    /// was ever priced.
    /// </summary>
    public decimal? TotalCost { get; init; }

    /// <summary>The currency. Populated when <see cref="TotalCost"/> is populated.</summary>
    public string? Currency { get; init; }

    /// <summary>The number of runs whose model is known but whose pricing is undefined.</summary>
    public long RunsWithUnknownPricing { get; init; }

    /// <summary>
    /// The error rate among settled runs (0–1). <see langword="null"/> if no
    /// run has settled.
    /// </summary>
    /// <remarks>
    /// The denominator is <em>settled</em> runs, not <see cref="TotalRuns"/>.
    /// Whether a run in progress will succeed or fail is not yet known;
    /// including it in the denominator would artificially lower the rate.
    /// </remarks>
    public double? ErrorRate
    {
        get
        {
            var settled = CompletedRuns + FailedRuns + CanceledRuns;
            return settled == 0 ? null : (double)FailedRuns / settled;
        }
    }

    /// <summary>
    /// The number of runs that received at least one <see cref="RunScore"/>
    /// (at the run or message level). Eval runs (<see cref="RunKind.Eval"/>)
    /// are excluded for the same reason as <see cref="TotalRuns"/> (K-141).
    /// </summary>
    public long ScoredRuns { get; init; }

    /// <summary>
    /// The positive rate among <see cref="RunScoreKind.Binary"/> scores (0–1).
    /// Star ratings are not included in this rate — averaging the two kinds
    /// would be meaningless. <see langword="null"/> if there is no binary score.
    /// </summary>
    public double? PositiveRate { get; init; }
}

/// <summary>A model's run summary. The input to cost computation.</summary>
public sealed record RunModelStatistics
{
    /// <summary>The model name.</summary>
    public required string ModelId { get; init; }

    /// <summary>The total number of runs made with this model.</summary>
    public required long TotalRuns { get; init; }

    /// <summary>The total input tokens.</summary>
    public long InputTokens { get; init; }

    /// <summary>The total output tokens.</summary>
    public long OutputTokens { get; init; }

    /// <summary>The total tokens.</summary>
    public long TotalTokens { get; init; }

    /// <summary>The total cost of runs made with this model. <see langword="null"/> if pricing is undefined.</summary>
    public decimal? TotalCost { get; init; }
}

/// <summary>
/// A summary of the runs in a time bucket. The result unit of
/// <c>/api/stats/timeseries</c>; empty buckets are also returned (with zero events).
/// </summary>
public sealed record TimeSeriesPoint
{
    /// <summary>The bucket's start time (UTC).</summary>
    public required DateTimeOffset Bucket { get; init; }

    /// <summary>The number of runs that started in this bucket.</summary>
    public long Runs { get; init; }

    /// <summary>The number of runs that ended in an error in this bucket.</summary>
    public long FailedRuns { get; init; }

    /// <summary>The total input tokens.</summary>
    public long InputTokens { get; init; }

    /// <summary>The total output tokens.</summary>
    public long OutputTokens { get; init; }

    /// <summary>The total cost. <see langword="null"/> if no run was ever priced.</summary>
    public decimal? Cost { get; init; }

    /// <summary>The average duration of settled runs (milliseconds).</summary>
    public double? AverageDurationMs { get; init; }
}

/// <summary>A definition version's run summary.</summary>
public sealed record RunVersionStatistics
{
    /// <summary>The agent name this breakdown belongs to.</summary>
    public required string AgentName { get; init; }

    /// <summary>The definition version.</summary>
    public required int Version { get; init; }

    /// <summary>The total number of runs made with this version.</summary>
    public required long TotalRuns { get; init; }

    /// <summary>This version's number of runs that ended in an error.</summary>
    public required long FailedRuns { get; init; }

    /// <summary>This version's total token usage.</summary>
    public long TotalTokens { get; init; }
}

/// <summary>An agent's run summary.</summary>
public sealed record RunAgentStatistics
{
    /// <summary>The agent name.</summary>
    public required string AgentName { get; init; }

    /// <summary>This agent's total number of runs.</summary>
    public required long TotalRuns { get; init; }

    /// <summary>This agent's number of runs that ended in an error.</summary>
    public required long FailedRuns { get; init; }

    /// <summary>This agent's total token usage.</summary>
    public long TotalTokens { get; init; }
}

/// <summary>An error class's summary.</summary>
public sealed record RunErrorStatistics
{
    /// <summary>The error class.</summary>
    public required RunErrorClass Class { get; init; }

    /// <summary>The total number of runs falling into this class.</summary>
    public required long TotalRuns { get; init; }

    /// <summary>This class's most frequent clusters, in descending order of count.</summary>
    public IReadOnlyList<RunErrorCluster> TopClusters { get; init; } = [];
}

/// <summary>The summary of runs sharing the same fingerprint.</summary>
public sealed record RunErrorCluster
{
    /// <summary>The digest of the normalized message.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>The number of runs in this cluster.</summary>
    public required long Count { get; init; }

    /// <summary>The raw error message of the most recent occurrence in the cluster.</summary>
    public required string SampleMessage { get; init; }

    /// <summary>The run identifier of the most recent occurrence in the cluster.</summary>
    public required Guid SampleRunId { get; init; }

    /// <summary>The moment this cluster was last seen (UTC).</summary>
    public required DateTimeOffset LastSeenAt { get; init; }
}
