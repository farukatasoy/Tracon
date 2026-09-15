using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>Wraps <see cref="ISkillScriptGrantStore"/> in a decorator that writes an audit trail.</summary>
/// <remarks>
/// Granting permission to run a script grants permission to run code on the server.
/// The <c>script.grant</c> and <c>script.revoke</c> actions therefore always enter
/// the audit trail.
/// </remarks>
internal sealed class AuditingSkillScriptGrantStore : ISkillScriptGrantStore, IAuditDecorated
{
    private readonly ISkillScriptGrantStore _inner;
    private readonly IAuditLog _auditLog;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ILogger<AuditingSkillScriptGrantStore> _logger;
    private readonly TraconMetrics? _metrics;

    /// <summary>Initializes a new audited grant store.</summary>
    /// <param name="inner">The wrapped store.</param>
    /// <param name="auditLog">The audit log.</param>
    /// <param name="actorResolver">The actor resolver.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="metrics">The metric set that counts a failed audit write.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public AuditingSkillScriptGrantStore(
        ISkillScriptGrantStore inner,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ILogger<AuditingSkillScriptGrantStore> logger,
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
        // write throws. The best-effort path swallows store failures and only logs
        // a warning; used here it left a persisted permission to RUN CODE ON THE
        // SERVER with no record of who granted it, while the caller still saw
        // 201 Created. An irreversible action takes the fail-closed path.
        //
        // Ordering follows the script runner: recording an attempt that then
        // fails to persist is harmless noise, a persisted grant with no record is
        // not.
        var grantEntity = Describe(grant.SkillName, grant.ScriptName);

        await AuditRecorder.WriteOrThrowAsync(
            _auditLog,
            _actorResolver.Resolve(),
            _logger,
            _metrics,
            grant.TenantId,
            action: "script.grant",
            entity: grantEntity,
            before: null,
            after: JsonSerializer.Serialize(grant, TraconCoreJsonContext.Default.SkillScriptGrant),
            refusal: $"Script permission '{grantEntity}' was not changed",
            timeProvider: null,
            cancellationToken).ConfigureAwait(false);

        return await _inner.GrantAsync(grant, cancellationToken).ConfigureAwait(false);
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
        var revokeEntity = Describe(skillName, scriptName);

        await AuditRecorder.WriteOrThrowAsync(
            _auditLog,
            _actorResolver.Resolve(),
            _logger,
            _metrics,
            tenantId,
            action: "script.revoke",
            entity: revokeEntity,
            before: null,
            after: null,
            refusal: $"Script permission '{revokeEntity}' was not changed",
            timeProvider: null,
            cancellationToken).ConfigureAwait(false);

        return await _inner.RevokeAsync(tenantId, skillName, scriptName, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Describe(string skillName, string? scriptName)
        => scriptName is null ? $"{skillName}/*" : $"{skillName}/{scriptName}";
}
