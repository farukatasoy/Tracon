namespace Tracon;

/// <summary>
/// Response for <c>GET {prefix}/api/loop-criteria</c>: the loop criterion kinds a
/// definition's <c>harness.loop.criteria</c> can name, and the judge model an
/// <c>aiJudge</c> criterion calls.
/// </summary>
/// <remarks>
/// The list is what the host registered; it is the same for every tenant. Whether
/// a tenant's egress policy allows the judge's provider is not part of it: the
/// validation report (<c>POST /api/agents/validate</c>) answers that for the
/// calling tenant.
/// </remarks>
public sealed record LoopCriterionCatalogResponse
{
    /// <summary>
    /// Gets the kinds: the built-in kinds first, in a fixed order, then the kinds
    /// registered in code with <c>AddLoopEvaluator</c>, ordered by name.
    /// </summary>
    public required IReadOnlyList<LoopCriterionKindResponse> Kinds { get; init; }

    /// <summary>
    /// Gets the model an <c>aiJudge</c> criterion calls, or <see langword="null"/>
    /// when the host bound none (<c>AddModelRunJudge</c>). With
    /// <see langword="null"/>, a definition with an <c>aiJudge</c> criterion does
    /// not compile; it never falls back to the agent's own model.
    /// </summary>
    public LoopJudgeModelResponse? JudgeModel { get; init; }
}

/// <summary>One loop criterion kind in <see cref="LoopCriterionCatalogResponse"/>.</summary>
public sealed record LoopCriterionKindResponse
{
    /// <summary>Gets the name a definition writes in <c>LoopCriterion.Kind</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Gets whether the kind is built in. A kind that is not built in was
    /// registered in code and reads no field of the criterion except its kind.
    /// </summary>
    public required bool BuiltIn { get; init; }

    /// <summary>
    /// Gets what the kind checks. A built-in kind always carries Tracon's own
    /// English sentence. A kind registered in code carries the text its
    /// registration gave, in the host's language, or <see langword="null"/>
    /// when it gave none.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// The judge model in <see cref="LoopCriterionCatalogResponse"/>: the provider and
/// model names only. The binding's provider settings are never returned.
/// </summary>
public sealed record LoopJudgeModelResponse
{
    /// <summary>Gets the provider name.</summary>
    public required string Provider { get; init; }

    /// <summary>Gets the model name.</summary>
    public required string Model { get; init; }
}
