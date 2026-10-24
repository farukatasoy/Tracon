namespace Tracon;

/// <summary>
/// Thrown when a tenant's quota period is spent and a run tree that is already running is
/// refused new work: a workflow function node or an agent step that has not started.
/// </summary>
/// <remarks>
/// <para>
/// The quota is read where the work begins, not only where the request comes in
/// (<c>429</c>). A workflow that runs for hours would otherwise pass a quota that was spent
/// in its first minutes. Only a start is refused: a node or a model call already in progress
/// is never cut off, so a tenant can pass its limit by as much as the work that was running
/// when the limit was reached.
/// </para>
/// <para>
/// This is a different fault from <see cref="TraconRunBudgetExceededException"/>, and the
/// remedy differs. A run tree's budget is a setting of the installation (<c>Tracon:AgentGraph</c>);
/// a tenant's quota resets when its period ends. Both end the run <see cref="RunStatus.Failed"/>
/// in the class <see cref="RunErrorClass.QuotaExceeded"/>, and neither is retried by a workflow
/// node's retry policy.
/// </para>
/// <para>
/// The exception is not thrown to an agent that called a sub-agent: there the refusal comes back
/// to the model as a tool result, as a spent run tree budget does.
/// </para>
/// </remarks>
public sealed class TraconTenantQuotaExceededException : TraconException
{
    /// <summary>
    /// The stable value written to <see cref="TraconException.ErrorType"/>.
    /// </summary>
    public const string TenantQuotaExceededErrorType = "tenant_quota_exceeded";

    /// <summary>Creates a new error.</summary>
    public TraconTenantQuotaExceededException()
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    public TraconTenantQuotaExceededException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public TraconTenantQuotaExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets the metric whose limit was reached, or <see langword="null"/> when the quota could not be verified.</summary>
    public QuotaMetric? Metric { get; init; }

    /// <summary>Gets the period whose limit was reached, or <see langword="null"/> when the quota could not be verified.</summary>
    public QuotaPeriod? Period { get; init; }

    /// <summary>Gets the limit that was reached, or <see langword="null"/> when the quota could not be verified.</summary>
    public decimal? Limit { get; init; }

    /// <inheritdoc />
    public override string ErrorType => TenantQuotaExceededErrorType;
}
