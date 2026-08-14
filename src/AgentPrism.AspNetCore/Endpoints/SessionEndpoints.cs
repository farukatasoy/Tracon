using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Oturum listeleme, okuma ve silme uclari.
/// </summary>
internal static class SessionEndpoints
{
    /// <summary>Oturum uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/sessions", async Task<Ok<IReadOnlyList<SessionRecord>>> (
                AgentSessionManager sessions,
                string? agentName,
                int? skip,
                int? take,
                CancellationToken cancellationToken) =>
            {
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
            .WithSummary("Oturumlari son guncellemeden eskiye listeler.");

        builder.MapGet("/api/sessions/{sessionId}", GetSessionAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismGetSession")
            .WithTags("AgentPrism", "Sessions")
            .WithSummary("Bir oturumun ustverisini ve sohbet gecmisini dondurur.");

        builder.MapDelete("/api/sessions/{sessionId}", DeleteSessionAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismDeleteSession")
            .WithTags("AgentPrism", "Sessions")
            .WithSummary("Bir oturumu siler.");

        builder.MapPost("/api/sessions/{sessionId}/branch", BranchSessionAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismBranchSession")
            .WithTags("AgentPrism", "Sessions")
            .WithSummary("Bir konusmayi belirli bir noktadan dallandirir ve yeni bir oturum acar.")
            .Accepts<SessionBranchRequest>("application/json")
            .WithDescription(
                "Ogeler 'upToSequence' degerine kadar (dahil) YENI bir konusmaya KOPYALANIR; " +
                "isaretci yalnizca koken bilgisidir. Dala yazmak ana konusmayi degistirmez. " +
                "Dallandirma yalnizca kalici bir SQL saglayicisi acikken calisir; bellek ici " +
                "kurulumda sohbet gecmisi oturum durumunun opak blogunda yasar ve 501 doner.")
            .Produces<SessionBranchResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status501NotImplemented);
    }

    /// <summary>
    /// Bir oturumun konusmasini dallandirir ve dali tasiyan yeni bir oturum acar.
    /// </summary>
    /// <remarks>
    /// Uc oturum uzerindedir, konusma uzerinde degil: konusma kimligi oturum
    /// durumunda yasar ve dallanan konusmayi kullanmanin tek yolu o kimligi
    /// tasiyan yeni bir oturumdur.
    /// </remarks>
    private static async Task<Results<Created<SessionBranchResult>, ProblemHttpResult>> BranchSessionAsync(
        string sessionId,
        HttpContext httpContext,
        ConversationBranchService branches,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ITenantContext tenants,
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
            after: $$"""{"parentSessionId":"{{result.ParentSessionId}}","branchFromSequence":{{result.BranchFromSequence}},"copiedItemCount":{{result.CopiedItemCount}}}""",
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"/api/sessions/{Uri.EscapeDataString(result.SessionId)}", result);
    }

    private static async Task<Results<Ok<SessionDetailResponse>, ProblemHttpResult>> GetSessionAsync(
        string sessionId,
        ISessionStore store,
        IAgentCatalog catalog,
        ChatHistoryProvider chatHistory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
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
    /// Bir oturumu ve ona bagli tum ekleri siler.
    /// </summary>
    /// <remarks>
    /// Ekler oturumdan SONRA silinir: oturum bulunamazsa hicbir yan etki olmaz.
    /// Bu cagri, kalici (PostgreSQL) ve bellek ici depoda ekleri temizleyen TEK
    /// yoldur — <c>attachments.session_id</c> BILEREK yabanci anahtar degildir
    /// (bkz. migration 0006 yorumu: bir ek, oturumu hic acilmadan once
    /// yuklenebilir). Gerekce: <c>docs/14-COK-MODLULUK.md</c>, acik soru 2.
    /// </remarks>
    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteSessionAsync(
        string sessionId,
        AgentSessionManager sessions,
        IAttachmentStore attachmentStore,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
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
