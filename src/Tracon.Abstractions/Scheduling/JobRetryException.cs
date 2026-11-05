namespace Tracon;

/// <summary>
/// The exception an <see cref="IJobHandler"/> throws to signal that it wants
/// the job retried after a specific delay.
/// </summary>
/// <remarks>
/// <para>
/// An ordinary exception makes the job re-leasable immediately. A handler
/// that wants backoff throws this instead; the background worker passes the
/// <see cref="RetryAfter"/> value into the
/// <see cref="IJobStore.ReleaseForRetryAsync"/> call.
/// </para>
/// <para>
/// The attempt count is still capped by <see cref="JobRecord.Attempt"/>: this
/// exception does not keep the job alive forever.
/// </para>
/// </remarks>
public sealed class JobRetryException : TraconException
{
    /// <summary>
    /// The stable value written for <see cref="TraconException.ErrorType"/>.
    /// </summary>
    public const string JobRetryErrorType = TraconErrorTypes.JobRetry;

    /// <summary>Creates a new retry request.</summary>
    public JobRetryException()
    {
    }

    /// <summary>Creates a new retry request.</summary>
    /// <param name="message">The error message.</param>
    public JobRetryException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new retry request.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public JobRetryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The time to wait before the next attempt. If <see langword="null"/>,
    /// the job may be re-leased immediately.
    /// </summary>
    /// <remarks>
    /// A store accepts a delay of zero up to <c>2147483647000</c> milliseconds (a little over 68 years). What a handler
    /// asks for is a request and not a call to the store, so a value outside that range is not an error: the worker brings
    /// a negative delay to "at once" and a longer one to 68 years, and writes a warning to the log.
    /// </remarks>
    public TimeSpan? RetryAfter { get; init; }

    /// <inheritdoc />
    public override string ErrorType => JobRetryErrorType;
}
