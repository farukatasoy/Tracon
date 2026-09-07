namespace AgentPrism.Core.UnitTests.Storage;

/// <summary>
/// Structural regression coverage for the phase 108 <see cref="InMemoryRunStore"/>
/// split into responsibility files (lifecycle, events, queries, statistics,
/// analytics) -- the two error modes that <c>RunStoreContract</c> does not
/// exercise because they are specific to this store's <see cref="InMemoryRunStore.MaxRuns"/>
/// trim and its <see cref="IRunScoreStore"/> dependency.
/// </summary>
public sealed class InMemoryRunStoreStructureTests
{
    private const string Tenant = "test";

    [Fact]
    public async Task Trim_drops_the_dropped_runs_events_and_tool_invocations_too()
    {
        var store = new InMemoryRunStore { MaxRuns = 1 };
        var droppedRunId = AgentPrismId.NewId();
        var keptRunId = AgentPrismId.NewId();

        await store.StartRunAsync(Start(droppedRunId));
        await store.AppendEventAsync(Event(droppedRunId, 0));
        await store.RecordToolInvocationAsync(Invocation(droppedRunId));

        // Starting a second run pushes the count above MaxRuns and trims the oldest.
        await store.StartRunAsync(Start(keptRunId));

        (await store.GetRunAsync(droppedRunId)).ShouldBeNull();

        var events = new List<RunEvent>();
        await foreach (var runEvent in store.ReadEventsAsync(droppedRunId))
        {
            events.Add(runEvent);
        }

        events.ShouldBeEmpty();
        (await store.ListToolInvocationsAsync(droppedRunId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failing_score_store_fails_the_statistics_call_instead_of_being_swallowed()
    {
        var store = new InMemoryRunStore(new ThrowingRunScoreStore());
        await store.StartRunAsync(Start(AgentPrismId.NewId()));

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await store.GetStatisticsAsync(new RunStatisticsQuery { TenantId = Tenant }));
    }

    [Fact]
    public async Task Cancelling_the_score_store_call_cancels_the_statistics_call()
    {
        using var cts = new CancellationTokenSource();
        var store = new InMemoryRunStore(new CancelingRunScoreStore(cts));
        await store.StartRunAsync(Start(AgentPrismId.NewId()));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.GetStatisticsAsync(
                new RunStatisticsQuery { TenantId = Tenant },
                cts.Token));
    }

    private static RunStartInfo Start(Guid runId)
        => new()
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = Tenant,
        };

    private static RunEvent Event(Guid runId, long sequence)
        => new()
        {
            RunId = runId,
            Sequence = sequence,
            Type = RunEventType.MessageDelta,
            Timestamp = DateTimeOffset.UtcNow,
        };

    private static ToolInvocationRecord Invocation(Guid runId)
        => new()
        {
            Id = AgentPrismId.NewId(),
            RunId = runId,
            ToolName = "test-tool",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>Always fails <see cref="IRunScoreStore.ListAsync"/>.</summary>
    private sealed class ThrowingRunScoreStore : IRunScoreStore
    {
        public ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunScore>> ListAsync(
            string tenantId,
            Guid runId,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The score store is unavailable.");

        public ValueTask<bool> DeleteAsync(
            string tenantId,
            Guid scoreId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<RunScoreSummary> SummarizeAsync(RunScoreQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Cancels the caller's token the moment <see cref="IRunScoreStore.ListAsync"/> runs.</summary>
    private sealed class CancelingRunScoreStore(CancellationTokenSource source) : IRunScoreStore
    {
        public ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunScore>> ListAsync(
            string tenantId,
            Guid runId,
            CancellationToken cancellationToken = default)
        {
            source.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<RunScore>>([]);
        }

        public ValueTask<bool> DeleteAsync(
            string tenantId,
            Guid scoreId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<RunScoreSummary> SummarizeAsync(RunScoreQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
