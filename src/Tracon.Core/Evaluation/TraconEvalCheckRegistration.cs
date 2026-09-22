using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// A custom check registration added through <c>ITraconBuilder.AddEvalCheck(...)</c>.
/// </summary>
/// <param name="Kind">The check type name in <see cref="EvalSuite.Checks"/>.</param>
/// <param name="Check">A code-defined check that does not call a model.</param>
internal sealed record TraconEvalCheckRegistration(string Kind, EvalCheck Check);
