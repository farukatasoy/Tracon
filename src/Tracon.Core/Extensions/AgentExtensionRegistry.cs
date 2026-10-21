using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// The agent extensions registered in code, frozen when the compiler is built, and the
/// one place that runs them.
/// </summary>
/// <remarks>
/// <para>
/// Registration is data-free and runs no extension: the registry stores the
/// instances, checks their names, and calls <see cref="IAgentExtension.Configure"/>
/// only when an agent that names the extension is compiled. Listing the names
/// (<c>GET /api/agent-extensions</c>) therefore runs no code of the host.
/// </para>
/// <para>
/// The set cannot change after construction, so a definition's extension names are the
/// only input of an extension that varies: they are part of the definition and so of the
/// compiled-agent cache key.
/// </para>
/// </remarks>
internal sealed class AgentExtensionRegistry
{
    private readonly Dictionary<string, IAgentExtension> _byName = new(StringComparer.Ordinal);

    /// <summary>Creates the registry.</summary>
    /// <param name="extensions">The extensions the host registered.</param>
    /// <exception cref="TraconException">A name is not valid, or two extensions share a name.</exception>
    public AgentExtensionRegistry(IEnumerable<IAgentExtension> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        foreach (var extension in extensions)
        {
            var name = extension.Name;

            if (!RunEventCustomTypes.IsValidType(name))
            {
                throw new TraconException(
                    $"The agent extension name '{name}' is not valid. A name is 1 to 128 characters of " +
                    "lowercase ASCII letters, digits, '.', '_' or '-', and starts with a letter or a digit.");
            }

            if (!_byName.TryAdd(name, extension))
            {
                throw new TraconException(
                    $"Multiple agent extensions named '{name}' have been registered. " +
                    "Extension names must be unique.");
            }
        }

        Extensions = [.. _byName.Values
            .OrderBy(static extension => extension.Name, StringComparer.Ordinal)
            .Select(static extension => new AgentExtensionInfo(extension.Name, extension.Description))];
    }

    /// <summary>Gets the registry for a compiler built without any extension.</summary>
    public static AgentExtensionRegistry Empty { get; } = new([]);

    /// <summary>Gets the registered extensions, ordered by name. No extension code runs to produce it.</summary>
    public IReadOnlyList<AgentExtensionInfo> Extensions { get; }

    /// <summary>
    /// Finds what is wrong with the extension names of a definition: a name that is not
    /// registered, and a name that appears twice.
    /// </summary>
    /// <param name="names">The definition's <see cref="AgentDefinition.ExtensionNames"/>.</param>
    /// <returns>The problems; empty when the list is acceptable.</returns>
    public IReadOnlyList<AgentExtensionProblem> FindProblems(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        List<AgentExtensionProblem>? problems = null;
        HashSet<string>? seen = null;

        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            var path = $"extensionNames[{index}]";

            if (!_byName.ContainsKey(name))
            {
                (problems ??= []).Add(new AgentExtensionProblem(
                    "unknown_extension",
                    $"The agent extension '{name}' is not registered in this code.",
                    path));
            }
            else if (!(seen ??= new HashSet<string>(StringComparer.Ordinal)).Add(name))
            {
                (problems ??= []).Add(new AgentExtensionProblem(
                    "duplicate_extension",
                    $"The agent extension '{name}' is listed more than once.",
                    path));
            }
        }

