using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Wraps an <see cref="AIFunction"/> so a call that does not settle within a
/// fixed duration ends in <see cref="AgentPrismToolTimeoutException"/> instead
/// of running unbounded.
/// </summary>
/// <remarks>
/// <para>
/// Installed by the tool registry, one layer <strong>inside</strong> the
/// authorization wrapper and <strong>outside</strong> the approval wrapper —
///  This
/// ordering matters: <c>ApprovalRequiredAIFunction</c> never blocks on the
/// human decision inside a single call (Microsoft Agent Framework returns a
/// pending request immediately and the decision resumes as a NEW run,
/// ), so wrapping it with a timeout only ever bounds the tool's own
/// execution, never an approval wait.
/// </para>
/// <para>
/// <see cref="CancellationToken"/> is <strong>cooperative</strong>. A tool
/// body that never reads its token is not forcibly stopped: this class races
/// the call against a delay and, on timeout, returns control to the caller
/// while the call keeps running in the background until it finishes or
/// faults on its own. This is a documented limit, not a bug.
/// </para>
/// </remarks>
public sealed class TimeoutAIFunction : DelegatingAIFunction
{
    private readonly TimeSpan _timeout;
    private readonly ILogger<TimeoutAIFunction> _logger;

    /// <summary>Creates a new timeout wrapper.</summary>
    /// <param name="innerFunction">The tool to wrap.</param>
    /// <param name="timeout">The longest duration one call may run.</param>
    /// <param name="logger">The logger for a call that outlives its timeout.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is zero or negative.</exception>
    public TimeoutAIFunction(AIFunction innerFunction, TimeSpan timeout, ILogger<TimeoutAIFunction> logger)
        : base(innerFunction)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(logger);

        _timeout = timeout;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var invocation = base.InvokeCoreAsync(arguments, cancellationToken).AsTask();

        // A plain, uncancellable delay: it must fire ONLY when the timeout
        // genuinely elapses, never when the caller's OWN cancellationToken
        // fires. If the caller cancels and the tool body honors it, `invocation`
        // wins the race on its own and the real OperationCanceledException
        // propagates unchanged below.
        using var delayCts = new CancellationTokenSource();
        var delay = Task.Delay(_timeout, delayCts.Token);

        var winner = await Task.WhenAny(invocation, delay).ConfigureAwait(false);

        if (winner == invocation)
        {
            delayCts.Cancel();
            return await invocation.ConfigureAwait(false);
        }

        // The wait is cut short here; the underlying call is NOT. Observe its
        // eventual outcome so a late fault never surfaces as an unobserved
        // task exception, and log it so the boundary is visible in practice.
        var toolName = Name;
        var timeout = _timeout;
        var logger = _logger;

        _ = invocation.ContinueWith(
            t =>
            {
                if (t.IsFaulted)
                {
                    if (logger.IsEnabled(LogLevel.Warning))
                    {
                        logger.LogWarning(
                            t.Exception,
                            "Tool '{ToolName}' faulted after its {TimeoutSeconds}s timeout had already been reported to the model.",
                            toolName,
                            timeout.TotalSeconds);
                    }
                }
                else if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Tool '{ToolName}' finished after its {TimeoutSeconds}s timeout had already been reported to the model.",
                        toolName,
                        timeout.TotalSeconds);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        throw new AgentPrismToolTimeoutException(
            $"Tool '{toolName}' did not complete within {timeout.TotalSeconds:F0}s.")
        {
            ToolName = toolName,
            Timeout = timeout,
        };
    }
}
