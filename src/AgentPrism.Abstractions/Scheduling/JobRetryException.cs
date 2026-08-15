namespace AgentPrism;

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
public sealed class JobRetryException : AgentPrismException
{
    /// <summary>
    /// The stable value written for <see cref="AgentPrismException.ErrorType"/>.
    /// </summary>
    public const string JobRetryErrorType = "job_retry";

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
    public TimeSpan? RetryAfter { get; init; }

    /// <inheritdoc />
    public override string ErrorType => JobRetryErrorType;
}
