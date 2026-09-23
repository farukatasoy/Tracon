using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Wraps <see cref="IMcpServerStore"/> in a decorator that writes an audit trail.
/// </summary>
/// <remarks>
/// Adding an MCP server accepts an external tool definition, so every
/// write to this store must enter the audit trail. The trail records the
/// header NAMES of a definition but never their values, and the secret filter
/// still applies to the rest.
/// </remarks>
internal sealed class AuditingMcpServerStore : IMcpServerStore, IAuditDecorated
{
    private readonly IMcpServerStore _inner;
    private readonly IAuditLog _auditLog;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ILogger<AuditingMcpServerStore> _logger;
    private readonly TraconMetrics? _metrics;

    /// <summary>Initializes a new audited MCP server store.</summary>
    public AuditingMcpServerStore(
        IMcpServerStore inner,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ILogger<AuditingMcpServerStore> logger,
        TraconMetrics? metrics)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(auditLog);
        ArgumentNullException.ThrowIfNull(actorResolver);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _auditLog = auditLog;
        _actorResolver = actorResolver;
        _logger = logger;
        _metrics = metrics;
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
            _metrics,
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
                _metrics,
                tenantId,
                action: "mcp.delete",
                entity: $"mcp:{name}",
                before: existing is null ? null : Serialize(existing),
                after: null,
                cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    /// <summary>Serializes a definition for the audit trail.</summary>
    /// <remarks>
    /// Header VALUES are masked before serialization: the audit trail is
    /// kept as plain text, and the secret filter only recognizes credential
    /// NAMES (<c>Cookie</c> and <c>Ocp-Apim-Subscription-Key</c> pass it).
    /// The names stay, so the trail still shows which headers changed.
    /// </remarks>
    private static string Serialize(McpServerDefinition server)
        => JsonSerializer.Serialize(
            server with { Headers = HeaderValueMask.Apply(server.Headers) },
            TraconCoreJsonContext.Default.McpServerDefinition);
}
