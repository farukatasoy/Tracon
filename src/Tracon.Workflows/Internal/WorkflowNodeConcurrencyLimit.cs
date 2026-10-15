using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Limits how many calls of one workflow function run at the same time in this process
/// (<see cref="WorkflowFunctionOptions.MaxConcurrency"/>).
/// </summary>
/// <remarks>
/// <para>
/// The only limit the engine had was <c>Tracon:Workflows:MaxConcurrentRuns</c>: run slots every tenant
/// shares. A node that is costly to run in parallel (a GPU voice synthesis, a render) needs its own
/// bound, and it has to hold across workflows, which is why the limit belongs to the function and not
/// to a workflow.
/// </para>
/// <para>
/// <strong>Position in the wrapper chain.</strong> <c>scope(retry(limit(gate(timeout(handler)))))</c>.
/// The limit is inside the retry, so every attempt takes a slot and a retry's delay gives it back; it
/// is outside the gate, so the budget is read after the wait (it can run out while a call waits); and
/// it is outside the timeout, so the wait is not part of the node's own time.
/// </para>
/// <para>
/// The wait honours the token of the run, so a cancelled run or one past its deadline does not stay
/// queued behind a slow node. Only the wait is cancelled by it: a call that holds a slot gives it back
/// when its handler returns or throws.
/// </para>
/// </remarks>
internal static class WorkflowNodeConcurrencyLimit
{
    /// <summary>Checks a registration's limit when the function is registered.</summary>
    /// <param name="maxConcurrency">The limit.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrency"/> is less than 1.</exception>
    public static void Validate(int maxConcurrency)
    {
        if (maxConcurrency < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrency),
                maxConcurrency,
                $"{nameof(WorkflowFunctionOptions)}.{nameof(WorkflowFunctionOptions.MaxConcurrency)} must be 1 or more " +
                $"(the number of calls that may run at once). Actual value: {maxConcurrency}. Leave it unset for no limit.");
        }
    }

    /// <summary>Creates the slots of one function.</summary>
    /// <param name="maxConcurrency">The limit; already checked.</param>
    /// <returns>The slots, shared by every call of the function in the process.</returns>
    public static SemaphoreSlim CreateSlots(int maxConcurrency)
        => new(maxConcurrency, maxConcurrency);

    /// <summary>Wraps a handler so a call takes a slot before it starts and gives it back when it ends.</summary>
    /// <typeparam name="TInput">The type the function accepts.</typeparam>
    /// <typeparam name="TOutput">The type the function returns.</typeparam>
    /// <param name="handler">The handler, already bound to its gate and timeout.</param>
    /// <param name="slots">The slots of the function.</param>
    /// <param name="limit">The number of slots, for the log.</param>
    /// <param name="functionName">The function's name, for the log.</param>
    /// <param name="logger">Where a call that has to wait is reported.</param>
    /// <returns>The handler that runs only while a slot is free.</returns>
    public static Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>> Wrap<TInput, TOutput>(
        Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>> handler,
        SemaphoreSlim slots,
        int limit,
        string functionName,
        ILogger logger)
        => async (input, context, cancellationToken) =>
        {
            // A free slot is the common case and costs one synchronous check; only a call that
            // has to wait says so, so a log at the Debug level shows queueing without a new event.
            if (!await slots.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug(
                        "Function node {Function} is waiting for a free slot (the limit is {Limit} call(s) at once).",
                        functionName,
                        limit);
                }

                await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                return await handler(input, context, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                slots.Release();
            }
        };
}
