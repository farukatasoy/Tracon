namespace Tracon.Core.UnitTests.Fakes;

/// <summary>An <see cref="IAgentDefinitionSource"/> that keeps its definitions in memory, per tenant.</summary>
internal class InMemoryDefinitionSource : IAgentDefinitionSource
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Tenant, string Name), List<AgentDefinition>> _history = [];
    private readonly Func<string> _tenant;

    public InMemoryDefinitionSource(string name = "memory", int priority = AgentSourcePriority.Database + 1, Func<string>? tenant = null)
    {
        Name = name;
        Priority = priority;
        _tenant = tenant ?? (static () => "default");
    }

    public string Name { get; }

    public int Priority { get; }

    /// <summary>Gets or sets the name the source reports a definition under instead of the one it holds, to break the contract on purpose.</summary>
    public string? ReturnDefinitionNamed { get; set; }

    /// <summary>Gets or sets an exception every call throws, to fail the source on purpose.</summary>
    public Exception? Failure { get; set; }

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

    public ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Failure is not null)
        {
            throw Failure;
        }

        var tenant = _tenant();

        lock (_gate)
        {
            return new ValueTask<IReadOnlyList<AgentDefinition>>(
                [.. _history.Where(entry => string.Equals(entry.Key.Tenant, tenant, StringComparison.Ordinal)).Select(static entry => entry.Value[^1])]);
        }
    }

    public ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Failure is not null)
        {
            throw Failure;
        }

        lock (_gate)
        {
            var found = _history.TryGetValue((_tenant(), agentName), out var versions) ? versions[^1] : null;

            return new ValueTask<AgentDefinition?>(found is not null && ReturnDefinitionNamed is { } other ? found with { Name = other } : found);
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
}

/// <summary>An <see cref="InMemoryDefinitionSource"/> that also returns one version.</summary>
internal sealed class VersionedInMemoryDefinitionSource(string name = "memory-versioned", int priority = AgentSourcePriority.Database + 1, Func<string>? tenant = null)
    : InMemoryDefinitionSource(name, priority, tenant), IVersionedAgentDefinitionSource
{
    public ValueTask<AgentDefinition?> GetVersionAsync(string agentName, int version, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<AgentDefinition?>(Version(agentName, version));
    }
}
