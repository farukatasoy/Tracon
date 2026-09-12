using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

// 🚨 This class exists for K-687. A denial must be BYTE FOR BYTE identical to the
// answer an unreachable session gets, otherwise the status code becomes an oracle
// for which sessions exist in another tenant. Two hand-written copies of that 404
// are a violation waiting to happen, so there is exactly one writer.
//
// The credential layer is deliberately NOT here. The conversation endpoint reads its
// token from the WebSocket subprotocol because a browser cannot put an Authorization
// header on a handshake (K-224); the live endpoints are plain HTTP and use the
// ordinary bearer layer. That exemption is specific to the handshake.
/// <summary>The session gates and the problem writer every voice endpoint shares.</summary>
/// <remarks>
/// A denial is written in one place so that every voice endpoint answers an
/// unreachable session, another tenant's session and a refused caller identically.
/// </remarks>
internal static class VoiceEndpointGates
{
    /// <summary>Checks whether a session belongs to this tenant.</summary>
    /// <param name="services">The request services.</param>
    /// <param name="sessionId">The session identifier, which is untrusted input.</param>
    /// <param name="tenantId">The resolved tenant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the session belongs to this tenant or does not exist yet.
    /// </returns>
    /// <remarks>
    /// A session that does not yet exist is accepted: the first turn opens it.
    /// </remarks>
    public static async ValueTask<bool> OwnsSessionAsync(
        IServiceProvider services,
        string sessionId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        // 🚨 K-283 (phase 41) removed the "someone else's session" rejection here ON
        // PURPOSE. GetAsync is scoped to the ambient tenant, so another tenant's
        // record returns null, the caller opens a FRESH session in their own tenant,
        // and the other tenant's record is never touched. That removes an existence
        // oracle. Do NOT "fix" this into an owner lookup without reopening K-283 --
        // the OpenAI-compatible endpoints answer 404 instead, and the difference
        // between the two surfaces is a decision, not an oversight.
        var store = services.GetRequiredService<ISessionStore>();
        var record = await store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        return record is null || string.Equals(record.TenantId, tenantId, StringComparison.Ordinal);
    }

    /// <summary>Asks the consumer's authorization handler and the ownership gate.</summary>
    /// <param name="context">The request.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <returns><see langword="true"/> when the caller must be refused.</returns>
    /// <remarks>
    /// The handler is asked <strong>even when the session does not exist yet</strong>,
    /// because only the consumer can say whether this caller may open one under this
    /// id. The gate's own body is discarded so that every denial goes through
    /// <see cref="WriteSessionNotFoundAsync"/>.
    /// </remarks>
    public static async ValueTask<bool> DeniesSessionAsync(
        HttpContext context,
        string sessionId,
        ITenantContext tenantContext)
    {
        var services = context.RequestServices;

        // 🚨 K-283: a session nothing has written yet is still offered to the
        // handler; refusing an unknown session here would break the FIRST
        // conversation of every installation and no other test would see it.
        if (await RunAuthorizationGate
                .CheckSessionAsync(
                    services.GetService<IRunAuthorizationHandler>(),
                    tenantContext,
                    sessionId,
                    services.GetService<IRunAttributionContext>(),
                    SessionAccess.Voice,
                    context.RequestAborted)
                .ConfigureAwait(false) is not null)
        {
            return true;
        }

        // 🚨 A voice connection writes into the session it opens. Leaving this
        // ungated would let one user speak into another user's conversation while
        // every HTTP route to it answered 404. K-283 survives: a session nothing has
        // written yet carries no owner and is not refused here.
        return await SessionOwnershipGate
            .DeniesAsync(
                services.GetService<IOptionsMonitor<TraconSessionOwnershipOptions>>(),
                services.GetService<IRunAttributionContext>(),
                services.GetRequiredService<ISessionStore>(),
                sessionId,
                context,
                context.RequestAborted)
            .ConfigureAwait(false);
    }

    /// <summary>Writes the single 404 the voice surface uses for "no such session".</summary>
    /// <param name="context">The request.</param>
    /// <param name="sessionId">The session identifier the caller asked for.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// The wording is the same whether the session belongs to another tenant, the
    /// handler denied the caller, or it genuinely does not exist. That is the point.
    /// </remarks>
    public static Task WriteSessionNotFoundAsync(HttpContext context, string sessionId)
        => WriteProblemAsync(
            context,
            StatusCodes.Status404NotFound,
            "Session not found",
            $"There is no session with id '{sessionId}', or it does not belong to this tenant.");

    /// <summary>Writes a problem response.</summary>
    /// <param name="context">The request.</param>
    /// <param name="statusCode">The status code.</param>
    /// <param name="title">The title.</param>
    /// <param name="detail">The detail.</param>
    /// <returns>The completion task.</returns>
    public static async Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        var result = Results.Problem(title: title, detail: detail, statusCode: statusCode);

        await result.ExecuteAsync(context).ConfigureAwait(false);
    }
}
