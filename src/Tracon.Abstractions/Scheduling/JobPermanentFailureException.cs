namespace Tracon;

/// <summary>
/// The exception an <see cref="IJobHandler"/> throws to fail its job on this attempt, whatever attempts are
/// left, because it knows another attempt would get the same answer.
/// </summary>
/// <remarks>
/// <para>
/// Any other exception a handler throws counts as a failed attempt, and the job is leased again until
/// <see cref="TraconSchedulingOptions.MaxAttempts"/> (or <see cref="JobRecord.MaxAttempts"/>) is spent. That
/// is the right answer for a network error. It is the wrong one for a rule of your own that cannot change
/// between attempts (the order the job is about does not exist, the input is malformed): each repeat pays for
/// the work in front of the check again. Throw this exception there.
/// </para>
/// <para>
/// The job ends <see cref="JobStatus.Failed"/> and <see cref="JobRecord.Attempt"/> stays at the attempt that
/// threw. The persisted <see cref="JobRecord.ErrorMessage"/> is <see cref="Code"/> and a short reference
/// (<c>order.not-found (ref: 1f3a9c2e)</c>), nothing else. <see cref="Reason"/> and the inner exception go to
/// the log under the same reference, because the message is stored, returned over HTTP and shown in the UI.
/// </para>
/// <para>
/// Only the job worker reads this exception. A workflow's node retry and an online evaluation do not treat it
/// as a failure that repeats the same answer. Cancellation wins over it: a job that was cancelled stays
/// <see cref="JobStatus.Cancelled"/>.
/// </para>
/// <example>
/// <code>
/// public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
/// {
///     var order = await orders.FindAsync(context.Job.TargetName, cancellationToken);
///
///     if (order is null)
///     {
///         // The order will not exist on the next attempt either.
///         throw new JobPermanentFailureException(
///             "order.not-found",
///             reason: $"Order {context.Job.TargetName} is not in the database.");
///     }
///
///     // ...
/// }
/// </code>
/// </example>
/// </remarks>
public sealed class JobPermanentFailureException : TraconException
{
    private const int MaxCodeLength = 64;

    /// <summary>
    /// The stable value written for <see cref="TraconException.ErrorType"/>.
    /// </summary>
    public const string JobPermanentFailureErrorType = TraconErrorTypes.JobPermanentFailure;

    /// <summary>Creates the exception.</summary>
    /// <param name="code">
    /// The stable code the job's error message starts with: 1 to 64 characters, lowercase ASCII letters, digits,
    /// <c>.</c>, <c>_</c> or <c>-</c> (<c>order.not-found</c>). A caller that matches on a job's error matches on this.
    /// </param>
    /// <param name="reason">
    /// Free text for the log only (it may name an order or a customer). It is never written to the job's error message.
    /// </param>
    /// <param name="innerException">The error that led to the decision, for the log; <see langword="null"/> when there is none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> is not a valid code.</exception>
    public JobPermanentFailureException(string code, string? reason = null, Exception? innerException = null)
        : base(DescribeCode(code), innerException)
    {
        Code = code;
        Reason = reason;
    }

    /// <summary>Gets the stable code the job's error message starts with.</summary>
    public string Code { get; }

    /// <summary>Gets the free text that goes to the log, or <see langword="null"/> when none was given.</summary>
    /// <remarks>It is never persisted: the job's error message carries <see cref="Code"/> and a reference only.</remarks>
    public string? Reason { get; }

    /// <inheritdoc />
    public override string ErrorType => JobPermanentFailureErrorType;

    /// <summary>Checks a code and builds the exception's message from it.</summary>
    /// <remarks>
    /// The message names the code and nothing else: a Tracon exception's message is persisted as it is where an
    /// exception reaches a run record, so free text must not enter it.
    /// </remarks>
    private static string DescribeCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (!IsValidCode(code))
        {
            throw new ArgumentException(
                $"A job failure code must be 1 to {MaxCodeLength} characters: lowercase ASCII letters, digits, '.', '_' or '-' " +
                "(for example 'order.not-found').",
                nameof(code));
        }

        return $"The job failed permanently with code '{code}'.";
    }

    private static bool IsValidCode(string code)
    {
        if (code.Length is 0 or > MaxCodeLength)
        {
            return false;
        }

        foreach (var character in code)
        {
            if (character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
