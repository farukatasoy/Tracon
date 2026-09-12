using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Tracon;

/// <summary>
/// Helper that checks the registered <see cref="IRunAuthorizationHandler"/>
/// before a run starts, a run resource is reached, or a session is accessed,
/// and produces a <c>403</c> (for a list or a run start) or a <c>404</c> (for a
/// single resource) when the handler denies the call.
/// </summary>
/// <remarks>
/// <para>
/// The check is called <strong>explicitly</strong> at every endpoint that
/// starts a run or touches a session; it is not an endpoint filter, for the
/// same reason <see cref="QuotaGate"/> is not: the endpoints that start runs
/// do not share one route shape.
/// </para>
/// <para>
/// <strong>Fail-closed.</strong> If the handler throws, the call is denied.
/// A gate that fails open on an exception is not a gate.
/// </para>
/// </remarks>
internal static class RunAuthorizationGate
{
    /// <summary>Checks whether a run may start; produces the response to return if it may not.</summary>
    /// <param name="handler">The authorization handler. If <see langword="null"/>, no check is performed.</param>
    /// <param name="tenants">The tenant context.</param>
    /// <param name="agentName">The name of the agent (or workflow) to run.</param>
    /// <param name="sessionId">The session the run would continue, or <see langword="null"/> for a sessionless run.</param>
    /// <param name="attributionContext">The attribution context, used to resolve the calling user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The <c>403</c> response to return if the handler denies the run; otherwise <see langword="null"/>.
    /// </returns>
    public static async ValueTask<ProblemHttpResult?> CheckRunAsync(
        IRunAuthorizationHandler? handler,
        ITenantContext tenants,
        string agentName,
        string? sessionId,
        IRunAttributionContext? attributionContext,
        CancellationToken cancellationToken)
    {
        if (handler is null)
        {
            return null;
        }

        var (userId, _) = RunAttributionReader.Read(attributionContext);

        var request = new RunAuthorizationRequest
        {
            TenantId = tenants.TenantId,
            AgentName = agentName,
            SessionId = sessionId,
            UserId = userId,
            Access = RunAccess.Start,
        };

        string? reason;

        try
        {
            var result = await handler.AuthorizeRunAsync(request, cancellationToken).ConfigureAwait(false);

            if (result.IsAllowed)
            {
                return null;
            }

            reason = result.Reason;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A consumer implementation reaches into its own policy and can
            // fail there. Fail-closed: the run does not start.
            reason = null;
        }

        return TypedResults.Problem(
            title: "Run not authorized",
            detail: reason ?? "The registered IRunAuthorizationHandler denied this run.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Checks whether a run resource may be reached; produces the response to
    /// return if it may not.
    /// </summary>
    /// <param name="handler">The authorization handler. If <see langword="null"/>, no check is performed.</param>
    /// <param name="tenants">The tenant context.</param>
    /// <param name="runId">
    /// The run the request is about, or <see langword="null"/> when there is no
    /// single run (a list, or an attachment with no run behind it).
    /// </param>
    /// <param name="agentName">
    /// The agent of the run the resource belongs to, or <see langword="null"/> when it is not known.
    /// </param>
    /// <param name="sessionId">The session the resource belongs to, or <see langword="null"/>.</param>
    /// <param name="attributionContext">The attribution context, used to resolve the calling user.</param>
    /// <param name="access">The kind of access being requested.</param>
    /// <param name="denied">
    /// The response to return when the handler denies the access. The caller
    /// supplies it because it must be <strong>byte for byte</strong> the
    /// response that endpoint already gives for a resource that genuinely does
    /// not exist: a denial that reads differently confirms the resource exists
    /// through a side channel. List endpoints pass a <c>403</c> instead — a
    /// list is an operation, not a resource, so there is no identity to leak.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <paramref name="denied"/> if the handler denies the access; otherwise <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// Called <strong>after</strong> the endpoint has established that the
    /// resource exists and belongs to the calling tenant, so another tenant's
    /// identity never reaches a consumer's handler. The one shape that cannot
    /// follow that order is a list, which has no resource to look up first.
    /// </remarks>
    public static async ValueTask<ProblemHttpResult?> CheckRunResourceAsync(
        IRunAuthorizationHandler? handler,
        ITenantContext tenants,
        Guid? runId,
        string? agentName,
        string? sessionId,
        IRunAttributionContext? attributionContext,
        RunAccess access,
        ProblemHttpResult denied,
        CancellationToken cancellationToken)
    {
        if (handler is null)
        {
            return null;
        }

        var (userId, _) = RunAttributionReader.Read(attributionContext);

        var request = new RunAuthorizationRequest
        {
            TenantId = tenants.TenantId,
            AgentName = agentName,
            SessionId = sessionId,
            UserId = userId,
            RunId = runId,
            Access = access,
        };

        try
        {
            if ((await handler.AuthorizeRunAsync(request, cancellationToken).ConfigureAwait(false)).IsAllowed)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail-closed: the access does not proceed. The denial reason a
            // handler would have supplied is deliberately NOT surfaced here —
            // the response must stay identical to a missing resource's.
        }

        return denied;
    }

    /// <summary>Checks whether a session access may proceed; produces the response to return if it may not.</summary>
    /// <param name="handler">The authorization handler. If <see langword="null"/>, no check is performed.</param>
    /// <param name="tenants">The tenant context.</param>
    /// <param name="sessionId">
    /// The session being accessed, or <see langword="null"/> for <see cref="SessionAccess.List"/>.
    /// </param>
    /// <param name="attributionContext">The attribution context, used to resolve the calling user.</param>
    /// <param name="access">The kind of access being requested.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The response to return if the handler denies the access; otherwise <see langword="null"/>.
    /// A denied <see cref="SessionAccess.List"/> returns <c>403</c> — a list is an operation, not a
    /// resource, so there is no identity to leak. Every other denied access returns <c>404</c>, with
    /// the SAME title and detail text a genuinely missing session gets (see <c>SessionEndpoints</c>):
    /// a <c>403</c> there would confirm the session exists, and wording that differed between "denied"
    /// and "missing" would leak the same thing through a side channel.
    /// </returns>
    public static async ValueTask<ProblemHttpResult?> CheckSessionAsync(
        IRunAuthorizationHandler? handler,
        ITenantContext tenants,
        string? sessionId,
        IRunAttributionContext? attributionContext,
        SessionAccess access,
        CancellationToken cancellationToken)
    {
        if (handler is null)
        {
            return null;
        }

        var (userId, _) = RunAttributionReader.Read(attributionContext);

        var request = new SessionAuthorizationRequest
        {
            TenantId = tenants.TenantId,
            SessionId = sessionId,
            UserId = userId,
            Access = access,
        };

        bool allowed;

        try
        {
            allowed = (await handler.AuthorizeSessionAsync(request, cancellationToken).ConfigureAwait(false)).IsAllowed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail-closed: the access does not proceed.
            allowed = false;
        }

        if (allowed)
        {
            return null;
        }

        return access == SessionAccess.List
            ? TypedResults.Problem(
                title: "Run not authorized",
                detail: "The registered IRunAuthorizationHandler denied this request.",
                statusCode: StatusCodes.Status403Forbidden)
            : TypedResults.Problem(
                title: "Session not found",
                detail: $"There is no session with id '{sessionId}'.",
                statusCode: StatusCodes.Status404NotFound);
    }
}
