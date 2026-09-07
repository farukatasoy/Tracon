using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// The case-by-case difference between two completed <see cref="EvalRun"/>
/// records of the same suite.
/// </summary>
/// <remarks>
/// <para>
/// A single "how many broke" number is not enough to act on: the engineer on
/// call wants to know <em>which</em> case broke. Every aligned case therefore
/// lands in exactly one <see cref="EvalCaseDiffKind"/> bucket, and each entry
/// carries both sides' agent run identifier, so a regression is one click away
/// from the two conversations that produced it.
/// </para>
/// <para>
/// <strong>Cases are aligned by <see cref="EvalCaseResult.CaseId"/>, and case
/// content is not snapshotted per run.</strong> If a case's
/// <see cref="EvalCase.Query"/> is edited between the two runs while its
/// identifier is kept, the two sides describe different questions and the diff
/// silently compares them as one. Through the HTTP surface this cannot happen
/// (<c>PUT /api/evals/{name}/cases</c> replaces cases and assigns fresh
/// identifiers, so an edit shows up as a <see cref="EvalCaseDiffKind.Removed"/>
/// plus an <see cref="EvalCaseDiffKind.Added"/> entry); it can only happen when
/// a consumer calls <see cref="IEvalStore.ReplaceCasesAsync"/> directly and
/// preserves identifiers. Nothing here detects that, and no guarantee is
/// offered against it.
/// </para>
/// <para>
/// The counters are for the whole diff and do <strong>not</strong> shrink with
/// paging; <see cref="Cases"/> is the requested page.
/// </para>
/// </remarks>
public sealed record EvalRunDiff
{
    /// <summary>The run being compared against (the older, known-good side).</summary>
    public required EvalRun Baseline { get; init; }

    /// <summary>The run being judged.</summary>
    public required EvalRun Candidate { get; init; }

    /// <summary>
    /// The requested page of aligned cases, ordered so that the buckets an
    /// engineer looks for come first: <see cref="EvalCaseDiffKind.Regressed"/>,
    /// <see cref="EvalCaseDiffKind.StillFailing"/>,
    /// <see cref="EvalCaseDiffKind.Added"/>, <see cref="EvalCaseDiffKind.Fixed"/>,
    /// <see cref="EvalCaseDiffKind.Removed"/>, then
    /// <see cref="EvalCaseDiffKind.Unchanged"/>; within a bucket by case
    /// identifier.
    /// </summary>
    public required IReadOnlyList<EvalCaseDiff> Cases { get; init; }

    /// <summary>The number of aligned cases across both runs, ignoring paging.</summary>
    public required int TotalCases { get; init; }

    /// <summary>The number of cases that passed on both sides.</summary>
    public required int UnchangedCount { get; init; }

    /// <summary>The number of cases that failed on the baseline and pass now.</summary>
    public required int FixedCount { get; init; }

    /// <summary>The number of cases that passed on the baseline and fail now.</summary>
    public required int RegressedCount { get; init; }

    /// <summary>The number of cases that failed on both sides.</summary>
    public required int StillFailingCount { get; init; }

    /// <summary>The number of cases present only in the candidate run.</summary>
    public required int AddedCount { get; init; }

    /// <summary>The number of cases present only in the baseline run.</summary>
    public required int RemovedCount { get; init; }
}

/// <summary>One case's outcome on both sides of an <see cref="EvalRunDiff"/>.</summary>
public sealed record EvalCaseDiff
{
    /// <summary>The identifier of the case being compared.</summary>
    public required Guid CaseId { get; init; }

    /// <summary>Which bucket this case falls into.</summary>
    public required EvalCaseDiffKind Kind { get; init; }

    /// <summary>
    /// Whether the case passed on the baseline side.
    /// <see langword="null"/> when the case is <see cref="EvalCaseDiffKind.Added"/>.
    /// </summary>
    public bool? BaselinePassed { get; init; }

    /// <summary>
    /// Whether the case passed on the candidate side.
    /// <see langword="null"/> when the case is <see cref="EvalCaseDiffKind.Removed"/>.
    /// </summary>
    public bool? CandidatePassed { get; init; }

    /// <summary>
    /// The identifier of the agent run the baseline result came from, so a
    /// regression can be traced to the exact conversation.
    /// </summary>
    public Guid? BaselineRunId { get; init; }

    /// <summary>The identifier of the agent run the candidate result came from.</summary>
    public Guid? CandidateRunId { get; init; }

    /// <summary>The baseline side's failure reason, when it failed.</summary>
    public string? BaselineFailureReason { get; init; }

    /// <summary>The candidate side's failure reason, when it failed.</summary>
    public string? CandidateFailureReason { get; init; }
}

/// <summary>The bucket an aligned case falls into within an <see cref="EvalRunDiff"/>.</summary>
/// <remarks>
/// <see cref="Added"/> and <see cref="Removed"/> are deliberately separate from
/// the four outcome buckets and are never folded into them: adding a case to a
/// suite moves the pass rate without anything having regressed, and a gate that
/// cannot tell those apart raises a false alarm. Written as a name in JSON;
/// the value order <strong>must not change</strong> — only append.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<EvalCaseDiffKind>))]
public enum EvalCaseDiffKind
{
    /// <summary>Passed on both sides.</summary>
    Unchanged = 0,

    /// <summary>Failed on the baseline, passes on the candidate.</summary>
    Fixed = 1,

    /// <summary>Passed on the baseline, fails on the candidate.</summary>
    Regressed = 2,

    /// <summary>Failed on both sides.</summary>
    StillFailing = 3,

    /// <summary>Present only in the candidate run.</summary>
    Added = 4,

    /// <summary>Present only in the baseline run.</summary>
    Removed = 5,
}
