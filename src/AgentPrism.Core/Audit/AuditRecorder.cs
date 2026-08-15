using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// A helper that supplies the common write path for all code that writes audit entries,
/// including store decorators and endpoint-layer exceptions.
/// </summary>
/// <remarks>
/// Applies the secret filter and swallows write errors. An audit trail error does
/// <strong>not</strong> interrupt the operation; it is only logged. This is the
/// Phase 6 rule that observability does not break functionality.
/// </remarks>
public static class AuditRecorder
{
    /// <summary>Writes a secret-filtered audit entry. It logs an error without interrupting the operation.</summary>
    /// <param name="auditLog">The log to write.</param>
    /// <param name="actorResolver">The actor resolver.</param>
    /// <param name="logger">The logger for a write error.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="action">The action name.</param>
    /// <param name="entity">The affected entity.</param>
    /// <param name="before">The previous state as JSON text.</param>
    /// <param name="after">The next state as JSON text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
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
                "Could not write action '{Action}' for entity '{Entity}' to the audit trail.",
                action,
                entity);
        }
    }
}
