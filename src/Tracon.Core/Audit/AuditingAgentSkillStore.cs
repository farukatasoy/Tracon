using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracon;

// A partial audit trail is worse than a missing one, because it reads as
// complete: the grant store was decorated while this one was not, so the trail
// recorded who PERMITTED a script to run and never who WROTE it. The gate that
// keeps this from happening again is AuditCoverageTests, which resolves every
// store from a real container and requires each one to be either decorated or
// listed as deliberately undecorated with a reason.
/// <summary>Wraps <see cref="IAgentSkillStore"/> in a decorator that writes an audit trail.</summary>
/// <remarks>
/// <para>
/// A skill carries the instructions given to the model and its
/// <see cref="AgentSkillDefinition.Scripts"/> — code that runs on the server
/// once <c>TraconSkillScriptOptions.AllowStoredScripts</c> is on. Saving
/// or deleting one is therefore an agent-behavior change of the same weight
/// as saving an agent definition, and it produces <c>skill.create</c>,
/// <c>skill.update</c>, and <c>skill.delete</c> entries.
/// </para>
/// <para>
/// Writes occur here, not at the endpoint layer, following
/// <see cref="AuditingAgentDefinitionStore"/>: the store is the one gateway
/// for every write path to a skill.
/// </para>
/// <para>
/// Unlike <see cref="AuditingSkillScriptGrantStore"/>, this decorator uses the
/// swallow-and-log <c>AuditRecorder</c> path rather than writing first and
/// throwing. Granting the right to run a script is irreversible from the
/// trail's point of view; saving a skill is not — it is versioned, reversible
/// configuration, and observability may not break functionality.
/// </para>
/// </remarks>
internal sealed class AuditingAgentSkillStore : IAgentSkillStore, IAuditDecorated
{
    private readonly IAgentSkillStore _inner;
    private readonly IAuditLog _auditLog;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ILogger<AuditingAgentSkillStore> _logger;
    private readonly TraconMetrics? _metrics;

    /// <summary>Initializes a new audited skill store.</summary>
    /// <param name="inner">The wrapped store.</param>
    /// <param name="auditLog">The audit log.</param>
    /// <param name="actorResolver">The actor resolver.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="metrics">The metric set that counts a failed audit write.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public AuditingAgentSkillStore(
        IAgentSkillStore inner,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ILogger<AuditingAgentSkillStore> logger,
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
    public ValueTask<IReadOnlyList<AgentSkillDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
        => _inner.ListAsync(tenantId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<AgentSkillDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
        => _inner.GetAsync(tenantId, name, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<AgentSkillDefinition> SaveAsync(
        AgentSkillDefinition skill,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skill);

        // 🚨 The tenant comes from the skill itself, not ITenantContext: every
        // member of this interface takes tenantId explicitly and an
        // implementation never reads the ambient tenant (see IAgentSkillStore's
        // remarks). Reading the ambient one here would file a background write
        // under the wrong tenant's trail.
        var existing = await _inner.GetAsync(skill.TenantId, skill.Name, cancellationToken).ConfigureAwait(false);
        var saved = await _inner.SaveAsync(skill, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            _auditLog,
            _actorResolver,
            _logger,
            _metrics,
            skill.TenantId,
            action: existing is null ? "skill.create" : "skill.update",
            entity: $"skill:{skill.Name}",
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
        ArgumentNullException.ThrowIfNull(name);

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
                action: "skill.delete",
                entity: $"skill:{name}",
                before: existing is null ? null : Serialize(existing),
                after: null,
                cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    private static string Serialize(AgentSkillDefinition skill)
        => JsonSerializer.Serialize(skill, TraconCoreJsonContext.Default.AgentSkillDefinition);
}
