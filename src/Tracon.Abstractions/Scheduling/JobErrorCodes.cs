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

    /// <summary>Formats the persisted message for <paramref name="code"/>.</summary>
    /// <param name="code">One of this class's constants.</param>
    /// <param name="correlationId">
    /// The id also written to the log entry carrying the untruncated detail,
    /// from <see cref="SafeErrorText.NewCorrelationId"/>.
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
