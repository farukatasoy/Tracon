using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// <see cref="ISessionStore"/>'u yalnizca silme islemi icin denetim izi yazan bir
/// dekorator ile sarar.
/// </summary>
/// <remarks>
/// Oturum kaydetme her turda olur; denetlenirse denetim izi en hacimli tabloya
/// donusurdu. Yalnizca silme (geri alinamaz islem) denetlenir.
/// </remarks>
public sealed class AuditingSessionStore : ISessionStore, IAuditDecorated
{
    private readonly ISessionStore _inner;
    private readonly IAuditLog _auditLog;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ILogger<AuditingSessionStore> _logger;

    /// <summary>Yeni bir denetimli oturum deposu olusturur.</summary>
    public AuditingSessionStore(
        ISessionStore inner,
        IAuditLog auditLog,
        ITenantContext tenantContext,
        IAuditActorResolver actorResolver,
        ILogger<AuditingSessionStore> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(auditLog);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(actorResolver);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _auditLog = auditLog;
        _tenantContext = tenantContext;
        _actorResolver = actorResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public object AuditedInner => _inner;

    /// <inheritdoc />
    public ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
        => _inner.GetAsync(sessionId, cancellationToken);

    /// <inheritdoc />
    public ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
        => _inner.SaveAsync(record, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(
        SessionQuery query,
        CancellationToken cancellationToken = default)
        => _inner.QueryAsync(query, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var deleted = await _inner.DeleteAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (deleted)
        {
            await AuditRecorder.WriteAsync(
                _auditLog,
                _actorResolver,
                _logger,
                _tenantContext.TenantId,
                action: "session.delete",
                entity: $"session:{sessionId}",
                before: null,
                after: null,
                cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }
}
