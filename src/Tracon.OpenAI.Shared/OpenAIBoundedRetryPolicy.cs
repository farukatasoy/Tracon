using System.ClientModel.Primitives;

namespace Tracon;

/// <summary>
/// The OpenAI client library's retry policy, except that a <c>Retry-After</c> longer than
/// <see cref="ProviderRetryAfter.MaximumHonoredDelay"/> is ignored instead of obeyed, and that the wait between two
/// tries runs on a <see cref="TimeProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// The library's own policy waits whatever the header says. Measured against a local server that answered
/// <c>429</c>: <c>Retry-After: 86400</c> held the request for the day (only the caller's own cancellation ended it),
/// and a value past about 49 days, or an HTTP date in the next century, made <c>Task.Delay</c> throw
/// <see cref="ArgumentOutOfRangeException"/> out of the retry loop, so the run failed with that exception instead of
/// with the provider's <c>429</c>. A header within the ceiling keeps the library's behavior; above it, the policy
/// uses the exponential backoff the library would have used without a header.
/// </para>
/// <para>
/// Retries and the retriable statuses stay the library's: only the delay is decided here, and the wait that follows it
/// is the library's wait moved onto the host's clock. The library sleeps on the system clock, so a host whose clock
/// is not the system's (a test host that stands in for time) would still wait the backoff for real.
/// <see cref="TimeProvider.System"/> keeps the library's behavior exactly: both waits are then the library's own.
/// </para>
/// </remarks>
internal sealed class OpenAIBoundedRetryPolicy : ClientRetryPolicy
{
    // The library's own first delay. It is private there, so the ordinary backoff is rebuilt from the same number.
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(0.8);

    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new policy.</summary>
    /// <param name="timeProvider">The clock the wait between two tries runs on; <see cref="TimeProvider.System"/> when omitted.</param>
    public OpenAIBoundedRetryPolicy(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    protected override TimeSpan GetNextDelay(PipelineMessage message, int tryCount)
    {
        var delay = base.GetNextDelay(message, tryCount);

        return delay <= ProviderRetryAfter.MaximumHonoredDelay
            ? delay
            : TimeSpan.FromMilliseconds(InitialDelay.TotalMilliseconds * (1 << (tryCount - 1)));
    }

    /// <inheritdoc />
    protected override Task WaitAsync(TimeSpan time, CancellationToken cancellationToken)
        => IsSystemClock
            ? base.WaitAsync(time, cancellationToken)
            : Task.Delay(time, _timeProvider, cancellationToken);

    /// <inheritdoc />
    protected override void Wait(TimeSpan time, CancellationToken cancellationToken)
    {
        if (IsSystemClock)
        {
            base.Wait(time, cancellationToken);

            return;
        }

        var elapsed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var timer = _timeProvider.CreateTimer(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            elapsed,
            time,
            Timeout.InfiniteTimeSpan);

        // Throws OperationCanceledException when the caller gives up first, as the library's own wait does.
        elapsed.Task.Wait(cancellationToken);
    }

    // The system clock keeps the library's own waits: the same code, and for the blocking loop the library's wait handle
    // instead of a timer and a task.
    private bool IsSystemClock => ReferenceEquals(_timeProvider, TimeProvider.System);
}
