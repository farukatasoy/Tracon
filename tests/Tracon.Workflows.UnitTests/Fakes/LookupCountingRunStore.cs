namespace Tracon.Workflows.UnitTests.Fakes;

/// <summary>
/// A run store that counts how often the writer asked it where an earlier attempt of a run stopped, and passes
/// everything to the store behind it.
/// </summary>
internal sealed class LookupCountingRunStore(IRunStore inner) : IRunStore
{
    private int _lastSequenceLookups;

    /// <summary>Gets how many times <see cref="GetLastEventSequenceAsync"/> was called.</summary>
    public int LastSequenceLookups => Volatile.Read(ref _lastSequenceLookups);

    /// <inheritdoc />
    public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
        => inner.GetRunAsync(runId, cancellationToken);

    /// <inheritdoc />
    public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
        => inner.CompleteRunAsync(completion, cancellationToken);

    /// <inheritdoc />
    public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
        => inner.StartRunAsync(info, cancellationToken);

    /// <inheritdoc />
    public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        => inner.AppendEventAsync(runEvent, cancellationToken);

    /// <inheritdoc />
    public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _lastSequenceLookups);
        return inner.GetLastEventSequenceAsync(runId, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
        => inner.QueryRunsAsync(query, cancellationToken);

    /// <inheritdoc />
    public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken cancellationToken = default)
        => inner.GetStatisticsAsync(query, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default)
        => inner.ReadEventsAsync(runId, fromSequence, cancellationToken);

    /// <inheritdoc />
    public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default)
        => inner.RecordToolInvocationAsync(invocation, cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> CompleteLateToolInvocationAsync(LateToolCompletion completion, CancellationToken cancellationToken = default)
        => inner.CompleteLateToolInvocationAsync(completion, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default)
        => inner.ListToolInvocationsAsync(runId, cancellationToken);

    /// <inheritdoc />
    public ValueTask RecordNonTokenUsageAsync(NonTokenUsageRecord record, CancellationToken cancellationToken = default)
        => inner.RecordNonTokenUsageAsync(record, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<NonTokenUsageRecord>> ListNonTokenUsageAsync(Guid runId, CancellationToken cancellationToken = default)
        => inner.ListNonTokenUsageAsync(runId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default)
        => inner.GetToolUsageAsync(query, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default)
        => inner.GetExperimentResultsAsync(query, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default)
        => inner.GetTimeSeriesAsync(query, cancellationToken);

    /// <inheritdoc />
    public ValueTask UpdateRunCostAsync(Guid runId, RunCost? cost, string? tenantId, CancellationToken cancellationToken = default)
        => inner.UpdateRunCostAsync(runId, cost, tenantId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RunCancellationRequest>> TouchHeartbeatAsync(IReadOnlyCollection<Guid> runIds, CancellationToken cancellationToken = default)
        => inner.TouchHeartbeatAsync(runIds, cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> RequestCancellationAsync(
        Guid runId,
        string tenantId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
        => inner.RequestCancellationAsync(runId, tenantId, at, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(TimeSpan staleAfter, int max, CancellationToken cancellationToken = default)
        => inner.ClaimOrphanedRunsAsync(staleAfter, max, cancellationToken);
}
