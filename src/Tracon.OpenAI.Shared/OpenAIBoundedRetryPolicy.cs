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
/// <para>
/// A message can carry a ceiling of its own (<see cref="CapRetries"/>) that lowers its retries below the policy's count.
/// The image generator uses it: the client is shared with chat, and a count that suits chat can be too high for an image call,
/// which is billed.
/// </para>
/// </remarks>
internal sealed class OpenAIBoundedRetryPolicy : ClientRetryPolicy
{
    // The library's own first delay. It is private there, so the ordinary backoff is rebuilt from the same number.
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(0.8);

    /// <summary>
    /// The number of retries made when none is asked for. It is the number the OpenAI client library made when it was
    /// measured against a local server (a call that answers <c>503</c> arrived four times), kept as Tracon's own default
    /// so that a library that changes its own does not change the count of a host that never asked.
    /// </summary>
    internal const int LibraryDefaultMaxRetries = 3;

    /// <summary>The most retries an option may ask for.</summary>
    internal const int MaximumMaxRetries = 10;

    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Lowers the retries of one message to at most <paramref name="maxRetries"/>, whatever count the policy was built with.
    /// </summary>
    /// <param name="message">The message that is about to be sent through a pipeline whose retry policy is this one.</param>
    /// <param name="maxRetries">How many times the message may be sent again after its first try; <c>0</c> sends it once.</param>
    /// <remarks>
    /// <para>
    /// The ceiling belongs to the message, so one client serves calls of both kinds: an image call, which is billed and may
    /// ask for fewer retries, and a chat call, which keeps the client's count. It only lowers: a ceiling above the policy's
    /// own count changes nothing, because the library's own limit is checked as well.
    /// </para>
    /// <para>
    /// The retries already made are counted here. The library keeps its own count on the message, but only as an
    /// <see langword="internal"/> member, so a derived policy cannot read it; the policy counts the same event the library
    /// does, a try that was completed and is followed by another one.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRetries"/> is negative.</exception>
    internal static void CapRetries(PipelineMessage message, int maxRetries)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);

        message.SetProperty(typeof(RetryCeiling), new RetryCeiling(maxRetries));
    }

    /// <summary>Initializes a new policy.</summary>
    /// <param name="timeProvider">The clock the wait between two tries runs on; <see cref="TimeProvider.System"/> when omitted.</param>
    /// <param name="maxRetries">How many times a failed call is sent again; <see cref="LibraryDefaultMaxRetries"/> when omitted.</param>
    public OpenAIBoundedRetryPolicy(TimeProvider? timeProvider = null, int? maxRetries = null)
        : base(maxRetries ?? LibraryDefaultMaxRetries)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The library's own asynchronous check calls this one, so it covers both retry loops. A message whose ceiling is reached is
    /// not retried; any other message is decided by the library.
    /// </remarks>
    protected override bool ShouldRetry(PipelineMessage message, Exception? exception)
        => !(RetryCeiling.Of(message) is { Reached: true }) && base.ShouldRetry(message, exception);

    /// <inheritdoc />
    /// <remarks>Called after a try was completed and the next one is about to be sent: the retry is counted here.</remarks>
    protected override void OnTryComplete(PipelineMessage message)
    {
        RetryCeiling.Of(message)?.Count();
        base.OnTryComplete(message);
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

    /// <summary>The ceiling of one message and the retries it has made so far, kept on the message itself.</summary>
    /// <remarks>
    /// One message goes through the retry loop on one thread at a time, and the loop calls the policy in order, so the count
    /// needs no lock.
    /// </remarks>
    private sealed class RetryCeiling(int limit)
    {
        private int _retries;

        /// <summary>Gets whether the message has made as many retries as it may.</summary>
        public bool Reached => _retries >= limit;

        /// <summary>Reads the ceiling of a message.</summary>
        /// <param name="message">The message.</param>
        /// <returns>The ceiling; <see langword="null"/> when the message has none.</returns>
        public static RetryCeiling? Of(PipelineMessage message)
            => message.TryGetProperty(typeof(RetryCeiling), out var value) ? value as RetryCeiling : null;

        /// <summary>Counts one retry.</summary>
        public void Count() => _retries++;
    }
}
