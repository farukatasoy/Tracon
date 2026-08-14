using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>Denetim izi okuma uclari.</summary>
/// <remarks>
/// Salt okunurdur: silme veya duzeltme ucu yoktur ve olmayacaktir. Saklama
/// politikasi Faz 25'in isidir.
/// </remarks>
internal static class AuditEndpoints
{
    /// <summary>Denetim izi uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/audit", async Task<Ok<IReadOnlyList<AuditEntry>>> (
                IAuditLog auditLog,
                ITenantContext tenants,
                string? actor,
                string? action,
                string? entity,
                DateTimeOffset? after,
                DateTimeOffset? before,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                var entries = await auditLog.QueryAsync(
                    new AuditQuery
                    {
                        TenantId = tenants.TenantId,
                        Actor = actor,
                        Action = action,
                        Entity = entity,
                        After = after,
                        Before = before,
                        Limit = limit is { } max ? Math.Clamp(max, 1, 500) : 100,
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(entries);
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AuditRead)
            .WithName("AgentPrismListAudit")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Filtrelenebilir denetim kayitlarini listeler.")
            .WithDescription(
                "actor, action, entity ve tarih araligina gore filtrelenebilir. Calistirmalar " +
                "(agent'in bir mesaji islemesi) bu deftere yazilmaz; runs tablosu zaten tam kaydi tutar. " +
                "Tek istisna 'content.blocked' eylemidir (Faz 48): bir IContentGuard'in engelleme " +
                "karari bir calistirma ayrintisi degil bir YONETISIM kararidir ve calistirma kaydi " +
                "saklama politikasiyla silindikten sonra da izlenebilir kalmalidir. Kayit yalniz " +
                "guard ve kural adini tasir, engellenen METNI tasimaz.");

        builder.MapGet("/api/audit/{entity}", async Task<Ok<IReadOnlyList<AuditEntry>>> (
                string entity,
                IAuditLog auditLog,
                ITenantContext tenants,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                var entries = await auditLog.QueryAsync(
                    new AuditQuery
                    {
                        TenantId = tenants.TenantId,
                        Entity = entity,
                        Limit = limit is { } max ? Math.Clamp(max, 1, 500) : 100,
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(entries);
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AuditRead)
            .WithName("AgentPrismGetEntityAudit")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Tek bir varligin degisiklik gecmisini, en yeniden eskiye dondurur.");
    }
}
