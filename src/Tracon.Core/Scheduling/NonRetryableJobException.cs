namespace Tracon;

/// <summary>
/// Thrown by a built-in job handler whose work is done, when a store write that
/// records that work kept failing until the lease window closed. The worker
/// fails the job at once and does not run the handler again.
/// </summary>
/// <remarks>
/// <para>
/// Running the handler again would repeat the work it already did: for a
/// queued run, a second model call and its tool side effects. So the job
/// fails on this attempt, whatever attempts are left.
/// </para>
/// <para>
/// The message is <see cref="JobErrorCodes.Format"/>'s: a stable code and the
/// correlation id of the ONE log entry that carries the cause. The thrower
/// writes that entry; the worker does not log the failure a second time.
/// </para>
/// <para>
/// Internal on purpose: no public seam lets a consumer handler opt out of its
/// retries, and the codes are not public constants yet.
/// </para>
/// </remarks>
internal sealed class NonRetryableJobException : TraconException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="code">The stable code the job's error message starts with.</param>
    /// <param name="correlationId">The id of the log entry that carries the cause.</param>
    /// <param name="innerException">The last store error.</param>
    public NonRetryableJobException(string code, string correlationId, Exception innerException)
        : base(JobErrorCodes.Format(code, correlationId), innerException)
    {
        Code = code;
    }

    /// <summary>Gets the stable code the job's error message starts with.</summary>
    public string Code { get; }

    /// <summary>
    /// Gets the stable code, so a run this failure closes (an approval that could
    /// not be recorded) carries the same code as its job.
    /// </summary>
    public override string ErrorType => Code;
}
