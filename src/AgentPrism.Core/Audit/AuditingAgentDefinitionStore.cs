using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// <see cref="IAgentDefinitionStore"/>'u denetim izi yazan bir dekorator ile sarar.
/// </summary>
/// <remarks>
/// Yazma uc katmaninda degil burada yapilir: bu, "bir agent yalnizca kayitli bir
/// tool'a isaret edebilir" kuralinin <c>ToolRegistry</c> icinde zorlanmasiyla ayni
/// gerekcedir — depo, agent tanimina yapilan <strong>her</strong> yazma yolunun
/// gectigi tek kapidir.
/// </remarks>
public sealed class AuditingAgentDefinitionStore : IAgentDefinitionStore, IAuditDecorated
{
    private readonly IAgentDefinitionStore _inner;
    private readonly IAuditLog _auditLog;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ILogger<AuditingAgentDefinitionStore> _logger;

    /// <summary>Yeni bir denetimli tanim deposu olusturur.</summary>
    public AuditingAgentDefinitionStore(
        IAgentDefinitionStore inner,
        IAuditLog auditLog,
        ITenantContext tenantContext,
        IAuditActorResolver actorResolver,
        ILogger<AuditingAgentDefinitionStore> logger)
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
    public ValueTask<AgentDefinition?> GetAsync(string name, CancellationToken cancellationToken = default)
        => _inner.GetAsync(name, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default)
        => _inner.ListAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AgentDefinition>> ListVersionsAsync(
        string name,
        CancellationToken cancellationToken = default)
        => _inner.ListVersionsAsync(name, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<AgentDefinition> SaveAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var existing = await _inner.GetAsync(definition.Name, cancellationToken).ConfigureAwait(false);
        var saved = await _inner.SaveAsync(definition, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            _auditLog,
            _actorResolver,
            _logger,
            _tenantContext.TenantId,
            action: existing is null ? "agent.create" : "agent.update",
            entity: $"agent:{definition.Name}",
            before: existing is null ? null : Serialize(existing),
            after: Serialize(saved),
            cancellationToken).ConfigureAwait(false);

        return saved;
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var existing = await _inner.GetAsync(name, cancellationToken).ConfigureAwait(false);
        var deleted = await _inner.DeleteAsync(name, cancellationToken).ConfigureAwait(false);

        if (deleted)
        {
            await AuditRecorder.WriteAsync(
                _auditLog,
                _actorResolver,
                _logger,
                _tenantContext.TenantId,
                action: "agent.delete",
                entity: $"agent:{name}",
                before: existing is null ? null : Serialize(existing),
                after: null,
                cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    /// <inheritdoc />
    public async ValueTask<AgentDefinition> RollbackAsync(
        string name,
        int version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var before = await _inner.GetAsync(name, cancellationToken).ConfigureAwait(false);
        var rolledBack = await _inner.RollbackAsync(name, version, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            _auditLog,
            _actorResolver,
            _logger,
            _tenantContext.TenantId,
            action: "agent.rollback",
            entity: $"agent:{name}",
            before: before is null ? null : $$"""{"version":{{before.Version}}}""",
            after: $$"""{"rolledBackToVersion":{{version}},"newVersion":{{rolledBack.Version}}}""",
            cancellationToken).ConfigureAwait(false);

        return rolledBack;
    }

    private static string Serialize(AgentDefinition definition)
        => JsonSerializer.Serialize(definition, AgentPrismCoreJsonContext.Default.AgentDefinition);
}
