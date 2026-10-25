namespace Tracon;

/// <summary>
/// Thrown when a run tree's budget runs out: its token or cost budget between two
/// model turns, or a workflow step or function node that the budget refuses to start.
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
/// for agent delegation that refusal is returned to the model as an ordinary
/// tool result and the calling agent can recover; this exception ends the run
/// that hit it.
/// </para>
/// <para>
/// A workflow step has no calling model to recover, so there the refused
/// reservation throws this exception too, and the workflow run ends
/// <c>Failed</c> with the same class as any other budget exhaustion.
/// </para>
/// <para>
/// A workflow function node is refused the same way, at its start, when the tree's
/// cost or time budget is already spent (the token budget does not stop a node). The
/// message says "Could not start function node" and names the setting to raise. A node
/// that is already running is never stopped by it, and the refusal is not retried.
/// </para>
/// <para>
/// A tool call is refused at its start when the tree's token, cost or time budget is spent.
/// The exception does not leave the tool: its message becomes the tool's result
/// ("… The tool call did not start."), a streaming run records the call as failed, and the
/// run ends at its next model call. A tool call that has started is never stopped.
/// </para>
/// </remarks>
public sealed class TraconRunBudgetExceededException : TraconException
{
    /// <summary>
    /// The stable value written to <see cref="TraconException.ErrorType"/>.
    /// </summary>
    public const string RunBudgetExceededErrorType = TraconErrorTypes.RunBudgetExceeded;

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
