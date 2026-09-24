using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// A code-defined loop stop criterion registered through
/// <c>ITraconBuilder.AddLoopEvaluator(...)</c>.
/// </summary>
/// <param name="Kind">
/// The criterion name a definition references in <see cref="LoopCriterion.Kind"/>.
/// </param>
/// <param name="Evaluator">
/// The criterion itself. A stop criterion written in code is the only way to
/// express one that is not pure data, the same boundary tools and eval checks
/// live behind.
/// </param>
// MAAI001: Microsoft.Agents.AI.LoopEvaluator is marked "evaluation purposes
// only". The suppression is deliberate and covers only this declaration, so a
// change in MAF's loop API lands in a known, small set of files. Rationale:
// docs/KARARLAR.md, decision K-020.
#pragma warning disable MAAI001
internal sealed record TraconLoopEvaluatorRegistration(string Kind, LoopEvaluator Evaluator);
#pragma warning restore MAAI001
