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
            .WithName("AgentPrismListSessions")
            .WithSummary("Oturumlari son guncellemeden eskiye listeler.");

        builder.MapGet("/api/sessions/{sessionId}", GetSessionAsync)
            .RequireRole(roles.Reader)
            .WithName("AgentPrismGetSession")
            .WithSummary("Bir oturumun ustverisini ve sohbet gecmisini dondurur.");

        builder.MapDelete("/api/sessions/{sessionId}", DeleteSessionAsync)
            .RequireRole(roles.Operator)
            .WithName("AgentPrismDeleteSession")
            .WithSummary("Bir oturumu siler.");
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
                title: "Oturum bulunamadi",
                detail: $"'{sessionId}' kimlikli bir oturum yok.",
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
                title: "Oturum bulunamadi",
                detail: $"'{sessionId}' kimlikli bir oturum yok.",
                statusCode: StatusCodes.Status404NotFound);
        }

        await attachmentStore
            .DeleteBySessionAsync(tenantContext.TenantId, sessionId, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }
}
