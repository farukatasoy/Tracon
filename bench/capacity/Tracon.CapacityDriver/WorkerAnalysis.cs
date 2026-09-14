using Tracon.Capacity;

namespace Tracon.CapacityDriver;

/// <summary>Turns the processes' own execution records into the worker axis.</summary>
/// <remarks>
/// <para>
/// 🚨 "Throughput went up" is not evidence that work was distributed. One
/// worker taking every job while the other three idle produces the same total
/// on a fast enough machine. This analysis therefore reports the
/// <em>distribution</em> beside the total, and when one process's share exceeds
/// <see cref="DominanceThreshold"/> the cell says contention could not be
/// measured rather than claiming a scaling result.
/// </para>
/// <para>
/// 🚨 An extra attempt is legitimate: <c>IJobHandler</c> is at-least-once
/// (K-641). An <em>overlapping</em> attempt is not, and it makes the cell
/// invalid.
/// </para>
/// </remarks>
public static class WorkerAnalysis
{
    /// <summary>Above this share of the jobs, one worker has effectively taken the queue.</summary>
    public const double DominanceThreshold = 0.9;

    /// <summary>Builds the worker summary from executions restricted to the measured cohort.</summary>
    /// <param name="executions">Every execution any process recorded.</param>
    /// <param name="cohort">The correlation values that belong to the measured window.</param>
    /// <param name="workerCount">How many worker processes were supposed to lease.</param>
    /// <param name="hostRunWorker">The host's effective <c>Scheduling:RunWorker</c> value.</param>
    /// <param name="acceptedJobs">
    /// How many jobs the queue ACCEPTED. 🚨 Not the number that ran: a job
    /// accepted and never executed would make an attempt ratio computed from
    /// executions alone read as a healthy 1.0, hiding exactly the backlog the
    /// measurement is looking for.
    /// </param>
    /// <returns>The summary.</returns>
    public static WorkerSummary Summarise(
        IReadOnlyList<CapacityExecution> executions,
        IReadOnlySet<string> cohort,
        int workerCount,
        bool hostRunWorker,
        long acceptedJobs)
    {
        ArgumentNullException.ThrowIfNull(executions);
        ArgumentNullException.ThrowIfNull(cohort);

        var relevant = executions.Where(e => cohort.Contains(e.Correlation)).ToList();

        var summary = new WorkerSummary
        {
            HostRunWorker = hostRunWorker,
            WorkerCount = workerCount,
            AcceptedJobs = acceptedJobs,
            ExecutedJobs = relevant.Select(static e => e.Correlation).Distinct(StringComparer.Ordinal).Count(),
            Attempts = relevant.Count,
        };

        foreach (var group in relevant.GroupBy(static e => e.Pid))
        {
            summary.Shares.Add(new WorkerShare
            {
                Pid = group.Key,
                Name = group.First().Process,
                Jobs = group.Select(static e => e.Correlation).Distinct(StringComparer.Ordinal).Count(),
                Attempts = group.Count(),
            });
        }

        summary.Shares.Sort(static (left, right) => right.Jobs.CompareTo(left.Jobs));

        if (summary.AcceptedJobs > 0)
        {
            summary.AttemptRatio = Math.Round((double)summary.Attempts / summary.AcceptedJobs, 4);
            summary.LargestShare = summary.Shares.Count == 0 || summary.ExecutedJobs == 0
                ? null
                : Math.Round((double)summary.Shares[0].Jobs / summary.ExecutedJobs, 4);
        }

        summary.ConcurrentOverlaps = CountOverlaps(relevant);

        // With one worker there is no contention to measure and no claim to
        // make; the cell is the baseline the others are read against.
        summary.ContentionMeasured =
            workerCount > 1
            && summary.Shares.Count > 1
            && summary.LargestShare is { } share
            && share < DominanceThreshold;

        return summary;
    }

    /// <summary>Counts pairs of executions of the same job whose intervals overlap.</summary>
    /// <param name="executions">The executions to inspect.</param>
    /// <returns>How many overlapping pairs exist.</returns>
    /// <remarks>
    /// Both sides of a pair are compared on the same process's clock only when
    /// they come from the same process; across processes the UTC stamps are
    /// used, which is the one place a cross-process comparison is unavoidable.
    /// The comparison is therefore deliberately conservative - it needs a
    /// genuine, non-touching overlap before it reports one.
    /// </remarks>
    public static long CountOverlaps(IReadOnlyList<CapacityExecution> executions)
    {
        ArgumentNullException.ThrowIfNull(executions);

        long overlaps = 0;

        foreach (var group in executions.GroupBy(static e => e.Correlation, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(static e => e.StartedTicks).ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                for (var j = i + 1; j < ordered.Count; j++)
                {
                    if (ordered[j].StartedTicks < ordered[i].CompletedTicks)
                    {
                        overlaps++;
                    }
                }
            }
        }

        return overlaps;
    }

    /// <summary>Reduces the executions to what the model boundary actually spent.</summary>
    /// <param name="executions">Every execution any process recorded.</param>
    /// <param name="cohort">The correlation values that belong to the measured window.</param>
    /// <returns>The model summary.</returns>
    public static ModelSummary SummariseModel(
        IReadOnlyList<CapacityExecution> executions,
        IReadOnlySet<string> cohort)
    {
        ArgumentNullException.ThrowIfNull(executions);
        ArgumentNullException.ThrowIfNull(cohort);

        var relevant = executions.Where(e => cohort.Contains(e.Correlation)).ToList();

        return new ModelSummary
        {
            Calls = relevant.Count,
            Turns = relevant.Sum(static e => (long)e.Turns),
            ToolInvocations = relevant.Sum(static e => (long)e.ToolCalls),
            Duration = LatencyStatistics.From(relevant.Select(static e => e.ModelMilliseconds)).ToSummary(),
        };
    }
}
