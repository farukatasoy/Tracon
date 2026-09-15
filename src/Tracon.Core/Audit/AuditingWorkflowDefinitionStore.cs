using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>Wraps <see cref="IWorkflowDefinitionStore"/> in a decorator that writes an audit trail.</summary>
/// <remarks>
/// A workflow definition chains catalog agents and can start dozens of model calls
/// from one request. The <c>workflow.save</c> and <c>workflow.delete</c> actions
/// therefore always enter the audit trail.
/// </remarks>
internal sealed class AuditingWorkflowDefinitionStore : IWorkflowDefinitionStore, IAuditDecorated
{
    private readonly IWorkflowDefinitionStore _inner;
    private readonly IAuditLog _auditLog;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ILogger<AuditingWorkflowDefinitionStore> _logger;
    private readonly TraconMetrics? _metrics;

    /// <summary>Initializes a new audited workflow definition store.</summary>
    /// <param name="inner">The wrapped store.</param>
    /// <param name="auditLog">The audit log.</param>
    /// <param name="actorResolver">The actor resolver.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="metrics">The metric set that counts a failed audit write.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public AuditingWorkflowDefinitionStore(
        IWorkflowDefinitionStore inner,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ILogger<AuditingWorkflowDefinitionStore> logger,
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
    public ValueTask<WorkflowDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
        => _inner.GetAsync(tenantId, name, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorkflowDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
        => _inner.ListAsync(tenantId, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<WorkflowDefinition> SaveAsync(
        string tenantId,
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Read the previous value before saving. Reading after saving would write the
        // new value as the old value.
        var before = await _inner.GetAsync(tenantId, definition.Name, cancellationToken).ConfigureAwait(false);
        var saved = await _inner.SaveAsync(tenantId, definition, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            _auditLog,
            _actorResolver,
            _logger,
            _metrics,
            tenantId,
            action: "workflow.save",
            entity: $"workflow:{saved.Name}",
            before: before is null ? null : Serialize(before),
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
        var before = await _inner.GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false);
        var deleted = await _inner.DeleteAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

        if (deleted)
        {
            await AuditRecorder.WriteAsync(
                _auditLog,
                _actorResolver,
                _logger,
                _metrics,
                tenantId,
                action: "workflow.delete",
                entity: $"workflow:{name}",
                before: before is null ? null : Serialize(before),
                after: null,
                cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    private static string Serialize(WorkflowDefinition definition)
        => JsonSerializer.Serialize(definition, TraconCoreJsonContext.Default.WorkflowDefinition);
}
