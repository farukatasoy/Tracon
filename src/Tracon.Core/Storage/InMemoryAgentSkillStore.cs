using System.Collections.Concurrent;

namespace Tracon;

/// <summary>A store that keeps skill definitions in process memory.</summary>
internal sealed class InMemoryAgentSkillStore : IAgentSkillStore
{
    private readonly ConcurrentDictionary<SkillKey, AgentSkillDefinition> _skills = new();

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AgentSkillDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        var skills = _skills
            .Where(pair => string.Equals(pair.Key.TenantId, tenantId, StringComparison.Ordinal))
            .Select(static pair => pair.Value)
            .OrderBy(static skill => skill.Name, StringComparer.Ordinal)
            .ToArray();

        return new ValueTask<IReadOnlyList<AgentSkillDefinition>>(skills);
    }

    /// <inheritdoc />
    public ValueTask<AgentSkillDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();

        _skills.TryGetValue(new SkillKey(tenantId, name), out var skill);
        return new ValueTask<AgentSkillDefinition?>(skill);
    }

    /// <inheritdoc />
    public ValueTask<AgentSkillDefinition> SaveAsync(
        AgentSkillDefinition skill,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skill);
        cancellationToken.ThrowIfCancellationRequested();

        var key = new SkillKey(skill.TenantId, skill.Name);
        var saved = _skills.AddOrUpdate(
            key,
            static (_, value) => Create(value),
            static (_, current, value) => Update(current, value),
            skill);

        return new ValueTask<AgentSkillDefinition>(saved);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<bool>(_skills.TryRemove(new SkillKey(tenantId, name), out _));
    }

    private static AgentSkillDefinition Create(AgentSkillDefinition skill)
    {
        var now = DateTimeOffset.UtcNow;
        return skill with
        {
            Id = skill.Id == Guid.Empty ? TraconId.NewId(now) : skill.Id,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static AgentSkillDefinition Update(AgentSkillDefinition current, AgentSkillDefinition skill)
        => skill with
        {
            Id = current.Id,
            Version = current.Version + 1,
            CreatedAt = current.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private readonly record struct SkillKey(string TenantId, string Name);
}
