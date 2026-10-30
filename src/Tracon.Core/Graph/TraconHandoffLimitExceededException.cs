namespace Tracon;

/// <summary>
/// The failure of a Handoff workflow run that made more handoffs than its limit allows.
/// </summary>
/// <remarks>
/// Internal on purpose: a consumer reads the failure from the run record (<c>RunError.Type</c> is
/// <see cref="TraconErrorTypes.HandoffLimitExceeded"/>), never by catching it.
/// </remarks>
internal sealed class TraconHandoffLimitExceededException : TraconException
{
    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message. It names the limit and the agent that asked for the handoff.</param>
    public TraconHandoffLimitExceededException(string message)
        : base(message)
    {
    }

    /// <inheritdoc />
    public override string ErrorType => TraconErrorTypes.HandoffLimitExceeded;
}
