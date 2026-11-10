namespace Tracon.Core.UnitTests.Fakes;

/// <summary>
/// A run store in front of an in-memory one that can hold the writes of event groups, refuse them, and says what
/// reached it in what order and from what context.
/// </summary>
/// <remarks>
/// Only <see cref="AppendEventsAsync"/> is held: a streamed text chunk is written there, and everything else
/// (the row, a tool call, the closing write) goes straight through, so a test can keep the store busy with a group
/// and still watch the writer do its other work.
/// </remarks>
internal sealed class GroupSpyRunStore(InMemoryRunStore inner) : IRunStore
{
    private readonly Lock _gate = new();
    private readonly List<IReadOnlyList<long>> _groups = [];
    private readonly List<long> _singles = [];
    private readonly List<long> _order = [];
    private TaskCompletionSource? _release;
    private int _groupsEntered;
    private int _lastSequenceLookups;

    /// <summary>Gets how many times the writer asked the store for the last event sequence of a run.</summary>
    public int LastSequenceLookups => Volatile.Read(ref _lastSequenceLookups);

    /// <summary>Gets the sequence numbers of each group that reached the store, in the order the groups arrived.</summary>
    public IReadOnlyList<IReadOnlyList<long>> Groups
    {
        get
        {
            lock (_gate)
            {
                return [.. _groups];
            }
        }
    }

    /// <summary>Gets the sequence number of every event that reached the store alone.</summary>
    public IReadOnlyList<long> Singles
    {
        get
        {
            lock (_gate)
            {
                return [.. _singles];
            }
        }
    }

    /// <summary>Gets the sequence number of every event that reached the store, in the order it was written.</summary>
    public IReadOnlyList<long> WriteOrder
    {
        get
        {
            lock (_gate)
            {
                return [.. _order];
            }
        }
    }

    /// <summary>Gets how many group writes have started, held ones included.</summary>
    public int GroupsEntered => Volatile.Read(ref _groupsEntered);

    /// <summary>Gets or sets whether a group write fails.</summary>
    public bool FailGroups { get; set; }

    /// <summary>Gets or sets what the store reads from the context a group write runs in; the result is kept in <see cref="Observed"/>.</summary>
    public Func<object?>? Probe { get; set; }

    /// <summary>Gets what <see cref="Probe"/> returned for each group write.</summary>
    public List<object?> Observed { get; } = [];

    /// <summary>Holds every group write that starts from now on, until <see cref="Release"/>.</summary>
    public void Hold()
    {
        lock (_gate)
        {
            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Lets the held group writes go on, and the ones that follow.</summary>
    public void Release()
    {
        lock (_gate)
        {
            _release?.TrySetResult();
        }
    }

    /// <summary>Reads the events the inner store holds for a run.</summary>
    /// <param name="runId">The run.</param>
    /// <returns>The events, in sequence order.</returns>
    public async Task<List<RunEvent>> ReadAsync(Guid runId)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in inner.ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        return events;
    }

    public async ValueTask AppendEventsAsync(IReadOnlyList<RunEvent> events, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _groupsEntered);

        Task? held;

        lock (_gate)
        {
            held = _release?.Task;
        }

        if (Probe is not null)
        {
            lock (_gate)
            {
                Observed.Add(Probe());
            }
        }

        if (held is not null)
        {
            await held.ConfigureAwait(false);
        }

        if (FailGroups)
        {
            throw new InvalidOperationException("the store is unavailable");
        }

        await inner.AppendEventsAsync(events, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            _groups.Add([.. events.Select(static runEvent => runEvent.Sequence)]);
            _order.AddRange(events.Select(static runEvent => runEvent.Sequence));
        }
    }

    public async ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
    {
        await inner.AppendEventAsync(runEvent, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            _singles.Add(runEvent.Sequence);
            _order.Add(runEvent.Sequence);
        }
    }

    public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default) => inner.StartRunAsync(info, cancellationToken);

    public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _lastSequenceLookups);
        return inner.GetLastEventSequenceAsync(runId, cancellationToken);
    }

    public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default) => inner.CompleteRunAsync(completion, cancellationToken);

    public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default) => inner.GetRunAsync(runId, cancellationToken);

    public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default) => inner.QueryRunsAsync(query, cancellationToken);

    public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken cancellationToken = default) => inner.GetStatisticsAsync(query, cancellationToken);

    public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default) => inner.ReadEventsAsync(runId, fromSequence, cancellationToken);

    public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default) => inner.RecordToolInvocationAsync(invocation, cancellationToken);

    public ValueTask<bool> CompleteLateToolInvocationAsync(LateToolCompletion completion, CancellationToken cancellationToken = default) => inner.CompleteLateToolInvocationAsync(completion, cancellationToken);

    public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default) => inner.ListToolInvocationsAsync(runId, cancellationToken);

    public ValueTask RecordNonTokenUsageAsync(NonTokenUsageRecord record, CancellationToken cancellationToken = default) => inner.RecordNonTokenUsageAsync(record, cancellationToken);

    public ValueTask<IReadOnlyList<NonTokenUsageRecord>> ListNonTokenUsageAsync(Guid runId, CancellationToken cancellationToken = default) => inner.ListNonTokenUsageAsync(runId, cancellationToken);

    public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default) => inner.GetToolUsageAsync(query, cancellationToken);

    public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default) => inner.GetExperimentResultsAsync(query, cancellationToken);

    public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default) => inner.GetTimeSeriesAsync(query, cancellationToken);

    public ValueTask UpdateRunCostAsync(Guid runId, RunCost? cost, string? tenantId, CancellationToken cancellationToken = default) => inner.UpdateRunCostAsync(runId, cost, tenantId, cancellationToken);

    public ValueTask<IReadOnlyList<RunCancellationRequest>> TouchHeartbeatAsync(IReadOnlyCollection<Guid> runIds, CancellationToken cancellationToken = default) => inner.TouchHeartbeatAsync(runIds, cancellationToken);

    public ValueTask<bool> RequestCancellationAsync(Guid runId, string tenantId, DateTimeOffset at, CancellationToken cancellationToken = default)
        => inner.RequestCancellationAsync(runId, tenantId, at, cancellationToken);

    public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(TimeSpan staleAfter, int max, CancellationToken cancellationToken = default) => inner.ClaimOrphanedRunsAsync(staleAfter, max, cancellationToken);
}
