using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;

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
/// <para>
/// <strong>A throw is a failed check, not a decision.</strong> It is written
/// to the log as an error (category <c>Tracon.RunAuthorization</c>) with the
/// handler type, the access kind and the resource identifiers. A <c>403</c>
/// then says that the check failed and can be retried, instead of claiming
/// that the handler denied the call. A <c>404</c> does not change at all: it
/// must stay identical to the answer for a resource that does not exist. The
/// exception text never reaches the response.
/// </para>
/// <para>
/// Only this request's own cancellation travels out of the gate. Any other
/// <see cref="OperationCanceledException"/> — a handler that calls its policy
/// store over HTTP and times out — is an ordinary failure and is denied like
/// one.
/// </para>
/// </remarks>
internal static class RunAuthorizationGate
{
    /// <summary>The problem detail of a <c>403</c> produced because the handler threw.</summary>
    internal const string FailedCheckDetail = "The run authorization check failed. Retry the request.";

    /// <summary>The problem title of every <c>403</c> this gate produces itself.</summary>
    private const string NotAuthorizedTitle = "Run not authorized";

    /// <summary>Checks whether a run may start; produces the response to return if it may not.</summary>
    /// <param name="handler">The authorization handler. If <see langword="null"/>, no check is performed.</param>
    /// <param name="tenants">The tenant context.</param>
    /// <param name="agentName">The name of the agent (or workflow) to run.</param>
    /// <param name="sessionId">The session the run would continue, or <see langword="null"/> for a sessionless run.</param>
    /// <param name="attributionContext">The attribution context, used to resolve the calling user.</param>
    /// <param name="httpContext">The request, used to log a failure.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The <c>403</c> response to return if the handler denies the run or throws; otherwise <see langword="null"/>.
    /// </returns>
    public static async ValueTask<ProblemHttpResult?> CheckRunAsync(
        IRunAuthorizationHandler? handler,
        ITenantContext tenants,
        string agentName,
        string? sessionId,
        IRunAttributionContext? attributionContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (handler is null)
        {
            return null;
        }

        var userId = AuthorizationGateDiagnostics.ReadUserId(attributionContext, httpContext);

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
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            // A consumer implementation reaches into its own policy and can
            // fail there. Fail-closed: the run does not start.
            LogHandlerFailure(httpContext, exception, handler, request, agentName);

            return Failed(NotAuthorizedTitle);
        }

        return TypedResults.Problem(
            title: NotAuthorizedTitle,
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
    /// <param name="httpContext">The request, used to log a failure.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <paramref name="denied"/> if the handler denies the access; otherwise <see langword="null"/>.
    /// When the handler throws, a <c>404</c> <paramref name="denied"/> is returned unchanged, and a
    /// <c>403</c> one is replaced by a <c>403</c> that says the check failed.
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
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(denied);
        ArgumentNullException.ThrowIfNull(httpContext);

        if (handler is null)
        {
            return null;
        }

        var userId = AuthorizationGateDiagnostics.ReadUserId(attributionContext, httpContext);

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
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            // Fail-closed: the access does not proceed.
            LogHandlerFailure(httpContext, exception, handler, request, agentName);

            // 🚨 A 404 stays byte for byte the caller's own "not found"
            // (K-684): wording that said "the check failed" would confirm the
            // resource exists. Only a 403 — a list, or a run started from
            // another run — has no identity to hide and can say what happened.
            return denied.StatusCode == StatusCodes.Status403Forbidden
                ? Failed(denied.ProblemDetails.Title ?? NotAuthorizedTitle)
                : denied;
        }

