using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary><see cref="ITenantStore"/>'u denetim izi yazan bir dekorator ile sarar.</summary>
public sealed class AuditingTenantStore : ITenantStore, IAuditDecorated
{
    private readonly ITenantStore _inner;
    private readonly IAuditLog _auditLog;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ILogger<AuditingTenantStore> _logger;

    /// <summary>Yeni bir denetimli kiraci deposu olusturur.</summary>
    public AuditingTenantStore(
        ITenantStore inner,
        IAuditLog auditLog,
        ITenantContext tenantContext,
        IAuditActorResolver actorResolver,
        ILogger<AuditingTenantStore> logger)
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
    public ValueTask<IReadOnlyList<TenantDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        => _inner.ListAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask<TenantDescriptor> SaveAsync(
        TenantDescriptor tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var saved = await _inner.SaveAsync(tenant, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            _auditLog,
            _actorResolver,
            _logger,
            // Kiraci kaydinin kendisi cok kiracili olmayabilir; kiracinin kendi
            // etkinlestirdigi baglam yerine, olusturulan slug'i kiraci olarak kullaniyoruz.
            tenant.Slug,
            action: "tenant.create",
            entity: $"tenant:{tenant.Slug}",
            before: null,
            after: Serialize(saved),
            cancellationToken).ConfigureAwait(false);

        return saved;
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var deleted = await _inner.DeleteAsync(slug, cancellationToken).ConfigureAwait(false);

        if (deleted)
        {
            await AuditRecorder.WriteAsync(
                _auditLog,
                _actorResolver,
                _logger,
                slug,
                action: "tenant.delete",
                entity: $"tenant:{slug}",
                before: null,
                after: null,
                cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    private static string Serialize(TenantDescriptor tenant)
        => JsonSerializer.Serialize(tenant, AgentPrismCoreJsonContext.Default.TenantDescriptor);
}
