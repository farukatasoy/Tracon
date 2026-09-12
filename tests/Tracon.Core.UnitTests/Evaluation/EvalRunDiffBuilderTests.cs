using System.Diagnostics;

namespace Tracon.Core.UnitTests.Evaluation;

public sealed class EvalRunDiffBuilderTests
{
    private static readonly Guid SuiteId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Four_outcome_buckets_are_separated()
    {
        var stillPassing = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var broke = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        var healed = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
        var stillBroken = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");

        var diff = EvalRunDiffBuilder.Build(
            Run(total: 4),
            Run(total: 4),
            [Result(stillPassing, true), Result(broke, true), Result(healed, false), Result(stillBroken, false)],
            [Result(stillPassing, true), Result(broke, false), Result(healed, true), Result(stillBroken, false)]);

        KindOf(diff, stillPassing).ShouldBe(EvalCaseDiffKind.Unchanged);
        KindOf(diff, broke).ShouldBe(EvalCaseDiffKind.Regressed);
        KindOf(diff, healed).ShouldBe(EvalCaseDiffKind.Fixed);
        KindOf(diff, stillBroken).ShouldBe(EvalCaseDiffKind.StillFailing);

        diff.UnchangedCount.ShouldBe(1);
        diff.RegressedCount.ShouldBe(1);
        diff.FixedCount.ShouldBe(1);
        diff.StillFailingCount.ShouldBe(1);
        diff.TotalCases.ShouldBe(4);
    }

    [Fact]
    public void A_case_added_to_the_suite_is_Added_and_never_Regressed()
    {
        var kept = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
        var fresh = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

        var diff = EvalRunDiffBuilder.Build(
            Run(total: 1),
            Run(total: 2),
            [Result(kept, true)],
            [Result(kept, true), Result(fresh, false)]);

        KindOf(diff, fresh).ShouldBe(EvalCaseDiffKind.Added);
        diff.AddedCount.ShouldBe(1);
        diff.RegressedCount.ShouldBe(0);
    }

    [Fact]
    public void A_case_removed_from_the_suite_is_Removed_and_never_Fixed()
    {
        var kept = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
        var dropped = Guid.Parse("cccccccc-0000-0000-0000-000000000002");

        var diff = EvalRunDiffBuilder.Build(
            Run(total: 2),
            Run(total: 1),
            [Result(kept, true), Result(dropped, false)],
            [Result(kept, true)]);

        KindOf(diff, dropped).ShouldBe(EvalCaseDiffKind.Removed);
        diff.RemovedCount.ShouldBe(1);
        diff.FixedCount.ShouldBe(0);
    }

    [Fact]
    public void Each_entry_carries_both_sides_run_id_and_failure_reason()
    {
        var caseId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
        var baselineRunId = Guid.Parse("dddddddd-0000-0000-0000-0000000000b1");
        var candidateRunId = Guid.Parse("dddddddd-0000-0000-0000-0000000000c1");

        var diff = EvalRunDiffBuilder.Build(
            Run(total: 1),
            Run(total: 1),
            [Result(caseId, true) with { RunId = baselineRunId }],
            [Result(caseId, false) with { RunId = candidateRunId, FailureReason = "containsExpected failed" }]);

        var entry = diff.Cases.Single();
        entry.Kind.ShouldBe(EvalCaseDiffKind.Regressed);
        entry.BaselineRunId.ShouldBe(baselineRunId);
        entry.CandidateRunId.ShouldBe(candidateRunId);
        entry.BaselinePassed.ShouldBe(true);
        entry.CandidatePassed.ShouldBe(false);
        entry.CandidateFailureReason.ShouldBe("containsExpected failed");
        entry.BaselineFailureReason.ShouldBeNull();
    }

    [Fact]
    public void A_case_with_several_rows_counts_as_passed_only_when_every_row_passed()
    {
        // Repetitions, or an at-least-once job redelivery (K-641): several rows
        // for one case within one run. The run summary itself counts the case
        // as passed only when all of them passed; the diff agrees.
        var caseId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

        var diff = EvalRunDiffBuilder.Build(
            Run(total: 1),
            Run(total: 1),
            [Result(caseId, true), Result(caseId, true)],
            [Result(caseId, true), Result(caseId, false) with { FailureReason = "flaky" }, Result(caseId, true)]);

        diff.TotalCases.ShouldBe(1);
        var entry = diff.Cases.Single();
        entry.Kind.ShouldBe(EvalCaseDiffKind.Regressed);
        entry.CandidateFailureReason.ShouldBe("flaky");
    }

    [Fact]
    public void Different_suites_refuse_to_align()
    {
        var other = Run(total: 1) with { SuiteId = Guid.Parse("22222222-2222-2222-2222-222222222222") };
        var caseId = Guid.NewGuid();

        var error = Should.Throw<EvalRunDiffUnavailableException>(() => EvalRunDiffBuilder.Build(
            Run(total: 1),
            other,
            [Result(caseId, true)],
            [Result(caseId, true)]));

        error.Reason.ShouldBe(EvalRunDiffUnavailableReason.DifferentSuites);
    }

    [Theory]
    [InlineData(EvalRunStatus.Pending)]
    [InlineData(EvalRunStatus.Running)]
    [InlineData(EvalRunStatus.Failed)]
    [InlineData(EvalRunStatus.Cancelled)]
    public void An_unfinished_run_refuses_to_align(EvalRunStatus status)
    {
        var caseId = Guid.NewGuid();

        var error = Should.Throw<EvalRunDiffUnavailableException>(() => EvalRunDiffBuilder.Build(
            Run(total: 1) with { Status = status },
            Run(total: 1),
            [Result(caseId, true)],
            [Result(caseId, true)]));

        error.Reason.ShouldBe(EvalRunDiffUnavailableReason.RunNotCompleted);
    }

