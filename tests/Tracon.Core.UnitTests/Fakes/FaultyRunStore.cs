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

    /// <summary>Gets or sets how many of the next closing writes fail; the ones after them go through.</summary>
    public int FailNextCompletes { get; set; }

    /// <summary>Gets or sets whether a closing write never answers, as a store behind a network that drops packets.</summary>
    public bool HangCompletes { get; set; }

    /// <summary>Gets how many closing writes reached the store, failed ones included.</summary>
    public int CompleteAttempts { get; private set; }

    /// <summary>Gets or sets whether closing a run is refused with the store's own verdict, which asking again does not change.</summary>
    public bool RefuseCompletes { get; set; }

    /// <summary>Gets how many calls that open or rewrite a run's row reached the store.</summary>
    public int StartAttempts { get; private set; }

    /// <summary>
    /// Gets or sets what happens after a row is written and before the call returns: a test cancels the caller's token
    /// there, so the call ends with a cancellation that arrives after the store took the row.
    /// </summary>
    public Action? AfterStart { get; set; }

    /// <summary>Gets or sets whether appending an event fails.</summary>
    public bool FailEventAppends { get; set; }

    /// <summary>Gets or sets whether writing the row of a tool invocation fails.</summary>
    public bool FailToolInvocations { get; set; }

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

    public async ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
    {
        StartAttempts++;

        if (FailContinuationStart && info.ContinuedFromRunId is not null)
        {
            throw new InvalidOperationException("the store is unavailable");
        }

        var record = await inner.StartRunAsync(info, cancellationToken);

        if (AfterStart is not null)
        {
            AfterStart();
            cancellationToken.ThrowIfCancellationRequested();
        }

        return record;
    }

    public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        => FailEventAppends ? throw new InvalidOperationException("the store is unavailable") : inner.AppendEventAsync(runEvent, cancellationToken);

    public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default) => inner.GetLastEventSequenceAsync(runId, cancellationToken);

    /// <summary>
    /// Gets or sets what happens just before a run is closed: a test fires a job's time
    /// limit there, and the call then observes its token as a store would.
    /// </summary>
    public Action? BeforeComplete { get; set; }

    public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
    {
        CompleteAttempts++;

        if (HangCompletes)
        {
            return new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)); // delay: simulated
        }

        if (RefuseCompletes)
        {
            throw new TraconException("the store refuses this closing write");
        }

        if (FailCompletes)
        {
            throw new InvalidOperationException("the store is unavailable");
        }

        if (FailNextCompletes > 0)
        {
            FailNextCompletes--;

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

    public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default)
        => FailToolInvocations
            ? throw new InvalidOperationException("the store is unavailable")
            : inner.RecordToolInvocationAsync(invocation, cancellationToken);

    public ValueTask<bool> CompleteLateToolInvocationAsync(LateToolCompletion completion, CancellationToken cancellationToken = default) => inner.CompleteLateToolInvocationAsync(completion, cancellationToken);

    public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default) => inner.ListToolInvocationsAsync(runId, cancellationToken);

    public ValueTask RecordNonTokenUsageAsync(NonTokenUsageRecord record, CancellationToken cancellationToken = default) => inner.RecordNonTokenUsageAsync(record, cancellationToken);

    public ValueTask<IReadOnlyList<NonTokenUsageRecord>> ListNonTokenUsageAsync(Guid runId, CancellationToken cancellationToken = default) => inner.ListNonTokenUsageAsync(runId, cancellationToken);

    public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default) => inner.GetToolUsageAsync(query, cancellationToken);

    public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default) => inner.GetExperimentResultsAsync(query, cancellationToken);

    public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default) => inner.GetTimeSeriesAsync(query, cancellationToken);

    public ValueTask UpdateRunCostAsync(Guid runId, RunCost? cost, string? tenantId, CancellationToken cancellationToken = default) => inner.UpdateRunCostAsync(runId, cost, tenantId, cancellationToken);

    public ValueTask<IReadOnlyList<RunCancellationRequest>> TouchHeartbeatAsync(IReadOnlyCollection<Guid> runIds, CancellationToken cancellationToken = default) => inner.TouchHeartbeatAsync(runIds, cancellationToken);

    public ValueTask<bool> RequestCancellationAsync(
        Guid runId,
        string tenantId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
        => inner.RequestCancellationAsync(runId, tenantId, at, cancellationToken);

    public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(TimeSpan staleAfter, int max, CancellationToken cancellationToken = default) => inner.ClaimOrphanedRunsAsync(staleAfter, max, cancellationToken);
}
