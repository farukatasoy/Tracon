namespace Tracon;

/// <summary>
/// Decides which interrupted runs automatic continuation may pick up. The one rule
/// both <see cref="RunReconciliationService"/> and <see cref="RunContinuationJobHandler"/>
/// apply.
/// </summary>
/// <remarks>
/// <para>
/// Continuation re-sends an agent turn's recorded input to its agent, in the run's
/// session. Only a <strong>root agent run</strong> is such a turn:
/// </para>
/// <list type="bullet">
/// <item>
/// A workflow run records its parameter values and no message, and it resumes from its
/// checkpoints. Treated as an agent turn, it opened a continuation under the workflow's
/// name that could only fail - or, with an agent of the same name, ran that agent in the
/// workflow's session.
/// </item>
/// <item>
/// A sub-run (a workflow step, a callable sub-agent) carries its root's session id and
/// nobody waits for its answer. Continued on its own, it repeated its tools and wrote
/// its turn into the root's session.
/// </item>
/// </list>
/// <para>
/// The reconciler checks this before it queues a continuation; the handler checks it
/// again, for a job queued before this rule existed or by hand.
/// </para>
/// </remarks>
internal static class RunContinuationEligibility
{
    /// <summary>Returns why a run is not continued.</summary>
    /// <param name="record">The interrupted run.</param>
    /// <returns>
    /// The reason, as a clause that follows "Automatic continuation was refused: ";
    /// <see langword="null"/> when the run is a root agent turn.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is <see langword="null"/>.</exception>
    public static string? Refusal(RunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.Kind == RunKind.Workflow)
        {
            return $"run '{record.Id}' is a workflow run, and continuation replays an agent turn, not a " +
                   "workflow graph. Resume it from its latest checkpoint with " +
                   $"POST /api/workflows/runs/{record.Id}/resume (GET /api/workflows/runs/{record.Id}/checkpoints " +
                   "lists the checkpoints it wrote).";
        }

        // Either field marks a sub-run: a store that dropped one of them must not
        // turn a workflow step into an agent turn of its own.
        if (record.ParentRunId is not null || record.Depth > 0)
        {
            var root = record.RootRunId ?? record.ParentRunId;

            return $"run '{record.Id}' is a sub-run" + (root is { } rootRunId ? $" of run '{rootRunId}'" : string.Empty) +
                   ". Only the run at the root of a tree is continued or resumed: on its own, a sub-run would run " +
                   "in its root's session with nobody waiting for its answer.";
        }

        return record.Kind == RunKind.Agent
            ? null
            : $"run '{record.Id}' is a '{record.Kind}' run; only an agent turn is continued.";
    }
}
