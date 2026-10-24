using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>A hosted tool a host registered in code with <c>AddHostedTool</c>.</summary>
/// <param name="Name">The name a definition selects it by.</param>
/// <param name="Kind">The kind of tool the factory builds.</param>
/// <param name="Factory">Builds the tool for one agent being compiled.</param>
/// <param name="Description">The description the editor shows, or <see langword="null"/>.</param>
internal sealed record HostedToolRegistration(
    string Name,
    HostedToolKind Kind,
    Func<HostedToolContext, AITool> Factory,
    string? Description);

/// <summary>One thing wrong with the hosted tools a definition selects.</summary>
/// <param name="Code">The validation code.</param>
/// <param name="Message">The sentence that names the tool and the rule.</param>
/// <param name="Path">The JSON path of the item in the definition.</param>
internal sealed record HostedToolProblem(string Code, string Message, string Path);

/// <summary>
/// The hosted tools registered in code, frozen when the compiler is built, and the one place that
/// decides which of them a definition may select and builds them.
/// </summary>
/// <remarks>
/// <para>
/// A hosted tool lives beside the function tools, not in <see cref="IToolRegistry"/>: that contract
/// returns an <see cref="AIFunctionDeclaration"/> and is a security boundary a host can decorate (the
/// MCP registry does). A hosted tool is an <see cref="AITool"/> the provider runs, so the compiler
/// reads both registries and one name never means two tools (the host start refuses a clash).
/// </para>
/// <para>
/// Registration is data-free and runs no factory: the registry stores the registrations and calls a
/// factory only when an agent that selects the tool is compiled. Listing the tools
/// (<c>GET /api/tools</c>) therefore runs no code of the host.
/// </para>
/// <para>
/// The tool the factory returns is NOT the registry's own object. Every compile builds a new one
/// for the tenant it compiles for, and <see cref="HostedToolIdentity"/> remembers the name it was
/// built under, because the provider's response names no tool: the run recorder reads the registered
/// name from the instance the model call was given.
/// </para>
/// </remarks>
internal sealed class HostedToolRegistry
{
    private readonly Dictionary<string, HostedToolRegistration> _byName = new(StringComparer.Ordinal);

    /// <summary>Creates the registry.</summary>
    /// <param name="registrations">The registrations the host added.</param>
    /// <exception cref="TraconException">A name is not valid, a kind is not defined, or two registrations share a name.</exception>
    public HostedToolRegistry(IEnumerable<HostedToolRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        foreach (var registration in registrations)
        {
            if (!ToolNameRules.IsValid(registration.Name))
            {
                throw new TraconException($"Tool name '{registration.Name}' is invalid. {ToolNameRules.Description}");
            }

            if (!Enum.IsDefined(registration.Kind))
            {
                throw new TraconException($"Hosted tool '{registration.Name}' has a kind that is not defined: {registration.Kind}.");
            }

            if (!_byName.TryAdd(registration.Name, registration))
            {
                throw new TraconException(
                    $"More than one hosted tool is registered with name '{registration.Name}'. Tool names must be unique.");
            }
        }

        Descriptors = [.. _byName.Values
            .OrderBy(static registration => registration.Name, StringComparer.Ordinal)
            .Select(static registration => new ToolDescriptor
            {
                Name = registration.Name,
                Description = registration.Description,
                HostedKind = registration.Kind,

                // Data leaves the process for a service the model call already uses, and the call is billed.
                Effect = ToolEffect.External,
            })];
    }

    /// <summary>Gets the registry of a compiler built without any hosted tool.</summary>
    public static HostedToolRegistry Empty { get; } = new([]);

    /// <summary>Gets the registered tools, ordered by name. No factory runs to produce it.</summary>
    public IReadOnlyList<ToolDescriptor> Descriptors { get; }

    /// <summary>Gets whether a hosted tool is registered under <paramref name="name"/>.</summary>
    /// <param name="name">The tool name. Comparison is case-sensitive.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool Contains(string name) => _byName.ContainsKey(name);

