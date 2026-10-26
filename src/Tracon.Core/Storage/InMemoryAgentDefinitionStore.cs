using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// A store that keeps agent definitions in process memory.
/// </summary>
/// <remarks>
/// <para>
/// This is <em>not</em> a test helper. It is a first-class implementation that lets
/// Tracon run without a database, so a developer who installs the package gets a
/// working control plane without setting up infrastructure.
/// </para>
/// <para>
/// Records are isolated <strong>per tenant</strong>. The tenant comes from
/// <see cref="ITenantContext"/>, and the same isolation contract as SQL
/// implementations applies.
/// </para>
/// <para>
/// <strong>Limits:</strong> data is limited to the process lifetime and is not shared
/// across nodes. Use <c>Tracon.PostgreSql</c> in production.
/// </para>
/// <para>
/// <strong>Numbering.</strong> A version number is an identity, so it is never given twice
/// for one name: a delete removes the versions but keeps the highest number the name has
/// ever had, and the next save continues from it. The SQL stores keep the same number in
/// <c>agent_definition_version_counters</c>.
/// </para>
/// </remarks>
internal sealed class InMemoryAgentDefinitionStore : IAgentDefinitionStore
{
    private readonly ConcurrentDictionary<(string TenantId, string Name), AgentHistory> _versions = new();
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
        cancellationToken.ThrowIfCancellationRequested();

        if (!_versions.TryGetValue(Key(name), out var history))
        {
            return new ValueTask<AgentDefinition?>((AgentDefinition?)null);
        }

        lock (history)
        {
            return new ValueTask<AgentDefinition?>(history.Versions.Count == 0 ? null : history.Versions[^1]);
        }
    }

    /// <inheritdoc />
    public ValueTask<AgentDefinition?> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_versions.TryGetValue(Key(name), out var history))
        {
            return new ValueTask<AgentDefinition?>((AgentDefinition?)null);
        }

        lock (history)
        {
            foreach (var candidate in history.Versions)
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
        cancellationToken.ThrowIfCancellationRequested();

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
                if (pair.Value.Versions.Count > 0)
                {
                    current.Add(pair.Value.Versions[^1]);
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
        cancellationToken.ThrowIfCancellationRequested();

        var tenantId = _tenantContext.TenantId;
        var history = _versions.GetOrAdd((tenantId, definition.Name), static _ => new AgentHistory());

        lock (history)
        {
            var saved = definition with
            {
                TenantId = tenantId,
                Origin = AgentDefinitionOrigin.Database,
                Version = history.LastVersion + 1,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            history.LastVersion = saved.Version;
            history.Versions.Add(saved);
            return new ValueTask<AgentDefinition>(saved);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_versions.TryGetValue(Key(name), out var history))
        {
            return new ValueTask<bool>(false);
        }

        // The entry stays: it carries the highest number the name has had, and the next save
        // continues from it. Only the versions go.
        lock (history)
        {
            var deleted = history.Versions.Count > 0;

            history.Versions.Clear();
            return new ValueTask<bool>(deleted);
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AgentDefinition>> ListVersionsAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_versions.TryGetValue(Key(name), out var history))
        {
            return new ValueTask<IReadOnlyList<AgentDefinition>>(Array.Empty<AgentDefinition>());
        }

        lock (history)
        {
            var snapshot = new List<AgentDefinition>(history.Versions);
            snapshot.Reverse();
            return new ValueTask<IReadOnlyList<AgentDefinition>>(snapshot);
        }
    }

    /// <inheritdoc />
    public ValueTask<AgentDefinition> RollbackAsync(string name, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_versions.TryGetValue(Key(name), out var history))
        {
            throw new TraconException($"Agent definition named '{name}' was not found.");
        }

        lock (history)
        {
            if (history.Versions.Count == 0)
            {
                throw new TraconException($"Agent definition named '{name}' was not found.");
            }

            AgentDefinition? target = null;

            foreach (var candidate in history.Versions)
            {
                if (candidate.Version == version)
                {
                    target = candidate;
                    break;
                }
            }

            if (target is null)
            {
                throw new TraconException($"Version {version} of agent '{name}' was not found.");
            }

            // Rollback does not remove the old version. It saves its content as a new version.
            var restored = target with
            {
                Version = history.LastVersion + 1,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            history.LastVersion = restored.Version;
            history.Versions.Add(restored);
            return new ValueTask<AgentDefinition>(restored);
        }
    }

    private (string TenantId, string Name) Key(string name) => (_tenantContext.TenantId, name);

    /// <summary>The versions of one name and the highest number the name has ever had; guarded by its own lock.</summary>
    private sealed class AgentHistory
    {
        /// <summary>The versions that exist now, oldest first. A delete empties it.</summary>
        public List<AgentDefinition> Versions { get; } = [];

        /// <summary>The highest version number ever given to the name. A delete does not lower it.</summary>
        public int LastVersion { get; set; }
    }
}
