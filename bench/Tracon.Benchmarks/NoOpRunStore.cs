namespace Tracon.Benchmarks;

/// <summary>
/// A run store that does nothing and allocates nothing, used to isolate
/// <see cref="RunEventWriter"/>'s own allocation from any storage cost -
/// the benchmark measures the writer, not a persistence layer.
/// </summary>
internal sealed class NoOpRunStore : IRunStore
{
    private static readonly IReadOnlyList<RunRecord> EmptyRuns = [];
    private static readonly IReadOnlyList<ToolInvocationRecord> EmptyInvocations = [];
    private static readonly IReadOnlyList<ToolUsage> EmptyToolUsage = [];
    private static readonly IReadOnlyList<ExperimentVariantResult> EmptyExperimentResults = [];
    private static readonly IReadOnlyList<TimeSeriesPoint> EmptyTimeSeries = [];

    private static readonly RunStatistics EmptyStatistics = new()
    {
        TotalRuns = 0,
        CompletedRuns = 0,
        FailedRuns = 0,
        CanceledRuns = 0,
        RunningRuns = 0,
    };

    public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
        => new(new RunRecord
        {
            Id = info.RunId,
            AgentName = info.AgentName,
            Kind = info.Kind,
            Status = info.Status,
            StartedAt = info.StartedAt,
            TenantId = info.TenantId,
        });

    public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default) => default;

    public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<long?>(null);

    public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default) => default;

    public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
        => new((RunRecord?)null);

    public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
        => new(EmptyRuns);

    public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken cancellationToken = default)
        => new(EmptyStatistics);

    public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default)
        => EmptyEventsAsync();

    public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default) => default;

    public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default)
        => new(EmptyInvocations);

    public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default)
        => new(EmptyToolUsage);

    public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default)
        => new(EmptyExperimentResults);

    public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default)
        => new(EmptyTimeSeries);

    public ValueTask UpdateRunCostAsync(Guid runId, RunCost? cost, string? tenantId = null, CancellationToken cancellationToken = default) => default;

    public ValueTask TouchHeartbeatAsync(IReadOnlyCollection<Guid> runIds, DateTimeOffset at, CancellationToken cancellationToken = default) => default;

    public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(DateTimeOffset staleBefore, int max, CancellationToken cancellationToken = default)
        => new(EmptyRuns);

#pragma warning disable CS1998 // no await needed - this is a fixed empty sequence
    private static async IAsyncEnumerable<RunEvent> EmptyEventsAsync()
    {
        yield break;
    }
#pragma warning restore CS1998
}
