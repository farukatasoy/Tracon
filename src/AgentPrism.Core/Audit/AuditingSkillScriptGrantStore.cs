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

        var saved = await _inner.GrantAsync(grant, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            _auditLog,
            _actorResolver,
            _logger,
            saved.TenantId,
            action: "script.grant",
            entity: Describe(saved.SkillName, saved.ScriptName),
            before: null,
            after: JsonSerializer.Serialize(saved, AgentPrismCoreJsonContext.Default.SkillScriptGrant),
            cancellationToken).ConfigureAwait(false);

        return saved;
    }

    /// <inheritdoc />
    public async ValueTask<bool> RevokeAsync(
        string tenantId,
        string skillName,
        string? scriptName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var revoked = await _inner.RevokeAsync(tenantId, skillName, scriptName, cancellationToken)
            .ConfigureAwait(false);

        if (revoked)
        {
            await AuditRecorder.WriteAsync(
                _auditLog,
                _actorResolver,
                _logger,
                tenantId,
                action: "script.revoke",
                entity: Describe(skillName, scriptName),
                before: null,
                after: null,
                cancellationToken).ConfigureAwait(false);
        }

        return revoked;
    }

    private static string Describe(string skillName, string? scriptName)
        => scriptName is null ? $"{skillName}/*" : $"{skillName}/{scriptName}";
}
