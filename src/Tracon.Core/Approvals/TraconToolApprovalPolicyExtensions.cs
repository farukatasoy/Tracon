using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>Chain extension that registers a code-defined approval policy for a tool.</summary>
/// <remarks>
/// This is an extension method, not a member of <see cref="ITraconBuilder"/>:
/// adding a member to the interface is a breaking change after release; adding
/// an extension method is not.
/// </remarks>
public static class TraconToolApprovalPolicyExtensions
{
    /// <summary>
    /// Registers a code-defined approval policy for a tool.
    /// </summary>
    /// <param name="builder">The configuration chain.</param>
    /// <param name="toolName">The tool the policy judges.</param>
    /// <param name="policy">
    /// The policy delegate. Runs at every call to <paramref name="toolName"/>, before the
    /// data rules (<see cref="ToolApprovalRule"/>). <see cref="ToolApprovalPolicyDecision.Required"/>
    /// and <see cref="ToolApprovalPolicyDecision.NotRequired"/> override the data rules;
    /// <see cref="ToolApprovalPolicyDecision.Undecided"/> defers to them. An exception is treated
    /// as <see cref="ToolApprovalPolicyDecision.Required"/> and logged — a broken policy does not
    /// silently release a tool from approval. The policy decides for a tool registered without
    /// <c>RequiresApproval</c> as well: the tool is held for approval because it has a policy, a
    /// <see cref="ToolApprovalPolicyDecision.Required"/> answer makes the call wait, and
    /// <see cref="ToolApprovalPolicyDecision.NotRequired"/> and
    /// <see cref="ToolApprovalPolicyDecision.Undecided"/> let it run. Every call of such a tool passes
    /// through the approval step, so a tool with a policy costs one more round of the model loop than a
    /// tool without.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="toolName"/> is empty or whitespace.</exception>
    /// <remarks>
    /// <para>
    /// A tool with a policy is reported as asking for approval (<see cref="ToolDescriptor.RequiresApproval"/>), so an agent
    /// that carries it cannot be exposed over MCP or A2A and a live replay of its runs is refused. The host does not start
    /// when the name is a hosted tool's; a name no tool in code carries is allowed, because a tool of an MCP server or one
    /// a run brings with it can arrive after the host has started.
    /// </para>
    /// <para>
    /// A policy holds the tools that run through Tracon's tool chain: tools registered in code, tools of MCP servers and the
    /// tools Tracon adds to a run (the workspace, the knowledge search, the conversation memory search). It holds nothing
    /// for a client-side tool, which Tracon never runs, for the skill tools, or for an agent called as a tool.
    /// </para>
    /// <para>
    /// The policy's dependencies must be captured at REGISTRATION time (the
    /// delegate's closure), not resolved from a service provider inside the
    /// delegate — the service provider a tool call sees is empty.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddToolApprovalPolicy("refund_order", context =>
    ///            context.GetNumber("amount") is { } amount &amp;&amp; amount &lt;= 100
    ///                ? ToolApprovalPolicyDecision.NotRequired
    ///                : ToolApprovalPolicyDecision.Required);
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddToolApprovalPolicy(
        this ITraconBuilder builder,
        string toolName,
        Func<ToolApprovalContext, ToolApprovalPolicyDecision> policy)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(policy);

        builder.Services.AddSingleton(new ToolApprovalPolicyRegistration(toolName, policy));

        return builder;
    }
}
