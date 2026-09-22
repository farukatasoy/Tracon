using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// A helper that supplies the common write path for all code that writes audit entries,
/// including store decorators and endpoint-layer exceptions.
/// </summary>
/// <remarks>
/// <para>
/// Applies the secret filter and offers the <strong>two</strong> guarantees the audit
/// trail publishes. <see cref="WriteAsync"/> is the best-effort path: a write error is
/// logged and swallowed, and the operation continues — the rule that observability does
/// not break functionality. <see cref="WriteOrThrowAsync"/> is the fail-closed path: a
/// write error is logged and rethrown, so the caller's operation is never applied.
/// </para>
/// <para>
/// Which operations take the fail-closed path is a published contract; the list is in
/// <see cref="IAuditLog"/>'s own documentation. Both paths count a failed write on
/// <see cref="TraconDiagnostics.AuditWriteFailureCounterName"/>, separated by the
/// <c>tracon.audit.outcome</c> tag, so a broken audit store is visible on a dashboard
/// rather than only in a log search.
/// </para>
/// </remarks>
internal static class AuditRecorder
{
    /// <summary>The outcome tag written when a failed audit write did not stop the operation.</summary>
    internal const string SwallowedOutcome = "swallowed";

    /// <summary>The outcome tag written when a failed audit write refused the operation.</summary>
    internal const string RefusedOutcome = "refused";

    /// <summary>Writes a secret-filtered audit entry. It logs an error without interrupting the operation.</summary>
    /// <param name="auditLog">The log to write.</param>
    /// <param name="actorResolver">The actor resolver.</param>
    /// <param name="logger">The logger for a write error.</param>
    /// <param name="metrics">
    /// The metric set that counts a failed write. When <see langword="null"/> the failure
    /// is only logged; every call site inside Tracon supplies it.
    /// </param>
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
        TraconMetrics? metrics,
        string tenantId,
        string action,
        string entity,
        string? before,
        string? after,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditLog);
        ArgumentNullException.ThrowIfNull(actorResolver);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            await auditLog.WriteAsync(
                Entry(tenantId, actorResolver.Resolve(), action, entity, before, after, DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Could not write action '{Action}' for entity '{Entity}' to the audit trail.",
                action,
                entity);

            metrics?.RecordAuditWriteFailure(tenantId, action, SwallowedOutcome);
        }
    }

    /// <summary>
    /// Writes a secret-filtered audit entry and rethrows when the store rejects it, so the
    /// caller's operation is never applied.
    /// </summary>
    /// <param name="auditLog">The log to write.</param>
    /// <param name="actor">
    /// The actor, already resolved. It is a plain string rather than an
    /// <see cref="IAuditActorResolver"/> because a background caller has no ambient actor
    /// to resolve and names a system actor directly.
    /// </param>
    /// <param name="logger">The logger for a write error.</param>
    /// <param name="metrics">
    /// The metric set that counts a failed write. When <see langword="null"/> the failure
    /// is only logged; every call site inside Tracon supplies it.
    /// </param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="action">The action name.</param>
    /// <param name="entity">The affected entity.</param>
    /// <param name="before">The previous state as JSON text.</param>
    /// <param name="after">The next state as JSON text.</param>
    /// <param name="refusal">
    /// The caller-specific first clause of the refusal message, such as
    /// <c>"Script 'notes/build' was not run"</c>. The sentence is completed with
    /// <c>" because it could not be written to the audit trail."</c>. It reaches the caller
    /// of the HTTP surface, so it must name the refused operation and must not carry a payload.
    /// </param>
    /// <param name="timeProvider">
    /// The clock the entry is stamped with. When <see langword="null"/>,
    /// <see cref="TimeProvider.System"/> is used.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="TraconException">The entry could not be written.</exception>
    public static async ValueTask WriteOrThrowAsync(
        IAuditLog auditLog,
        string? actor,
        ILogger logger,
        TraconMetrics? metrics,
        string tenantId,
        string action,
        string entity,
        string? before,
        string? after,
        string refusal,
        TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditLog);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(refusal);

        try
        {
            await auditLog.WriteAsync(
                Entry(
                    tenantId,
                    actor,
                    action,
                    entity,
                    before,
                    after,
                    (timeProvider ?? TimeProvider.System).GetUtcNow()),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Could not write action '{Action}' for entity '{Entity}' to the audit trail; the operation was refused.",
                action,
                entity);

            metrics?.RecordAuditWriteFailure(tenantId, action, RefusedOutcome);

            throw new TraconException(
                $"{refusal} because it could not be written to the audit trail.",
                ex);
        }
    }

    private static AuditEntry Entry(
        string tenantId,
        string? actor,
        string action,
        string entity,
        string? before,
        string? after,
        DateTimeOffset createdAt)
        => new()
        {
            Id = TraconId.NewId(),
            TenantId = tenantId,
            Actor = actor,
            Action = action,
            Entity = entity,
            Before = AuditSecretFilter.Redact(before),
            After = AuditSecretFilter.Redact(after),
            CreatedAt = createdAt,
        };
}
