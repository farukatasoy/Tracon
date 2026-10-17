namespace Tracon;

/// <summary>
/// Cancels a <see cref="CancellationTokenSource"/> whose owner may dispose it at the
/// same moment.
/// </summary>
/// <remarks>
/// A registry or a host keeps sources that finished work disposes. Reading the
/// collection and cancelling are two steps, so a source can be disposed in between;
/// <see cref="CancellationTokenSource.Cancel()"/> then throws
/// <see cref="ObjectDisposedException"/>. A disposed source means the work already
/// ended, which is what the cancel asked for. Every site that cancels a source it
/// does not own goes through here.
/// </remarks>
internal static class SafeCancellation
{
    /// <summary>Cancels <paramref name="source"/> unless its owner already disposed it.</summary>
    /// <param name="source">The source to cancel.</param>
    /// <returns><see langword="true"/> if the source was live when cancelled.</returns>
    public static bool TryCancel(this CancellationTokenSource source)
    {
        try
        {
            source.Cancel();

            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Cancels every source in a snapshot. A source disposed since the snapshot was
    /// taken is skipped; it does not stop the rest from being cancelled.
    /// </summary>
    /// <param name="sources">The snapshot to cancel.</param>
    /// <returns>The completion task.</returns>
    public static async ValueTask CancelAllAsync(IEnumerable<CancellationTokenSource> sources)
    {
        foreach (var source in sources)
        {
            try
            {
                await source.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Finished and disposed since the snapshot: nothing left to cancel.
            }
        }
    }
}
