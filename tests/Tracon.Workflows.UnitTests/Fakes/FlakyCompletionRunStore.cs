namespace Tracon.Workflows.UnitTests.Fakes;

/// <summary>
/// A run store that refuses the first <see cref="FailuresLeft"/> closing writes and passes
/// everything else to the store behind it.
/// </summary>
internal sealed class FlakyCompletionRunStore(IRunStore inner, int failures) : IRunStore
{
    /// <summary>Gets how many closing writes are still refused.</summary>
    public int FailuresLeft { get; private set; } = failures;

    /// <inheritdoc />
    public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
    {
        if (FailuresLeft > 0)
        {
            FailuresLeft--;

            throw new InvalidOperationException("the run store blinked");
        }

        return inner.CompleteRunAsync(completion, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
        => inner.StartRunAsync(info, cancellationToken);

    /// <inheritdoc />
    public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        => inner.AppendEventAsync(runEvent, cancellationToken);

    /// <inheritdoc />
    public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default)
        => inner.GetLastEventSequenceAsync(runId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
        => inner.GetRunAsync(runId, cancellationToken);

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
    public ValueTask<IReadOnlyList<RunCancellationRequest>> TouchHeartbeatAsync(IReadOnlyCollection<Guid> runIds, DateTimeOffset at, CancellationToken cancellationToken = default)
        => inner.TouchHeartbeatAsync(runIds, at, cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> RequestCancellationAsync(
        Guid runId,
        string tenantId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
        => inner.RequestCancellationAsync(runId, tenantId, at, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(DateTimeOffset staleBefore, int max, CancellationToken cancellationToken = default)
        => inner.ClaimOrphanedRunsAsync(staleBefore, max, cancellationToken);
}
