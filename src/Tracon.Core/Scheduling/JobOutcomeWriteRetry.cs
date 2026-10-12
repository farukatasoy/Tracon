using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Tries a store write again when it records the outcome of work a job handler
/// has already finished, with a pause that doubles each time.
/// </summary>
/// <remarks>
/// <para>
/// The worker's own rule, applied inside a handler: the work's own <c>try</c>
/// holds the work and nothing else. A store error while recording a finished
/// step belongs to the store, not to the step. It must not be recorded as the
/// step's failure, and it must not make the worker run the step again: a second
/// model call for an eval case, a second delivery of a webhook.
/// </para>
/// <para>
/// Every write must be safe to repeat. Each caller writes a record that a
/// second, identical write leaves as it was.
/// </para>
/// <para>
/// One instance is one window. It opens when the instance is created, which is
/// when the work finished, and every write run through it shares it. The window
/// is one lease renewal interval, a third of
/// <see cref="TraconSchedulingOptions.LeaseDuration"/> (the interval the worker
/// renews at). The worker renews the lease while the handler runs, so while
/// those renewals succeed the window ends well inside the lease. When the
/// window runs out, <see cref="RunAsync"/> throws the last store error on, and
/// the caller decides what that means for its work.
/// </para>
/// </remarks>
internal sealed class JobOutcomeWriteRetry
{
    /// <summary>The pause before the second try.</summary>
    internal static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>The longest pause between two tries.</summary>
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(5);

    /// <summary>The window used when the lease duration gives none.</summary>
    private static readonly TimeSpan FallbackWindow = TimeSpan.FromSeconds(30);

    private readonly Guid _jobId;
    private readonly TimeProvider _clock;
    private readonly ILogger? _logger;
    private readonly TimeSpan _window;
    private readonly long _openedAt;

    /// <summary>Opens the window for one job's outcome writes.</summary>
    /// <param name="jobId">The job whose work is being recorded, for the log.</param>
    /// <param name="options">The scheduling options; <see langword="null"/> uses their defaults.</param>
    /// <param name="clock">The clock that measures the window and the pauses.</param>
    /// <param name="logger">The caller's logger; <see langword="null"/> logs nothing.</param>
    public JobOutcomeWriteRetry(Guid jobId, TraconSchedulingOptions? options, TimeProvider clock, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _jobId = jobId;
        _clock = clock;
        _logger = logger;
        _window = WindowFor(options);
        _openedAt = clock.GetTimestamp();
    }

    /// <summary>
    /// Gets how long the tries may go on: one lease renewal interval.
    /// </summary>
    /// <param name="options">The scheduling options; <see langword="null"/> uses their defaults.</param>
    /// <returns>A third of the lease duration, or 30 seconds when that is not positive.</returns>
    public static TimeSpan WindowFor(TraconSchedulingOptions? options)
    {
        // The same interval, and the same fallback, the worker computes when it
        // starts renewing a job's lease.
        var window = (options ?? new TraconSchedulingOptions()).LeaseDuration / 3;

        return window > TimeSpan.Zero ? window : FallbackWindow;
    }

    /// <summary>
    /// Runs the write, and tries it again after a store error until it succeeds
    /// or the window runs out.
    /// </summary>
    /// <param name="write">The store write. It carries whatever token the caller chose for it.</param>
    /// <param name="step">What the write records, for the log: "record eval run 123's outcome".</param>
    /// <param name="cancellationToken">Ends the pauses between tries (the worker's shutdown token).</param>
    /// <returns>A task that completes once the write succeeded.</returns>
    /// <remarks>
    /// The last store error is thrown on when the window has run out; a write
    /// that starts after that is tried once. A cancellation of
    /// <paramref name="cancellationToken"/> during a pause is thrown on too.
    /// Each failed try that is followed by another is logged as a warning;
    /// the final error is left to the caller to log.
    /// </remarks>
    public async ValueTask RunAsync(Func<ValueTask> write, string step, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);

        var delay = FirstDelay;

        while (true)
        {
            try
            {
                await write().ConfigureAwait(false);

                return;
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                var left = _window - _clock.GetElapsedTime(_openedAt);

                if (left <= TimeSpan.Zero)
                {
                    throw;
                }

                if (delay > left)
                {
                    delay = left;
                }

                if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(exception, "Job {JobId}: could not {Step}; trying again in {Delay}.", _jobId, step, delay);
                }
            }

            await Task.Delay(delay, _clock, cancellationToken).ConfigureAwait(false);

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks));
        }
    }
}
