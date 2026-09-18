using System.Text.Json;

namespace Tracon;

/// <summary>The result of a single <see cref="EvalCase"/> within an <see cref="EvalRun"/>.</summary>
/// <remarks>
/// <see cref="CaseId"/> deliberately carries no foreign key: even if a case is
/// later changed or deleted, the past result record stays intelligible
/// (append-only spirit, same rationale).
/// </remarks>
public sealed record EvalCaseResult
{
    /// <summary>The result record identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The identifier of the run this result belongs to.</summary>
    public required Guid EvalRunId { get; init; }

    /// <summary>The identifier of the case being measured.</summary>
    public required Guid CaseId { get; init; }

    /// <summary>
    /// The identifier of the run record created while processing this case.
    /// This lets an eval failure jump straight to its transcript and span tree.
    /// </summary>
    public Guid? RunId { get; init; }

    /// <summary>Reports whether the case passed all of its checks.</summary>
    public required bool Passed { get; init; }

    /// <summary>The text output produced by the agent.</summary>
    public string? Output { get; init; }

    /// <summary>Per-check score list (free-form JSON).</summary>
    /// <remarks>
    /// An unset value is stored as the empty JSON array, never as
    /// <see cref="JsonValueKind.Undefined"/> - see <see cref="FreeFormJson"/>.
    /// </remarks>
    public JsonElement Scores
    {
        get;
        init => field = FreeFormJson.OrEmpty(value);
    }

    /// <summary>The failure reason. Populated only when <c>Passed</c> is <see langword="false"/>.</summary>
    public string? FailureReason { get; init; }
}
