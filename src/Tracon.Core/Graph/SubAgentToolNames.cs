using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Names the tool a callable agent becomes in <see cref="SubAgentCallMode.Tool"/> mode and
/// finds the definitions whose tools would share a name.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft Agent Framework's <c>AsAIFunction</c> names the tool after the agent and replaces
/// every run of characters other than letters and digits with one underscore. Tracon passes the
/// name itself, so the name the rule below checks is the name the model sees even if the
/// framework changes its own rule.
/// </para>
/// <para>
/// A model provider refuses two tools with one name, and only after the run started. The rule is
/// one function: the validator reports its problems with a path, and the compiler throws on the
/// first one, so a definition saved over HTTP or written in code meets the same refusal.
/// </para>
/// </remarks>
internal static class SubAgentToolNames
{
    /// <summary>The validation code of a callable agent whose tool name is already taken.</summary>
    internal const string ConflictCode = "sub_agent_tool_name_conflict";

    // The framework's own names for the skill tools (AgentSkillsProvider). Tracon builds that
    // provider whenever the definition names a skill, so the three tools are always there then.
    private static readonly string[] SkillToolNames = ["load_skill", "read_skill_resource", "run_skill_script"];

    /// <summary>Gets the name of the tool that calls <paramref name="agentName"/>.</summary>
    /// <param name="agentName">The callable agent's name.</param>
    /// <returns>The agent name with each run of characters other than letters and digits replaced by one underscore.</returns>
    public static string NameOf(string agentName)
    {
        ArgumentNullException.ThrowIfNull(agentName);

        var name = new StringBuilder(agentName.Length);
        var inRun = false;

        foreach (var character in agentName)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                name.Append(character);
                inRun = false;
            }
            else if (!inRun)
            {
                name.Append('_');
                inRun = true;
            }
        }

        return name.ToString();
    }

    /// <summary>
    /// Finds every callable agent of a definition in <see cref="SubAgentCallMode.Tool"/> mode
    /// whose tool name another tool of the agent already has.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The problems, in the order of <see cref="AgentDefinition.CallableAgentNames"/>; empty when the names are free or the mode is not <see cref="SubAgentCallMode.Tool"/>.</returns>
    public static IReadOnlyList<SubAgentToolProblem> FindProblems(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.SubAgents is not { CallMode: SubAgentCallMode.Tool } || definition.CallableAgentNames.Count == 0)
        {
            return [];
        }

        // Tool name -> what holds it, for the message.
        var taken = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in definition.ToolNames)
        {
            taken.TryAdd(name, $"the tool '{name}' in toolNames");
        }

        if (definition.SkillNames.Count > 0)
        {
            foreach (var name in SkillToolNames)
            {
                taken.TryAdd(name, $"the skill tool '{name}'");
            }
        }

        if (definition.Memory is { EnableVectorSearch: true })
        {
            taken.TryAdd(VectorSearchToolFactory.ToolName, $"the tool '{VectorSearchToolFactory.ToolName}' of semantic search");
        }

        List<SubAgentToolProblem>? problems = null;

        for (var index = 0; index < definition.CallableAgentNames.Count; index++)
        {
            var agentName = definition.CallableAgentNames[index];
            var toolName = NameOf(agentName);

            if (taken.TryGetValue(toolName, out var holder))
            {
                (problems ??= []).Add(new SubAgentToolProblem(
                    ConflictCode,
                    $"The callable agent '{agentName}' becomes the tool '{toolName}', and {holder} already has that name. " +
                    "Rename one of them, or call the agents as background tasks.",
                    $"callableAgentNames[{index}]"));
            }
            else
            {
                taken[toolName] = $"the callable agent '{agentName}'";
            }
        }

        return problems ?? [];
    }

    /// <summary>Makes one tool for each sub-agent wrapper.</summary>
    /// <param name="children">The wrappers (<see cref="ChildAgentInvoker"/>) of the callable agents.</param>
    /// <returns>One tool for each wrapper, in the same order, named by <see cref="NameOf"/>.</returns>
    public static List<AIFunction> CreateFunctions(IReadOnlyList<AIAgent> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        var functions = new List<AIFunction>(children.Count);

        foreach (var child in children)
        {
            functions.Add(new SubAgentToolFunction(
                child.AsAIFunction(new AIFunctionFactoryOptions { Name = NameOf(child.Name!) }),
                child.Name!));
        }

        return functions;
    }
}

/// <summary>A tool name two tools of one agent would share.</summary>
/// <param name="Code">The validation code.</param>
/// <param name="Message">The human-readable description.</param>
/// <param name="Path">The path of the list item that causes it.</param>
internal sealed record SubAgentToolProblem(string Code, string Message, string Path);

/// <summary>
/// The tool of one callable agent. A sub-agent that fails for a reason Tracon itself names - its
/// model provider is down, or the agent no longer exists - answers the parent with the reason as the
/// tool result, as a background task does with <c>Task failed: ...</c>.
/// </summary>
/// <remarks>
/// <para>
/// Left to <see cref="FunctionInvokingChatClient"/>, the exception reaches the model as
/// <c>Error: Function failed.</c> with no reason, and it counts as a failed tool turn: a model that
/// retries the agent three times in a row ends the parent's run with the child's exception. The
/// background path never does either, because MAF reports a failed task as the text of a
/// successful call.
/// </para>
/// <para>
/// Only <see cref="TraconException"/> is turned into text. Its message is written by Tracon and is
/// safe to show: the provider's own message never reaches it (<c>ProviderFailureNormalizer</c>). Any
/// other exception keeps the framework's behavior, because its message can carry a connection string
/// or a key. The caller's own cancellation is never swallowed.
/// </para>
/// </remarks>
internal sealed class SubAgentToolFunction(AIFunction inner, string agentName) : DelegatingAIFunction(inner)
{
    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (TraconException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return $"Agent '{agentName}' could not complete: {exception.Message}";
        }
    }
}
