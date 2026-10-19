using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// The agent an <see cref="IAgentExtension"/> is configuring: the definition, the
/// tenant, and the one options object the compiler is about to build the agent from.
/// </summary>
/// <remarks>
/// Exactly one of <see cref="ChatAgentOptions"/> and <see cref="HarnessAgentOptions"/>
/// is set. It is the one the agent's kind selects: a definition with
/// <see cref="AgentDefinition.Harness"/> is a harness agent. Change the options
/// object in place; do not keep it. The compiler reads it once after the last
/// extension has run.
/// </remarks>
public sealed class AgentExtensionContext
{
    private readonly List<Func<AIAgent, AIAgent>> _wrappers = [];

    internal AgentExtensionContext(
        AgentDefinition definition,
        string tenantId,
        IServiceProvider services,
        ChatClientAgentOptions? chatAgentOptions,
        HarnessAgentOptions? harnessAgentOptions)
    {
        Definition = definition;
        TenantId = tenantId;
        Services = services;
        ChatAgentOptions = chatAgentOptions;
        HarnessAgentOptions = harnessAgentOptions;
    }

    /// <summary>Gets the definition the agent is compiled from. Changing a copy of it has no effect.</summary>
    public AgentDefinition Definition { get; }

    /// <summary>Gets the tenant the agent is compiled for.</summary>
    public string TenantId { get; }

    /// <summary>
    /// Gets the application's root service provider. A scoped service is not available
    /// here: the compiled agent outlives the request that compiled it.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets the options of a chat agent, or <see langword="null"/> when the agent is a
    /// harness agent.
    /// </summary>
    public ChatClientAgentOptions? ChatAgentOptions { get; }

    /// <summary>
    /// Gets the options of a harness agent, or <see langword="null"/> when the agent is a
    /// chat agent.
    /// </summary>
    // MAAI001: Microsoft.Agents.AI.HarnessAgentOptions members are marked "evaluation
    // purposes only". Naming the type here does not need the suppression; using its
    // members in the caller's own code does. Rationale: docs/KARARLAR.md, decision K-020.
    public HarnessAgentOptions? HarnessAgentOptions { get; }

    /// <summary>
    /// Adds a wrapper that is put around the compiled agent. A Microsoft Agent
    /// Framework middleware (<c>AIAgentBuilder.Use</c>) belongs here.
    /// </summary>
    /// <param name="wrap">Receives the agent and returns the agent that replaces it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wrap"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A wrapper sits innermost: Tracon's own wrappers and the catalog's decorators (run
    /// recording, budget, tool approval) stay outside it. With several wrappers the
    /// first one added is the innermost.
    /// </para>
    /// <para>
    /// A wrapper is not checked. It sees what the model call sees and can change it;
    /// that is what a middleware is for.
    /// </para>
    /// </remarks>
    public void Use(Func<AIAgent, AIAgent> wrap)
    {
        ArgumentNullException.ThrowIfNull(wrap);
        _wrappers.Add(wrap);
    }

    internal IReadOnlyList<Func<AIAgent, AIAgent>> Wrappers => _wrappers;
}
