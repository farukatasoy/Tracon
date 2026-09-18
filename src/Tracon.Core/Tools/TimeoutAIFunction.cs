using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Wraps an <see cref="AIFunction"/> so a call that does not settle within a
/// fixed duration ends in <see cref="TraconToolTimeoutException"/> instead
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
/// On timeout the call is <strong>cancelled</strong> and the caller is handed
/// <see cref="TraconToolTimeoutException"/>. Cancellation is
/// <see cref="CancellationToken">cooperative</see>: a body that reads its token
/// stops there and spends nothing more, while one that never reads it is not
/// forcibly stopped and keeps running in the background until it finishes or
/// faults on its own. That remaining case is not left silent — when such a call
/// does settle, its result and whatever usage it reported are written onto the
/// call's existing record, which is the only way a charge incurred after the
/// timeout reaches a cost report at all.
/// </para>
/// </remarks>
public sealed class TimeoutAIFunction : DelegatingAIFunction
{
    private readonly TimeSpan _timeout;
    private readonly ILogger<TimeoutAIFunction> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new timeout wrapper.</summary>
    /// <param name="innerFunction">The tool to wrap.</param>
    /// <param name="timeout">The longest duration one call may run.</param>
    /// <param name="logger">The logger for a call that outlives its timeout.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is zero or negative.</exception>
    public TimeoutAIFunction(AIFunction innerFunction, TimeSpan timeout, ILogger<TimeoutAIFunction> logger)
        : this(innerFunction, timeout, logger, TimeProvider.System)
    {
    }

    /// <summary>Creates a new timeout wrapper with an explicit time source.</summary>
    /// <param name="innerFunction">The tool to wrap.</param>
    /// <param name="timeout">The longest duration one call may run.</param>
    /// <param name="logger">The logger for a call that outlives its timeout.</param>
    /// <param name="timeProvider">The time source used for the timeout and for the real duration of a late call.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is zero or negative.</exception>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public TimeoutAIFunction(
        AIFunction innerFunction,
        TimeSpan timeout,
        ILogger<TimeoutAIFunction> logger,
        TimeProvider timeProvider)
        : base(innerFunction)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeout = timeout;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        // 🚨 Captured HERE, in the synchronous part of the call. Both values
        // live in an AsyncLocal, and the continuation that observes a late
        // settlement cannot be relied on to still see either of them.
        var scope = TraconRunContext.Current;
        var callId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId;
        var startedAt = _timeProvider.GetTimestamp();

        // The tool body is handed a token that this wrapper can fire. Cancelling
        // it on timeout is what stops a cooperative tool from spending money
        // nobody is waiting for any more; the caller's own token still flows
        // through, so a real caller cancellation keeps its own identity below.
        using var timedOut = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var invocation = base.InvokeCoreAsync(arguments, timedOut.Token).AsTask();

        // A plain, uncancellable delay: it must fire ONLY when the timeout
        // genuinely elapses, never when the caller's OWN cancellationToken
        // fires. If the caller cancels and the tool body honors it, `invocation`
        // wins the race on its own and the real OperationCanceledException
        // propagates unchanged below.
        using var delayCts = new CancellationTokenSource();
        var delay = Task.Delay(_timeout, _timeProvider, delayCts.Token);

        var winner = await Task.WhenAny(invocation, delay).ConfigureAwait(false);

        if (winner == invocation)
        {
            delayCts.Cancel();

            return await invocation.ConfigureAwait(false);
        }

        var toolName = Name;
        var timeout = _timeout;

        // Ask the body to stop. A body that reads its token ends here; one that
        // does not keeps running, and the observer below books whatever it
        // eventually produces.
        await timedOut.CancelAsync().ConfigureAwait(false);

        LateToolCompletionRecorder.Observe(
            invocation,
            scope,
            callId,
            toolName,
            timeout,
            startedAt,
            _timeProvider,
            _logger);

        throw new TraconToolTimeoutException(
            $"Tool '{toolName}' did not complete within {FormatSeconds(timeout)}.")
        {
            ToolName = toolName,
            Timeout = timeout,
        };
    }

    /// <summary>
    /// Formats a duration for the sentence the MODEL reads.
    /// </summary>
    /// <remarks>
    /// A whole-second format rendered every sub-second timeout as "0s", so a
    /// tool bounded at 500ms told the model it had not finished within zero
    /// seconds. One decimal is kept only where it carries meaning.
    /// </remarks>
    private static string FormatSeconds(TimeSpan timeout)
        => timeout.TotalSeconds >= 1
            ? $"{timeout.TotalSeconds:0.##}s"
            : $"{timeout.TotalMilliseconds:0.##}ms";
}
