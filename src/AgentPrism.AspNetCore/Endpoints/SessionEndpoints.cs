using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Session listing, reading, and deletion endpoints.
/// </summary>
internal static class SessionEndpoints
{
    /// <summary>Maps the session endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/sessions", async Task<Results<Ok<IReadOnlyList<SessionRecord>>, ProblemHttpResult>> (
                AgentSessionManager sessions,
                ITenantContext tenants,
                [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                [FromServices] IRunAttributionContext? attributionContext,
                string? agentName,
                int? skip,
                int? take,
                CancellationToken cancellationToken) =>
            {
                // 🚨 A denied list is NOT filtered, it is REJECTED (403): the
                // caller who asked for a filtered list already keeps that
                // filter on their own side, and server-side filtering would
                // break the skip/take paging contract - phase 139, F-185.
                if (await RunAuthorizationGate
                        .CheckSessionAsync(runAuthorizationHandler, tenants, sessionId: null, attributionContext, SessionAccess.List, cancellationToken)
                        .ConfigureAwait(false) is { } authorizationProblem)
                {
                    return authorizationProblem;
                }

                var records = await sessions.QuerySessionsAsync(
                    new SessionQuery
                    {
                        AgentName = agentName,
                        Skip = Math.Max(skip ?? 0, 0),
                        Take = Math.Clamp(take ?? 50, 1, 200),
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(records);
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismListSessions")
            .WithTags("AgentPrism", "Sessions")
            .WithSummary("Lists sessions from most recently updated to oldest.")
            .WithDescription(
                "Paging is offset based: 'skip' defaults to 0 and 'take' to 50, and 'take' is " +
                "clamped to the 1..200 range rather than rejected, so an out-of-range value never " +
                "fails the request. 'agentName' narrows the list to one agent. Because the order " +
                "is by last update, a session that changes while a client pages can move between " +
                "pages; use the session id, not the position, as the identity. If a registered " +
                "IRunAuthorizationHandler denies the caller, the response is 403 — the list is " +
                "REJECTED, never silently filtered, because server-side filtering would break the " +
                "skip/take paging contract.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        builder.MapGet("/api/sessions/{sessionId}", GetSessionAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismGetSession")
            .WithTags("AgentPrism", "Sessions")
            .WithSummary("Returns a session's metadata and chat history.")
            .WithDescription(
                "'messages' is the readable chat history and is null when the configured session " +
                "storage cannot expose one — with an in-memory setup the history lives inside an " +
                "opaque state blob. 'state' always carries that raw provider state. Messages come " +
                "back in sequence order, so the index of a message is the sequence number the " +
                "branch endpoint expects. If a registered IRunAuthorizationHandler denies the " +
                "caller, the response is 404 — identical to a session that does not exist, so a " +
                "denial never confirms the session's existence.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        builder.MapDelete("/api/sessions/{sessionId}", DeleteSessionAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismDeleteSession")
            .WithTags("AgentPrism", "Sessions")
            .WithSummary("Deletes a session.")
            .WithDescription(
                "Attachments linked to the session are deleted with it, and this call is the only " +
                "way they are cleaned up: an attachment may be uploaded before any session exists, " +
                "so the link is deliberately not a database foreign key. The attachments are " +
                "removed only after the session itself is found, so a 404 leaves no side effect. " +
                "Runs recorded under the session are kept — run history does not depend on the " +
                "session still existing. If a registered IRunAuthorizationHandler denies the " +
                "caller, the response is also 404, identical to a session that does not exist.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        builder.MapPost("/api/sessions/{sessionId}/branch", BranchSessionAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismBranchSession")
            .WithTags("AgentPrism", "Sessions")
            .WithSummary("Branches a conversation from a specific point and opens a new session.")
            .Accepts<SessionBranchRequest>("application/json")
            .WithDescription(
                "Items up to and including 'upToSequence' are COPIED into a NEW conversation; " +
                "the pointer is only provenance information. Writing to the branch does not " +
                "change the parent conversation. Branching only works while a persistent SQL " +
                "provider is enabled; in an in-memory setup, chat history lives in an opaque " +
                "blob of session state and this returns 501. If a registered " +
                "IRunAuthorizationHandler denies the caller, the response is 404, identical to a " +
                "session that does not exist.")
            .Produces<SessionBranchResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status501NotImplemented);
    }

    /// <summary>
    /// Branches a session's conversation and opens a new session that carries the branch.
    /// </summary>
    /// <remarks>
    /// The endpoint operates on the session, not the conversation: the conversation id
    /// lives in the session state, and the only way to use the branched conversation is
    /// through a new session that carries that id.
    /// </remarks>
    private static async Task<Results<Created<SessionBranchResult>, ProblemHttpResult>> BranchSessionAsync(
        string sessionId,
        HttpContext httpContext,
        ConversationBranchService branches,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ITenantContext tenants,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<SessionBranchRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        // 🚨 Checked BEFORE branching: a denial produces the SAME 404 title
        // and detail ConversationBranchService's own SessionNotFound outcome
        // does a few lines below, so a caller cannot tell "denied" from
        // "does not exist" - phase 139, F-185.
        if (await RunAuthorizationGate
                .CheckSessionAsync(runAuthorizationHandler, tenants, sessionId, attributionContext, SessionAccess.Branch, cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        var request = bound!;

        var outcome = await branches.BranchAsync(sessionId, request, cancellationToken).ConfigureAwait(false);

        if (outcome.Status != SessionBranchStatus.Branched)
        {
            return outcome.Status switch
            {
                SessionBranchStatus.NotSupported => TypedResults.Problem(
                    title: "Branching not supported",
                    detail: outcome.Detail,
                    statusCode: StatusCodes.Status501NotImplemented),
                SessionBranchStatus.SessionNotFound or SessionBranchStatus.AgentNotFound => TypedResults.Problem(
                    title: "Session not found",
                    detail: outcome.Detail,
                    statusCode: StatusCodes.Status404NotFound),
                SessionBranchStatus.SessionExists => TypedResults.Problem(
                    title: "Session id in use",
                    detail: outcome.Detail,
                    statusCode: StatusCodes.Status409Conflict),
                _ => TypedResults.Problem(
                    title: "Could not branch",
                    detail: outcome.Detail,
                    statusCode: StatusCodes.Status400BadRequest),
            };
        }

        var result = outcome.Result!;

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("AgentPrism.SessionEndpoints"),
            tenants.TenantId,
            action: "session.branch",
            entity: $"session:{result.SessionId}",
            before: null,
            after: AuditPayload.Write(writer =>
            {
                writer.WriteString("parentSessionId", result.ParentSessionId);
                writer.WriteNumber("branchFromSequence", result.BranchFromSequence);
                writer.WriteNumber("copiedItemCount", result.CopiedItemCount);
            }),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"/api/sessions/{Uri.EscapeDataString(result.SessionId)}", result);
    }

    private static async Task<Results<Ok<SessionDetailResponse>, ProblemHttpResult>> GetSessionAsync(
        string sessionId,
        ISessionStore store,
        IAgentCatalog catalog,
        ChatHistoryProvider chatHistory,
        ITenantContext tenants,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await RunAuthorizationGate
                .CheckSessionAsync(runAuthorizationHandler, tenants, sessionId, attributionContext, SessionAccess.Read, cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        var record = await store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (record is null)
        {
            return TypedResults.Problem(
                title: "Session not found",
                detail: $"There is no session with id '{sessionId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var messages = await ChatHistoryReader
            .ReadAsync(record, catalog, chatHistory, loggerFactory, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new SessionDetailResponse
        {
            Id = record.Id,
            AgentName = record.AgentName,
            TenantId = record.TenantId,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            Messages = messages is null
                ? null
                : JsonSerializer.SerializeToElement(messages, AIJsonUtilities.DefaultOptions),
            State = record.State,
        });
    }

    /// <summary>
    /// Deletes a session and all attachments linked to it.
    /// </summary>
    /// <remarks>
    /// Attachments are deleted AFTER the session: if the session is not found, there
    /// is no side effect. This call is the ONLY way to clean up attachments in both
    /// the persistent (PostgreSQL) and in-memory stores — <c>attachments.session_id</c>
    /// is DELIBERATELY not a foreign key (see the migration 0006 comment: an
    /// attachment can be uploaded before a session is ever opened).
    /// </remarks>
    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteSessionAsync(
        string sessionId,
        AgentSessionManager sessions,
        IAttachmentStore attachmentStore,
        ITenantContext tenantContext,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        CancellationToken cancellationToken)
    {
        if (await RunAuthorizationGate
                .CheckSessionAsync(runAuthorizationHandler, tenantContext, sessionId, attributionContext, SessionAccess.Delete, cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        if (!await sessions.DeleteSessionAsync(sessionId, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.Problem(
                title: "Session not found",
                detail: $"There is no session with id '{sessionId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        await attachmentStore
            .DeleteBySessionAsync(tenantContext.TenantId, sessionId, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }
}
