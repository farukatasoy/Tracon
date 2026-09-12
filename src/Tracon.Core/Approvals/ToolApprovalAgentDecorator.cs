using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// Wraps every agent resolved from the catalog with Microsoft Agent Framework's
/// <see cref="ToolApprovalAgent"/> decorator and supplies persistent "do not ask again"
/// rules as automatic approval rules.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Order"/> is 20, which makes this the <em>innermost</em> of three decorators.
/// Approval must occur at the layer closest to the model call. If it occurred outside,
/// telemetry and run recording would include approval waiting time in their duration.
/// </para>
/// <para>
/// This class does <strong>not</strong> decide which tool needs approval.
/// <see cref="ToolRegistry"/> makes that decision. It wraps an approval-required tool
/// in <c>ApprovalRequiredAIFunction</c>, causing MAF to produce
/// <c>ToolApprovalRequestContent</c> instead of running the call. This class only
/// applies <em>automatic approval</em> rules.
/// </para>
/// </remarks>
internal sealed class ToolApprovalAgentDecorator : IAgentDecorator
{
    private readonly ToolApprovalRuleEvaluator _evaluator;

    /// <summary>Initializes a new approval decorator.</summary>
    /// <param name="evaluator">The evaluator that applies persistent rules.</param>
    /// <exception cref="ArgumentNullException"><paramref name="evaluator"/> is <see langword="null"/>.</exception>
    public ToolApprovalAgentDecorator(ToolApprovalRuleEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        _evaluator = evaluator;
    }

    /// <inheritdoc />
    public int Order => 20;

    /// <inheritdoc />
    public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(descriptor);

        var agentName = descriptor.Name;

        var options = new ToolApprovalAgentOptions
        {
            AutoApprovalRules =
            [
                context => _evaluator.IsAutoApprovedAsync(agentName, context.FunctionCallContent),
            ],
        };

        return new ToolApprovalAgent(agent, options);
    }
}
