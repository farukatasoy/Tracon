using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary><see cref="IWorkflowDefinitionStore"/>'u denetim izi yazan bir dekorator ile sarar.</summary>
/// <remarks>
/// Bir workflow tanimi, katalogdaki agent'lari birbirine zincirler ve tek bir
/// istekle onlarca model cagrisi baslatabilir. <c>workflow.save</c> ve
/// <c>workflow.delete</c> eylemleri bu yuzden her zaman denetim izine yazilir.
/// </remarks>
public sealed class AuditingWorkflowDefinitionStore : IWorkflowDefinitionStore, IAuditDecorated
{
    private readonly IWorkflowDefinitionStore _inner;
    private readonly IAuditLog _auditLog;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ILogger<AuditingWorkflowDefinitionStore> _logger;

    /// <summary>Yeni bir denetimli workflow tanim deposu olusturur.</summary>
    /// <param name="inner">Sarilan depo.</param>
    /// <param name="auditLog">Denetim izi.</param>
    /// <param name="actorResolver">Aktor cozumleyici.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public AuditingWorkflowDefinitionStore(
        IWorkflowDefinitionStore inner,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ILogger<AuditingWorkflowDefinitionStore> logger)
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

        // Onceki hal kayittan ONCE okunur: yazdiktan sonra okumak yeni degeri
        // "eski" diye yazardi.
        var before = await _inner.GetAsync(tenantId, definition.Name, cancellationToken).ConfigureAwait(false);
        var saved = await _inner.SaveAsync(tenantId, definition, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            _auditLog,
            _actorResolver,
            _logger,
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
        => JsonSerializer.Serialize(definition, AgentPrismCoreJsonContext.Default.WorkflowDefinition);
}