        // The denial reason a handler supplied is deliberately NOT surfaced
        // here — the response must stay identical to a missing resource's.
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
    /// <param name="httpContext">The request, used to log a failure.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The response to return if the handler denies the access or throws; otherwise <see langword="null"/>.
    /// A denied <see cref="SessionAccess.List"/> returns <c>403</c> — a list is an operation, not a
    /// resource, so there is no identity to leak. Every other denied access returns <c>404</c>, with
    /// the SAME title and detail text a genuinely missing session gets (see <c>SessionEndpoints</c>):
    /// a <c>403</c> there would confirm the session exists, and wording that differed between "denied"
    /// and "missing" would leak the same thing through a side channel. When the handler throws, the
    /// <c>404</c> stays the same and the list <c>403</c> says that the check failed.
    /// </returns>
    public static async ValueTask<ProblemHttpResult?> CheckSessionAsync(
        IRunAuthorizationHandler? handler,
        ITenantContext tenants,
        string? sessionId,
        IRunAttributionContext? attributionContext,
        SessionAccess access,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (handler is null)
        {
            return null;
        }

        var userId = AuthorizationGateDiagnostics.ReadUserId(attributionContext, httpContext);

        var request = new SessionAuthorizationRequest
        {
            TenantId = tenants.TenantId,
            SessionId = sessionId,
            UserId = userId,
            Access = access,
        };

        bool allowed;
        var failed = false;

        try
        {
            allowed = (await handler.AuthorizeSessionAsync(request, cancellationToken).ConfigureAwait(false)).IsAllowed;
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            // Fail-closed: the access does not proceed.
            LogHandlerFailure(
                httpContext, exception, handler, access.ToString(), request.TenantId, agentName: null, runId: null, sessionId);
            allowed = false;
            failed = true;
        }

        if (allowed)
        {
            return null;
        }

        if (access == SessionAccess.List)
        {
            return failed
                ? Failed(NotAuthorizedTitle)
                : TypedResults.Problem(
                    title: NotAuthorizedTitle,
                    detail: "The registered IRunAuthorizationHandler denied this request.",
                    statusCode: StatusCodes.Status403Forbidden);
        }

        return TypedResults.Problem(
            title: "Session not found",
            detail: $"There is no session with id '{sessionId}'.",
            statusCode: StatusCodes.Status404NotFound);
    }

    /// <summary>Builds the <c>403</c> returned when the handler threw instead of deciding.</summary>
    /// <param name="title">The problem title the surface uses for its own denial.</param>
    /// <returns>The problem response.</returns>
    private static ProblemHttpResult Failed(string title)
        => TypedResults.Problem(
            title: title,
            detail: FailedCheckDetail,
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>Writes the error line for a handler that threw during a run check.</summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="exception">What the handler threw.</param>
    /// <param name="handler">The handler that threw.</param>
    /// <param name="request">The request the handler was asked about.</param>
    /// <param name="agentName">The agent, when known.</param>
    private static void LogHandlerFailure(
        HttpContext httpContext,
        Exception exception,
        IRunAuthorizationHandler handler,
        RunAuthorizationRequest request,
        string? agentName)
        => LogHandlerFailure(
            httpContext, exception, handler, request.Access.ToString(), request.TenantId, agentName, request.RunId, request.SessionId);

    /// <summary>Writes the error line for a handler that threw.</summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="exception">What the handler threw.</param>
    /// <param name="handler">The handler that threw.</param>
    /// <param name="access">The kind of access that was being checked.</param>
    /// <param name="tenantId">The tenant of the request.</param>
    /// <param name="agentName">The agent, when known.</param>
    /// <param name="runId">The run, when there is one.</param>
    /// <param name="sessionId">The session, when there is one.</param>
    /// <remarks>
    /// The user id is deliberately not written: the resource identifiers are
    /// enough to find the request, and the exception carries the cause.
    /// </remarks>
    private static void LogHandlerFailure(
        HttpContext httpContext,
        Exception exception,
        IRunAuthorizationHandler handler,
        string access,
        string tenantId,
        string? agentName,
        Guid? runId,
        string? sessionId)
        => AuthorizationGateDiagnostics
            .CreateLogger(httpContext, AuthorizationGateDiagnostics.RunAuthorizationCategory)
            .LogError(
                exception,
                "The registered IRunAuthorizationHandler ({HandlerType}) threw while checking {Access} access " +
                "(tenant '{TenantId}', agent '{AgentName}', run '{RunId}', session '{SessionId}'). " +
                "The request was denied (fail-closed).",
                handler.GetType().FullName,
                access,
                tenantId,
                agentName,
                runId,
                sessionId);
}