    /// <summary>Finds a registration.</summary>
    /// <param name="name">The tool name. Comparison is case-sensitive.</param>
    /// <param name="registration">The registration found.</param>
    /// <returns><see langword="true"/> when the name is registered.</returns>
    public bool TryGet(string name, [NotNullWhen(true)] out HostedToolRegistration? registration)
        => _byName.TryGetValue(name, out registration);

    /// <summary>Lists the hosted tools a definition selects, in the order of its tool names.</summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The selected tools with the index of each in <see cref="AgentDefinition.ToolNames"/>.</returns>
    public IReadOnlyList<(int Index, HostedToolRegistration Registration)> Selected(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (_byName.Count == 0)
        {
            return [];
        }

        List<(int, HostedToolRegistration)>? selected = null;

        for (var index = 0; index < definition.ToolNames.Count; index++)
        {
            if (_byName.TryGetValue(definition.ToolNames[index], out var registration))
            {
                (selected ??= []).Add((index, registration));
            }
        }

        return selected ?? [];
    }

    /// <summary>
    /// Finds what is wrong with the hosted tools a definition selects, apart from the provider's
    /// support (<see cref="HostedToolSupportCheck"/>): two tools of one kind, a second web search, and a
    /// tool mode that requires a hosted tool.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The problems; empty when the selection is acceptable.</returns>
    /// <remarks>
    /// One rule for two readers: the compiler stops on the first problem and the validator reports
    /// every one with its path. The provider's response names the kind and not the tool, so a second tool of
    /// one kind could not be told from the first in the run record.
    /// </remarks>
    public IReadOnlyList<HostedToolProblem> FindProblems(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var selected = Selected(definition);

        if (selected.Count == 0)
        {
            return [];
        }

        var problems = new List<HostedToolProblem>();
        var firstOfKind = new Dictionary<HostedToolKind, string>();

        foreach (var (index, registration) in selected)
        {
            if (firstOfKind.TryGetValue(registration.Kind, out var first))
            {
                problems.Add(new HostedToolProblem(
                    ValidationCodes.DuplicateHostedToolKind,
                    $"The hosted tool '{registration.Name}' is a {registration.Kind} tool, and '{first}' already is. " +
                    "An agent selects at most one hosted tool of each kind: the provider's response names the kind, " +
                    "not the tool, so the run record could not tell the two apart.",
                    $"toolNames[{index}]"));

                continue;
            }

            firstOfKind[registration.Kind] = registration.Name;

            if (registration.Kind == HostedToolKind.WebSearch && definition.Harness is { EnableWebSearch: true })
            {
                problems.Add(new HostedToolProblem(
                    ValidationCodes.HostedToolConflict,
                    $"The hosted tool '{registration.Name}' is a web search, and the harness already adds one " +
                    $"({nameof(HarnessSettings)}.{nameof(HarnessSettings.EnableWebSearch)}). Turn one of them off: " +
                    "the model would receive two web search tools.",
                    $"toolNames[{index}]"));
            }
        }

        if (RequiredToolName(definition.Model.ToolMode) is { } required && _byName.ContainsKey(required))
        {
            problems.Add(new HostedToolProblem(
                ValidationCodes.HostedToolNotCallable,
                $"{nameof(ModelBinding)}.{nameof(ModelBinding.ToolMode)} requires the tool '{required}', which is a hosted tool. " +
                "The provider runs a hosted tool when the model chooses it; a tool mode can name only a tool Tracon calls.",
                "model.toolMode"));
        }

        return problems;
    }

    /// <summary>Builds the tool of one registration for the agent being compiled.</summary>
    /// <param name="registration">The registration.</param>
    /// <param name="context">What the factory may read.</param>
    /// <param name="agentName">The agent being compiled, for the error.</param>
    /// <returns>The tool, remembered under the registered name.</returns>
    /// <exception cref="TraconCompilationException">
    /// The factory threw, returned nothing, returned a tool that is not of the registered kind, or
    /// returned an instance already registered under another name.
    /// </exception>
    public static AITool Create(HostedToolRegistration registration, HostedToolContext context, string agentName)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(context);

        AITool? tool;

        try
        {
            tool = registration.Factory(context);
        }

