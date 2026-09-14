using Tracon.Capacity;
using Tracon.CapacityDriver;

namespace Tracon.Capacity.Tests;

/// <summary>The worker axis: distribution, at-least-once, and the overlap that invalidates a cell.</summary>
public sealed class CapacityWorkerAnalysisTests
{
    private static CapacityExecution Execution(string correlation, int pid, long start, long end)
        => new()
        {
            Correlation = correlation,
            Pid = pid,
            Process = "worker-" + pid,
            StartedTicks = start,
            CompletedTicks = end,
            Turns = 2,
            ModelMilliseconds = 1000,
            ToolCalls = 1,
        };

    [Fact]
    public void Work_spread_across_two_workers_reports_the_distribution_and_measures_contention()
    {
        List<CapacityExecution> executions =
        [
            Execution("a", 100, 0, 10),
            Execution("b", 200, 20, 30),
            Execution("c", 100, 40, 50),
            Execution("d", 200, 60, 70),
        ];

        var summary = WorkerAnalysis.Summarise(
            executions, new HashSet<string>(["a", "b", "c", "d"], StringComparer.Ordinal),
            workerCount: 2, hostRunWorker: false, acceptedJobs: 4);

        summary.AcceptedJobs.ShouldBe(4);
        summary.ExecutedJobs.ShouldBe(4);
        summary.Shares.Count.ShouldBe(2);
        summary.Shares.ShouldAllBe(share => share.Jobs == 2);
        summary.LargestShare.ShouldBe(0.5);
        summary.AttemptRatio.ShouldBe(1);
        summary.ConcurrentOverlaps.ShouldBe(0);
        summary.ContentionMeasured.ShouldBeTrue();
    }

    [Fact]
    public void One_worker_taking_everything_is_reported_as_contention_not_measured()
    {
        // 🚨 Total throughput alone would have looked like scaling. It is not:
        // three of the four processes never leased anything.
        var executions = Enumerable.Range(0, 10)
            .Select(i => Execution("job-" + i, 100, i * 10, (i * 10) + 5))
            .ToList();

        var summary = WorkerAnalysis.Summarise(
            executions,
            executions.Select(static e => e.Correlation).ToHashSet(StringComparer.Ordinal),
            workerCount: 4,
            hostRunWorker: false,
            acceptedJobs: 10);

        summary.LargestShare.ShouldBe(1);
        summary.ContentionMeasured.ShouldBeFalse();
    }

    [Fact]
    public void A_repeated_attempt_is_legitimate_and_shows_up_as_a_ratio_above_one()
    {
        // at-least-once (K-641): the same job may run again after a lost lease.
        // The intervals do NOT overlap, so this is not an isolation failure.
        List<CapacityExecution> executions =
        [
            Execution("a", 100, 0, 10),
            Execution("a", 200, 20, 30),
        ];

        var summary = WorkerAnalysis.Summarise(
            executions, new HashSet<string>(["a"], StringComparer.Ordinal),
            workerCount: 2, hostRunWorker: false, acceptedJobs: 1);

        summary.AcceptedJobs.ShouldBe(1);
        summary.Attempts.ShouldBe(2);
        summary.AttemptRatio.ShouldBe(2);
        summary.ConcurrentOverlaps.ShouldBe(0);
    }

    [Fact]
    public void An_overlapping_attempt_of_the_same_job_is_counted_and_is_not_allowed_by_at_least_once()
    {
        List<CapacityExecution> executions =
        [
            Execution("a", 100, 0, 100),
            Execution("a", 200, 50, 150),
        ];

        WorkerAnalysis.CountOverlaps(executions).ShouldBe(1);
    }

    [Fact]
    public void Touching_intervals_are_not_an_overlap()
    {
        // One attempt ends exactly where the next begins. The comparison is
        // deliberately conservative: it needs a genuine overlap.
        List<CapacityExecution> executions =
        [
            Execution("a", 100, 0, 100),
            Execution("a", 200, 100, 200),
        ];

        WorkerAnalysis.CountOverlaps(executions).ShouldBe(0);
    }

    [Fact]
    public void Executions_outside_the_measured_cohort_are_ignored()
    {
        List<CapacityExecution> executions =
        [
            Execution("warmup", 100, 0, 10),
            Execution("measured", 100, 20, 30),
        ];

        var summary = WorkerAnalysis.Summarise(
            executions, new HashSet<string>(["measured"], StringComparer.Ordinal),
            workerCount: 1, hostRunWorker: false, acceptedJobs: 1);

        summary.Attempts.ShouldBe(1);
        summary.AcceptedJobs.ShouldBe(1);
    }

    [Fact]
    public void A_job_accepted_but_never_run_lowers_the_attempt_ratio_instead_of_hiding_the_backlog()
    {
        // 🚨 Computing the ratio from executions alone would read 1.0 here -
        // one job, one attempt, healthy. Ten were accepted; nine never ran.
        List<CapacityExecution> executions = [Execution("a", 100, 0, 10)];

        var summary = WorkerAnalysis.Summarise(
            executions, new HashSet<string>(["a"], StringComparer.Ordinal),
            workerCount: 2, hostRunWorker: false, acceptedJobs: 10);

        summary.AcceptedJobs.ShouldBe(10);
        summary.ExecutedJobs.ShouldBe(1);
        summary.AttemptRatio.ShouldBe(0.1);
    }

    [Fact]
    public void The_model_summary_reports_the_provider_boundary_rather_than_the_client_view()
    {
        List<CapacityExecution> executions =
        [
            Execution("a", 100, 0, 10),
            Execution("b", 100, 0, 10),
        ];

        var model = WorkerAnalysis.SummariseModel(
            executions, new HashSet<string>(["a", "b"], StringComparer.Ordinal));

        model.Calls.ShouldBe(2);
        model.Turns.ShouldBe(4);
        model.ToolInvocations.ShouldBe(2);
        model.Duration.Count.ShouldBe(2);
        model.Duration.P50.ShouldBe(1000);
    }
}
