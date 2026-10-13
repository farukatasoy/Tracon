using Microsoft.AspNetCore.Http;

namespace Tracon;

/// <summary>
/// Reads the <c>Prefer: respond-async</c> preference (RFC 7240).
/// </summary>
/// <remarks>
/// <para>
/// One reading for every endpoint that queues a run: the agent run endpoint, the
/// three workflow run endpoints, and the idempotency filter that has to know
/// whether a request is queued before it reserves its key.
/// </para>
/// <para>
/// For a request that does NOT carry the header, this check is a single
/// dictionary lookup; no extra query is made (the no-surprises rule: no silent cost).
/// </para>
/// </remarks>
internal static class RespondAsyncPreference
{
    /// <summary>The preference token.</summary>
    public const string Token = "respond-async";

    /// <summary>Gets whether the request asks for the work to be queued.</summary>
    /// <param name="httpContext">The request's context.</param>
    /// <returns><see langword="true"/> when a <c>Prefer</c> header lists <c>respond-async</c>.</returns>
    public static bool IsRequested(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return httpContext.Request.Headers.TryGetValue("Prefer", out var values) &&
               values.Any(static value => value is not null &&
                   value.Split(',').Any(static token =>
                       token.Trim().Equals(Token, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Marks the response as queued: RFC 7240 treats the preference as advisory,
    /// so a <c>202</c> carries <c>Preference-Applied</c> to say it was honored.
    /// </summary>
    /// <param name="httpContext">The request's context.</param>
    public static void MarkApplied(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.Headers["Preference-Applied"] = Token;
    }
}
