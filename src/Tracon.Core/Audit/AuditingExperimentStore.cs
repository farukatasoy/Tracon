using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Wraps <see cref="IExperimentStore"/> in a decorator that writes an audit trail.
/// </summary>
/// <remarks>
/// Creating, starting, or stopping an experiment is a deliberate Admin decision that
/// affects live traffic. It has the same rationale as <see cref="AuditingAgentDefinitionStore"/>
/// and is not an execution byproduct such as <c>IJobStore</c> or <c>IEvalStore</c>.
/// </remarks>
internal sealed class AuditingExperimentStore : IExperimentStore, IAuditDecorated
{
    private readonly IExperimentStore _inner;
    private readonly IAuditLog _auditLog;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ILogger<AuditingExperimentStore> _logger;

    /// <summary>Initializes a new audited experiment store.</summary>
    public AuditingExperimentStore(
        IExperimentStore inner,
        IAuditLog auditLog,
        ITenantContext tenantContext,
        IAuditActorResolver actorResolver,
        ILogger<AuditingExperimentStore> logger)
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
    public ValueTask<IReadOnlyList<Experiment>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
        => _inner.ListAsync(tenantId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<Experiment?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default)
        => _inner.GetAsync(tenantId, name, cancellationToken);

    /// <inheritdoc />
    public ValueTask<Experiment?> GetRunningAsync(string tenantId, string agentName, CancellationToken cancellationToken = default)
        => _inner.GetRunningAsync(tenantId, agentName, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<Experiment> SaveAsync(Experiment experiment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);

        var existing = await _inner.GetAsync(experiment.TenantId, experiment.Name, cancellationToken).ConfigureAwait(false);
        var saved = await _inner.SaveAsync(experiment, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            _auditLog,
            _actorResolver,
            _logger,
            _tenantContext.TenantId,
            action: existing is null ? "experiment.create" : "experiment.update",
            entity: $"experiment:{experiment.Name}",
            before: existing is null ? null : Serialize(existing),
            after: Serialize(saved),
            cancellationToken).ConfigureAwait(false);

        return saved;
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var existing = await _inner.GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false);
        var deleted = await _inner.DeleteAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

        if (deleted)
        {
            await AuditRecorder.WriteAsync(
                _auditLog,
                _actorResolver,
                _logger,
                _tenantContext.TenantId,
                action: "experiment.delete",
                entity: $"experiment:{name}",
                before: existing is null ? null : Serialize(existing),
                after: null,
                cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    /// <inheritdoc />
    public async ValueTask<Experiment> StartAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var started = await _inner.StartAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            _auditLog,
            _actorResolver,
            _logger,
            _tenantContext.TenantId,
            action: "experiment.start",
            entity: $"experiment:{name}",
            before: null,
            after: Serialize(started),
            cancellationToken).ConfigureAwait(false);

        return started;
    }

    /// <inheritdoc />
    public async ValueTask<Experiment> StopAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var stopped = await _inner.StopAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            _auditLog,
            _actorResolver,
            _logger,
            _tenantContext.TenantId,
            action: "experiment.stop",
            entity: $"experiment:{name}",
            before: null,
            after: Serialize(stopped),
            cancellationToken).ConfigureAwait(false);

        return stopped;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Experiment>> ListRunningWithCanaryAsync(CancellationToken cancellationToken = default)
        => _inner.ListRunningWithCanaryAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask<Experiment> SetCanaryPolicyAsync(
        string tenantId,
        string name,
        CanaryPolicy? policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var existing = await _inner.GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false);
        var updated = await _inner.SetCanaryPolicyAsync(tenantId, name, policy, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            _auditLog,
            _actorResolver,
            _logger,
            _tenantContext.TenantId,
            action: "experiment.canary_policy",
            entity: $"experiment:{name}",
            before: existing is null ? null : Serialize(existing),
            after: Serialize(updated),
            cancellationToken).ConfigureAwait(false);

        return updated;
    }

    /// <inheritdoc />
    /// <remarks>
    /// This method is not audited. Only <c>CanaryEvaluationService</c> calls it, and
    /// gradual increases do not enter the audit trail according to the 56.2 flow.
    /// Only rollback is written. See <see cref="RollbackCanaryAsync"/>.
    /// </remarks>
    public ValueTask<Experiment> AdvanceCanaryRampAsync(
        string tenantId,
        string name,
        IReadOnlyList<ExperimentVariant> variants,
        CancellationToken cancellationToken = default)
        => _inner.AdvanceCanaryRampAsync(tenantId, name, variants, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// This method is not audited., <c>CanaryEvaluationService</c>
    /// writes its audit record directly through <see cref="IAuditLog.WriteAsync"/>
    /// before it calls this method. An additional best-effort write here could leave
    /// a rollback applied after its write failed.
    /// </remarks>
    public ValueTask<Experiment> RollbackCanaryAsync(
        string tenantId,
        string name,
        IReadOnlyList<ExperimentVariant> variants,
        string reason,
        CancellationToken cancellationToken = default)
        => _inner.RollbackCanaryAsync(tenantId, name, variants, reason, cancellationToken);

    private static string Serialize(Experiment experiment)
        => JsonSerializer.Serialize(experiment, TraconCoreJsonContext.Default.Experiment);
}
