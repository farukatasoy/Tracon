using System.Collections.Concurrent;

namespace Tracon;

/// <summary>An in-memory implementation of <see cref="IRunCancellationRegistry"/>.</summary>
internal sealed class RunCancellationRegistry : IRunCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    /// <inheritdoc />
    public int ActiveCount => _entries.Count;

    /// <inheritdoc />
    public IReadOnlyCollection<Guid> ActiveRunIds => [.. _entries.Keys];

    /// <inheritdoc />
    public IDisposable Register(Guid runId, Guid rootRunId, string? tenantId, CancellationTokenSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _entries[runId] = new Entry(runId, rootRunId, tenantId, source);

        return new Registration(this, runId);
    }

    /// <inheritdoc />
    public bool TryCancel(Guid runId, string? tenantId)
    {
        if (!_entries.TryGetValue(runId, out var entry) || !TenantMatches(entry.TenantId, tenantId))
        {
            return false;
        }

        // 🚨 A run disposes its source right after it releases its entry, so an entry
        // read here can already carry a disposed source. That run has ended: report
        // "not cancelled" and keep going, so one finished branch does not leave its
        // siblings uncancelled.
        var reached = entry.Source.TryCancel();

        // Only cancellation of the root run propagates through the tree. Cancelling
        // a child run alone (RunId != RootRunId) does not affect sibling branches or
        // the root. The root sees that branch as Failed and continues.
        if (entry.RunId == entry.RootRunId)
        {
            foreach (var candidate in _entries.Values)
            {
                if (candidate.RunId != runId && candidate.RootRunId == runId)
                {
                    candidate.Source.TryCancel();
                }
            }
        }

        return reached;
    }

    private void Release(Guid runId) => _entries.TryRemove(runId, out _);

    private static bool TenantMatches(string? registered, string? requested)
        => string.Equals(registered, requested, StringComparison.Ordinal);

    private readonly record struct Entry(Guid RunId, Guid RootRunId, string? TenantId, CancellationTokenSource Source);

    private sealed class Registration(RunCancellationRegistry registry, Guid runId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                registry.Release(runId);
            }
        }
    }
}
