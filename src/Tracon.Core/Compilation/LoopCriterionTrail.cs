namespace Tracon;

/// <summary>
/// What one loop criterion reports besides its decision: the model that judged and the
/// agent that reviewed. The run's audit trail carries it (<see cref="RunEventType.LoopIterationCompleted"/>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RecordingLoopEvaluator"/> opens one for each criterion it evaluates and puts it on
/// a copy of the run's scope (<see cref="AgentRunScope.CriterionTrail"/>) for the length of that
/// call, then reads it back. A write that a criterion makes to the ambient scope does not flow back
/// to its caller, so the holder is the only way a judge's chat client or a reviewer can hand its
/// identity to the evaluator that called it.
/// </para>
/// <para>
/// It holds identities only: a provider and a model name, an agent name and version, a run id.
/// Never a verdict text, a draft or a feedback.
/// </para>
/// </remarks>
internal sealed class LoopCriterionTrail
{
    /// <summary>Gets the provider and model that answered the judge call, or <see langword="null"/> when none did.</summary>
    public (string Provider, string Model)? Judge { get; private set; }

    /// <summary>Gets the reviewer that was asked, or <see langword="null"/> when no reviewer was.</summary>
    public LoopReviewerTrail? Reviewer { get; private set; }

    /// <summary>Records the model that answered an <c>aiJudge</c> call.</summary>
    /// <param name="provider">The provider that answered. A fallback link when one stood in.</param>
    /// <param name="model">The model that answered.</param>
    public void RecordJudge(string provider, string model) => Judge = (provider, model);

    /// <summary>Records the reviewer an <c>agentReview</c> criterion asked.</summary>
    /// <param name="reviewer">The reviewer.</param>
    public void RecordReviewer(LoopReviewerTrail reviewer)
    {
        ArgumentNullException.ThrowIfNull(reviewer);

        Reviewer = reviewer;
    }
}

/// <summary>The reviewer an <c>agentReview</c> criterion asked.</summary>
/// <param name="AgentName">The reviewer agent's name.</param>
/// <param name="AgentVersion">The version the reviewer's run recorded, or <see langword="null"/> when the run never started.</param>
/// <param name="RunId">The reviewer's child run, or <see langword="null"/> when the call was refused before the run started.</param>
internal sealed record LoopReviewerTrail(string AgentName, int? AgentVersion, Guid? RunId);
