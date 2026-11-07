using FrameworkRunStatus = Microsoft.Agents.AI.Workflows.RunStatus;

namespace Tracon;

/// <summary>
/// Ends the wait of a reopened workflow event stream that has nothing left to wait for.
/// </summary>
/// <remarks>
/// <para>
/// The framework's event stream ends at the halt signal the run loop writes when a pass is over. A stream opened after
/// another one has already read that signal finds the loop waiting for input and no signal in the channel: it waits for ever.
/// The pump reopens a stream when the status says pending requests after events were read since an answer, and in about one
/// run in ten thousand the first stream had read the halt of the pass already (measured with two open questions, eight runs at
/// a time).
/// </para>
/// <para>
/// A stream that gets no event for <see cref="Grace"/> is looked at again through the framework's own status. While the run is
/// running, the wait goes on, however long a step takes. When the status says the run is halted (pending requests or idle),
/// nothing can arrive: the loop wakes only for input that this pump sends, and the pump is the one waiting. The stream's own
/// token is cancelled and the wait ends as the end of the stream.
/// </para>
/// </remarks>
internal static class StalledStreamGuard
{
    /// <summary>How long a reopened stream may stay silent before the status is read.</summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Tells whether a status means the run loop waits for input that only the pump can send.
    /// </summary>
    /// <param name="status">The framework's status of the run.</param>
    /// <returns><see langword="true"/> for pending requests and idle.</returns>
    internal static bool IsHalted(FrameworkRunStatus status)
        => status is FrameworkRunStatus.PendingRequests or FrameworkRunStatus.Idle;

    /// <summary>
    /// Waits for the step of a stream that was just asked for, and gives the wait up when the run is halted and the stream
    /// stays silent.
    /// </summary>
    /// <param name="moving">The task of the step (<c>MoveNextAsync</c> of the stream).</param>
    /// <param name="readStatus">Reads the framework's status of the run.</param>
    /// <param name="giveUp">The source behind the stream's token; cancelled when the wait is given up.</param>
    /// <param name="grace">How long the stream may stay silent before the status is read.</param>
    /// <param name="clock">The clock of the delay.</param>
    /// <param name="cancellationToken">The run's token; its cancellation propagates.</param>
    /// <returns>What the step answered, or <see langword="false"/> when the wait was given up.</returns>
    internal static async ValueTask<bool> MoveNextAsync(
        Task<bool> moving,
        Func<ValueTask<FrameworkRunStatus>> readStatus,
        CancellationTokenSource giveUp,
        TimeSpan grace,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(moving);
        ArgumentNullException.ThrowIfNull(readStatus);
        ArgumentNullException.ThrowIfNull(giveUp);
        ArgumentNullException.ThrowIfNull(clock);

        // The stream's own token, read while its source is alive.
        var streamToken = giveUp.Token;

        try
        {
            while (true)
            {
                using var tick = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var silence = Task.Delay(grace, clock, tick.Token);

                if (await Task.WhenAny(moving, silence).ConfigureAwait(false) == moving)
                {
                    await tick.CancelAsync().ConfigureAwait(false);

                    return await AnswerAsync(moving, streamToken, cancellationToken).ConfigureAwait(false);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    // The run was cancelled. The stream's token is linked to the run's, so the step ends by itself: it is
                    // waited for, because a stream that is disposed under a step that still runs throws.
                    return await AnswerAsync(moving, streamToken, cancellationToken).ConfigureAwait(false);
                }

                if (!IsHalted(await readStatus().ConfigureAwait(false)))
                {
                    continue;
                }

                await giveUp.CancelAsync().ConfigureAwait(false);

                return await AnswerAsync(moving, streamToken, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception) when (!moving.IsCompleted)
        {
            // Whatever went wrong here (the status read, a callback on the stream's token), the step must not be left
            // running under a stream the caller disposes next.
            await giveUp.CancelAsync().ConfigureAwait(false);

            try
            {
                await moving.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The step honored the token that was just cancelled.
            }

            throw;
        }
    }

    private static async ValueTask<bool> AnswerAsync(Task<bool> moving, CancellationToken streamToken, CancellationToken cancellationToken)
    {
        try
        {
            return await moving.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (streamToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The stream honored the token this guard cancelled: the wait was given up, and that is the end of the stream.
            return false;
        }
    }
}
