using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// A code-defined agent registration. Use one of three forms: declarative
/// <see cref="Definition"/>, <see cref="Factory"/>, which gives full control, or
/// <see cref="ModelFactory"/>, which gives full control over the agent and declares its model.
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

    /// <summary>
    /// The factory that creates the agent without a declared model, or <see langword="null"/> when
    /// <see cref="Definition"/> or <see cref="ModelFactory"/> is used.
    /// </summary>
    public Func<IServiceProvider, AIAgent>? Factory { get; private init; }

    /// <summary>
    /// The model a factory agent declares, or <see langword="null"/> when the agent is declarative or
    /// declares none.
    /// </summary>
    public ModelBinding? Model { get; private init; }

    /// <summary>
    /// The factory that creates the agent from the chat client Tracon built for <see cref="Model"/>, or
    /// <see langword="null"/> when <see cref="Definition"/> or <see cref="Factory"/> is used.
    /// </summary>
    public Func<AgentFactoryContext, CancellationToken, ValueTask<AIAgent>>? ModelFactory { get; private init; }

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

    /// <summary>
    /// Creates a factory-based registration that declares the agent's model. Tracon builds the chat
    /// client for the model, with the tenant's key and egress policy, and hands it to the factory.
    /// </summary>
    /// <param name="name">The agent name.</param>
    /// <param name="model">The model the agent declares.</param>
    /// <param name="factory">The factory that creates the agent.</param>
    /// <param name="description">A short description.</param>
    /// <param name="displayName">The name displayed in the UI.</param>
    /// <returns>The registration.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty, or <paramref name="model"/> has no provider, no model name or a request field.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    public static CodeAgentRegistration FromModelFactory(
        string name,
        ModelBinding model,
        Func<AgentFactoryContext, CancellationToken, ValueTask<AIAgent>> factory,
        string? description = null,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(factory);
        FactoryModelFields.Validate(name, model, nameof(model));

        return new CodeAgentRegistration(name)
        {
            Model = model,
            ModelFactory = factory,
            Description = description,
            DisplayName = displayName,
        };
    }

    /// <summary>
    /// Calls the factory of a factory registration. This is the only place that calls it, so the
    /// catalog and the detail view build the agent the same way.
    /// </summary>
    /// <param name="services">The service provider handed to a factory that declares no model.</param>
    /// <param name="createChatClient">
    /// Builds the client of the declared model. The catalog passes the tenant-aware registry path;
    /// the detail view, which never calls the model, passes the plain one.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The agent, and the client the factory received when the model is declared.</returns>
    /// <exception cref="InvalidOperationException">The registration is declarative.</exception>
    /// <exception cref="TraconException">The factory of a declared model returned <see langword="null"/>.</exception>
    public async ValueTask<FactoryAgentBuild> BuildFactoryAgentAsync(
        IServiceProvider services,
        Func<ModelBinding, CancellationToken, ValueTask<IChatClient>> createChatClient,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(createChatClient);

        if (ModelFactory is { } modelFactory && Model is { } model)
        {
            var chatClient = await createChatClient(model, cancellationToken).ConfigureAwait(false);
            var context = new AgentFactoryContext
            {
                AgentName = Name,
                Services = services,
                Model = model,
                ChatClient = chatClient,
            };

            var declared = await modelFactory(context, cancellationToken).ConfigureAwait(false)
                ?? throw new TraconException($"The factory of agent '{Name}' returned null. A factory returns the agent it built.");

            return new FactoryAgentBuild(declared, chatClient);
        }

        if (Factory is { } factory)
        {
            return new FactoryAgentBuild(factory(services), DeclaredChatClient: null);
        }

        throw new InvalidOperationException($"Agent '{Name}' is declarative and has no factory.");
    }
}

/// <summary>An agent a factory built.</summary>
/// <param name="Agent">The agent; <see langword="null"/> only when a factory without a declared model returned nothing.</param>
/// <param name="DeclaredChatClient">The client the factory received, or <see langword="null"/> when the agent declares no model.</param>
internal readonly record struct FactoryAgentBuild(AIAgent? Agent, IChatClient? DeclaredChatClient);
