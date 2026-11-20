namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// An <see cref="IAgentDefinitionSource"/> that keeps definitions in memory, one history per tenant and name.
/// </summary>
/// <remarks>
/// It reads the tenant from a delegate, so a test that turns tenancy on can show that the source — not the
/// catalog — decides which definitions a tenant sees. Its priority sits below the database's, so it wins a name
/// clash with a stored definition.
/// </remarks>
internal class InMemoryDefinitionSource : IAgentDefinitionSource
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Tenant, string Name), List<AgentDefinition>> _history = [];
    private readonly Func<string> _tenant;

    public InMemoryDefinitionSource(
        string name = "memory-definitions",
        int priority = AgentSourcePriority.Database - 50,
        Func<string>? tenant = null)
    {
        Name = name;
        Priority = priority;
        _tenant = tenant ?? (static () => "default");
    }

    public string Name { get; }

    public int Priority { get; }

    /// <summary>Gets or sets a step the source awaits before it reads, so a test can hold or fail a call.</summary>
    public Func<CancellationToken, Task>? BeforeRead { get; set; }

    /// <summary>Gets how many times <see cref="GetAsync"/> ran.</summary>
    public int GetCalls { get; private set; }

    /// <summary>Adds a definition as the next version of its name, for a tenant.</summary>
    /// <param name="definition">The definition. Its version is replaced by the next number of its history.</param>
    /// <param name="tenant">The tenant that holds it; the ambient tenant when <see langword="null"/>.</param>
    /// <returns>This source.</returns>
    public InMemoryDefinitionSource Add(AgentDefinition definition, string? tenant = null)
    {
        lock (_gate)
        {
            var key = (tenant ?? "default", definition.Name);

            if (!_history.TryGetValue(key, out var versions))
            {
                versions = [];
                _history[key] = versions;
            }

            versions.Add(definition with { Version = versions.Count + 1 });
        }

        return this;
    }

    public async ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await PauseAsync(cancellationToken).ConfigureAwait(false);

        var tenant = _tenant();

        lock (_gate)
        {
            return [.. _history.Where(entry => string.Equals(entry.Key.Tenant, tenant, StringComparison.Ordinal)).Select(static entry => entry.Value[^1])];
        }
    }

    public async ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetCalls++;
        await PauseAsync(cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            return _history.TryGetValue((_tenant(), agentName), out var versions) ? versions[^1] : null;
        }
    }

    protected AgentDefinition? Version(string agentName, int version)
    {
        lock (_gate)
        {
            return _history.TryGetValue((_tenant(), agentName), out var versions) && version >= 1 && version <= versions.Count
                ? versions[version - 1]
                : null;
        }
    }

    protected async ValueTask PauseAsync(CancellationToken cancellationToken)
    {
        if (BeforeRead is { } step)
        {
            await step(cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>An <see cref="InMemoryDefinitionSource"/> that also returns a specific version.</summary>
internal sealed class VersionedInMemoryDefinitionSource(
    string name = "memory-definitions",
    int priority = AgentSourcePriority.Database - 50,
    Func<string>? tenant = null)
    : InMemoryDefinitionSource(name, priority, tenant), IVersionedAgentDefinitionSource
{
    public async ValueTask<AgentDefinition?> GetVersionAsync(string agentName, int version, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await PauseAsync(cancellationToken).ConfigureAwait(false);

        return Version(agentName, version);
    }
}