        return problems ?? [];
    }

    /// <summary>
    /// Runs the extensions a definition names, in the definition's order, on the options of
    /// the agent that is being compiled.
    /// </summary>
    /// <param name="definition">The definition being compiled.</param>
    /// <param name="tenantId">The tenant it is compiled for.</param>
    /// <param name="services">The root service provider.</param>
    /// <param name="chatOptions">The options of a chat agent, or <see langword="null"/>.</param>
    /// <param name="harnessOptions">The options of a harness agent, or <see langword="null"/>.</param>
    /// <returns>The wrappers and the chat client rings the extensions added, each in the order they were added.</returns>
    /// <exception cref="TraconCompilationException">
    /// A name is not registered or appears twice, an extension threw, or an extension changed a
    /// protected member.
    /// </exception>
    public ExtensionConfiguration Configure(
        AgentDefinition definition,
        string tenantId,
        IServiceProvider services,
        ChatClientAgentOptions? chatOptions,
        HarnessAgentOptions? harnessOptions)
    {
        if (definition.ExtensionNames.Count == 0)
        {
            return ExtensionConfiguration.Empty;
        }

        // The same rules the validator reports with a path: a saved definition can carry a
        // name that was registered when it was saved and is not any more.
        if (FindProblems(definition.ExtensionNames) is { Count: > 0 } problems)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' has an invalid extension list: {string.Join(" ", problems.Select(static problem => problem.Message))}")
            {
                AgentName = definition.Name,
            };
        }

        var wrappers = new List<ExtensionWrapper>();
        var modelCallRings = new List<ExtensionChatClientRing>();
        var turnRings = new List<ExtensionChatClientRing>();

        foreach (var name in definition.ExtensionNames)
        {
            var extension = _byName[name];
            var context = new AgentExtensionContext(definition, tenantId, services, chatOptions, harnessOptions);
            var checkpoint = chatOptions is not null
                ? AgentOptionsGuard.Take(chatOptions)
                : AgentOptionsGuard.Take(harnessOptions!);

            try
            {
                extension.Configure(context);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Only the extension's name and the exception type reach the message: the
                // message of a host's exception can carry a connection string.
                throw new TraconCompilationException(
                    $"Agent extension '{name}' failed while agent '{definition.Name}' was compiled " +
                    $"({exception.GetType().Name}).",
                    exception)
                {
                    AgentName = definition.Name,
                };
            }

            if (checkpoint.Changes() is { Count: > 0 } changed)
            {
                throw new TraconCompilationException(
                    $"Agent extension '{name}' changed {string.Join(", ", changed)} while agent '{definition.Name}' was compiled. " +
                    "The definition or a Tracon tenant and security decision owns that member, so an extension " +
                    "cannot change it. Change the definition instead, or leave the member as it was.")
                {
                    AgentName = definition.Name,
                };
            }

            wrappers.AddRange(context.Wrappers.Select(wrap => new ExtensionWrapper(name, wrap)));

            foreach (var (slot, ring) in context.Rings)
            {
                (slot == ChatClientSlot.ModelCall ? modelCallRings : turnRings).Add(new ExtensionChatClientRing(name, ring));
            }
        }

        return new ExtensionConfiguration(
            wrappers,
            modelCallRings.Count == 0 && turnRings.Count == 0
                ? ChatClientRings.Empty
                : new ChatClientRings(definition.Name, modelCallRings, turnRings));
    }

    /// <summary>
    /// Builds the agent from options the definition's extensions changed, and names the extensions
    /// when Microsoft Agent Framework refuses those options.
    /// </summary>
    /// <param name="build">Builds the agent from the options.</param>
    /// <param name="definition">The definition being compiled.</param>
    /// <returns>The agent <paramref name="build"/> returned.</returns>
    /// <exception cref="TraconCompilationException">The definition names an extension and the build threw.</exception>
    /// <remarks>
    /// Microsoft Agent Framework checks the options when it builds the agent, after every extension
    /// ran: a second provider with the state key of one Tracon built is refused there. Without this,
    /// the refusal reached the caller as an agent source failure that named no extension. Only the
    /// exception type reaches the message, for the reason <see cref="Configure"/> gives.
    /// </remarks>
    public static AIAgent Build(Func<AIAgent> build, AgentDefinition definition)
    {
        if (definition.ExtensionNames.Count == 0)
        {
            return build();
        }

        try
        {
            return build();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not TraconException)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' could not be built after the agent extensions " +
                $"{string.Join(", ", definition.ExtensionNames.Select(static name => $"'{name}'"))} ran " +
                $"({exception.GetType().Name}). A provider that an extension adds must not repeat one that " +
                "Tracon built, for example a second skill provider.",
                exception)
            {
                AgentName = definition.Name,
            };
        }
    }

    /// <summary>Puts the wrappers around a compiled agent, the first one innermost.</summary>
    /// <param name="agent">The compiled agent.</param>
    /// <param name="wrappers">The wrappers from <see cref="Configure"/>.</param>
    /// <param name="agentName">The agent's name, for the error.</param>
    /// <returns>The wrapped agent; <paramref name="agent"/> itself when there is no wrapper.</returns>
    /// <exception cref="TraconCompilationException">A wrapper threw or returned <see langword="null"/>.</exception>
    public static AIAgent Wrap(AIAgent agent, IReadOnlyList<ExtensionWrapper> wrappers, string agentName)
    {
        foreach (var (extension, wrap) in wrappers)
        {
            try
            {
                agent = wrap(agent) ?? throw new TraconCompilationException(
                    $"Agent extension '{extension}' returned no agent from a wrapper while agent '{agentName}' was compiled.")
                {
                    AgentName = agentName,
                };
            }
            catch (TraconCompilationException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new TraconCompilationException(
                    $"Agent extension '{extension}' failed in a wrapper while agent '{agentName}' was compiled " +
                    $"({exception.GetType().Name}).",
                    exception)
                {
                    AgentName = agentName,
                };
            }
        }

        return agent;
    }
}

/// <summary>What the extensions of one agent added besides their changes to the options object.</summary>
/// <param name="Wrappers">The wrappers, in the order they were added.</param>
/// <param name="Rings">The chat client rings, in the order they were added.</param>
internal sealed record ExtensionConfiguration(IReadOnlyList<ExtensionWrapper> Wrappers, ChatClientRings Rings)
{
    /// <summary>Gets the result for an agent that names no extension.</summary>
    public static ExtensionConfiguration Empty { get; } = new([], ChatClientRings.Empty);
}

/// <summary>A wrapper an extension added, with the extension's name for the error message.</summary>
/// <param name="Extension">The extension's name.</param>
/// <param name="Wrap">Receives the agent and returns the agent that replaces it.</param>
internal readonly record struct ExtensionWrapper(string Extension, Func<AIAgent, AIAgent> Wrap);

/// <summary>An agent extension as the catalog lists it.</summary>
/// <param name="Name">The name a definition writes in <see cref="AgentDefinition.ExtensionNames"/>.</param>
/// <param name="Description">What the extension does, or <see langword="null"/>.</param>
internal sealed record AgentExtensionInfo(string Name, string? Description);

/// <summary>A problem in a definition's extension list.</summary>
/// <param name="Code">The validation code.</param>
/// <param name="Message">The message.</param>
/// <param name="Path">The path of the offending list item.</param>
internal sealed record AgentExtensionProblem(string Code, string Message, string Path);
