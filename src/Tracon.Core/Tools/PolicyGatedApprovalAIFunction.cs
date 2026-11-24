using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Marks a tool that waits for approval only because a code-defined policy is registered for it.
/// </summary>
/// <remarks>
/// <para>
/// A tool registered without <c>RequiresApproval</c> never asks a person, so a policy that says
/// <see cref="ToolApprovalPolicyDecision.Required"/> for it would change nothing. <see cref="ToolWrapperChain"/> therefore wraps
/// such a tool in <see cref="ApprovalRequiredAIFunction"/> when its name has a policy, and puts this marker around that wrapper.
/// </para>
/// <para>
/// The marker exists for the approval rule evaluator, which has to tell this tool from a flagged one. A flagged tool whose
/// policy is <see cref="ToolApprovalPolicyDecision.Undecided"/> falls through to the data rules and asks when none matches. A
/// tool that carries no flag of its own has no reason to ask when the policy has no opinion, so its call runs. The evaluator
/// finds the marker through <see cref="AIFunctionDeclaration"/>'s service lookup, which every wrapper above this one forwards.
/// </para>
/// <para>
/// It changes neither the call nor the result. It sits outside <see cref="ApprovalRequiredAIFunction"/>, which is sealed and so
/// cannot carry the marker itself, and inside the timeout layer.
/// </para>
/// </remarks>
/// <param name="innerFunction">The <see cref="ApprovalRequiredAIFunction"/> it marks.</param>
internal sealed class PolicyGatedApprovalAIFunction(AIFunction innerFunction) : DelegatingAIFunction(innerFunction)
{
}
