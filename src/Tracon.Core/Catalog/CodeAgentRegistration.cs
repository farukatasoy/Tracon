using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// A code-defined agent registration. Use one of two forms: declarative
/// <see cref="Definition"/> or <see cref="Factory"/>, which gives full control.
/// </summary>
internal sealed class CodeAgentRegistration
{
    private CodeAgentRegistration(string name)
    {
        Name = name;
    }

    /// <summary>The agent name.</summary>
    public string Name { get; }

    /// <summary>The declarative definition, or <see langword="null"/> when <see cref="Factory"/> is used.</summary>
    public AgentDefinition? Definition { get; private init; }

    /// <summary>The factory that creates the agent, or <see langword="null"/> when <see cref="Definition"/> is used.</summary>
    public Func<IServiceProvider, AIAgent>? Factory { get; private init; }

    /// <summary>The name displayed in the UI.</summary>
    public string? DisplayName { get; private init; }

    /// <summary>A short description.</summary>
    public string? Description { get; private init; }

    /// <summary>
    /// Creates a declarative registration. The definition passes through the
    /// Tracon compiler, which validates tools and the model.
    /// </summary>
    /// <param name="definition">The agent definition.</param>
    /// <returns>The registration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    public static CodeAgentRegistration FromDefinition(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new CodeAgentRegistration(definition.Name)
        {
            Definition = definition with { Origin = AgentDefinitionOrigin.Code, Version = 1 },
            DisplayName = definition.DisplayName,
            Description = definition.Description,
        };
    }

    /// <summary>
    /// Creates a factory-based registration. The caller fully controls how the agent
    /// is built. Tracon only exposes it in the catalog and wraps it with run recording.
    /// </summary>
    /// <param name="name">The agent name.</param>
    /// <param name="factory">The factory that creates the agent.</param>
    /// <param name="description">A short description.</param>
    /// <param name="displayName">The name displayed in the UI.</param>
    /// <returns>The registration.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public static CodeAgentRegistration FromFactory(
        string name,
        Func<IServiceProvider, AIAgent> factory,
        string? description = null,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        return new CodeAgentRegistration(name)
        {
            Factory = factory,
            Description = description,
            DisplayName = displayName,
        };
    }
}
