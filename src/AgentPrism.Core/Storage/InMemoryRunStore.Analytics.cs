using System.Runtime.InteropServices;

namespace AgentPrism;

/// <summary>
/// Experiment variant results, time series buckets and tool usage aggregates.
/// </summary>
internal sealed partial class InMemoryRunStore
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(
        ExperimentResultsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tenantId = query.TenantId ?? _tenantContext.TenantId;
        var perVariant = new Dictionary<string, VariantTally>(StringComparer.Ordinal);

        foreach (var record in _runs.Values)
        {
            if (record.ExperimentId != query.ExperimentId || record.Variant is not { Length: > 0 } variant)
            {
                continue;
            }

            if (!string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
            {
                continue;
            }

            var runAverageScore = await GetRunAverageScoreAsync(record, tenantId, cancellationToken).ConfigureAwait(false);

            perVariant.TryGetValue(variant, out var tally);
            perVariant[variant] = tally.Add(record, runAverageScore);
        }

        return [.. perVariant.Select(pair => pair.Value.ToResult(pair.Key))];
    }

    /// <summary>
    /// The average of a run's numeric (<c>RunScoreKind.Numeric</c>)
    /// scores. <see langword="null"/> if there is no run-level score.
    /// </summary>
    private async ValueTask<double?> GetRunAverageScoreAsync(RunRecord record, string tenantId, CancellationToken cancellationToken)
    {
        var scores = await _scores.ListAsync(tenantId, record.Id, cancellationToken).ConfigureAwait(false);

        double sum = 0;
        long count = 0;

        foreach (var score in scores)
        {
            if (score.Kind == RunScoreKind.Numeric && score.MessageId is null)
            {
                sum += score.Value;
                count++;
            }
        }

        return count == 0 ? null : sum / count;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(
        RunTimeSeriesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        RunTimeSeriesBucketing.Validate(query.From, query.To, query.Bucket);

        var step = RunTimeSeriesBucketing.StepFor(query.Bucket);
        var buckets = new SortedDictionary<DateTimeOffset, BucketTally>();

        // Empty buckets must be returned too (K-152 — a gap in the time
        // series chart should look like "zero", not "no data"). PostgreSql's
        // generate_series is matched here by pre-seeding.
        for (var cursor = RunTimeSeriesBucketing.Truncate(query.From, query.Bucket);
             cursor < query.To;
             cursor += step)
        {
            buckets[cursor] = default;
        }

        foreach (var record in _runs.Values)
        {
            if (record.StartedAt < query.From || record.StartedAt >= query.To)
            {
                continue;
            }

            if (!string.Equals(record.TenantId, query.TenantId ?? _tenantContext.TenantId, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.AgentName is { } agentName && !string.Equals(record.AgentName, agentName, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.ModelId is { } modelId && !string.Equals(record.ModelId, modelId, StringComparison.Ordinal))
            {
                continue;
            }

            // Deliberately does NOT exclude Eval/Workflow runs — unlike
            // GetStatisticsAsync (see docs/KARARLAR.md K-152).
            if (query.Kind is { } kind && record.Kind != kind)
            {
                continue;
            }

            var bucket = RunTimeSeriesBucketing.Truncate(record.StartedAt, query.Bucket);
            buckets.TryGetValue(bucket, out var tally);
            buckets[bucket] = tally.Add(record);
        }

        var points = buckets
            .Select(static pair => pair.Value.ToPoint(pair.Key))
            .ToList();

        return new ValueTask<IReadOnlyList<TimeSeriesPoint>>(points);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(
        ToolUsageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var perTool = new Dictionary<string, ToolTally>(StringComparer.Ordinal);

        foreach (var (runId, log) in _toolInvocations)
        {
            if (!_runs.TryGetValue(runId, out var run))
            {
                continue;
            }

            if (!string.Equals(run.TenantId, query.TenantId ?? _tenantContext.TenantId, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.StartedAfter is { } after && run.StartedAt <= after)
            {
                continue;
            }

            ToolInvocationRecord[] snapshot;

            lock (log)
            {
                snapshot = [.. log];
            }

            foreach (var invocation in snapshot)
            {
                perTool.TryGetValue(invocation.ToolName, out var tally);
                perTool[invocation.ToolName] = tally.Add(invocation);
            }
        }

        return new ValueTask<IReadOnlyList<ToolUsage>>(
        [
            .. perTool
                .Select(static pair => pair.Value.ToUsage(pair.Key))
                .OrderByDescending(static usage => usage.TotalCalls)
                .ThenBy(static usage => usage.ToolName, StringComparer.Ordinal)
                .Take(Math.Max(query.MaxTools, 0)),
        ]);
    }

    /// <summary>Accumulated counters for an experiment variant.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct VariantTally(
        int Version,
        long TotalRuns,
        long CompletedRuns,
        long FailedRuns,
        long CanceledRuns,
        long InputTokens,
        long OutputTokens,
        long TotalTokens,
        double TotalDurationMs,
        long SettledCount,
        decimal? CostSum,
        string? Currency,
        double ScoreSum,
        long ScoredRunCount)
    {
        public VariantTally Add(RunRecord record, double? runAverageScore)
        {
            var settled = record.CompletedAt is { } completedAt;
            var costSum = CostSum;

            // RunCost.Total() rather than a hand written two-term sum: the cache
            // charge is a third addend, and the SQL providers total all three.
            // Diverging here would make the SAME query answer differently
            // depending on which store is registered.
            if (record.Cost?.Total() is { } runCost)
            {
                costSum = (costSum ?? 0) + runCost;
            }

            return new VariantTally(
                record.AgentVersion ?? Version,
                TotalRuns + 1,
                CompletedRuns + (record.Status == RunStatus.Completed ? 1 : 0),
                FailedRuns + (record.Status == RunStatus.Failed ? 1 : 0),
                CanceledRuns + (record.Status == RunStatus.Canceled ? 1 : 0),
                InputTokens + (record.Usage?.InputTokens ?? 0),
                OutputTokens + (record.Usage?.OutputTokens ?? 0),
                TotalTokens + (record.Usage?.TotalTokens ?? 0),
                TotalDurationMs + (settled ? (record.CompletedAt!.Value - record.StartedAt).TotalMilliseconds : 0),
                SettledCount + (settled ? 1 : 0),
                costSum,
                Currency ?? record.Cost?.Currency,
                ScoreSum + (runAverageScore ?? 0),
                ScoredRunCount + (runAverageScore.HasValue ? 1 : 0));
        }

        public ExperimentVariantResult ToResult(string variant)
            => new()
            {
                Variant = variant,
                Version = Version,
                TotalRuns = TotalRuns,
                CompletedRuns = CompletedRuns,
                FailedRuns = FailedRuns,
                CanceledRuns = CanceledRuns,
                InputTokens = InputTokens,
                OutputTokens = OutputTokens,
                TotalTokens = TotalTokens,
                AverageDurationMs = SettledCount == 0 ? null : TotalDurationMs / SettledCount,
                TotalCost = CostSum,
                Currency = Currency,
                AverageScore = ScoredRunCount == 0 ? null : ScoreSum / ScoredRunCount,
            };
    }

    /// <summary>Accumulated counters for a time bucket.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct BucketTally(
        long Runs,
        long FailedRuns,
        long InputTokens,
        long OutputTokens,
        decimal? CostSum,
        double TotalDurationMs,
        long SettledCount)
    {
        public BucketTally Add(RunRecord record)
        {
            var settled = record.CompletedAt is { } completedAt;
            var costSum = CostSum;

            // RunCost.Total() rather than a hand written two-term sum: the cache
            // charge is a third addend, and the SQL providers total all three.
            // Diverging here would make the SAME query answer differently
            // depending on which store is registered.
            if (record.Cost?.Total() is { } runCost)
            {
                costSum = (costSum ?? 0) + runCost;
            }

            return new BucketTally(
                Runs + 1,
                FailedRuns + (record.Status == RunStatus.Failed ? 1 : 0),
                InputTokens + (record.Usage?.InputTokens ?? 0),
                OutputTokens + (record.Usage?.OutputTokens ?? 0),
                costSum,
                TotalDurationMs + (settled ? (record.CompletedAt!.Value - record.StartedAt).TotalMilliseconds : 0),
                SettledCount + (settled ? 1 : 0));
        }

        public TimeSeriesPoint ToPoint(DateTimeOffset bucket)
            => new()
            {
                Bucket = bucket,
                Runs = Runs,
                FailedRuns = FailedRuns,
                InputTokens = InputTokens,
                OutputTokens = OutputTokens,
                Cost = CostSum,
                AverageDurationMs = SettledCount == 0 ? null : TotalDurationMs / SettledCount,
            };
    }

    /// <summary>Accumulated counters for a tool.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ToolTally(
        long TotalCalls,
        long FailedCalls,
        double TotalDurationMs,
        long TimedCalls,
        DateTimeOffset? LastCalledAt)
    {
        public ToolTally Add(ToolInvocationRecord invocation)
            => new(
                TotalCalls + 1,
                FailedCalls + (invocation.Succeeded ? 0 : 1),
                TotalDurationMs + (invocation.Duration?.TotalMilliseconds ?? 0),
                TimedCalls + (invocation.Duration is null ? 0 : 1),
                LastCalledAt is { } last && last > invocation.CreatedAt ? last : invocation.CreatedAt);

        public ToolUsage ToUsage(string toolName)
            => new()
            {
                ToolName = toolName,
                TotalCalls = TotalCalls,
                FailedCalls = FailedCalls,
                // The denominator is timed calls: including calls that report
                // no duration in the denominator would artificially lower the average.
                AverageDurationMs = TimedCalls == 0 ? null : TotalDurationMs / TimedCalls,
                LastCalledAt = LastCalledAt,
            };
    }
}