        // The factory is synchronous and holds no token: no cancellation of ours can reach this catch, so every
        // exception is the factory's own failure.
        catch (Exception ex)
        {
            throw new TraconCompilationException(
                $"Agent '{agentName}' could not be compiled: the factory of the hosted tool '{registration.Name}' threw {ex.GetType().Name}.",
                ex)
            {
                AgentName = agentName,
            };
        }

        if (tool is null || !IsOfKind(tool, registration.Kind))
        {
            throw new TraconCompilationException(
                $"Agent '{agentName}' could not be compiled: the factory of the hosted tool '{registration.Name}' " +
                $"must return a {ExpectedType(registration.Kind)} for a {registration.Kind} tool" +
                (tool is null ? ", and it returned nothing." : $", and it returned a {tool.GetType().Name}."))
            {
                AgentName = agentName,
            };
        }

        if (!HostedToolIdentity.TryRegister(tool, registration.Name))
        {
            throw new TraconCompilationException(
                $"Agent '{agentName}' could not be compiled: the factory of the hosted tool '{registration.Name}' " +
                "returned an instance that another hosted tool returned too. Return a new tool on every call.")
            {
                AgentName = agentName,
            };
        }

        return tool;
    }

    /// <summary>
    /// Produces the cache fingerprint of what the definition's hosted tools put on the wire, for the
    /// tenant they are compiled for.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="context">What the factories may read.</param>
    /// <returns>
    /// An empty text when the definition selects no hosted tool. <see cref="HostedToolFingerprint.Opaque"/> is set when a
    /// tool holds a value the fingerprint cannot compare.
    /// </returns>
    /// <remarks>
    /// The factory's result is baked into the compiled agent, and it can come from state outside the
    /// definition (the identifier of a tenant's own vector store). The thing that is embedded is hashed, not a
    /// proxy for it: a tenant that replaces its vector store changes the key and the next resolve
    /// compiles against the new one. This is why a factory runs on every resolve, not only on a compile.
    /// </remarks>
    public HostedToolFingerprint Fingerprint(AgentDefinition definition, HostedToolContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);

        var selected = Selected(definition);

        if (selected.Count == 0)
        {
            return HostedToolFingerprint.None;
        }

        var text = new StringBuilder();
        var opaque = false;

        foreach (var (_, registration) in selected)
        {
            var description = HostedToolDescription.Of(Create(registration, context, definition.Name));

            text.Append('|').Append(registration.Name).Append(':').Append(registration.Kind).Append(':').Append(description.Text);
            opaque |= description.Opaque;
        }

        return new HostedToolFingerprint(
            string.Concat("hosted:", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))),
            opaque);
    }

    /// <summary>Gets whether <paramref name="tool"/> is the type the adapters map for <paramref name="kind"/>.</summary>
    internal static bool IsOfKind(AITool tool, HostedToolKind kind) => kind switch
    {
        HostedToolKind.WebSearch => tool is HostedWebSearchTool,
        HostedToolKind.CodeInterpreter => tool is HostedCodeInterpreterTool,
        HostedToolKind.FileSearch => tool is HostedFileSearchTool,
        _ => false,
    };

    private static string ExpectedType(HostedToolKind kind) => kind switch
    {
        HostedToolKind.WebSearch => nameof(HostedWebSearchTool),
        HostedToolKind.CodeInterpreter => nameof(HostedCodeInterpreterTool),
        _ => nameof(HostedFileSearchTool),
    };

    private static string? RequiredToolName(string? toolMode)
    {
        const string RequiredPrefix = "required:";

        return toolMode is not null && toolMode.Trim() is { } text && text.StartsWith(RequiredPrefix, StringComparison.OrdinalIgnoreCase)
            ? text[RequiredPrefix.Length..].Trim()
            : null;
    }
}

/// <summary>The cache fingerprint of the hosted tools a definition selects.</summary>
/// <param name="Text">The fingerprint; empty when the definition selects none.</param>
/// <param name="Opaque">Whether a tool holds a value the fingerprint cannot compare (see <see cref="HostedToolDescription"/>).</param>
internal readonly record struct HostedToolFingerprint(string Text, bool Opaque)
{
    /// <summary>Gets the fingerprint of a definition that selects no hosted tool.</summary>
    public static HostedToolFingerprint None { get; } = new(string.Empty, Opaque: false);
}
