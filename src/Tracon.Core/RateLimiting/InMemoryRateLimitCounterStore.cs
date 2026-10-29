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
/// One entry per partition, so the size is bounded by the number of tenants plus the number of
/// triggers.
/// </para>
/// </remarks>
internal sealed class InMemoryRateLimitCounterStore : IRateLimitCounterStore
{
    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<RateLimitWindowCount> IncrementAsync(
        string partitionKey,
        DateTimeOffset windowStart,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(partitionKey);
        cancellationToken.ThrowIfCancellationRequested();

        var window = _windows.GetOrAdd(partitionKey, static (_, start) => new Window(start), windowStart);

        lock (window)
        {
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

    private sealed class Window(DateTimeOffset start)
    {
        public DateTimeOffset Start = start;

        public long Count;
    }
}
