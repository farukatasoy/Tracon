using System.ComponentModel;

namespace Tracon;

/// <remarks>
/// The list is what the host registered; it is the same for every tenant. Whether
/// a tenant's egress policy allows the judge's provider is not part of it: the
/// validation report (<c>POST /api/agents/validate</c>) answers that for the
/// calling tenant.
/// </remarks>
[Description(
    "Response for `GET {prefix}/api/loop-criteria`: the loop criterion kinds a\n" +
    "definition's `harness.loop.criteria` can name, and the judge model an\n" +
    "`aiJudge` criterion calls.")]
internal sealed record LoopCriterionCatalogResponse
{
    [Description(
        "Gets the kinds: the built-in kinds first, in a fixed order, then the kinds\n" +
        "registered in code with `AddLoopEvaluator`, ordered by name.")]
    public required IReadOnlyList<LoopCriterionKindResponse> Kinds { get; init; }

    [Description(
        "Gets the model an `aiJudge` criterion calls, or `null`\n" +
        "when the host bound none (`AddModelRunJudge`). With\n" +
        "`null`, a definition with an `aiJudge` criterion does\n" +
        "not compile; it never falls back to the agent's own model.")]
    public LoopJudgeModelResponse? JudgeModel { get; init; }
}

[Description("One loop criterion kind in LoopCriterionCatalogResponse.")]
internal sealed record LoopCriterionKindResponse
{
    [Description("Gets the name a definition writes in `LoopCriterion.Kind`.")]
    public required string Kind { get; init; }

    [Description(
        "Gets whether the kind is built in. A kind that is not built in was\n" +
        "registered in code and reads no field of the criterion except its kind.")]
    public required bool BuiltIn { get; init; }

    [Description(
        "Gets what the kind checks. A built-in kind always carries Tracon's own\n" +
        "English sentence. A kind registered in code carries the text its\n" +
        "registration gave, in the host's language, or `null`\n" +
        "when it gave none.")]
    public string? Description { get; init; }
}

[Description(
    "The judge model in LoopCriterionCatalogResponse: the provider and\n" +
    "model names only. The binding's provider settings are never returned.")]
internal sealed record LoopJudgeModelResponse
{
    [Description("Gets the provider name.")]
    public required string Provider { get; init; }

    [Description("Gets the model name.")]
    public required string Model { get; init; }
}
