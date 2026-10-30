using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Refuses to start a tool call once the run tree's token, cost or time budget is spent.
/// </summary>
/// <remarks>
/// <para>
/// The tree budget already refuses the next model call (<c>RunBudgetChatClient</c>), a new
/// child run and a workflow function node. A tool call was the one start it did not read:
/// when one model turn asked for two paid tools and the first passed the ceiling, the second
/// still ran. Its money and its side effect were gone, and the only model call that could
/// read its result was already refused.
/// </para>
/// <para>
/// The condition is <see cref="AgentRunBudget.IsExhausted"/>, the same one the model call
/// reads, because a tool's result is read by a model call and that call is refused when the
/// condition holds. A workflow function node is gated on cost and time only, since its
/// output goes to the next node and not to a model.
/// </para>
/// <para>
/// The refusal is a <see cref="TraconRunBudgetExceededException"/>. The layer above it,
/// <see cref="ExplainedFailureAIFunction"/>, turns it into the tool's result and marks the
/// call failed in the run record; the run itself ends at its next model call with the
/// identity it always had (<c>Failed</c>, <c>run_budget_exceeded</c>).
/// </para>
/// <para>
/// A call that has started is never stopped, and the amount a tool spent is known only when
/// it ends, so the call that crosses the ceiling is the last one that starts. Tool calls
/// that start together (<see cref="ToolLoopSettings.AllowConcurrentToolCalls"/>) all start
/// before any of them reports.
/// </para>
/// </remarks>
/// <param name="innerFunction">The tool to wrap.</param>
internal sealed class BudgetGatingAIFunction(AIFunction innerFunction)
    : DelegatingAIFunction(innerFunction)
{
    /// <summary>
    /// Gets the sentence for the tool call the ambient run's budget refuses, or
    /// <see langword="null"/> when the call may start.
    /// </summary>
    /// <remarks>
    /// The one place the condition is written. The skill script runner is not a wrapper of
    /// this chain (Microsoft Agent Framework builds the script tool), so it asks here too.
    /// </remarks>
    internal static string? Refusal()
        => TraconRunContext.Current?.Budget is { IsExhausted: true } budget
            ? budget.DescribeToolStartExhaustion()
            : null;

    /// <inheritdoc />
    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        // One ambient read when the run has no budget; the check itself reads counters and
        // the clock, never the store. The token is not read: a cancellation travels the way
        // it always did.
        if (Refusal() is { } refusal)
        {
            throw new TraconRunBudgetExceededException(refusal);
        }

        return base.InvokeCoreAsync(arguments, cancellationToken);
    }
}
