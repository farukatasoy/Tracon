using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>Wraps <see cref="ISkillScriptGrantStore"/> in a decorator that writes an audit trail.</summary>
/// <remarks>
/// Granting permission to run a script grants permission to run code on the server.
/// The <c>script.grant</c> and <c>script.revoke</c> actions therefore always enter
/// the audit trail.
/// </remarks>
public sealed class AuditingSkillScriptGrantStore : ISkillScriptGrantStore, IAuditDecorated
{
    private readonly ISkillScriptGrantStore _inner;
    private readonly IAuditLog _auditLog;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ILogger<AuditingSkillScriptGrantStore> _logger;

    /// <summary>Initializes a new audited grant store.</summary>
    /// <param name="inner">The wrapped store.</param>
    /// <param name="auditLog">The audit log.</param>
    /// <param name="actorResolver">The actor resolver.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public AuditingSkillScriptGrantStore(
        ISkillScriptGrantStore inner,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ILogger<AuditingSkillScriptGrantStore> logger)
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
    public ValueTask<IReadOnlyList<SkillScriptGrant>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
        => _inner.ListAsync(tenantId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<SkillScriptGrant?> FindActiveAsync(
        string tenantId,
        string skillName,
        string scriptName,
        DateTimeOffset instant,
        CancellationToken cancellationToken = default)
        => _inner.FindActiveAsync(tenantId, skillName, scriptName, instant, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<SkillScriptGrant> GrantAsync(
        SkillScriptGrant grant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);

        // 🚨 The audit row is written BEFORE the grant is persisted, and a failed
        // write throws. AuditRecorder swallows store failures and only logs a
        // warning; used here it left a persisted permission to RUN CODE ON THE
        // SERVER with no record of who granted it, while the caller still saw
        // 201 Created. An irreversible action must call IAuditLog directly.
        //
        // Ordering follows the script runner: recording an attempt that then
        // fails to persist is harmless noise, a persisted grant with no record is
        // not.
        await WriteAuditOrThrowAsync(
            grant.TenantId,
            action: "script.grant",
            entity: Describe(grant.SkillName, grant.ScriptName),
            after: JsonSerializer.Serialize(grant, AgentPrismCoreJsonContext.Default.SkillScriptGrant),
            cancellationToken).ConfigureAwait(false);

        return await _inner.GrantAsync(grant, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes one audit row and throws when the store rejects it.
    /// </summary>
    /// <remarks>
    /// Granting or revoking the right to run a script is irreversible from the
    /// audit trail's point of view, so it may not use the swallow-and-log path.
    /// </remarks>
    private async ValueTask WriteAuditOrThrowAsync(
        string tenantId,
        string action,
        string entity,
        string? after,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditLog.WriteAsync(
                new AuditEntry
                {
                    Id = AgentPrismId.NewId(),
                    TenantId = tenantId,
                    Actor = _actorResolver.Resolve(),
                    Action = action,
                    Entity = entity,
                    After = AuditSecretFilter.Redact(after),
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Script permission '{Entity}' could not be written to the audit trail; the change was refused.", entity);

            throw new AgentPrismException(
                $"Script permission '{entity}' was not changed because it could not be written to the audit trail.",
                ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> RevokeAsync(
        string tenantId,
        string skillName,
        string? scriptName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        // Same rule as the grant path: the record is written first and a failed
        // write throws. A revoke that turns out to be a no-op is recorded too;
        // that is deliberate, because the trail records the ATTEMPT to change a
        // script permission and noise is cheaper than a silent gap.
        await WriteAuditOrThrowAsync(
            tenantId,
            action: "script.revoke",
            entity: Describe(skillName, scriptName),
            after: null,
            cancellationToken).ConfigureAwait(false);

        return await _inner.RevokeAsync(tenantId, skillName, scriptName, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Describe(string skillName, string? scriptName)
        => scriptName is null ? $"{skillName}/*" : $"{skillName}/{scriptName}";
}
