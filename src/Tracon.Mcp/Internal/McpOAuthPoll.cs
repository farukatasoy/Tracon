namespace Tracon;

/// <summary>Looks at the distributed cache again and again until a value shows up or the time is over.</summary>
/// <remarks>
/// The distributed cache has no change notification, so an instance that waits for a value another
/// instance writes has to look. Every interval and limit comes from <see cref="McpOAuthDistributedTiming"/>:
/// constants of the design that no operator sets.
/// </remarks>
internal static class McpOAuthPoll
{
    /// <summary>Calls <paramref name="probe"/> until it returns a value, or until <paramref name="limit"/> has passed.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="probe">Looks for the value; <see langword="null"/> means "not yet".</param>
    /// <param name="interval">The time between two looks.</param>
    /// <param name="limit">The longest time to wait; <see langword="null"/> waits until cancellation.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The first value; <see langword="null"/> when the limit passed first.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static async Task<T?> UntilAsync<T>(
        Func<CancellationToken, ValueTask<T?>> probe,
        TimeSpan interval,
        TimeSpan? limit,
        TimeProvider clock,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(clock);

        var deadline = limit is { } wait ? clock.GetUtcNow() + wait : (DateTimeOffset?)null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await probe(cancellationToken).ConfigureAwait(false) is { } found)
            {
                return found;
            }

            var delay = interval;

            if (deadline is { } end)
            {
                var remaining = end - clock.GetUtcNow();

                if (remaining <= TimeSpan.Zero)
                {
                    return null;
                }

                if (remaining < delay)
                {
                    delay = remaining;
                }
            }

            await Task.Delay(delay, clock, cancellationToken).ConfigureAwait(false);
        }
    }
}
