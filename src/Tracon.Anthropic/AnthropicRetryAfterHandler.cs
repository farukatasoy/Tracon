namespace Tracon;

/// <summary>
/// Drops a <c>Retry-After</c> that asks for longer than <see cref="ProviderRetryAfter.MaximumHonoredDelay"/> from a
/// response before the Anthropic client library reads it.
/// </summary>
/// <remarks>
/// <para>
/// The library retries <c>429</c> and <c>5xx</c> answers itself and waits whatever <c>Retry-After</c> (or
/// <c>Retry-After-Ms</c>) says, up to about 24 days. A day-long header held the run for a day. The library takes a
/// message handler but no retry policy, and it reads the headers from the very response this handler returns, so the
/// handler removes the excessive header and the library uses its own exponential backoff for that attempt.
/// </para>
/// <para>
/// A header within the ceiling, or one that is not a delay at all, is left alone: the library already honors the first
/// and ignores the second.
/// </para>
/// </remarks>
internal sealed class AnthropicRetryAfterHandler(TimeProvider timeProvider) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        ProviderRetryAfter.RemoveExcessiveDelays(response.Headers, timeProvider.GetUtcNow());

        return response;
    }
}
