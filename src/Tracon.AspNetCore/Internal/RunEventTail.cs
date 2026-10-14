using System.Runtime.CompilerServices;

namespace Tracon;

/// <summary>
/// Reads a run's events from the store as they are written, until the run ends.
/// </summary>
/// <remarks>
/// <para>
/// The event store offers no notification channel, so a run in progress is followed by
/// polling. Order: <em>the status is read first</em>, then the events are drained. The other
/// way around, a run that completes between the two steps could have its final events left
/// unread when the loop ends.
/// </para>
/// <para>
/// One reader for every stream that follows a run from the store
/// (<c>GET /api/runs/{runId}/events</c>, and a repeated workflow answer that follows the run
/// the first one started): two hand-written copies of this loop would drift apart. The frame
/// names belong to each stream; this type only reads.
/// </para>
/// </remarks>
internal static class RunEventTail
{
    /// <summary>Reads the events of a run from <paramref name="next"/> onwards.</summary>
    /// <param name="runs">The run store.</param>
    /// <param name="runId">The run.</param>
    /// <param name="next">The first sequence number to read.</param>
    /// <param name="pollInterval">How long to wait between two reads of a run in progress.</param>
    /// <param name="idleAsync">
    /// Called after a read of a run in progress that found no new event, before the wait; a
    /// stream writes its keep-alive here.
    /// </param>
    /// <param name="missingRowGrace">
    /// How long a run whose row does not exist yet is waited for. <see cref="TimeSpan.Zero"/>
    /// ends the read at once, which is right for a run that was asked for by id: no row means
    /// the run is gone.
    /// </param>
    /// <param name="cancellationToken">The cancellation token; it ends the read.</param>
    /// <returns>The events, in sequence order, together with the run as it was read last.</returns>
    /// <remarks>
    /// The read ends when the row is gone, or its status is no longer
    /// <see cref="RunStatus.Running"/> or <see cref="RunStatus.Queued"/>: <c>Queued</c> is an expected
    /// intermediate status, since a worker may not have picked the job up yet.
    /// </remarks>
    public static async IAsyncEnumerable<(RunEvent Event, RunRecord? Run)> ReadAsync(
        IRunStore runs,
        Guid runId,
        long next,
        TimeSpan pollInterval,
        Func<CancellationToken, ValueTask> idleAsync,
        TimeSpan missingRowGrace,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(idleAsync);

        var missingSince = (long?)null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var snapshot = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);
            var readAny = false;

            await foreach (var runEvent in runs
                               .ReadEventsAsync(runId, next, cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return (runEvent, snapshot);

                next = runEvent.Sequence + 1;
                readAny = true;
            }

            if (snapshot is null)
            {
                missingSince ??= Environment.TickCount64;

                if (Environment.TickCount64 - missingSince.Value >= (long)missingRowGrace.TotalMilliseconds)
                {
                    break;
                }
            }
            else if (snapshot.Status is not (RunStatus.Running or RunStatus.Queued))
            {
                break;
            }

            if (!readAny)
            {
                await idleAsync(cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
