using System.Runtime.CompilerServices;

namespace Tracon;

/// <summary>
/// The append-only event log and the tool invocation log.
/// </summary>
internal sealed partial class InMemoryRunStore
{
    /// <inheritdoc />
    public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_events.TryGetValue(runEvent.RunId, out var log))
        {
            throw new TraconException(
                $"No run with id '{runEvent.RunId}' was found. StartRunAsync must be called before adding an event.");
        }

        // EXPECTED tenant check (K-355). SAME behavior as the SQL store;
        // contract tests exercise both implementations against the same assertion.
        EnsureExpectedTenant(runEvent.RunId, runEvent.TenantId, "The event was not written.");

        lock (log)
        {
            // A duplicate Sequence is a caller error (RunEventWriter assigns
            // it once, per run), not a legitimate retry -- rejecting it here
            // matches the SQL stores, which reject the same case as a
            // primary-key violation on (run_id, seq). Silently accepting it
            // a second time would leave an invisible gap in the append-only
            // stream.
            foreach (var existing in log)
            {
                if (existing.Sequence == runEvent.Sequence)
                {
                    throw new TraconException(
                        $"Run '{runEvent.RunId}' already has an event with sequence '{runEvent.Sequence}'. " +
                        "The event was not written.");
                }
            }

            log.Add(runEvent);
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<long?> GetLastEventSequenceAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_events.TryGetValue(runId, out var log))
        {
            return ValueTask.FromResult<long?>(null);
        }

        lock (log)
        {
            long? highest = null;

            foreach (var existing in log)
            {
                if (highest is null || existing.Sequence > highest)
                {
                    highest = existing.Sequence;
                }
            }

            return ValueTask.FromResult(highest);
        }
    }

    /// <inheritdoc />
    public ValueTask RecordToolInvocationAsync(
        ToolInvocationRecord invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        // EXPECTED tenant check (K-355).
        EnsureExpectedTenant(invocation.RunId, invocation.TenantId, "The tool invocation was not written.");

        // If the run was dropped (MaxRuns), the call is silently discarded:
        // the record is for observability and must not interrupt the run.
        if (_toolInvocations.TryGetValue(invocation.RunId, out var log))
        {
            lock (log)
            {
                log.Add(invocation);
            }
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<bool> CompleteLateToolInvocationAsync(
        LateToolCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();

        // 🚨 A tenant mismatch affects no row here rather than throwing, the
        // same shape UpdateRunCostAsync uses: the call settles after its run
        // has been reported done and the caller only logs the outcome.
        if (!OwnedByExpectedTenant(completion.RunId, completion.TenantId) ||
            !_toolInvocations.TryGetValue(completion.RunId, out var log))
        {
            return new ValueTask<bool>(false);
        }

        lock (log)
        {
            for (var index = 0; index < log.Count; index++)
            {
                var record = log[index];

                // The identity is the call, not the row: a run may call the
                // same tool several times and only one of them settled late.
                // An already-settled row is left alone — the same idempotency
                // the SQL statement's `late_completed_at IS NULL` guard gives.
                if (record.LateCompletedAt is not null ||
                    !string.Equals(record.ToolCallId, completion.ToolCallId, StringComparison.Ordinal))
                {
                    continue;
                }

                // `TimedOut` and `Error` are deliberately carried over
                // untouched: they record what the MODEL was told.
                // Every value falls back to what the row already carries, the
                // same COALESCE the SQL statement applies: a tool that reported
                // its usage BEFORE it hung already has that measurement here,
                // and the late write carries none of its own.
                log[index] = record with
                {
                    Result = completion.Result ?? record.Result,
                    Usage = completion.Usage ?? record.Usage,
                    Duration = completion.Duration ?? record.Duration,
                    LateCompletedAt = completion.LateCompletedAt,
                };

                return new ValueTask<bool>(true);
            }
        }

        return new ValueTask<bool>(false);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsOwnedByCurrentTenant(runId) || !_toolInvocations.TryGetValue(runId, out var log))
        {
            return new ValueTask<IReadOnlyList<ToolInvocationRecord>>([]);
        }

        ToolInvocationRecord[] snapshot;

        lock (log)
        {
            snapshot = [.. log];
        }

        Array.Sort(snapshot, static (left, right) => left.CreatedAt.CompareTo(right.CreatedAt));

        return new ValueTask<IReadOnlyList<ToolInvocationRecord>>(snapshot);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RunEvent> ReadEventsAsync(
        Guid runId,
        long fromSequence = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsOwnedByCurrentTenant(runId) || !_events.TryGetValue(runId, out var log))
        {
            yield break;
        }

        RunEvent[] snapshot;

        lock (log)
        {
            snapshot = [.. log];
        }

        foreach (var runEvent in snapshot)
        {
            if (runEvent.Sequence < fromSequence)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            yield return runEvent;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
