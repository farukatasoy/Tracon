using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Finds the definition the catalog runs for an agent name by walking the agent sources in the
/// catalog's own order, and stopping at the first source that holds the name.
/// </summary>
/// <remarks>
/// <para>
/// The parameter gate of <c>POST /api/agents/{name}/run</c>, the eval job, the queue and every path
/// that binds values read the definition behind an agent to know its schema. They used to look in
/// the code registrations and then the store, never in the sources, so a custom source with a lower
/// <see cref="IAgentSource.Priority"/> value than the store won the name in the catalog and lost it
/// here: a stored definition with a schema pulled the run away from the source that won.
/// </para>
/// <para>
/// The walk uses the order <see cref="CompositeAgentCatalog"/> uses, so the two cannot disagree. A
/// definition-backed source (<see cref="IDefinitionBackedSource"/>) answers from its definition. An
/// opaque <see cref="IAgentSource"/> answers from its listing and carries no schema.
/// </para>
/// </remarks>
internal sealed class AgentDefinitionLookup
{
    private readonly IAgentSource[] _sources;
    private readonly AgentSourceFailures _failures;

    /// <summary>Creates a lookup over the registered sources.</summary>
    /// <param name="sources">The agent sources.</param>
    /// <param name="failures">
    /// Where a failing source is recorded. <see langword="null"/> keeps the failure unrecorded, which
    /// only a test wants.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="sources"/> is <see langword="null"/>.</exception>
    public AgentDefinitionLookup(IEnumerable<IAgentSource> sources, AgentSourceFailures? failures = null)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _sources = [.. sources.OrderBy(static source => source.Priority)];
        _failures = failures ?? new AgentSourceFailures(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

    /// <summary>Finds the definition behind an agent name.</summary>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="version">A pinned version, or <see langword="null"/> for the current one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Where the name lives and, when the agent has one, its definition.</returns>
    /// <exception cref="TraconException">A source raised a Tracon error; it is not wrapped.</exception>
    public async ValueTask<FoundDefinition> FindAsync(string agentName, int? version, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        foreach (var source in _sources)
        {
            if (source is IDefinitionBackedSource backed)
            {
                HeldDefinition? held;

                try
                {
                    held = await backed.FindDefinitionAsync(agentName, version, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
                {
                    throw _failures.Handle(source, "resolve", exception);
                }

                if (held is { } found)
                {
                    return new FoundDefinition(found.Definition, backed.Origin, source);
                }

                continue;
            }

            if (await HoldsAsync(source, agentName, cancellationToken).ConfigureAwait(false))
            {
                return new FoundDefinition(Definition: null, AgentDefinitionOrigin.Custom, source);
            }
        }

        return FoundDefinition.None;
    }

    /// <summary>The source name a definition of <paramref name="origin"/> lists under.</summary>
    /// <param name="origin">Where the definition comes from.</param>
    /// <returns>The source name of a built-in source.</returns>
    public static string BuiltInSourceName(AgentDefinitionOrigin origin)
        => origin == AgentDefinitionOrigin.Code ? "code" : "database";

    /// <summary>
    /// Whether an opaque source holds a name. A source whose listing fails is taken to hold it, so this
    /// lookup never picks another source's schema over the one that failed. The failure is recorded here under
    /// <c>list</c>; the catalog resolves the agent next and records and raises its own, in its own words.
    /// </summary>
    private async ValueTask<bool> HoldsAsync(IAgentSource source, string agentName, CancellationToken cancellationToken)
    {
        IReadOnlyList<AgentDescriptor> descriptors;

        try
        {
            descriptors = await source.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            _failures.Record(source, "list", exception, LogLevel.Error);

            return true;
        }

        foreach (var descriptor in descriptors)
        {
            if (string.Equals(descriptor.Name, agentName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Where an agent name lives, and the definition behind it when it has one.</summary>
/// <param name="Definition">
/// The definition the catalog runs; <see langword="null"/> for an agent a source built itself, a
/// factory code agent, a version that does not exist, or a name nothing holds.
/// </param>
/// <param name="Origin">Where <paramref name="Definition"/> comes from.</param>
/// <param name="Source">The source that holds the name; <see langword="null"/> when none does.</param>
internal readonly record struct FoundDefinition(AgentDefinition? Definition, AgentDefinitionOrigin Origin, IAgentSource? Source)
{
    /// <summary>The outcome when no source holds the name.</summary>
    public static FoundDefinition None { get; } = new(null, AgentDefinitionOrigin.Database, null);

    /// <summary>Gets whether a source holds the name.</summary>
    public bool Held => Source is not null;

    /// <summary>Gets the name of the source that holds the name, or an empty string.</summary>
    public string SourceName => Source?.Name ?? string.Empty;

    /// <summary>Splits the result into the definition and where it comes from.</summary>
    /// <param name="definition">The definition.</param>
    /// <param name="origin">Where it comes from.</param>
    public void Deconstruct(out AgentDefinition? definition, out AgentDefinitionOrigin origin)
    {
        definition = Definition;
        origin = Origin;
    }
}
