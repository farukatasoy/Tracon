namespace Tracon.Core.UnitTests.Fakes;

/// <summary>A run store whose calls fail while a switch is on: the store is unavailable.</summary>
internal sealed class FaultyRunStore(InMemoryRunStore inner) : IRunStore
{
    /// <summary>Gets or sets whether run reads (one run, or a query) fail.</summary>
    public bool FailReads { get; set; }

    /// <summary>Gets or sets whether writing a continuation's row fails.</summary>
    public bool FailContinuationStart { get; set; }

    /// <summary>Gets or sets whether closing a run fails.</summary>
    public bool FailCompletes { get; set; }

    public int ReadsRefused { get; private set; }

    public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (FailReads)
        {
            ReadsRefused++;
            throw new InvalidOperationException("the store is unavailable");
        }

        return inner.GetRunAsync(runId, cancellationToken);
    }

    public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
        => FailContinuationStart && info.ContinuedFromRunId is not null
            ? throw new InvalidOperationException("the store is unavailable")
            : inner.StartRunAsync(info, cancellationToken);

    public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default) => inner.AppendEventAsync(runEvent, cancellationToken);

    public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default) => inner.GetLastEventSequenceAsync(runId, cancellationToken);

    /// <summary>
    /// Gets or sets what happens just before a run is closed: a test fires a job's time
    /// limit there, and the call then observes its token as a store would.
    /// </summary>
    public Action? BeforeComplete { get; set; }

    public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
    {
        if (FailCompletes)
        {
            throw new InvalidOperationException("the store is unavailable");
        }

        BeforeComplete?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();

        return inner.CompleteRunAsync(completion, cancellationToken);
    }

    public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
        => FailReads ? throw new InvalidOperationException("the store is unavailable") : inner.QueryRunsAsync(query, cancellationToken);

    public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken cancellationToken = default) => inner.GetStatisticsAsync(query, cancellationToken);

    public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default) => inner.ReadEventsAsync(runId, fromSequence, cancellationToken);

    public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default) => inner.RecordToolInvocationAsync(invocation, cancellationToken);

    public ValueTask<bool> CompleteLateToolInvocationAsync(LateToolCompletion completion, CancellationToken cancellationToken = default) => inner.CompleteLateToolInvocationAsync(completion, cancellationToken);

    public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default) => inner.ListToolInvocationsAsync(runId, cancellationToken);

    public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default) => inner.GetToolUsageAsync(query, cancellationToken);

    public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default) => inner.GetExperimentResultsAsync(query, cancellationToken);

    public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default) => inner.GetTimeSeriesAsync(query, cancellationToken);

    public ValueTask UpdateRunCostAsync(Guid runId, RunCost? cost, string? tenantId = null, CancellationToken cancellationToken = default) => inner.UpdateRunCostAsync(runId, cost, tenantId, cancellationToken);

    public ValueTask TouchHeartbeatAsync(IReadOnlyCollection<Guid> runIds, DateTimeOffset at, CancellationToken cancellationToken = default) => inner.TouchHeartbeatAsync(runIds, at, cancellationToken);

    public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(DateTimeOffset staleBefore, int max, CancellationToken cancellationToken = default) => inner.ClaimOrphanedRunsAsync(staleBefore, max, cancellationToken);
}
