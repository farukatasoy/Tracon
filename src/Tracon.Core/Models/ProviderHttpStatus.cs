namespace Tracon;

// 🚨 F-338. RunErrorClass.ProviderError answers "which fault was this", and
// K-817 widened it on purpose to every provider HTTP error, 4xx included. A
// retry asks a different question. A workflow node retry that read only the
// class repeated a 401 MaxAttempts times, and a retried 409 on a
// non-idempotent call repeated its side effect. The fallback chain had the
// same gap: a 404 on an SDK wrapper type fell through to the transport rule
// and moved to the next provider, which got the same bad request. The class
// is left as it is; the retry decisions read the status here instead.

/// <summary>
/// Reads the HTTP status a failure carries and decides whether repeating the
/// same request can change the answer.
/// </summary>
/// <remarks>
/// <see cref="RunErrorClass.ProviderError"/> tells which fault occurred and
/// holds a permanent 4xx too, so it cannot answer this question. The workflow
/// node retry and the model fallback chain both read the status here, so the
/// two retry decisions cannot disagree about a status.
/// </remarks>
internal static class ProviderHttpStatus
{
    /// <summary>
    /// Reads the HTTP status from anywhere in <paramref name="exception"/>'s graph.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The status, or <see langword="null"/> when no frame carries one.</returns>
    /// <remarks>
    /// <see cref="HttpRequestException.StatusCode"/> is read first, because the
    /// message .NET writes for it ("Response status code does not indicate
    /// success: 401 (Unauthorized).") has no "HTTP nnn" text. That type is in
    /// the base class library, not a provider SDK, so reading it does not
    /// break the rule that Core knows no SDK exception type. Then the text
    /// reader the normalizer already uses runs, which is what a provider SDK
    /// and a normalized <c>upstream_error</c> carry.
    /// </remarks>
    internal static int? Read(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return ReadStatusCode(exception) ?? ProviderFailureNormalizer.ReadStatus(exception);
    }

    /// <summary>
    /// Determines whether a request that received <paramref name="status"/>
    /// gets the same answer when it is sent again.
    /// </summary>
    /// <param name="status">The HTTP status.</param>
    /// <returns>
    /// <see langword="true"/> for a 4xx other than 408 (request timeout) and
    /// 429 (rate limit).
    /// </returns>
    internal static bool IsPermanent(int status)
        => status is >= 400 and <= 499 and not 408 and not 429;

    /// <summary>
    /// Determines whether <paramref name="exception"/> carries a status that
    /// no retry and no other provider can change.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns><see langword="true"/> when <see cref="Read"/> finds a status and <see cref="IsPermanent"/> is true for it.</returns>
    internal static bool IsPermanentFailure(Exception exception)
        => Read(exception) is { } status && IsPermanent(status);

    private static int? ReadStatusCode(Exception exception)
    {
        for (var candidate = exception; candidate is not null; candidate = candidate.InnerException)
        {
            if (candidate is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    if (ReadStatusCode(inner) is { } nested)
                    {
                        return nested;
                    }
                }
            }

            if (candidate is HttpRequestException { StatusCode: { } statusCode })
            {
                return (int)statusCode;
            }
        }

        return null;
    }
}
