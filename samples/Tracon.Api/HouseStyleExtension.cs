using Microsoft.Agents.AI;
using Tracon;

namespace Tracon.Api;

// MAAI001: Microsoft Agent Framework marks the harness options "for evaluation
// purposes only"; an extension that reaches them names the type.
#pragma warning disable MAAI001

/// <summary>
/// An agent extension: a context provider that adds a house-style rule to every model call of
/// the agents that select it (Phase 222, F-588).
/// </summary>
/// <remarks>
/// <para>
/// The code is registered once with <c>AddAgentExtension&lt;HouseStyleExtension&gt;()</c>.
/// An agent selects it by name: a definition written in code lists it in
/// <c>ExtensionNames</c>, and the console offers it in the agent editor. The agent still goes
/// through the definition compiler, so it keeps the model ring, the tenant's provider
/// credential, egress, and cost.
/// </para>
/// <para>
/// The extension adds its provider after the ones Tracon built. It does not touch a member the
/// definition owns: the compiler would refuse the compilation and name this extension.
/// </para>
/// </remarks>
internal sealed class HouseStyleExtension : IAgentExtension
{
    /// <summary>The name a definition writes in <c>ExtensionNames</c>.</summary>
    public const string ExtensionName = "house-style";

    /// <summary>The rule the provider adds. The local echo model prints it back, so a run shows it.</summary>
    public const string Rule = "HOUSE-STYLE: answer in two short sentences.";

    /// <inheritdoc />
    public string Name => ExtensionName;

    /// <inheritdoc />
    public string? Description => "Adds the house style rule to the instructions of every model call.";

    /// <inheritdoc />
    public void Configure(AgentExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var provider = new HouseStyleProvider();

        if (context.ChatAgentOptions is { } chat)
        {
            chat.AIContextProviders = [.. chat.AIContextProviders ?? [], provider];
        }
        else if (context.HarnessAgentOptions is { } harness)
        {
            harness.AIContextProviders = [.. harness.AIContextProviders ?? [], provider];
        }
    }

    private sealed class HouseStyleProvider : AIContextProvider
    {
        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
            => new(new AIContext { Instructions = Rule });
    }
}
#pragma warning restore MAAI001
