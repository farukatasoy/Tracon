using System.Globalization;

namespace Tracon;

/// <summary>
/// Aligns two eval runs' per-case results into an <see cref="EvalRunDiff"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole comparison policy — the bucket rules, the repetition rule,
/// the ordering and the three refusals — in one place, so that every
/// <see cref="IEvalStore"/> implementation shares it instead of rewriting it.
/// A store's <see cref="IEvalStore.DiffRunsAsync"/> reads the two runs and
/// their case results and hands them here.
/// </para>
/// </remarks>
public static class EvalRunDiffBuilder
{
    /// <summary>Aligns two completed runs of the same suite, case by case.</summary>
    /// <param name="baseline">The run being compared against.</param>
    /// <param name="candidate">The run being judged.</param>
    /// <param name="baselineResults">The baseline run's per-case results.</param>
    /// <param name="candidateResults">The candidate run's per-case results.</param>
    /// <param name="skip">The number of aligned cases to skip.</param>
    /// <param name="take">The maximum number of aligned cases to return.</param>
    /// <returns>The difference between the two runs.</returns>
    /// <exception cref="EvalRunDiffUnavailableException">
    /// The two runs measure different suites, one of them has not completed, or
    /// one of them holds results for fewer cases than its summary counts - the
    /// rest were removed by retention, which deletes in batches and can leave a
    /// run partly trimmed. Never answered with a shortened diff: reporting only
    /// the surviving cases reads as "nothing changed" for the rest.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A case that carries several result rows within one run — the suite ran
    /// with repetitions, or the job was redelivered and recorded its results a
    /// second time (<see cref="IJobHandler"/> is at-least-once) — counts as
    /// passed only when <strong>every</strong> one of its rows passed. That is
    /// the same rule <see cref="EvalRun.Passed"/> itself uses for repetitions,
    /// and it fails safe for the redelivery case: a stale failing row can show
    /// a case as broken when it is not, but it can never hide a break.
    /// </para>
    /// </remarks>
    public static EvalRunDiff Build(
        EvalRun baseline,
        EvalRun candidate,
        IReadOnlyList<EvalCaseResult> baselineResults,
        IReadOnlyList<EvalCaseResult> candidateResults,
        int skip = 0,
        int take = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(baselineResults);
        ArgumentNullException.ThrowIfNull(candidateResults);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegative(take);

        if (baseline.SuiteId != candidate.SuiteId)
        {
            throw new EvalRunDiffUnavailableException(
                EvalRunDiffUnavailableReason.DifferentSuites,
                FormattableString.Invariant(
                    $"The baseline run measures suite {baseline.SuiteId} and the candidate run measures suite {candidate.SuiteId}; runs of different suites do not align."));
        }

        RequireCompleted(baseline, "baseline");
        RequireCompleted(candidate, "candidate");

        var baselineOutcomes = Reduce(baselineResults);
        var candidateOutcomes = Reduce(candidateResults);

        RequireDetails(baseline, baselineOutcomes.Count, "baseline");
        RequireDetails(candidate, candidateOutcomes.Count, "candidate");

        var entries = new List<EvalCaseDiff>(baselineOutcomes.Count + candidateOutcomes.Count);

        foreach (var (caseId, baselineOutcome) in baselineOutcomes)
        {
            if (candidateOutcomes.TryGetValue(caseId, out var candidateOutcome))
            {
                entries.Add(new EvalCaseDiff
                {
                    CaseId = caseId,
                    Kind = (baselineOutcome.Passed, candidateOutcome.Passed) switch
                    {
                        (true, true) => EvalCaseDiffKind.Unchanged,
                        (false, true) => EvalCaseDiffKind.Fixed,
                        (true, false) => EvalCaseDiffKind.Regressed,
                        (false, false) => EvalCaseDiffKind.StillFailing,
                    },
                    BaselinePassed = baselineOutcome.Passed,
                    CandidatePassed = candidateOutcome.Passed,
                    BaselineRunId = baselineOutcome.RunId,
                    CandidateRunId = candidateOutcome.RunId,
                    BaselineFailureReason = baselineOutcome.FailureReason,
                    CandidateFailureReason = candidateOutcome.FailureReason,
                });

                continue;
            }

            entries.Add(new EvalCaseDiff
            {
                CaseId = caseId,
                Kind = EvalCaseDiffKind.Removed,
                BaselinePassed = baselineOutcome.Passed,
                CandidatePassed = null,
                BaselineRunId = baselineOutcome.RunId,
                BaselineFailureReason = baselineOutcome.FailureReason,
            });
        }

        foreach (var (caseId, candidateOutcome) in candidateOutcomes)
        {
            if (baselineOutcomes.ContainsKey(caseId))
            {
                continue;
            }

            entries.Add(new EvalCaseDiff
            {
                CaseId = caseId,
                Kind = EvalCaseDiffKind.Added,
                BaselinePassed = null,
                CandidatePassed = candidateOutcome.Passed,
                CandidateRunId = candidateOutcome.RunId,
                CandidateFailureReason = candidateOutcome.FailureReason,
            });
        }

        entries.Sort(static (left, right) =>
        {
            var byKind = BucketOrder(left.Kind).CompareTo(BucketOrder(right.Kind));
            return byKind != 0 ? byKind : left.CaseId.CompareTo(right.CaseId);
        });

        return new EvalRunDiff
        {
            Baseline = baseline,
            Candidate = candidate,
            Cases = [.. entries.Skip(skip).Take(take)],
            TotalCases = entries.Count,
            UnchangedCount = Count(entries, EvalCaseDiffKind.Unchanged),
            FixedCount = Count(entries, EvalCaseDiffKind.Fixed),
            RegressedCount = Count(entries, EvalCaseDiffKind.Regressed),
            StillFailingCount = Count(entries, EvalCaseDiffKind.StillFailing),
            AddedCount = Count(entries, EvalCaseDiffKind.Added),
            RemovedCount = Count(entries, EvalCaseDiffKind.Removed),
        };
    }

