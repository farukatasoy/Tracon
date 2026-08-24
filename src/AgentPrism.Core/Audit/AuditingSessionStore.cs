using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Wraps <see cref="ISessionStore"/> in a decorator that writes an audit trail only
/// for delete operations.
/// </summary>
/// <remarks>
/// Session saves occur on every turn. Auditing them would make the audit trail the
/// highest-volume table. Only deletion, an irreversible operation, is audited.
/// </remarks>
internal sealed class AuditingSessionStore : ISessionStore, IAuditDecorated
{
    private readonly ISessionStore _inner;
    private readonly IAuditLog _auditLog;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ILogger<AuditingSessionStore> _logger;

    /// <summary>Initializes a new audited session store.</summary>
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
    /// <remarks>
    /// The same pitfall as <see cref="TryCreateAsync"/> applies. If this delegation is
    /// omitted, the decorator falls back to its default implementation, which filters
    /// by tenant and cannot correctly answer a cross-tenant query. It would silently
    /// disable the truly tenant-independent implementation in the inner store.
    /// </remarks>
    public ValueTask<string?> GetOwnerTenantIdAsync(string sessionId, CancellationToken cancellationToken = default)
        => _inner.GetOwnerTenantIdAsync(sessionId, cancellationToken);

    /// <inheritdoc />
    public ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
        => _inner.SaveAsync(record, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Omitting this delegation would use the decorator's default
    /// <c>ISessionStore.TryCreateAsync</c> implementation, which performs check-then-create
    /// through <see cref="GetAsync"/> and <see cref="SaveAsync"/>. It would disable the
    /// real atomic implementation in <c>SqlSessionStore</c>/<c>InMemorySessionStore</c>.
    /// Since <c>AuditingSessionStore</c> is always the single registered <see cref="ISessionStore"/>
    /// in DI, this would silently undo that atomicity.
    /// </remarks>
    public ValueTask<bool> TryCreateAsync(SessionRecord record, CancellationToken cancellationToken = default)
        => _inner.TryCreateAsync(record, cancellationToken);

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
