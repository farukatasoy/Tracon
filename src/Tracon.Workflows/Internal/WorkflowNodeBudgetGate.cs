using Microsoft.Agents.AI.Workflows;

namespace Tracon;

/// <summary>
/// Refuses to start a workflow function node once the call tree's cost or time budget is
/// spent, or the tenant's cost quota is.
/// </summary>
/// <remarks>
/// <para>
/// The tree budget stopped a model call and a new agent step, and a function node started
/// anyway and did its paid work: speech, a render, a GPU minute. A chain of function nodes
/// only never stopped at all. The gate sits where every <c>AddWorkflowFunction</c> family
/// passes (<c>NodeWrapper</c>), before the handler's first line, so no registration needs
/// its own check and a node nobody remembered to guard is guarded.
/// </para>
/// <para>
/// <strong>Only the start is gated.</strong> A node that is running is never stopped, so the
/// tree passes the ceiling by as much as its last node spends. Token is not read: the limit is
/// on by default (200,000) and a function node spends no tokens, so refusing on it would
/// change every installation. The refusal is the one a refused agent step ends with:
/// <see cref="TraconRunBudgetExceededException"/>, run <c>Failed</c>, class <c>QuotaExceeded</c>.
/// </para>
/// <para>
/// <strong>The tenant's quota is read second.</strong> The budget is in memory and the quota
/// is a store read, so a spent budget answers first and costs nothing. A spent cost quota refuses with
/// <see cref="TraconTenantQuotaExceededException"/>, the same class and a different identity, because the
/// remedy is different: the budget is a setting, the quota resets when its period ends. The quota counts
/// the tree's own spend that its counter has not seen, so the nodes the run already finished count.
/// </para>
/// <para>
/// <strong>Read-only on the ambient scope.</strong> The gate reads
/// <see cref="TraconRunContext.Current"/> and writes nothing, so the trap of an
/// <see cref="AsyncLocal{T}"/> set in a callee does not arise. The scope is the run's: the
/// runner sets it before the graph executes, and it flows into the background task Microsoft
/// Agent Framework runs the node on (the same read reports a node's usage).
/// </para>
/// </remarks>
internal static class WorkflowNodeBudgetGate
{
    /// <summary>Wraps a handler so each call checks the budget and the tenant's quota before it starts.</summary>
    /// <typeparam name="TInput">The type the function accepts.</typeparam>
    /// <typeparam name="TOutput">The type the function returns.</typeparam>
    /// <param name="handler">The handler, already bound to its node's settings and timeout.</param>
    /// <param name="functionName">The function's name, for the refusal's text.</param>
    /// <returns>The handler that starts only while the tree has room.</returns>
    /// <remarks>
    /// Installed inside the retry wrapper, so every attempt is checked: a second attempt is new
    /// paid work, and the first may have spent past the ceiling.
    /// </remarks>
    public static Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>> Wrap<TInput, TOutput>(
        Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>> handler,
        string functionName)
        => async (input, context, cancellationToken) =>
        {
            // No scope, or a scope with no budget: a handler called directly by a test, or by
            // an executor no run opened. There is nothing to read, so nothing to refuse. A
            // budget with room changes nothing either: the handler is called exactly as it
            // was before the gate existed, cancelled token included.
            if (TraconRunContext.Current?.Budget is { } budget)
            {
                if (budget.IsNodeStartExhausted)
                {
                    // The caller's stop wins over the refusal: a run that was cancelled is Canceled,
                    // not "out of budget".
                    cancellationToken.ThrowIfCancellationRequested();

                    throw new TraconRunBudgetExceededException(
                        $"Could not start function node '{functionName}': {budget.DescribeNodeStartExhaustion()}");
                }

                if (await TreeQuotaGate.RefusalAsync(budget, TreeStartKind.FunctionNode, cancellationToken).ConfigureAwait(false) is { } quota)
                {
                    // The stop wins here too, whichever way the store took the token.
                    cancellationToken.ThrowIfCancellationRequested();

                    throw TreeQuotaGate.ToException(
                        quota,
                        $"Could not start function node '{functionName}': {quota.Reason} A node that is already running is not stopped.");
                }
            }

            return await handler(input, context, cancellationToken).ConfigureAwait(false);
        };
}