    private static void RequireCompleted(EvalRun run, string side)
    {
        if (run.Status == EvalRunStatus.Completed)
        {
            return;
        }

        throw new EvalRunDiffUnavailableException(
            EvalRunDiffUnavailableReason.RunNotCompleted,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The {side} run {run.Id} is {run.Status}, not Completed; an unfinished run cannot be compared."));
    }

    private static void RequireDetails(EvalRun run, int caseCount, string side)
    {
        // 🚨 Counted, not merely checked for emptiness. The eval_case_results
        // retention target deletes in BATCHES (IRetentionStore.DeleteBatchAsync)
        // and the sweep can stop between them, so a run can be left holding
        // SOME of its rows. Comparing what survives reports every deleted case
        // as Removed and quietly narrows the regression count to the rows that
        // happen to remain - which is the "nothing changed" failure mode this
        // whole type exists to prevent, only harder to see.
        if (caseCount >= run.Total)
        {
            return;
        }

        throw new EvalRunDiffUnavailableException(
            EvalRunDiffUnavailableReason.DetailsRemoved,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The {side} run {run.Id} summarises {run.Total} case(s) but keeps results for only {caseCount}; the missing ones cannot be compared, so no diff is produced."));
    }

    private static Dictionary<Guid, CaseOutcome> Reduce(IReadOnlyList<EvalCaseResult> results)
    {
        var outcomes = new Dictionary<Guid, CaseOutcome>();

        foreach (var result in results)
        {
            if (!outcomes.TryGetValue(result.CaseId, out var existing))
            {
                outcomes[result.CaseId] = new CaseOutcome(result.Passed, result.RunId, result.FailureReason);
                continue;
            }

            // Already failing: the first failure is the one worth reporting, so
            // later rows never overwrite its reason.
            if (!existing.Passed)
            {
                continue;
            }

            outcomes[result.CaseId] = result.Passed
                ? new CaseOutcome(true, result.RunId, null)
                : new CaseOutcome(false, result.RunId, result.FailureReason);
        }

        return outcomes;
    }

    private static int Count(List<EvalCaseDiff> entries, EvalCaseDiffKind kind)
    {
        var count = 0;

        foreach (var entry in entries)
        {
            if (entry.Kind == kind)
            {
                count++;
            }
        }

        return count;
    }

    private static int BucketOrder(EvalCaseDiffKind kind) => kind switch
    {
        EvalCaseDiffKind.Regressed => 0,
        EvalCaseDiffKind.StillFailing => 1,
        EvalCaseDiffKind.Added => 2,
        EvalCaseDiffKind.Fixed => 3,
        EvalCaseDiffKind.Removed => 4,
        _ => 5,
    };

    private readonly record struct CaseOutcome(bool Passed, Guid? RunId, string? FailureReason);
}
