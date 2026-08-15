using System.Text.Json;

namespace AgentPrism;

/// <summary>The result of a single <see cref="EvalCase"/> within an <see cref="EvalRun"/>.</summary>
/// <remarks>
/// <see cref="CaseId"/> deliberately carries no foreign key: even if a case is
/// later changed or deleted, the past result record stays intelligible
/// (append-only spirit, same rationale as K-014).
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
    public JsonElement Scores { get; init; }

    /// <summary>The failure reason. Populated only when <see cref="Passed"/> is <see langword="false"/>.</summary>
    public string? FailureReason { get; init; }
}
