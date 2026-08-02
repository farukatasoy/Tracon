using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// <see cref="IMcpServerStore"/>'u denetim izi yazan bir dekorator ile sarar.
/// </summary>
/// <remarks>
/// MCP sunucusu eklemek disaridan tool tanimi kabul etmek demektir (karar K-058);
/// bu yuzden bu depoya yapilan her yazi denetim izine dusmelidir.
/// <see cref="McpServerDefinition"/> hicbir zaman sir tasimaz (karar K-059), ama
/// sir suzgeci yine de uygulanir.
/// </remarks>
public sealed class AuditingMcpServerStore : IMcpServerStore, IAuditDecorated
{
    private readonly IMcpServerStore _inner;
    private readonly IAuditLog _auditLog;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ILogger<AuditingMcpServerStore> _logger;

    /// <summary>Yeni bir denetimli MCP sunucu deposu olusturur.</summary>
    public AuditingMcpServerStore(
        IMcpServerStore inner,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ILogger<AuditingMcpServerStore> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(auditLog);
        ArgumentNullException.ThrowIfNull(actorResolver);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _auditLog = auditLog;
        _actorResolver = actorResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public object AuditedInner => _inner;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<McpServerDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
        => _inner.ListAsync(tenantId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<McpServerDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
        => _inner.GetAsync(tenantId, name, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<McpServerDefinition> SaveAsync(
        McpServerDefinition server,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        var existing = await _inner.GetAsync(server.TenantId, server.Name, cancellationToken).ConfigureAwait(false);
        var saved = await _inner.SaveAsync(server, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            _auditLog,
            _actorResolver,
            _logger,
            server.TenantId,
            action: existing is null ? "mcp.create" : "mcp.update",
            entity: $"mcp:{server.Name}",
            before: existing is null ? null : Serialize(existing),
            after: Serialize(saved),
            cancellationToken).ConfigureAwait(false);

        return saved;
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var existing = await _inner.GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false);
        var deleted = await _inner.DeleteAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

        if (deleted)
        {
            await AuditRecorder.WriteAsync(
                _auditLog,
                _actorResolver,
                _logger,
                tenantId,
                action: "mcp.delete",
                entity: $"mcp:{name}",
                before: existing is null ? null : Serialize(existing),
                after: null,
                cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    private static string Serialize(McpServerDefinition server)
        => JsonSerializer.Serialize(server, AgentPrismCoreJsonContext.Default.McpServerDefinition);
}
