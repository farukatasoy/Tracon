using System.Collections.Concurrent;

namespace Tracon;

/// <summary>A store that keeps script execution grants in process memory.</summary>
/// <remarks>
/// Use the persistent store from <c>Tracon.PostgreSql</c> in production.
/// This implementation is for development and tests. Grants are lost when the
/// process restarts, so <strong>no script runs by default</strong>.
/// </remarks>
internal sealed class InMemorySkillScriptGrantStore : ISkillScriptGrantStore
{
    private readonly ConcurrentDictionary<GrantKey, SkillScriptGrant> _grants = new();

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<SkillScriptGrant>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var grants = _grants
            .Where(pair => string.Equals(pair.Key.TenantId, tenantId, StringComparison.Ordinal))
            .Select(static pair => pair.Value)
            .OrderBy(static grant => grant.SkillName, StringComparer.Ordinal)
            .ThenBy(static grant => grant.ScriptName ?? string.Empty, StringComparer.Ordinal)
            .ToArray();

        return new ValueTask<IReadOnlyList<SkillScriptGrant>>(grants);
    }

    /// <inheritdoc />
    public ValueTask<SkillScriptGrant?> FindActiveAsync(
        string tenantId,
        string skillName,
        string scriptName,
        DateTimeOffset instant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(skillName);
        ArgumentNullException.ThrowIfNull(scriptName);

        // First look for a script-specific grant. If it does not exist, look for a
        // grant that covers the full skill. A narrow grant always wins over a broad grant.
        if (_grants.TryGetValue(new GrantKey(tenantId, skillName, scriptName), out var specific) &&
            specific.IsActiveAt(instant))
        {
            return new ValueTask<SkillScriptGrant?>(specific);
        }

        if (_grants.TryGetValue(new GrantKey(tenantId, skillName, null), out var wide) &&
            wide.IsActiveAt(instant))
        {
            return new ValueTask<SkillScriptGrant?>(wide);
        }

        return new ValueTask<SkillScriptGrant?>((SkillScriptGrant?)null);
    }

    /// <inheritdoc />
    public ValueTask<SkillScriptGrant> GrantAsync(
        SkillScriptGrant grant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);

        var stored = grant with
        {
            Id = grant.Id == Guid.Empty ? TraconId.NewId() : grant.Id,
            GrantedAt = grant.GrantedAt == default ? DateTimeOffset.UtcNow : grant.GrantedAt,
            RevokedAt = null,
        };

        _grants[new GrantKey(stored.TenantId, stored.SkillName, stored.ScriptName)] = stored;

        return new ValueTask<SkillScriptGrant>(stored);
    }

    /// <inheritdoc />
    public ValueTask<bool> RevokeAsync(
        string tenantId,
        string skillName,
        string? scriptName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(skillName);

        return new ValueTask<bool>(_grants.TryRemove(new GrantKey(tenantId, skillName, scriptName), out _));
    }

    private readonly record struct GrantKey(string TenantId, string SkillName, string? ScriptName);
}
