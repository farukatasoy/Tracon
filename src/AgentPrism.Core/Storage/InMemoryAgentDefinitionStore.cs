using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// A store that keeps agent definitions in process memory.
/// </summary>
/// <remarks>
/// <para>
/// This is <em>not</em> a test helper. It is a first-class implementation that lets
/// AgentPrism run without a database, so a developer who installs the package gets a
/// working control plane without setting up infrastructure.
/// </para>
/// <para>
/// Records are isolated <strong>per tenant</strong>. The tenant comes from
/// <see cref="ITenantContext"/>, and the same isolation contract as SQL
/// implementations applies.
/// </para>
/// <para>
/// <strong>Limits:</strong> data is limited to the process lifetime and is not shared
/// across nodes. Use <c>AgentPrism.PostgreSql</c> in production.
/// </para>
/// </remarks>
public sealed class InMemoryAgentDefinitionStore : IAgentDefinitionStore
{
    private readonly ConcurrentDictionary<(string TenantId, string Name), List<AgentDefinition>> _versions = new();
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes a new in-memory definition store.</summary>
    /// <param name="tenantContext">
    /// The current tenant context. If omitted, the store behaves as single tenant.
    /// </param>
    public InMemoryAgentDefinitionStore(ITenantContext? tenantContext = null)
        => _tenantContext = tenantContext ?? FixedTenantContext.Default;

    /// <inheritdoc />
    public ValueTask<AgentDefinition?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!_versions.TryGetValue(Key(name), out var history))
        {
            return new ValueTask<AgentDefinition?>((AgentDefinition?)null);
        }

        lock (history)
        {
            return new ValueTask<AgentDefinition?>(history.Count == 0 ? null : history[^1]);
        }
    }

    /// <inheritdoc />
    public ValueTask<AgentDefinition?> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!_versions.TryGetValue(Key(name), out var history))
        {
            return new ValueTask<AgentDefinition?>((AgentDefinition?)null);
        }

        lock (history)
        {
            foreach (var candidate in history)
            {
                if (candidate.Version == version)
                {
                    return new ValueTask<AgentDefinition?>(candidate);
                }
            }

            return new ValueTask<AgentDefinition?>((AgentDefinition?)null);
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var current = new List<AgentDefinition>(_versions.Count);

        foreach (var pair in _versions)
        {
            if (!string.Equals(pair.Key.TenantId, tenantId, StringComparison.Ordinal))
            {
                continue;
            }

            lock (pair.Value)
            {
                if (pair.Value.Count > 0)
                {
                    current.Add(pair.Value[^1]);
                }
            }
        }

        current.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return new ValueTask<IReadOnlyList<AgentDefinition>>(current);
    }

    /// <inheritdoc />
    public ValueTask<AgentDefinition> SaveAsync(AgentDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var tenantId = _tenantContext.TenantId;
        var history = _versions.GetOrAdd((tenantId, definition.Name), static _ => []);

        lock (history)
        {
            var saved = definition with
            {
                TenantId = tenantId,
                Origin = AgentDefinitionOrigin.Database,
                Version = history.Count == 0 ? 1 : history[^1].Version + 1,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            history.Add(saved);
            return new ValueTask<AgentDefinition>(saved);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new ValueTask<bool>(_versions.TryRemove(Key(name), out _));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AgentDefinition>> ListVersionsAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!_versions.TryGetValue(Key(name), out var history))
        {
            return new ValueTask<IReadOnlyList<AgentDefinition>>(Array.Empty<AgentDefinition>());
        }

        lock (history)
        {
            var snapshot = new List<AgentDefinition>(history);
            snapshot.Reverse();
            return new ValueTask<IReadOnlyList<AgentDefinition>>(snapshot);
        }
    }

    /// <inheritdoc />
    public ValueTask<AgentDefinition> RollbackAsync(string name, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!_versions.TryGetValue(Key(name), out var history))
        {
            throw new AgentPrismException($"Agent definition named '{name}' was not found.");
        }

        lock (history)
        {
            AgentDefinition? target = null;

            foreach (var candidate in history)
            {
                if (candidate.Version == version)
                {
                    target = candidate;
                    break;
                }
            }

            if (target is null)
            {
                throw new AgentPrismException($"Version {version} of agent '{name}' was not found.");
            }

            // Rollback does not remove the old version. It saves its content as a new version.
            var restored = target with
            {
                Version = history[^1].Version + 1,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            history.Add(restored);
            return new ValueTask<AgentDefinition>(restored);
        }
    }

    private (string TenantId, string Name) Key(string name) => (_tenantContext.TenantId, name);
}
