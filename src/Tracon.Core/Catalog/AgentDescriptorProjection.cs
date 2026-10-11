namespace Tracon;

/// <summary>
/// Builds the catalog summary of one <see cref="AgentDefinition"/>.
/// </summary>
/// <remarks>
/// One place for the projection. Five call sites built it by hand
/// — the store source, eval, the parameterized run, replay and continuation —
/// and the copies had already drifted. A descriptor must describe the SAME
/// definition the agent beside it was compiled from: the decorators read the
/// response format, the model and the version off it, so a descriptor of
/// another version validates the response against the wrong schema and
/// records the wrong model, cost and version.
/// </remarks>
internal static class AgentDescriptorProjection
{
    /// <summary>Projects a definition into its catalog summary.</summary>
    /// <param name="definition">The definition the agent was compiled from.</param>
    /// <param name="origin">Where the definition comes from.</param>
    /// <param name="sourceName">The name of the source that holds it.</param>
    /// <returns>The summary.</returns>
    public static AgentDescriptor FromDefinition(
        AgentDefinition definition,
        AgentDefinitionOrigin origin,
        string sourceName)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        return new AgentDescriptor
        {
            Name = definition.Name,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Origin = origin,
            SourceName = sourceName,
            Version = definition.Version,
            Model = definition.Model,
            ToolNames = definition.ToolNames,
            SkillNames = definition.SkillNames,
            CallableAgentNames = definition.CallableAgentNames,
            UsesHarness = definition.Harness is not null,
            UpdatedAt = definition.UpdatedAt,
        };
    }
}

/// <summary>
/// An agent source that returns, with each agent it resolves, the descriptor
/// of the very definition it compiled.
/// </summary>
/// <remarks>
/// Internal on purpose: the catalog's own
/// store source implements it; a third-party <see cref="IVersionedAgentSource"/>
/// does not, and for it the catalog keeps the listed descriptor and replaces
/// only <see cref="AgentDescriptor.Version"/> on a pinned resolve.
/// </remarks>
internal interface IDescribingAgentSource
{
    /// <summary>Resolves an agent and the descriptor of the definition it was compiled from.</summary>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="version">The pinned version; <see langword="null"/> for the current one.</param>
    /// <param name="culture">The instruction culture.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The agent and its descriptor; <see langword="null"/> when the source has no such agent or version.</returns>
    ValueTask<DescribedAgent?> ResolveDescribedAsync(
        string agentName,
        int? version,
        string? culture,
        CancellationToken cancellationToken);
}

/// <summary>An agent with the descriptor of the definition it was compiled from.</summary>
/// <param name="Agent">The compiled agent.</param>
/// <param name="Descriptor">Its descriptor.</param>
internal readonly record struct DescribedAgent(Microsoft.Agents.AI.AIAgent Agent, AgentDescriptor Descriptor);
