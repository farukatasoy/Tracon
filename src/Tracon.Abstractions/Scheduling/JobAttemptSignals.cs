namespace Tracon;

/// <summary>Why the worker cancelled the token it gave a job's handler.</summary>
internal enum JobStopReason
{
    /// <summary>The token is not cancelled.</summary>
    None = 0,

    /// <summary>The worker is shutting down; the job is left to its lease.</summary>
    WorkerStopping = 1,

    /// <summary>The job was cancelled, or its record is gone.</summary>
    Cancelled = 2,

    /// <summary>The attempt reached the job's time limit (<see cref="JobRecord.MaxDuration"/>).</summary>
    MaxDurationExceeded = 3,
}

/// <summary>
/// The three sources behind the token one job attempt's handler receives, kept apart so
/// a reader can say which one fired.
/// </summary>
/// <remarks>
/// <para>
/// The handler is given ONE token, linked to all three. A cancelled token is a question,
/// not an answer: the built-in workflow handlers need the answer, because a shutdown
/// leaves the work to the lease, a cancel ends the job, and a time limit ends the work
/// without a continuation.
/// </para>
/// <para>
/// When more than one source fired, the order below decides: a worker that is stopping
/// leaves the job to its lease whatever else happened, and a cancelled job stays
/// cancelled whatever the clock says.
/// </para>
/// </remarks>
// K-737 (an OperationCanceledException is a question) and Phase 196 open question 13-A
// (a time limit is its own reason; recovery never continues the run it stopped).
internal sealed class JobAttemptSignals
{
    /// <summary>
    /// The error type a run carries when the job running it reached its time limit and
    /// cancelled it. A recovery plan reads it: such a run is never continued.
    /// </summary>
    public const string MaxDurationExceededRunErrorType = "job_max_duration_exceeded";

    private readonly CancellationToken _cancelled;
    private readonly CancellationToken _deadline;

    /// <summary>Creates the signals of one attempt.</summary>
    /// <param name="maxDuration">The attempt's time limit; <see langword="null"/> when it has none.</param>
    /// <param name="workerStopping">Cancelled when the worker shuts down.</param>
    /// <param name="cancelled">Cancelled when the worker finds the job cancelled or gone.</param>
    /// <param name="deadline">Cancelled when the attempt reaches the job's time limit.</param>
    public JobAttemptSignals(
        TimeSpan? maxDuration,
        CancellationToken workerStopping,
        CancellationToken cancelled,
        CancellationToken deadline)
    {
        MaxDuration = maxDuration;
        WorkerStopping = workerStopping;
        _cancelled = cancelled;
        _deadline = deadline;
    }

    /// <summary>Gets the token the worker's shutdown cancels.</summary>
    public CancellationToken WorkerStopping { get; }

    /// <summary>Gets the attempt's time limit; <see langword="null"/> when it has none.</summary>
    public TimeSpan? MaxDuration { get; }

    /// <summary>Gets why the handler's token is cancelled, read now.</summary>
    public JobStopReason Reason
        => WorkerStopping.IsCancellationRequested ? JobStopReason.WorkerStopping
            : _cancelled.IsCancellationRequested ? JobStopReason.Cancelled
            : _deadline.IsCancellationRequested ? JobStopReason.MaxDurationExceeded
            : JobStopReason.None;

    /// <summary>Builds the error a run stopped by this attempt's time limit closes with.</summary>
    /// <param name="what">What was stopped, as the start of the sentence ("Workflow run").</param>
    /// <returns>The run error. Its class is filled where the run closes.</returns>
    public RunError ToRunError(string what)
        => new()
        {
            Type = MaxDurationExceededRunErrorType,
            Message = $"{what} was stopped: the job running it reached its time limit" +
                      (MaxDuration is { } limit ? $" ({limit})" : string.Empty) +
                      ". Raise the job's 'maxDuration' or 'Tracon:Scheduling:MaxDurationByHandlerKey', " +
                      "or shorten the work.",
        };
}

/// <summary>
/// Carries the current job attempt's <see cref="JobAttemptSignals"/> to the runs the
/// attempt starts, so a run cancelled by the job's time limit can name it.
/// </summary>
/// <remarks>
/// <para>
/// The worker begins the scope in the body of the method that calls the handler, the
/// same way it opens the job's tenant scope: an <see cref="AsyncLocal{T}"/> written in a
/// helper does not flow back to its caller.
/// </para>
/// <para>
/// Read where a run closes, and only to name a cancellation the run already knows came
/// from its caller. It never changes a run's status.
/// </para>
/// </remarks>
internal static class AmbientJobAttempt
{
    private static readonly AsyncLocal<JobAttemptSignals?> Slot = new();

    /// <summary>Gets the signals of the job attempt that is running in this flow, if any.</summary>
    public static JobAttemptSignals? Current => Slot.Value;

    /// <summary>Makes <paramref name="signals"/> current until the result is disposed.</summary>
    /// <param name="signals">The attempt's signals.</param>
    /// <returns>The scope; disposing it restores the previous value.</returns>
    public static IDisposable Begin(JobAttemptSignals signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        var previous = Slot.Value;
        Slot.Value = signals;

        return new Scope(previous);
    }

    private sealed class Scope(JobAttemptSignals? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Slot.Value = previous;
        }
    }
}
