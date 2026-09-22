namespace Tracon;

/// <summary>
/// A code-defined approval policy registered through
/// <c>ITraconBuilder.AddToolApprovalPolicy(...)</c>.
/// </summary>
/// <param name="ToolName">The tool the policy judges.</param>
/// <param name="Policy">The policy delegate. Runs before the data rules and can override them.</param>
internal sealed record ToolApprovalPolicyRegistration(string ToolName, Func<ToolApprovalContext, ToolApprovalPolicyDecision> Policy);
