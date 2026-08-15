using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>Wraps <see cref="IToolApprovalRuleStore"/> in a decorator that writes an audit trail.</summary>
public sealed class AuditingToolApprovalRuleStore : IToolApprovalRuleStore, IAuditDecorated
{
    private readonly IToolApprovalRuleStore _inner;
    private readonly IAuditLog _auditLog;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ILogger<AuditingToolApprovalRuleStore> _logger;

    /// <summary>Initializes a new audited approval rule store.</summary>
    public AuditingToolApprovalRuleStore(
        IToolApprovalRuleStore inner,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ILogger<AuditingToolApprovalRuleStore> logger)
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
    public ValueTask<IReadOnlyList<ToolApprovalRule>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
        => _inner.ListAsync(tenantId, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<ToolApprovalRule> AddAsync(
        ToolApprovalRule rule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var saved = await _inner.AddAsync(rule, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            _auditLog,
            _actorResolver,
            _logger,
            rule.TenantId,
            action: "approval.rule.create",
            entity: $"rule:{saved.Id}",
            before: null,
            after: Serialize(saved),
            cancellationToken).ConfigureAwait(false);

        return saved;
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(
        string tenantId,
        Guid ruleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var deleted = await _inner.DeleteAsync(tenantId, ruleId, cancellationToken).ConfigureAwait(false);

        if (deleted)
        {
            await AuditRecorder.WriteAsync(
                _auditLog,
                _actorResolver,
                _logger,
                tenantId,
                action: "approval.rule.delete",
                entity: $"rule:{ruleId}",
                before: null,
                after: null,
                cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    private static string Serialize(ToolApprovalRule rule)
        => JsonSerializer.Serialize(rule, AgentPrismCoreJsonContext.Default.ToolApprovalRule);
}
