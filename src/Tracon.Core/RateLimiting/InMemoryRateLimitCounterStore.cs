using System.Collections.Concurrent;

namespace Tracon;

/// <summary>A counter store that keeps the windows in process memory.</summary>
/// <remarks>
/// <para>
/// The default store. It is the right one for a single process, and for tests. With several
/// instances each one counts alone, so <see cref="RateLimitScope.Deployment"/> then limits per
/// instance, as <see cref="RateLimitScope.Instance"/> does. Every SQL provider package replaces
/// it with a store that all instances share.
/// </para>
/// <para>
/// One entry per partition that has a window in progress. The partition key carries the tenant, and an
/// installation that accepts any tenant identifier lets a caller make up partitions, so an entry whose window
/// is over is removed (<see cref="RemoveEndedWindows"/>): its count would restart at one anyway.
/// </para>
/// </remarks>
internal sealed class InMemoryRateLimitCounterStore : IRateLimitCounterStore
{
    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private long _nextSweepTicks;

    /// <summary>Gets how many partitions have an entry.</summary>
    internal int TrackedPartitions => _windows.Count;

    /// <summary>
    /// Gets or sets an action that runs between the lookup of a partition's entry and the lock on it: the point
    /// where a sweep can remove the entry. A test sets it to make that interleaving happen on purpose; it is
    /// <see langword="null"/> in production.
    /// </summary>
    internal Action? AfterLookup { get; set; }

    /// <inheritdoc />
    public ValueTask<RateLimitWindowCount> IncrementAsync(
        string partitionKey,
        DateTimeOffset windowStart,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(partitionKey);
        cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            var window = _windows.GetOrAdd(partitionKey, static (_, start) => new Window(start), windowStart);

            AfterLookup?.Invoke();

            lock (window)
            {
                // The sweep removed this entry between the lookup and the lock: count in the entry that
                // replaces it, or the partition would have a count nobody reads.
                if (window.Removed)
                {
                    continue;
                }

                // A later window moves the stored one forward and restarts the count. An earlier one
                // (a slow clock) is counted in the stored window; the window never moves back.
                if (windowStart > window.Start)
                {
                    window.Start = windowStart;
                    window.Count = 0;
                }

                window.Count++;

                return new ValueTask<RateLimitWindowCount>(
                    new RateLimitWindowCount { WindowStart = window.Start, Count = window.Count });
            }
        }
    }

    /// <summary>
    /// Removes the entries whose window has ended, at most once per <paramref name="longestWindow"/>.
    /// </summary>
    /// <param name="now">The time the caller reads.</param>
    /// <param name="longestWindow">
    /// The longest window any partition of this process uses. The store cannot tell a partition's own window from
    /// the entry (it keeps the start only), so it removes by the longest one: an entry is removed only when even
    /// that window is over, and a longer window than the partition's own keeps an entry a little longer, never a
    /// shorter time.
    /// </param>
    internal void RemoveEndedWindows(DateTimeOffset now, TimeSpan longestWindow)
    {
        var due = Volatile.Read(ref _nextSweepTicks);

        // A floor of a second: a window of a few milliseconds must not turn every request into a scan.
        var interval = Math.Max(longestWindow.Ticks, TimeSpan.TicksPerSecond);

        if (now.UtcTicks < due
            || Interlocked.CompareExchange(ref _nextSweepTicks, now.UtcTicks + interval, due) != due)
        {
            return;
        }

        foreach (var pair in _windows)
        {
            lock (pair.Value)
            {
                // A window that starts in the future (a clock that jumped ahead) is not over, and stays.
                if (pair.Value.Start + longestWindow <= now)
                {
                    pair.Value.Removed = true;
                    _windows.TryRemove(pair);
                }
            }
        }
    }

    private sealed class Window(DateTimeOffset start)
    {
        public DateTimeOffset Start = start;

        public long Count;

        public bool Removed;
    }
}
