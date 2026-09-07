using System.Runtime.InteropServices;

namespace AgentPrism;

/// <summary>
/// Aggregate statistics: run counts, token/cost totals, breakdowns and
/// error clustering.
/// </summary>
internal sealed partial class InMemoryRunStore
{
    /// <inheritdoc />
    public async ValueTask<RunStatistics> GetStatisticsAsync(
        RunStatisticsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        long total = 0, completed = 0, failed = 0, canceled = 0, running = 0, awaitingInput = 0;
        long inputTokens = 0, outputTokens = 0, totalTokens = 0;
        long cachedInputTokens = 0, reasoningTokens = 0, audioInputTokens = 0, audioOutputTokens = 0;
        long scoredRuns = 0, binaryScores = 0, positiveBinaryScores = 0;
        var perAgent = new Dictionary<string, AgentTally>(StringComparer.Ordinal);
        var perModel = new Dictionary<string, ModelTally>(StringComparer.Ordinal);
        var perUser = new Dictionary<string, BreakdownTally>(StringComparer.Ordinal);
        var perLabel = new Dictionary<(string Key, string Value), BreakdownTally>();
        var perVersion = new Dictionary<(string AgentName, int Version), AgentTally>();
        var perErrorClass = new Dictionary<RunErrorClass, long>();
        var perErrorCluster = new Dictionary<(RunErrorClass Class, string Fingerprint), ErrorClusterTally>();
        decimal? costSum = null;
        string? currency = null;
        long runsWithUnknownPricing = 0;

        foreach (var record in _runs.Values)
        {
            if (query.AgentName is { } agentName && !string.Equals(record.AgentName, agentName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(record.TenantId, query.TenantId ?? _tenantContext.TenantId, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.StartedAfter is { } after && record.StartedAt <= after)
            {
                continue;
            }

            if (query.UserId is { } filterUserId && !string.Equals(record.UserId, filterUserId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!MatchesLabel(record, query.LabelKey, query.LabelValue))
            {
                continue;
            }

            // Eval case runs are synthetic test calls, not real traffic; they
            // are excluded to avoid polluting the summary
            // (docs/arsiv/fazlar/18-DEGERLENDIRME.md, open question 4).
            if (record.Kind == RunKind.Eval)
            {
                continue;
            }

            total++;

            // Fetching a run's scores one by one (N+1) is acceptable in the
            // in-memory store; the production path is the single-query JOIN
            // in the SQL providers. See docs/arsiv/fazlar/31-GERI-BILDIRIM-VE-PUANLAMA.md.
            // TenantId can rarely be empty (RunRecord.TenantId is nullable);
            // if empty, no score could have been written for this run.
            var runScores = record.TenantId is { Length: > 0 } scoreTenantId
                ? await _scores.ListAsync(scoreTenantId, record.Id, cancellationToken).ConfigureAwait(false)
                : [];

            if (runScores.Count > 0)
            {
                scoredRuns++;
            }

            foreach (var score in runScores)
            {
                // 🚨 A null value is skipped ENTIRELY, denominator included: it
                // records that no measurement was made, not a negative one.
                // The SQL side carries the same `value IS NOT NULL` on both
                // sides of the ratio.
                if (score.Kind != RunScoreKind.Binary || score.Value is null)
                {
                    continue;
                }

                binaryScores++;

                if (score.Value == 1)
                {
                    positiveBinaryScores++;
                }
            }

            switch (record.Status)
            {
                case RunStatus.Completed: completed++; break;
                case RunStatus.Failed: failed++; break;
                case RunStatus.Canceled: canceled++; break;
                case RunStatus.Running: running++; break;
                case RunStatus.AwaitingInput: awaitingInput++; break;
                default: break;
            }

            // Error breakdown: rows written before the error class field was
            // added (Class == null) fall into the Unknown bucket (K-014 --
            // no retroactive backfill is done).
            if (record.Status == RunStatus.Failed && record.Error is { } runError)
            {
                var errorClass = runError.Class ?? RunErrorClass.Unknown;
                var fingerprint = runError.Fingerprint ?? string.Empty;

                perErrorClass.TryGetValue(errorClass, out var classTotal);
                perErrorClass[errorClass] = classTotal + 1;

                var clusterKey = (errorClass, fingerprint);
                perErrorCluster.TryGetValue(clusterKey, out var clusterTally);
                perErrorCluster[clusterKey] = clusterTally.Add(record, runError);
            }

            inputTokens += record.Usage?.InputTokens ?? 0;
            outputTokens += record.Usage?.OutputTokens ?? 0;
            totalTokens += record.Usage?.TotalTokens ?? 0;

            // These four are counted INSIDE the totals above and are reported
            // beside them; a caller that adds them counts the same tokens twice.
            cachedInputTokens += record.Usage?.CachedInputTokens ?? 0;
            reasoningTokens += record.Usage?.ReasoningTokens ?? 0;
            audioInputTokens += record.Usage?.AudioInputTokens ?? 0;
            audioOutputTokens += record.Usage?.AudioOutputTokens ?? 0;

            if (record.Cost is { } cost)
            {
                if (cost.Source == PricingSource.Unknown)
                {
                    runsWithUnknownPricing++;
                }

                if (cost.InputCost is { } ic)
                {
                    costSum = (costSum ?? 0) + ic;
                }

                if (cost.OutputCost is { } oc)
                {
                    costSum = (costSum ?? 0) + oc;
                }

                // The run's own InputCost already has its cached tokens subtracted
                // out, so the cache charge is a third addend of the total.
                if (cost.CachedInputCost is { } cc)
                {
                    costSum = (costSum ?? 0) + cc;
                }

                currency ??= cost.Currency;
            }

            var runCost = RunCostTotal(record.Cost);

            // Runs carrying no user identity stay out of the breakdown but remain
            // in the totals, exactly as runs with an unknown model do.
            if (record.UserId is { Length: > 0 } breakdownUserId)
            {
                perUser.TryGetValue(breakdownUserId, out var userTally);
                perUser[breakdownUserId] = userTally.Add(record, runCost);
            }

            // 🚨 A run carrying three labels contributes to THREE entries, so this
            // breakdown does not partition the runs and its rows do not sum to
            // TotalRuns. That is inherent to a label set, not a defect.
            if (record.Labels is { Count: > 0 } recordLabels)
            {
                foreach (var (labelKey, labelValue) in recordLabels)
                {
                    var key = (labelKey, labelValue);
                    perLabel.TryGetValue(key, out var labelTally);
                    perLabel[key] = labelTally.Add(record, runCost);
                }
            }

            perAgent.TryGetValue(record.AgentName, out var tally);
            perAgent[record.AgentName] = new AgentTally(
                tally.TotalRuns + 1,
                tally.FailedRuns + (record.Status == RunStatus.Failed ? 1 : 0),
                tally.TotalTokens + (record.Usage?.TotalTokens ?? 0));

            // Runs with an unknown version do not enter the breakdown but are
            // still counted in the totals; otherwise the two figures would not agree.
            if (record.AgentVersion is { } agentVersion)
            {
                var key = (record.AgentName, agentVersion);
                perVersion.TryGetValue(key, out var versionTally);
                perVersion[key] = new AgentTally(
                    versionTally.TotalRuns + 1,
                    versionTally.FailedRuns + (record.Status == RunStatus.Failed ? 1 : 0),
                    versionTally.TotalTokens + (record.Usage?.TotalTokens ?? 0));
            }

            // Runs with an unknown model name do not enter the breakdown but
            // are still counted in the totals; otherwise the two figures would not agree.
            if (record.ModelId is { Length: > 0 } modelId)
            {
                perModel.TryGetValue(modelId, out var modelTally);
                var modelCost = runCost is { } modelRunCost
                    ? (modelTally.CostSum ?? 0) + modelRunCost
                    : modelTally.CostSum;
                perModel[modelId] = new ModelTally(
                    modelTally.TotalRuns + 1,
                    modelTally.InputTokens + (record.Usage?.InputTokens ?? 0),
                    modelTally.OutputTokens + (record.Usage?.OutputTokens ?? 0),
                    modelTally.TotalTokens + (record.Usage?.TotalTokens ?? 0),
                    modelCost);
            }
        }

        var byAgent = perAgent
            .Select(static pair => new RunAgentStatistics
            {
                AgentName = pair.Key,
                TotalRuns = pair.Value.TotalRuns,
                FailedRuns = pair.Value.FailedRuns,
                TotalTokens = pair.Value.TotalTokens,
            })
            .OrderByDescending(static agent => agent.TotalRuns)
            .ThenBy(static agent => agent.AgentName, StringComparer.Ordinal)
            .Take(Math.Max(query.MaxAgents, 0))
            .ToList();

        var byErrorClass = perErrorClass
            .Select(pair => new RunErrorStatistics
            {
                Class = pair.Key,
                TotalRuns = pair.Value,
                TopClusters = [.. perErrorCluster
                    .Where(cluster => cluster.Key.Class == pair.Key)
                    .OrderByDescending(static cluster => cluster.Value.Count)
                    .ThenByDescending(static cluster => cluster.Value.LastSeenAt)
                    .Take(TopErrorClusterCount)
                    .Select(static cluster => new RunErrorCluster
                    {
                        Fingerprint = cluster.Key.Fingerprint,
                        Count = cluster.Value.Count,
                        SampleMessage = cluster.Value.SampleMessage,
                        SampleRunId = cluster.Value.SampleRunId,
                        LastSeenAt = cluster.Value.LastSeenAt,
                    })],
            })
            .OrderByDescending(static stat => stat.TotalRuns)
            .ThenBy(static stat => stat.Class)
            .ToList();

        return new RunStatistics
        {
            TotalRuns = total,
            CompletedRuns = completed,
            FailedRuns = failed,
            CanceledRuns = canceled,
            RunningRuns = running,
            AwaitingInputRuns = awaitingInput,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            CachedInputTokens = cachedInputTokens,
            ReasoningTokens = reasoningTokens,
            AudioInputTokens = audioInputTokens,
            AudioOutputTokens = audioOutputTokens,
            TotalCost = costSum,
            Currency = currency,
            RunsWithUnknownPricing = runsWithUnknownPricing,
            ScoredRuns = scoredRuns,
            PositiveRate = binaryScores == 0 ? null : (double)positiveBinaryScores / binaryScores,
            ByAgent = byAgent,
            ByModel = [.. perModel
                .Select(static pair => new RunModelStatistics
                {
                    ModelId = pair.Key,
                    TotalRuns = pair.Value.TotalRuns,
                    InputTokens = pair.Value.InputTokens,
                    OutputTokens = pair.Value.OutputTokens,
                    TotalTokens = pair.Value.TotalTokens,
                    TotalCost = pair.Value.CostSum,
                })
                .OrderByDescending(static model => model.TotalRuns)
                .ThenBy(static model => model.ModelId, StringComparer.Ordinal)],
            ByVersion = [.. perVersion
                .Select(static pair => new RunVersionStatistics
                {
                    AgentName = pair.Key.AgentName,
                    Version = pair.Key.Version,
                    TotalRuns = pair.Value.TotalRuns,
                    FailedRuns = pair.Value.FailedRuns,
                    TotalTokens = pair.Value.TotalTokens,
                })
                .OrderBy(static version => version.AgentName, StringComparer.Ordinal)
                .ThenByDescending(static version => version.Version)],
            ByUser = [.. perUser
                .Select(static pair => new RunUserStatistics
                {
                    UserId = pair.Key,
                    TotalRuns = pair.Value.TotalRuns,
                    FailedRuns = pair.Value.FailedRuns,
                    TotalTokens = pair.Value.TotalTokens,
                    TotalCost = pair.Value.CostSum,
                })
                .OrderByDescending(static user => user.TotalRuns)
                .ThenBy(static user => user.UserId, StringComparer.Ordinal)
                .Take(Math.Max(query.MaxAgents, 0))],
            ByLabel = [.. perLabel
                .Select(static pair => new RunLabelStatistics
                {
                    Key = pair.Key.Key,
                    Value = pair.Key.Value,
                    TotalRuns = pair.Value.TotalRuns,
                    FailedRuns = pair.Value.FailedRuns,
                    TotalTokens = pair.Value.TotalTokens,
                    TotalCost = pair.Value.CostSum,
                })
                .OrderByDescending(static label => label.TotalRuns)
                .ThenBy(static label => label.Key, StringComparer.Ordinal)
                .ThenBy(static label => label.Value, StringComparer.Ordinal)
                .Take(Math.Max(query.MaxAgents, 0))],
            ByErrorClass = byErrorClass,
        };
    }

    /// <summary>The upper bound applied when selecting an error class's most frequent clusters.</summary>
    private const int TopErrorClusterCount = 3;

    /// <summary>
    /// Sums a run's own cost the same way every breakdown does.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the run was never priced — writing zero would
    /// make "no price is known" look like "it cost nothing".
    /// </returns>
    private static decimal? RunCostTotal(RunCost? cost)
        => cost is { InputCost: not null } or { OutputCost: not null } or { CachedInputCost: not null }
            ? (cost!.InputCost ?? 0) + (cost.OutputCost ?? 0) + (cost.CachedInputCost ?? 0)
            : null;

    /// <summary>Accumulated counters for an agent. Used only in summary computation.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct AgentTally(long TotalRuns, long FailedRuns, long TotalTokens);

    /// <summary>The running tally of one user or one label key/value pair.</summary>
    private readonly record struct BreakdownTally(
        long TotalRuns,
        long FailedRuns,
        long TotalTokens,
        decimal? CostSum)
    {
        /// <summary>Folds one run into the tally.</summary>
        /// <param name="record">The run being counted.</param>
        /// <param name="runCost">The run's own total cost, or <see langword="null"/> when it was never priced.</param>
        /// <returns>The updated tally.</returns>
        public BreakdownTally Add(RunRecord record, decimal? runCost)
            => new(
                TotalRuns + 1,
                FailedRuns + (record.Status == RunStatus.Failed ? 1 : 0),
                TotalTokens + (record.Usage?.TotalTokens ?? 0),

                // An unpriced run leaves the sum untouched rather than adding
                // zero: the difference between "free" and "unknown" is the whole
                // point of RunsWithUnknownPricing.
                runCost is { } cost ? (CostSum ?? 0) + cost : CostSum);
    }

    /// <summary>Accumulated counters for a model.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ModelTally(
        long TotalRuns,
        long InputTokens,
        long OutputTokens,
        long TotalTokens,
        decimal? CostSum);

    /// <summary>Accumulated counters for an error fingerprint cluster.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ErrorClusterTally(long Count, string SampleMessage, Guid SampleRunId, DateTimeOffset LastSeenAt)
    {
        /// <summary>
        /// Updates the most recent sample. "Most recent" here is the run's
        /// start time (<see cref="RunRecord.StartedAt"/>) -- the record has
        /// no separate "occurred at" timestamp specific to the error itself.
        /// </summary>
        public ErrorClusterTally Add(RunRecord record, RunError error)
        {
            var isNewer = Count == 0 || record.StartedAt > LastSeenAt;

            return new ErrorClusterTally(
                Count + 1,
                isNewer ? error.Message : SampleMessage,
                isNewer ? record.Id : SampleRunId,
                isNewer ? record.StartedAt : LastSeenAt);
        }
    }
}