    [Fact]
    public void A_run_whose_details_retention_removed_throws_instead_of_diffing_empty()
    {
        // 🚨 The whole point: an empty diff reads as "nothing changed" and turns
        // a CI gate green over a regression.
        var caseId = Guid.NewGuid();

        var error = Should.Throw<EvalRunDiffUnavailableException>(() => EvalRunDiffBuilder.Build(
            Run(total: 12),
            Run(total: 1),
            [],
            [Result(caseId, false)]));

        error.Reason.ShouldBe(EvalRunDiffUnavailableReason.DetailsRemoved);
        error.Message.ShouldContain("baseline");
    }

    [Fact]
    public void A_PARTLY_trimmed_run_throws_too_rather_than_diffing_what_survived()
    {
        // 🚨 The eval_case_results retention target deletes in batches and the
        // sweep can stop between them. Diffing the survivors would report every
        // deleted case as Removed and shrink the regression count to whatever
        // rows happen to be left - a green gate over cases nobody measured.
        var survived = Enumerable.Range(0, 3).Select(static _ => Guid.NewGuid()).ToArray();

        var error = Should.Throw<EvalRunDiffUnavailableException>(() => EvalRunDiffBuilder.Build(
            Run(total: 10),
            Run(total: 10),
            [.. survived.Select(static id => Result(id, true))],
            [.. survived.Select(static id => Result(id, true))]));

        error.Reason.ShouldBe(EvalRunDiffUnavailableReason.DetailsRemoved);
        error.Message.ShouldContain("only 3");
    }

    [Fact]
    public void Repetitions_do_not_read_as_missing_cases()
    {
        // The completeness check counts DISTINCT cases: a suite run with
        // repetitions holds several rows per case and must not look trimmed.
        var ids = Enumerable.Range(0, 4).Select(static _ => Guid.NewGuid()).ToArray();
        var rows = ids.SelectMany(static id => new[] { Result(id, true), Result(id, true) }).ToArray();

        var diff = EvalRunDiffBuilder.Build(Run(total: 4), Run(total: 4), rows, rows);

        diff.TotalCases.ShouldBe(4);
    }

    [Fact]
    public void The_candidate_side_losing_its_details_throws_too()
    {
        var caseId = Guid.NewGuid();

        var error = Should.Throw<EvalRunDiffUnavailableException>(() => EvalRunDiffBuilder.Build(
            Run(total: 1),
            Run(total: 12),
            [Result(caseId, false)],
            []));

        error.Reason.ShouldBe(EvalRunDiffUnavailableReason.DetailsRemoved);
        error.Message.ShouldContain("candidate");
    }

    [Fact]
    public void Regressions_come_first_so_the_first_page_carries_them()
    {
        var passing = Enumerable.Range(0, 40).Select(static _ => Guid.NewGuid()).ToArray();
        var broke = Guid.NewGuid();

        var baseline = passing.Select(static id => Result(id, true)).Append(Result(broke, true)).ToArray();
        var candidate = passing.Select(static id => Result(id, true)).Append(Result(broke, false)).ToArray();

        var diff = EvalRunDiffBuilder.Build(Run(total: 41), Run(total: 41), baseline, candidate, skip: 0, take: 5);

        diff.Cases.Count.ShouldBe(5);
        diff.Cases[0].CaseId.ShouldBe(broke);
        diff.TotalCases.ShouldBe(41);
        diff.RegressedCount.ShouldBe(1);
        diff.UnchangedCount.ShouldBe(40);
    }

    [Fact]
    public void Paging_moves_the_window_but_never_the_counters()
    {
        var ids = Enumerable.Range(0, 10).Select(static _ => Guid.NewGuid()).ToArray();
        var rows = ids.Select(static id => Result(id, true)).ToArray();

        var page = EvalRunDiffBuilder.Build(Run(total: 10), Run(total: 10), rows, rows, skip: 8, take: 50);

        page.Cases.Count.ShouldBe(2);
        page.TotalCases.ShouldBe(10);
        page.UnchangedCount.ShouldBe(10);
    }

    [Fact]
    public void Aligning_thousands_of_cases_stays_linear()
    {
        const int count = 20_000;
        var ids = Enumerable.Range(0, count).Select(static _ => Guid.NewGuid()).ToArray();
        var baseline = ids.Select(static id => Result(id, true)).ToArray();
        var candidate = ids.Select(static id => Result(id, true)).ToArray();

        var stopwatch = Stopwatch.StartNew();
        var diff = EvalRunDiffBuilder.Build(Run(total: count), Run(total: count), baseline, candidate, 0, 10);
        stopwatch.Stop();

        diff.TotalCases.ShouldBe(count);

        // A nested scan over both sides would be 4x10^8 comparisons here. The
        // ceiling is deliberately loose - it fails on a quadratic alignment,
        // not on a slow machine.
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    private static EvalCaseDiffKind KindOf(EvalRunDiff diff, Guid caseId)
        => diff.Cases.Single(entry => entry.CaseId == caseId).Kind;

    private static EvalRun Run(int total) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = "tenant",
        SuiteId = SuiteId,
        Status = EvalRunStatus.Completed,
        Total = total,
        StartedAt = DateTimeOffset.UnixEpoch,
    };

    private static EvalCaseResult Result(Guid caseId, bool passed) => new()
    {
        Id = TraconId.NewId(),
        EvalRunId = Guid.NewGuid(),
        CaseId = caseId,
        Passed = passed,
        FailureReason = passed ? null : "failed",
    };
}
