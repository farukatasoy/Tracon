using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// <c>IAgentPrismBuilder.AddEvalCheck(...)</c> ile eklenen ozel bir denetim kaydi.
/// </summary>
/// <param name="Kind">Denetimin <see cref="EvalSuite.Checks"/> icindeki tur adi.</param>
/// <param name="Check">Model cagirmayan, kod ile yazilmis denetim.</param>
public sealed record AgentPrismEvalCheckRegistration(string Kind, EvalCheck Check);
