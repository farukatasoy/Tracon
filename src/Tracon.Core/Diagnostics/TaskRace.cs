namespace Tracon;

/// <summary>
/// Waits for a task, a hard cutoff, or the caller's cancellation, whichever comes first, and leaves nothing running.
/// </summary>
/// <remarks>
/// <para>
/// A body that ignores its token cannot be waited on directly, so callers race it against a timer and
/// against the caller's token. Written as two <c>Task.Delay</c> calls that are never cancelled, the race
/// left the timer scheduled for the whole wait limit after the work had won, together with a registration
/// on the caller's token: a loop that raced once per update left N of each until the limit passed.
/// </para>
/// <para>
/// Both waiting tasks are cancelled as soon as one task has won. The cutoff is not linked to the caller's
/// token, so an elapsed timeout is never taken for the caller's own cancellation.
/// </para>
/// </remarks>
internal static class TaskRace
{
    /// <summary>Waits for <paramref name="work"/>, the cutoff, or the caller.</summary>
    /// <param name="work">What is waited for. Its outcome is not read here.</param>
    /// <param name="wait">How long <paramref name="work"/> may take.</param>
    /// <param name="timeProvider">The clock of the cutoff.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="work"/> finished first; <see langword="false"/> when the cutoff
    /// elapsed or the caller cancelled first (the caller's token tells which).
    /// </returns>
    public static async Task<bool> FinishedFirstAsync(
        Task work,
        TimeSpan wait,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(timeProvider);

        using var finished = new CancellationTokenSource();
        using var callerOrFinished = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, finished.Token);

        var cutoff = Task.Delay(wait, timeProvider, finished.Token);
        var callerCancellation = Task.Delay(Timeout.InfiniteTimeSpan, callerOrFinished.Token);

        var winner = await Task.WhenAny(work, cutoff, callerCancellation).ConfigureAwait(false);

        await finished.CancelAsync().ConfigureAwait(false);

        return ReferenceEquals(winner, work);
    }
}
