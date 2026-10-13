using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// A code-defined loop stop criterion registered through
/// <c>ITraconBuilder.AddLoopEvaluator(...)</c>.
/// </summary>
/// <param name="Kind">
/// The criterion name a definition references in <see cref="LoopCriterion.Kind"/>.
/// </param>
/// <param name="Factory">
/// Creates the criterion from the application's root service provider. A stop
/// criterion written in code is the only way to express one that is not pure
/// data, the same boundary tools and eval checks live behind. The registry calls
/// it on the first compilation that names <paramref name="Kind"/>, not at setup
/// time: a criterion that needs the agent catalog would otherwise close the
/// catalog → compiler → catalog dependency cycle.
/// </param>
/// <param name="Description">
/// The text the console shows next to the kind, or <see langword="null"/>.
/// </param>
// MAAI001: Microsoft.Agents.AI.LoopEvaluator is marked "evaluation purposes
// only". The suppression is deliberate and covers only this declaration, so a
// change in MAF's loop API lands in a known, small set of files. Rationale:
// docs/KARARLAR.md, decision K-020.
#pragma warning disable MAAI001
internal sealed record TraconLoopEvaluatorRegistration(
    string Kind,
    Func<IServiceProvider, LoopEvaluator> Factory,
    string? Description)
{
    /// <summary>Registers a ready instance: the factory returns it unchanged.</summary>
    /// <param name="kind">The criterion name.</param>
    /// <param name="evaluator">The criterion.</param>
    /// <param name="description">The text the console shows next to the kind.</param>
    public TraconLoopEvaluatorRegistration(string kind, LoopEvaluator evaluator, string? description = null)
        : this(kind, _ => evaluator, description)
    {
    }
}
#pragma warning restore MAAI001
