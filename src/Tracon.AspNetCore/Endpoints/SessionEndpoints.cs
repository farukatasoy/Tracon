using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Session listing, reading, and deletion endpoints.
/// </summary>
internal static class SessionEndpoints
{
    /// <summary>Maps the session endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/sessions", async Task<Results<Ok<IReadOnlyList<SessionRecord>>, ProblemHttpResult>> (
                HttpContext httpContext,
                AgentSessionManager sessions,
                ITenantContext tenants,
                [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                [FromServices] IRunAttributionContext? attributionContext,
                [FromServices] IOptionsMonitor<TraconSessionOwnershipOptions>? ownershipOptions,
                string? agentName,
                int? skip,
                int? take,
                CancellationToken cancellationToken) =>
            {
                // 🚨 A handler that DENIES the list still rejects it outright
                // (403) rather than quietly returning fewer rows: the handler
                // answered a yes/no question and silently downgrading a "no"
                // to a short list would hide the refusal from the caller.
                if (await RunAuthorizationGate
                        .CheckSessionAsync(runAuthorizationHandler, tenants, sessionId: null, attributionContext, SessionAccess.List, httpContext, cancellationToken)
                        .ConfigureAwait(false) is { } authorizationProblem)
                {
                    return authorizationProblem;
                }

                // 🚨 Ownership narrowing is a DIFFERENT thing and it DOES
                // filter - phase 148. Until then Tracon knew of no owner
                // narrower than the tenant, so the only honest answer to "show
                // this user their own sessions" was to reject the whole list;
                // filtering after the fact would also have broken the
                // skip/take contract by cutting rows out of an already-built
                // page. Neither objection survives now: the owner lives in the
                // row, the filter is a WHERE clause, and paging runs over the
                // already-narrowed set. Off by default, in which case this
                // resolves to null and nothing about the query changes.
                var ownerFilter = await SessionOwnershipGate
                    .ResolveListOwnerFilterAsync(ownershipOptions, attributionContext, httpContext, cancellationToken)
                    .ConfigureAwait(false);

                var records = await sessions.QuerySessionsAsync(
                    new SessionQuery
                    {
                        AgentName = agentName,
                        OwnerId = ownerFilter,
                        Skip = Math.Max(skip ?? 0, 0),
                        Take = Math.Clamp(take ?? 50, 1, 200),
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(records);
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconListSessions")
            .WithTags("Tracon", "Sessions")
            .WithSummary("Lists sessions from most recently updated to oldest.")
            .WithDescription(
                "Paging is offset based: 'skip' defaults to 0 and 'take' to 50, and 'take' is " +
                "clamped to the 1..200 range rather than rejected, so an out-of-range value never " +
                "fails the request. 'agentName' narrows the list to one agent. Because the order " +
                "is by last update, a session that changes while a client pages can move between " +
                "pages; use the session id, not the position, as the identity. If a registered " +
                "IRunAuthorizationHandler denies the caller, the response is 403 — a denied list is " +
                "REJECTED, never quietly shortened. When session ownership is turned on, the list is " +
                "additionally narrowed to the calling user's own sessions before paging is applied, " +
                "unless the caller satisfies the configured management policy; sessions written " +
                "before ownership was turned on carry no owner and appear only in that management " +
                "listing.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        builder.MapGet("/api/sessions/{sessionId}", GetSessionAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconGetSession")
            .WithTags("Tracon", "Sessions")
            .WithSummary("Returns a session's metadata and chat history.")
            .WithDescription(
                "'messages' is the readable chat history and is null when the configured session " +
                "storage cannot expose one — with an in-memory setup the history lives inside an " +
                "opaque state blob. 'state' always carries that raw provider state. Messages come " +
                "back in sequence order, so the index of a message is the sequence number the " +
                "branch endpoint expects. If a registered IRunAuthorizationHandler denies the " +
                "caller, the response is 404 — identical to a session that does not exist, so a " +
                "denial never confirms the session's existence. With session ownership turned on, " +
                "another user's session answers the same 404.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        builder.MapDelete("/api/sessions/{sessionId}", DeleteSessionAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconDeleteSession")
            .WithTags("Tracon", "Sessions")
            .WithSummary("Deletes a session.")
            .WithDescription(
                "Attachments linked to the session are deleted with it, and this call is the only " +
                "way they are cleaned up: an attachment may be uploaded before any session exists, " +
                "so the link is deliberately not a database foreign key. The attachments are " +
                "removed only after the session itself is found, so a 404 leaves no side effect. " +
                "Runs recorded under the session are kept — run history does not depend on the " +
                "session still existing. If a registered IRunAuthorizationHandler denies the " +
                "caller, the response is also 404, identical to a session that does not exist; with " +
                "session ownership turned on, another user's session answers that same 404 and is " +
                "left untouched.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        builder.MapPost("/api/sessions/{sessionId}/branch", BranchSessionAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconBranchSession")
            .WithTags("Tracon", "Sessions")
            .WithSummary("Branches a conversation from a specific point and opens a new session.")
            .Accepts<SessionBranchRequest>("application/json")
            .WithDescription(
                "Items up to and including 'upToSequence' are COPIED into a NEW conversation; " +
                "the pointer is only provenance information. Writing to the branch does not " +
                "change the parent conversation. Branching only works while a persistent SQL " +
                "provider is enabled; in an in-memory setup, chat history lives in an opaque " +
                "blob of session state and this returns 501. If a registered " +
                "IRunAuthorizationHandler denies the caller, the response is 404, identical to a " +
                "session that does not exist; with session ownership turned on, another user's " +
                "session answers that same 404. The new session inherits the SOURCE session's " +
                "owner, not the caller's — a branch is a copy, not a handover.")
            .Produces<SessionBranchResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status501NotImplemented);
    }

    /// <summary>
    /// The one "session not found" response every endpoint here returns.
    /// </summary>
    /// <remarks>
    /// A single factory on purpose. Three endpoints answer this for two
    /// different reasons — the session genuinely does not exist, or the caller
    /// may not reach it — and the two have to be indistinguishable byte for
    /// byte. Two hand-written copies of the same strings drift; one factory
    /// cannot. <c>RunAuthorizationGate</c> writes the identical pair for its
    /// own denials.
    /// </remarks>
    /// <param name="sessionId">The session the caller asked for.</param>
    /// <returns>The 404 problem response.</returns>
    private static ProblemHttpResult SessionNotFound(string sessionId)
        => TypedResults.Problem(
            title: "Session not found",
            detail: $"There is no session with id '{sessionId}'.",
            statusCode: StatusCodes.Status404NotFound);

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
        ISessionStore store,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ITenantContext tenants,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        [FromServices] IOptionsMonitor<TraconSessionOwnershipOptions>? ownershipOptions,
        ILoggerFactory loggerFactory,
        [FromServices] TraconMetrics metrics,
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
                .CheckSessionAsync(runAuthorizationHandler, tenants, sessionId, attributionContext, SessionAccess.Branch, httpContext, cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        // Same 404 as above, for the same reason. The branch that DOES get
        // made keeps the SOURCE session's owner rather than the caller's:
        // branching is a copy, not a handover, and the caller only got here by
        // already being that owner.
        if (await SessionOwnershipGate
                .DeniesAsync(ownershipOptions, attributionContext, store, sessionId, httpContext, cancellationToken)
                .ConfigureAwait(false))
        {
            return SessionNotFound(sessionId);
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
            loggerFactory.CreateLogger("Tracon.SessionEndpoints"),
            metrics,
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
        HttpContext httpContext,
        ISessionStore store,
        IAgentCatalog catalog,
        ChatHistoryProvider chatHistory,
        ITenantContext tenants,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        [FromServices] IOptionsMonitor<TraconSessionOwnershipOptions>? ownershipOptions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await RunAuthorizationGate
                .CheckSessionAsync(runAuthorizationHandler, tenants, sessionId, attributionContext, SessionAccess.Read, httpContext, cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        // 🚨 The SAME body a missing session gets, written a few lines below
        // from the same two strings: a denial that read differently would
        // confirm the session exists through a side channel (K-671).
        if (await SessionOwnershipGate
                .DeniesAsync(ownershipOptions, attributionContext, store, sessionId, httpContext, cancellationToken)
                .ConfigureAwait(false))
        {
            return SessionNotFound(sessionId);
        }

        var record = await store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (record is null)
        {
            return SessionNotFound(sessionId);
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
        HttpContext httpContext,
        AgentSessionManager sessions,
        ISessionStore store,
        IAttachmentStore attachmentStore,
        ITenantContext tenantContext,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        [FromServices] IOptionsMonitor<TraconSessionOwnershipOptions>? ownershipOptions,
        CancellationToken cancellationToken)
    {
        if (await RunAuthorizationGate
                .CheckSessionAsync(runAuthorizationHandler, tenantContext, sessionId, attributionContext, SessionAccess.Delete, httpContext, cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        // 🚨 Checked BEFORE the delete, not after: a denial must leave the
        // other owner's session, and its attachments, untouched.
        if (await SessionOwnershipGate
                .DeniesAsync(ownershipOptions, attributionContext, store, sessionId, httpContext, cancellationToken)
                .ConfigureAwait(false))
        {
            return SessionNotFound(sessionId);
        }

        if (!await sessions.DeleteSessionAsync(sessionId, cancellationToken).ConfigureAwait(false))
        {
            return SessionNotFound(sessionId);
        }

        await attachmentStore
            .DeleteBySessionAsync(tenantContext.TenantId, sessionId, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }
}
