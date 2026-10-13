namespace Tracon;

/// <summary>
/// The stable codes a job's <see cref="JobRecord.ErrorMessage"/> carries when
/// the failure comes from the queue itself rather than from a handler.
/// </summary>
/// <remarks>
/// A code is machine readable and never changes; the prose around it may. A
/// consumer that has to tell "nobody handles this key" apart from "the
/// handler threw" matches on the code, not on the sentence.
/// </remarks>
public static class JobErrorCodes
{
    /// <summary>
    /// No <see cref="IJobHandler"/> is registered for the job's
    /// <see cref="JobRecord.HandlerKey"/>, so the job failed without ever
    /// being executed.
    /// </summary>
    /// <remarks>
    /// The persisted message deliberately does NOT repeat the key: an
    /// unregistered key may have come from an untrusted source, and
    /// <see cref="JobRecord.ErrorMessage"/> is read back over HTTP and shown
    /// in the UI. The raw key goes to the log only, tagged with the same
    /// correlation id the message carries.
    /// </remarks>
    public const string UnknownHandlerKey = "tracon.job.unknown-handler-key";

    /// <summary>
    /// The handler registered for the job's
    /// <see cref="JobRecord.HandlerKey"/> could not be constructed — one of
    /// its dependencies is missing from the container — so the job failed
    /// without ever being executed.
    /// </summary>
    /// <remarks>
    /// A configuration mistake, not a transient one: retrying cannot fix a
    /// dependency that is not registered. The job is therefore failed rather
    /// than released, so it does not re-lease forever. The exception's own
    /// detail goes to the log, under the correlation id the message carries.
    /// </remarks>
    public const string HandlerActivationFailed = "tracon.job.handler-activation-failed";

    /// <summary>
    /// The job was leased again after its lease ran out — the worker running
    /// it stopped renewing, most often because its process died — and the new
    /// attempt is past the job's attempt limit, so the job failed without
    /// running again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The limit is <see cref="JobRecord.MaxAttempts"/>, or the scheduling
    /// <c>MaxAttempts</c> setting when the job carries none. A lease that runs
    /// out counts as an attempt, because the work it covered may have had side
    /// effects; a job limited to one attempt is never run twice.
    /// </para>
    /// <para>
    /// The handler never ran, so a record the handler would have closed — a
    /// queued agent run, say — keeps the status it had when the earlier attempt
    /// stopped. Run reconciliation closes such a run.
    /// </para>
    /// <para>
    /// The earlier worker is usually dead, but it may only have lost its lease
    /// (its renewals kept failing). If it then finishes, its result does not
    /// change the job: a job that reached a final status keeps it.
    /// </para>
    /// </remarks>
    public const string AttemptsExhausted = "tracon.job.attempts-exhausted";

    /// <summary>
    /// The attempt ran longer than the job's time limit
    /// (<see cref="JobRecord.MaxDuration"/>, or
    /// <c>TraconSchedulingOptions.MaxDurationByHandlerKey</c> for its key), the worker
    /// cancelled the handler's token, and the handler stopped with an error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The attempt counts like any failed one: the job is leased again while attempts
    /// are left, and fails with this code on its last. The limit is per attempt, so
    /// the next attempt gets the full time again.
    /// </para>
    /// <para>
    /// A run the handler had started is cancelled with the token, so it ends
    /// <c>Canceled</c>. The built-in workflow handlers treat the limit as the end of
    /// the work, not as an interruption: a queued workflow run's job fails at once with
    /// this code, a workflow batch fails the item whose run was stopped (and the job, with
    /// this code, when no item is left for another attempt), and recovery
    /// (<c>Tracon:Workflows:ResumeInterruptedRuns</c>) never continues a run the limit
    /// stopped. Its row carries the error type <c>job_max_duration_exceeded</c>.
    /// </para>
    /// </remarks>
    public const string MaxDurationExceeded = "tracon.job.max-duration-exceeded";

    /// <summary>Formats the persisted message for <paramref name="code"/>.</summary>
    /// <param name="code">One of this class's constants.</param>
    /// <param name="correlationId">
    /// The id also written to the log entry carrying the untruncated detail
    /// (Tracon uses a short random hexadecimal id).
    /// </param>
    /// <returns>The message to persist.</returns>
    /// <exception cref="ArgumentException"><paramref name="code"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="correlationId"/> is <see langword="null"/>.</exception>
    public static string Format(string code, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(correlationId);

        return $"{code} (ref: {correlationId})";
    }
}
