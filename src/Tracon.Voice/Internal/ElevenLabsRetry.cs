using System.Net;
using System.Net.Http.Headers;

namespace Tracon;

/// <summary>
/// Decides which failures of a speech request are sent again and how long the client waits before it does.
/// </summary>
/// <remarks>
/// <para>
/// Only what a repeat can change is repeated: <c>429</c>, a <c>5xx</c> answer, and a connection that failed or dropped before the
/// answer was complete.
/// Any other <c>4xx</c> is the same on every try. The client's own timeout is never repeated: the provider may have finished a
/// request the client gave up on, and a second one would pay for the first again.
/// </para>
/// <para>
/// The wait is the provider's <c>Retry-After</c> when it asks for a delay of at most
/// <see cref="ProviderRetryAfter.MaximumHonoredDelay"/>. A longer one is ignored, as it is for the other providers: a
/// header comes from the network, and a request that holds a concurrency slot must not be held for a day by it. Without a usable
/// header the wait is <see cref="InitialBackoff"/>, doubled for every repeat, and never more than the same ceiling. There is no
/// jitter: the concurrency limit already bounds how many requests wait together, and a fixed schedule is one a test can read.
/// </para>
/// </remarks>
internal static class ElevenLabsRetry
{
    /// <summary>The most repeats an option may ask for.</summary>
    internal const int MaximumMaxRetries = 10;

    /// <summary>The wait before the first repeat when the provider asked for none: the same first delay the OpenAI adapter uses.</summary>
    internal static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(800);

    /// <summary>Tells whether an answer with this status can come out differently on another try.</summary>
    /// <param name="status">The HTTP status.</param>
    /// <returns><see langword="true"/> for <c>429</c> and every <c>5xx</c>.</returns>
    internal static bool IsRetriable(HttpStatusCode status)
        => status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    /// <summary>
    /// Tells whether a failure of the call is a connection that failed or dropped before the answer was complete, which another
    /// try can fix.
    /// </summary>
    /// <param name="failure">The failure <c>SendToProviderAsync</c> raised.</param>
    /// <returns>
    /// <see langword="false"/> for an answer with a status (the status decides) and for the client's own timeout.
    /// </returns>
    internal static bool IsConnectionFailure(ProviderInvocationException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure.StatusCode is null && failure.Original is HttpRequestException;
    }

    /// <summary>Gets the wait before the repeat that follows a failed try.</summary>
    /// <param name="repeat">The number of the repeat, counted from one.</param>
    /// <param name="response">The failed answer, or <see langword="null"/> when the try got none.</param>
    /// <param name="now">The current time, for a <c>Retry-After</c> that is a date.</param>
    /// <returns>The wait; never negative and never more than <see cref="ProviderRetryAfter.MaximumHonoredDelay"/>.</returns>
    internal static TimeSpan Delay(int repeat, HttpResponseMessage? response, DateTimeOffset now)
    {
        if (response is not null && RetryAfter(response.Headers, now) is { } asked)
        {
            return asked;
        }

        return Backoff(repeat);
    }

    /// <summary>Gets the wait of the exponential backoff.</summary>
    /// <param name="repeat">The number of the repeat, counted from one.</param>
    /// <returns>The initial wait, doubled for every earlier repeat, up to the ceiling.</returns>
    internal static TimeSpan Backoff(int repeat)
    {
        // The shift is bounded: ten repeats double the first wait nine times, and the ceiling is reached at the eighth.
        var factor = 1L << Math.Clamp(repeat - 1, 0, MaximumMaxRetries);
        var wait = TimeSpan.FromTicks(InitialBackoff.Ticks * factor);

        return wait < ProviderRetryAfter.MaximumHonoredDelay ? wait : ProviderRetryAfter.MaximumHonoredDelay;
    }

    /// <summary>Reads the delay the provider asked for, when it is one the client honors.</summary>
    /// <param name="headers">The answer's headers.</param>
    /// <param name="now">The current time, for a header that is a date.</param>
    /// <returns>
    /// The delay (zero for a date that has passed), or <see langword="null"/> when the header is missing, is not a delay, or asks
    /// for longer than <see cref="ProviderRetryAfter.MaximumHonoredDelay"/>.
    /// </returns>
    private static TimeSpan? RetryAfter(HttpResponseHeaders headers, DateTimeOffset now)
    {
        // A value that is neither seconds nor a date reads as no header at all.
        var header = headers.RetryAfter;
        var asked = header?.Delta ?? (header?.Date is { } date ? date - now : null);

        if (asked is not { } delay)
        {
            return null;
        }

        if (delay < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return delay <= ProviderRetryAfter.MaximumHonoredDelay ? delay : null;
    }
}
