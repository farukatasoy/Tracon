namespace Tracon;

/// <summary>
/// The exception an <see cref="IJobHandler"/> throws when it stopped before it started any work it cannot take back,
/// so that the attempt it was running is not counted when the worker is shutting down.
/// </summary>
/// <remarks>
/// <para>
/// A job that is interrupted by a host shutdown is left to its lease: it is leased again once the lease runs out, and
/// that lease counts as an attempt, because a worker that crashed may have done work with side effects. For a job
/// limited to one attempt, the attempt that was cut off at shutdown is then the only one, and the next host fails the
/// job with <see cref="JobErrorCodes.AttemptsExhausted"/> without running the handler.
/// </para>
/// <para>
/// Throw this exception when the token was cancelled for the worker stopping and the handler knows it has not begun
/// the work yet (it found the token cancelled before its first call out), or has undone it. The worker then returns the
/// job to the queue at once and gives the attempt back, so the next host runs the job with the attempt count it had.
/// The worker never gives an attempt back on its own: a handler that did begin its work does not throw this, and a
/// job that ran part of its work is leased again as an attempt that was used.
/// </para>
/// <para>
/// The worker honors it only while it is stopping. Thrown at any other time it is an ordinary failed attempt, and the
/// worker writes a warning to the log: a handler cannot use it to run its own job again for free.
/// </para>
/// <example>
/// <code>
/// public sealed class ChargeJobHandler(IPaymentGateway payments) : IJobHandler
/// {
///     public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
///     {
///         if (cancellationToken.IsCancellationRequested)
///         {
///             // The host is stopping and this attempt has paid for nothing yet.
///             throw new JobInterruptedException();
///         }
///
///         await payments.ChargeAsync(context.Job.TargetName, cancellationToken);
///     }
/// }
///
/// // Your own service: the call out that cannot be taken back.
/// public interface IPaymentGateway
/// {
///     ValueTask ChargeAsync(string orderId, CancellationToken cancellationToken);
/// }
/// </code>
/// </example>
/// </remarks>
public sealed class JobInterruptedException : TraconException
{
    /// <summary>
    /// The stable value written for <see cref="TraconException.ErrorType"/>.
    /// </summary>
    public const string JobInterruptedErrorType = TraconErrorTypes.JobInterrupted;

    private const string DefaultMessage = "The job's handler stopped before it began its work.";

    /// <summary>Creates the exception.</summary>
    public JobInterruptedException()
        : base(DefaultMessage)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="reason">
    /// Free text for the log only (why the handler stopped). It is never part of <see cref="Exception.Message"/>, which
    /// is stored when the exception ends a failed attempt.
    /// </param>
    public JobInterruptedException(string reason)
        : base(DefaultMessage)
    {
        Reason = reason;
    }

    /// <summary>Gets the free text that goes to the log, or <see langword="null"/> when none was given.</summary>
    public string? Reason { get; }

    /// <inheritdoc />
    public override string ErrorType => JobInterruptedErrorType;
}
