using System.Globalization;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Bounds one call of a workflow function node with its timeout
/// (<see cref="WorkflowNodeReference.Timeout"/>, or <see cref="WorkflowFunctionOptions.Timeout"/>).
/// </summary>
/// <remarks>
/// <para>
/// The same shape as the tool timeout: the handler is given a token this wrapper can
/// cancel, and the call races a delay. When the delay wins, the token is cancelled, the
/// run stops waiting, and <see cref="TraconWorkflowFunctionTimeoutException"/> fails the
/// node. A handler that reads its token stops there; one that does not keeps running in
/// the background, and its end is logged and its result discarded.
/// </para>
/// <para>
/// <strong>The reason is read from this wrapper's own delay, never from the token.</strong>
/// The caller's token is cancelled by the run's own stop - its caller, the cancel
/// endpoint, the run's deadline - and that stop is not this node's timeout: the run ends
/// <c>Canceled</c> with the reason it already carries.
/// </para>
/// <para>
/// Installed per compile, inside the retry wrapper, so the timeout applies to each
/// attempt and a timed-out attempt is retried like any transient failure.
/// </para>
/// </remarks>
// K-497 / K-805: the tool timeout's pattern, reused for the code node (F-303).
// K-844: only the node's own delay names the timeout; the caller's stop stays Canceled.
internal static class WorkflowNodeTimeout
{
    /// <summary>Checks a registration's timeout when the function is registered.</summary>
    /// <param name="timeout">The timeout.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout"/> is outside one millisecond to about 49.7 days, the range
    /// the timeout's delay accepts.
    /// </exception>
    public static void Validate(TimeSpan timeout)
    {
        // F-280: checked here, where the value meets the timer at registration, not at
        // the first call in the middle of a run.
        if (!TimerPeriod.IsValid(timeout))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                TimerPeriod.Failure($"{nameof(WorkflowFunctionOptions)}.{nameof(WorkflowFunctionOptions.Timeout)}", timeout));
        }
    }

    /// <summary>Wraps a handler so each call is bounded by <paramref name="timeout"/>.</summary>
    /// <typeparam name="TInput">The type the function accepts.</typeparam>
    /// <typeparam name="TOutput">The type the function returns.</typeparam>
    /// <param name="handler">The handler, already bound to its node's settings.</param>
    /// <param name="functionName">The function's name, for the error and the log.</param>
    /// <param name="timeout">The timeout; already checked.</param>
    /// <param name="timeProvider">The time source of the delay.</param>
    /// <param name="logger">Where a call that ends after its timeout is reported.</param>
    /// <returns>The bounded handler.</returns>
    public static Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>> Wrap<TInput, TOutput>(
        Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>> handler,
        string functionName,
        TimeSpan timeout,
        TimeProvider timeProvider,
        ILogger logger)
        => async (input, context, cancellationToken) =>
        {
            // Captured in the synchronous part of the call: the run scope is an
            // AsyncLocal, and the continuation that observes a late end cannot rely on it.
            var runId = TraconRunContext.Current?.RunId;
            var startedAt = timeProvider.GetTimestamp();

            // Disposed when the call has settled, never before: a body that outlives
            // the timeout may still be reading its token.
            var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<TOutput> invocation;

            try
            {
                invocation = handler(input, context, budget.Token).AsTask();
            }
            catch
            {
                budget.Dispose();
                throw;
            }

            // The wait ends when the timeout elapses OR the caller stops: a body that
            // ignores its token no longer holds a run that was cancelled or reached
            // its deadline. Which of the two it was is read from the caller's token
            // below, never from how the delay ended. Linked rather than the caller's
            // token itself, so a call that returns in time leaves no registration on
            // the run's token.
            using var waitStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = Task.Delay(timeout, timeProvider, waitStop.Token);

            var winner = await Task.WhenAny(invocation, delay).ConfigureAwait(false);

            if (winner == invocation)
            {
                await waitStop.CancelAsync().ConfigureAwait(false);
                budget.Dispose();

                return await invocation.ConfigureAwait(false);
            }

            // Ask the body to stop. A body that reads its token ends here; one that
            // does not keeps running, and the observer below reports its end.
            await budget.CancelAsync().ConfigureAwait(false);

            ObserveLate(invocation, budget, functionName, timeout, runId, startedAt, timeProvider, logger);

            // 🚨 The caller's own stop wins, even when the delay fired in the same
            // instant: the run's cancellation or deadline is not this node's timeout.
            cancellationToken.ThrowIfCancellationRequested();

            throw new TraconWorkflowFunctionTimeoutException(
                $"Function node '{functionName}' did not return within {Format(timeout)} and was cancelled. " +
                "The run stopped waiting for it; anything it returns later is discarded. Raise the node's " +
                "'timeout', or the timeout the function was registered with.")
            {
                FunctionName = functionName,
                Timeout = timeout,
            };
        };

    /// <summary>
    /// Formats a duration for an operator: seconds with up to two decimals, or
    /// milliseconds below one second, so a short timeout never reads as "0".
    /// </summary>
    /// <param name="duration">The duration.</param>
    /// <returns>The text.</returns>
    internal static string Format(TimeSpan duration)
        => duration.TotalSeconds >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:0.##} s")
            : string.Create(CultureInfo.InvariantCulture, $"{duration.TotalMilliseconds:0.##} ms");

    /// <summary>
    /// Reports the end of a call the run stopped waiting for, observes its exception and
    /// releases its token source.
    /// </summary>
    private static void ObserveLate<TOutput>(
        Task<TOutput> invocation,
        CancellationTokenSource budget,
        string functionName,
        TimeSpan timeout,
        Guid? runId,
        long startedAt,
        TimeProvider timeProvider,
        ILogger logger)
        => _ = invocation.ContinueWith(
            task =>
            {
                budget.Dispose();

                var elapsed = timeProvider.GetElapsedTime(startedAt);

                // Observed in every branch: a fault nobody reads would surface as an
                // unobserved task exception, long after the run closed.
                if (task.IsFaulted)
                {
                    logger.LogWarning(
                        task.Exception,
                        "Function node {Function} of workflow run {RunId} faulted {Elapsed} after it started, past its timeout of {Timeout}; the run had already stopped waiting for it.",
                        functionName,
                        runId,
                        elapsed,
                        timeout);
                }
                else if (task.IsCanceled)
                {
                    logger.LogInformation(
                        "Function node {Function} of workflow run {RunId} stopped on its cancelled token {Elapsed} after it started.",
                        functionName,
                        runId,
                        elapsed);
                }
                else
                {
                    logger.LogWarning(
                        "Function node {Function} of workflow run {RunId} returned {Elapsed} after it started, past its timeout of {Timeout}; its result is discarded. A handler that ignores its token keeps its side effects.",
                        functionName,
                        runId,
                        elapsed,
                        timeout);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
