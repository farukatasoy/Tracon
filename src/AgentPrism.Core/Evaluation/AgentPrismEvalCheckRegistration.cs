using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// A custom check registration added through <c>IAgentPrismBuilder.AddEvalCheck(...)</c>.
/// </summary>
/// <param name="Kind">The check type name in <see cref="EvalSuite.Checks"/>.</param>
/// <param name="Check">A code-defined check that does not call a model.</param>
public sealed record AgentPrismEvalCheckRegistration(string Kind, EvalCheck Check);
