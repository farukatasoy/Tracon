using Microsoft.Agents.AI.Workflows;

namespace Tracon;

/// <summary>
/// Tells code running inside a workflow function node which node it is in.
/// </summary>
/// <remarks>
/// <para>
/// <c>IWorkflowContext.ReportUsageAsync</c> records the node that reported an item.
/// Microsoft Agent Framework's <see cref="IWorkflowContext"/> carries neither the
/// executor's identity nor a service provider, and the handler a consumer writes is
/// shared by every run, so the only place the node's name is known is the wrapper
/// that registers the function. The wrapper writes it to an <see cref="AsyncLocal{T}"/>
/// in its OWN body; the value flows down into the handler, into everything the handler
/// awaits, and into a body that outlives its timeout.
/// </para>
/// <para>
/// <strong>The assignment is made in the wrapper's own async body</strong> and never
/// in a helper that returns to it: an <see cref="AsyncLocal{T}"/> set in a callee does
/// not flow back to the caller (the same trap as <c>Activity.Current</c> and
/// <see cref="TraconRunContext"/>). The wrapper is the OUTERMOST of
/// <c>scope(retry(gate(timeout(handler))))</c>, so every attempt of a retried node sees it.
/// </para>
/// </remarks>
internal static class WorkflowFunctionNodeScope
{
    private static readonly AsyncLocal<string?> Holder = new();

    /// <summary>
    /// Gets the name of the function node the current flow runs in, or <see langword="null"/>
    /// outside one — in a custom executor of a graph built in code, for example.
    /// </summary>
    public static string? CurrentName => Holder.Value;

    /// <summary>Wraps a handler so the node's name is visible inside it.</summary>
    /// <typeparam name="TInput">The type the function accepts.</typeparam>
    /// <typeparam name="TOutput">The type the function returns.</typeparam>
    /// <param name="nodeName">The node's name.</param>
    /// <param name="handler">The handler, already wrapped in its retry and timeout.</param>
    /// <returns>The handler that reports its node.</returns>
    public static Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>> Wrap<TInput, TOutput>(
        string nodeName,
        Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>> handler)
        => async (input, context, cancellationToken) =>
        {
            // In THIS body, before the first await: see the remarks.
            Holder.Value = nodeName;

            return await handler(input, context, cancellationToken).ConfigureAwait(false);
        };
}
