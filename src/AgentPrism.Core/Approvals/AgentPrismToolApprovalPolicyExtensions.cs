using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>Chain extension that registers a code-defined approval policy for a tool.</summary>
/// <remarks>
/// This is an extension method, not a member of <see cref="IAgentPrismBuilder"/>:
/// adding a member to the interface is a breaking change after release; adding
/// an extension method is not.
/// </remarks>
public static class AgentPrismToolApprovalPolicyExtensions
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
    /// silently release a tool from approval.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="toolName"/> is empty or whitespace.</exception>
    /// <remarks>
    /// <para>
    /// The policy's dependencies must be captured at REGISTRATION time (the
    /// delegate's closure), not resolved from a service provider inside the
    /// delegate — the service provider a tool call sees is empty.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .AddToolApprovalPolicy("refund_order", context =>
    ///            context.GetNumber("amount") is { } amount &amp;&amp; amount &lt;= 100
    ///                ? ToolApprovalPolicyDecision.NotRequired
    ///                : ToolApprovalPolicyDecision.Required);
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder AddToolApprovalPolicy(
        this IAgentPrismBuilder builder,
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
