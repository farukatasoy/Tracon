using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Denetim izi yazan tum kod yollarinin (depo dekoratorleri, uc katmani istisnalari)
/// ortak yazma yolunu tasiyan yardimci.
/// </summary>
/// <remarks>
/// Sir suzgecini uygular ve yazma hatasini yutar: denetim izi hatasi islemi
/// <strong>kesmez</strong>, yalnizca loglanir. Faz 6'nin "gozlemlenebilirlik
/// islevi bozmaz" kuralinin aynisi.
/// </remarks>
public static class AuditRecorder
{
    /// <summary>Sir suzgecinden gecirilmis bir denetim kaydi yazar; hata loglanir, islem kesilmez.</summary>
    /// <param name="auditLog">Yazilacak defter.</param>
    /// <param name="actorResolver">Aktor cozumleyici.</param>
    /// <param name="logger">Yazma hatasinin loglanacagi gunlukleyici.</param>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="action">Eylem adi.</param>
    /// <param name="entity">Etkilenen varlik.</param>
    /// <param name="before">Onceki durum, JSON metni.</param>
    /// <param name="after">Sonraki durum, JSON metni.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    public static async ValueTask WriteAsync(
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ILogger logger,
        string tenantId,
        string action,
        string entity,
        string? before,
        string? after,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditLog.WriteAsync(
                new AuditEntry
                {
                    Id = AgentPrismId.NewId(),
                    TenantId = tenantId,
                    Actor = actorResolver.Resolve(),
                    Action = action,
                    Entity = entity,
                    Before = AuditSecretFilter.Redact(before),
                    After = AuditSecretFilter.Redact(after),
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "'{Action}' eylemi '{Entity}' varligi icin denetim izine yazilamadi.",
                action,
                entity);
        }
    }
}
