using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// The rule for model options that come with one run, on <see cref="ChatClientAgentRunOptions"/>,
/// rather than with the agent.
/// </summary>
/// <remarks>
/// <para>
/// No HTTP endpoint carries such options: a definition version, an experiment variant or a
/// declared parameter changes a run, so the definition stays the record of what ran. Microsoft
/// Agent Framework itself sends them, and so can code that calls a catalog agent. The handoff step
/// of a workflow gives its agent the handoff tools and instructions this way.
/// </para>
/// <para>
/// The rule is the extension rule (<see cref="AgentOptionsGuard"/>) applied to one run, and it
/// reads the same table. A protected member is refused: a model id or a temperature the run sets
/// would reach the provider while the run record and the budget price the definition's model. Two
/// protected members are allowed, because the framework merges them and the definition keeps its
/// own: instructions are appended, and a tool is accepted when it is only a declaration that no
/// one runs, as a handoff tool is. A tool that runs is refused, because it would skip the tool
/// chain. A chat client factory is refused, because it can replace the model client and its rings.
/// </para>
/// <para>
/// A run may also turn parallel tool calls off (<see cref="ChatOptions.AllowMultipleToolCalls"/> set to
/// <see langword="false"/>), which a handoff step does so that it hands off to one agent. It can only
/// narrow what the definition allowed; turning them on is refused like any other member the definition owns.
/// </para>
/// </remarks>
internal static class RunChatOptionsRule
{
    /// <summary>Refuses run options that change what the definition owns.</summary>
    /// <param name="options">The options a run was given.</param>
    /// <exception cref="ArgumentException">The options set a member the definition owns.</exception>
    public static void Check(AgentRunOptions? options)
    {
        if (options is not ChatClientAgentRunOptions run)
        {
            return;
        }

        var problems = new List<string>();

        if (run.ChatClientFactory is not null)
        {
            problems.Add($"{nameof(ChatClientAgentRunOptions)}.{nameof(ChatClientAgentRunOptions.ChatClientFactory)}");
        }

        if (run.ChatOptions is { } chat)
        {
            problems.AddRange(AgentOptionsGuard.Chat
                .SetProtectedMembers(chat, nameof(ChatOptions.Tools), nameof(ChatOptions.Instructions), nameof(ChatOptions.AllowMultipleToolCalls))
                .Select(static member => $"{nameof(ChatOptions)}.{member}"));

            // A handoff step turns parallel tool calls off so that it hands off to one agent. Narrowing is
            // the only change the definition's setting can take from a run.
            if (chat.AllowMultipleToolCalls is true)
            {
                problems.Add($"{nameof(ChatOptions)}.{nameof(ChatOptions.AllowMultipleToolCalls)}");
            }

            // An AIFunction runs in the loop; a hosted tool runs at the provider. Only a bare
            // declaration comes back to the caller unrun.
            if (chat.Tools?.Any(static tool => tool is AIFunction or not AIFunctionDeclaration) == true)
            {
                problems.Add($"{nameof(ChatOptions)}.{nameof(ChatOptions.Tools)} (a tool that runs)");
            }
        }

        if (problems.Count > 0)
        {
            throw new ArgumentException(
                $"The run options set {string.Join(", ", problems)}. The agent definition owns these: a run may add " +
                "instructions and tool declarations, and turn parallel tool calls off. Change the definition, or " +
                "register a tool in code and name it in the definition.",
                nameof(options));
        }
    }
}
