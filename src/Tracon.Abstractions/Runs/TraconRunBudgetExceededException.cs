namespace Tracon;

/// <summary>
/// Thrown when a run tree's token or cost budget runs out mid-run, between
/// two model turns.
/// </summary>
/// <remarks>
/// <para>
/// Thrown by the wrapper installed inside the tool-call loop, one layer
/// before the raw provider client; Microsoft Agent Framework's
/// function-invoking client does not catch it, so it propagates out of
/// <c>AIAgent.RunAsync</c>/<c>RunStreamingAsync</c> and fails the run.
/// </para>
/// <para>
/// This is a different fault from <see cref="AgentRunBudget"/> refusing to
/// reserve a <em>new child run</em> (<see cref="AgentRunBudget.TryReserveRun"/>):
/// that refusal is returned to the model as an ordinary tool result and the
/// calling agent can recover; this exception ends the run that hit it.
/// </para>
/// </remarks>
public sealed class TraconRunBudgetExceededException : TraconException
{
    /// <summary>
    /// The stable value written to <see cref="TraconException.ErrorType"/>.
    /// </summary>
    public const string RunBudgetExceededErrorType = "run_budget_exceeded";

    /// <summary>Creates a new error.</summary>
    public TraconRunBudgetExceededException()
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    public TraconRunBudgetExceededException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public TraconRunBudgetExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <inheritdoc />
    public override string ErrorType => RunBudgetExceededErrorType;
}
