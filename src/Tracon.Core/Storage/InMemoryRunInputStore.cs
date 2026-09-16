using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// The default store that keeps run inputs in memory.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory implementation is <strong>first class</strong>. Replay
/// works in a deployment without a SQL provider. Records live for the process lifetime,
/// and the oldest record is removed when <see cref="MaxRuns"/> is exceeded.
/// </para>
/// <para>
/// Messages are stored as <em>objects</em> here. Serialization occurs only in the SQL
/// implementation, so polymorphic content is never lost on the in-memory path.
/// </para>
/// </remarks>
internal sealed class InMemoryRunInputStore : IRunInputStore
{
    private readonly ConcurrentDictionary<Guid, RunInputRecord> _inputs = new();
    private readonly ConcurrentQueue<Guid> _insertionOrder = new();

    /// <summary>
    /// The maximum number of inputs to keep in memory. The oldest input is removed
    /// when this limit is exceeded.
    /// </summary>
    public int MaxRuns { get; init; } = 1_000;

    /// <inheritdoc />
    public ValueTask SaveAsync(RunInputRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        // Ignore a second write. A queued run can start twice with the same identifier
        // in Phase 46, and the input must not change.
        if (_inputs.TryAdd(record.RunId, record))
        {
            _insertionOrder.Enqueue(record.RunId);
            TrimIfNeeded();
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<RunInputRecord?> GetAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_inputs.TryGetValue(runId, out var record))
        {
            return new ValueTask<RunInputRecord?>((RunInputRecord?)null);
        }

        // The store also enforces the tenant boundary. "Missing" and "belongs to
        // another tenant" have the same result for the caller and do not leak existence.
        return new ValueTask<RunInputRecord?>(
            string.Equals(record.TenantId, tenantId, StringComparison.Ordinal) ? record : null);
    }

    private void TrimIfNeeded()
    {
        while (_inputs.Count > MaxRuns && _insertionOrder.TryDequeue(out var oldest))
        {
            _inputs.TryRemove(oldest, out _);
        }
    }
}
