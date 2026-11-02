using System.ClientModel.Primitives;

namespace Tracon;

/// <summary>
/// The OpenAI client library's retry policy, except that a <c>Retry-After</c> longer than
/// <see cref="ProviderRetryAfter.MaximumHonoredDelay"/> is ignored instead of obeyed.
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
/// Retries, the retriable statuses and the wait itself stay the library's: only the delay is decided here.
/// </para>
/// </remarks>
internal sealed class OpenAIBoundedRetryPolicy : ClientRetryPolicy
{
    // The library's own first delay. It is private there, so the ordinary backoff is rebuilt from the same number.
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(0.8);

    /// <inheritdoc />
    protected override TimeSpan GetNextDelay(PipelineMessage message, int tryCount)
    {
        var delay = base.GetNextDelay(message, tryCount);

        return delay <= ProviderRetryAfter.MaximumHonoredDelay
            ? delay
            : TimeSpan.FromMilliseconds(InitialDelay.TotalMilliseconds * (1 << (tryCount - 1)));
    }
}
