using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Tracon.Samples.FileRunStore;

/// <summary>
/// A reference <see cref="IRunStore"/> implementation backed by a single JSON
/// file on disk, written using nothing but the public
/// <c>Tracon.Abstractions</c> package -- no reference to
/// <c>Tracon.Core</c> or any Tracon SQL package.
/// </summary>
/// <remarks>
/// <para>
/// This is a worked example for "how do I write my own <see cref="IRunStore"/>",
/// not a production persistence engine: every mutating call rewrites the
/// whole snapshot file. That is fine at sample scale and would not be at
/// production scale -- a real store backs each entity with its own indexed
/// storage, the way the four shipped providers do.
/// </para>
/// <para>
/// It is exercised end-to-end by
/// <c>Tracon.Testing.Contracts.Xunit</c>'s <c>RunStoreContract</c> (and
/// every other applicable contract) in
/// <c>Tracon.Samples.FileRunStore.Tests</c>, restored from a local NuGet
/// feed built by <c>dotnet pack</c> rather than a project reference -- the
/// same path an external consumer would take.
/// </para>
/// </remarks>
public sealed class JsonFileRunStore : IRunStore
{
    private readonly string _filePath;
    private readonly ITenantContext _tenantContext;
    private readonly Lock _gate = new();

    private Dictionary<Guid, RunRecord> _runs = new();
    private Dictionary<Guid, List<RunEvent>> _events = new();
    private Dictionary<Guid, List<ToolInvocationRecord>> _toolInvocations = new();
    private readonly Dictionary<Guid, DateTimeOffset> _heartbeats = new();

    /// <summary>Creates a store backed by <paramref name="filePath"/>.</summary>
    /// <param name="filePath">
    /// The JSON snapshot file. Its parent directory is created if it does not
    /// exist; the file itself is created on the first write if it does not
    /// exist yet.
    /// </param>
    /// <param name="scores">
    /// The score store consulted by <see cref="GetStatisticsAsync"/> and
    /// <see cref="GetExperimentResultsAsync"/>. Defaults to an in-memory,
    /// non-persistent implementation when not given.
    /// </param>
    /// <param name="tenantContext">
    /// The current tenant's context. Defaults to a fixed <c>"default"</c>
    /// tenant when not given.
    /// </param>
    public JsonFileRunStore(string filePath, IRunScoreStore? scores = null, ITenantContext? tenantContext = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _filePath = filePath;
        _tenantContext = tenantContext ?? new FixedTenantContext("default");
        Scores = scores ?? new InMemoryRunScoreStore(
            _tenantContext,
            runId =>
            {
                lock (_gate)
                {
                    return _runs.TryGetValue(runId, out var run) ? run.AgentName : null;
                }
            });

        Load();
    }

    /// <summary>
    /// The score store this run store reads run-level scores from.
    /// </summary>
    /// <remarks>
    /// Scores are written through <see cref="IRunScoreStore"/>, not through
    /// <see cref="IRunStore"/>, but <see cref="GetExperimentResultsAsync"/>
    /// and <see cref="GetStatisticsAsync"/> read them back. A caller that let
    /// this store build its own default needs this handle to reach the same
    /// backend both stores point at.
    /// </remarks>
    public IRunScoreStore Scores { get; }

    /// <inheritdoc />
    public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var record = new RunRecord
            {
                Id = info.RunId,
                AgentName = info.AgentName,
                Kind = info.Kind,
                WorkflowName = info.WorkflowName,
                Status = info.Status,
                StartedAt = info.StartedAt,
                TenantId = info.TenantId ?? _tenantContext.TenantId,
                UserId = info.UserId,
                Labels = info.Labels,
                SessionId = info.SessionId,
                ModelId = info.ModelId,
                ModelProvider = info.ModelProvider,
                IsStreaming = info.IsStreaming,
                ParentRunId = info.ParentRunId,
                RootRunId = info.RootRunId,
                Depth = info.Depth,
                AgentVersion = info.AgentVersion,
                ExperimentId = info.ExperimentId,
                Variant = info.Variant,
                ReplayOfRunId = info.ReplayOfRunId,
                ContinuedFromRunId = info.ContinuedFromRunId,
            };

            // An UPSERT: a queued run calls this twice with the SAME id. The
            // second call must not reset the event log, and UserId/Labels are
            // COALESCED rather than overwritten -- see IRunStore.StartRunAsync.
            var isNew = !_runs.TryGetValue(record.Id, out var previous);

            if (!isNew)
            {
                record = record with
                {
                    UserId = record.UserId ?? previous!.UserId,
                    Labels = record.Labels ?? previous!.Labels,
                };
            }

            _runs[record.Id] = record;

            if (isNew)
            {
                _events[record.Id] = [];
                _toolInvocations[record.Id] = [];
            }

            Save();

            return new ValueTask<RunRecord>(record);
        }
    }

    /// <inheritdoc />
    public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_events.TryGetValue(runEvent.RunId, out var log))
            {
                throw new TraconException(
                    $"No run with id '{runEvent.RunId}' was found. StartRunAsync must be called before adding an event.");
            }

            EnsureExpectedTenant(runEvent.RunId, runEvent.TenantId, "The event was not written.");

            // A duplicate Sequence is a caller error, not a legitimate retry.
            foreach (var existing in log)
            {
                if (existing.Sequence == runEvent.Sequence)
                {
                    throw new TraconException(
                        $"Run '{runEvent.RunId}' already has an event with sequence '{runEvent.Sequence}'. " +
                        "The event was not written.");
                }
            }

            log.Add(runEvent);
            Save();
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_runs.TryGetValue(completion.RunId, out var existing))
            {
                throw new TraconException($"No run with id '{completion.RunId}' was found.");
            }

            EnsureExpectedTenant(completion.RunId, completion.TenantId, "The run was not completed.");

            _runs[completion.RunId] = existing with
            {
                Status = completion.Status,
                CompletedAt = completion.CompletedAt,
                EventCount = completion.EventCount,
                Usage = completion.Usage,
                Error = completion.Error,
                Cost = completion.Cost,
                ModelId = completion.ModelId ?? existing.ModelId,

                // Null leaves the stored value alone, exactly like ModelId: a
                // fallback link answering is the only reason a completion
                // carries a provider at all.
                ModelProvider = completion.ModelProvider ?? existing.ModelProvider,
            };

            Save();
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask UpdateRunCostAsync(
        Guid runId,
        RunCost? cost,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_runs.TryGetValue(runId, out var existing))
            {
                if (tenantId is not null && !string.Equals(existing.TenantId, tenantId, StringComparison.Ordinal))
                {
                    return default;
                }

                _runs[runId] = existing with { Cost = cost };
                Save();
            }
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask TouchHeartbeatAsync(
        IReadOnlyCollection<Guid> runIds,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runIds);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            foreach (var runId in runIds)
            {
                if (_runs.TryGetValue(runId, out var run) && run.Status == RunStatus.Running)
                {
                    _heartbeats[runId] = at;
                }
            }
        }

        // Heartbeats are not persisted to disk: they are a cheap, high
        // frequency maintenance signal, and a store restart legitimately
        // resets "last seen" to the run's own StartedAt (ClaimOrphanedRunsAsync
        // falls back to it below).
        return default;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(
        DateTimeOffset staleBefore,
        int max,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;

            var candidates = _runs.Values
                .Where(record => record.Status == RunStatus.Running)
                .Select(record => (Record: record, LastSeen: _heartbeats.TryGetValue(record.Id, out var hb) ? hb : record.StartedAt))
                .Where(candidate => candidate.LastSeen < staleBefore)
                .OrderBy(static candidate => candidate.LastSeen)
                .Take(Math.Max(max, 0))
                .ToList();

            var claimed = new List<RunRecord>(candidates.Count);

            foreach (var (record, lastSeen) in candidates)
            {
                var message = $"The process running the run is not responding; last heartbeat: {lastSeen:O}.";

                var updated = record with
                {
                    Status = RunStatus.Failed,
                    CompletedAt = now,
                    Error = new RunError
                    {
                        Type = "orphaned",
                        Message = message,
                        Class = RunErrorClass.Infrastructure,
                        Fingerprint = "orphaned",
                    },
                    EventCount = record.EventCount + 1,
                };

                _runs[record.Id] = updated;
                _heartbeats.Remove(record.Id);

                if (_events.TryGetValue(record.Id, out var log))
                {
                    log.Add(new RunEvent
                    {
                        RunId = record.Id,
                        Sequence = log.Count,
                        Type = RunEventType.RunFailed,
                        Timestamp = now,
                        Text = message,
                    });
                }

                claimed.Add(updated);
            }

            if (claimed.Count > 0)
            {
                Save();
            }

            return new ValueTask<IReadOnlyList<RunRecord>>(claimed);
        }
    }

    /// <inheritdoc />
    public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var record = TryGetOwnedRun(runId);
            return new ValueTask<RunRecord?>(record is null ? null : WithTreeTotals(record));
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var effectiveTenantId = query.TenantId ?? _tenantContext.TenantId;

            HashSet<Guid>? sessionRootIds = null;

            if (query.SessionId is { } sessionId)
            {
                sessionRootIds = [.. _runs.Values
                    .Where(candidate =>
                        string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal)
                        && string.Equals(candidate.TenantId, effectiveTenantId, StringComparison.Ordinal))
                    .Select(static candidate => candidate.Id)];
            }

            var matches = _runs.Values.Where(record =>
            {
                if (query.AgentName is { } agentName && !string.Equals(record.AgentName, agentName, StringComparison.Ordinal))
                {
                    return false;
                }

                if (query.Status is { } status && record.Status != status)
                {
                    return false;
                }

                if (query.Kind is { } kind && record.Kind != kind)
                {
                    return false;
                }

                if (query.UserId is { } userId && !string.Equals(record.UserId, userId, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!MatchesLabel(record, query.LabelKey, query.LabelValue))
                {
                    return false;
                }

                if (!string.Equals(record.TenantId, effectiveTenantId, StringComparison.Ordinal))
                {
                    return false;
                }

                if (sessionRootIds is not null && !sessionRootIds.Contains(record.RootRunId ?? record.Id))
                {
                    return false;
                }

                if (query.ErrorType is { } errorType && !string.Equals(record.Error?.Type, errorType, StringComparison.Ordinal))
                {
                    return false;
                }

                if (query.StartedAfter is { } after && record.StartedAt <= after)
                {
                    return false;
                }

                if (query.RootRunId is { } rootRunId && record.RootRunId != rootRunId && record.Id != rootRunId)
                {
                    return false;
                }

                if (query.ParentRunId is { } parentRunId)
                {
                    if (record.ParentRunId != parentRunId)
                    {
                        return false;
                    }
                }
                else if (query.OnlyRootRuns && record.ParentRunId is not null)
                {
                    return false;
                }

                return true;
            })
            .OrderByDescending(static record => record.StartedAt)
            .ToList();

            var start = Math.Clamp(query.Skip, 0, matches.Count);
            var count = Math.Clamp(query.Take, 0, matches.Count - start);

            IReadOnlyList<RunRecord> page = [.. matches.Skip(start).Take(count).Select(WithTreeTotals)];

            return new ValueTask<IReadOnlyList<RunRecord>>(page);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RunEvent> ReadEventsAsync(
        Guid runId,
        long fromSequence = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RunEvent[] snapshot;

        lock (_gate)
        {
            if (TryGetOwnedRun(runId) is null || !_events.TryGetValue(runId, out var log))
            {
                yield break;
            }

            snapshot = [.. log];
        }

        foreach (var runEvent in snapshot)
        {
            if (runEvent.Sequence < fromSequence)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            yield return runEvent;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask RecordToolInvocationAsync(
        ToolInvocationRecord invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            EnsureExpectedTenant(invocation.RunId, invocation.TenantId, "The tool invocation was not written.");

            if (_toolInvocations.TryGetValue(invocation.RunId, out var log))
            {
                log.Add(invocation);
                Save();
            }
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (TryGetOwnedRun(runId) is null || !_toolInvocations.TryGetValue(runId, out var log))
            {
                return new ValueTask<IReadOnlyList<ToolInvocationRecord>>([]);
            }

            IReadOnlyList<ToolInvocationRecord> ordered = [.. log.OrderBy(static invocation => invocation.CreatedAt)];
            return new ValueTask<IReadOnlyList<ToolInvocationRecord>>(ordered);
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(
        ToolUsageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var perTool = new Dictionary<string, ToolTally>(StringComparer.Ordinal);

            foreach (var (runId, log) in _toolInvocations)
            {
                if (!_runs.TryGetValue(runId, out var run)
                    || !string.Equals(run.TenantId, query.TenantId ?? _tenantContext.TenantId, StringComparison.Ordinal)
                    || (query.StartedAfter is { } after && run.StartedAt <= after))
                {
                    continue;
                }

                foreach (var invocation in log)
                {
                    perTool.TryGetValue(invocation.ToolName, out var tally);
                    perTool[invocation.ToolName] = tally.Add(invocation);
                }
            }

            IReadOnlyList<ToolUsage> usage =
            [
                .. perTool
                    .Select(static pair => pair.Value.ToUsage(pair.Key))
                    .OrderByDescending(static entry => entry.TotalCalls)
                    .ThenBy(static entry => entry.ToolName, StringComparer.Ordinal)
                    .Take(Math.Max(query.MaxTools, 0)),
            ];

            return new ValueTask<IReadOnlyList<ToolUsage>>(usage);
        }
    }

    /// <inheritdoc />
    public async ValueTask<RunStatistics> GetStatisticsAsync(
        RunStatisticsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        List<RunRecord> runs;

        lock (_gate)
        {
            runs = [.. _runs.Values];
        }

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

        var effectiveTenantId = query.TenantId ?? _tenantContext.TenantId;

        foreach (var record in runs)
        {
            if (query.AgentName is { } agentName && !string.Equals(record.AgentName, agentName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(record.TenantId, effectiveTenantId, StringComparison.Ordinal))
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

            // Eval runs are synthetic test calls, not real traffic.
            if (record.Kind == RunKind.Eval)
            {
                continue;
            }

            total++;

            var runScores = record.TenantId is { Length: > 0 } scoreTenantId
                ? await Scores.ListAsync(scoreTenantId, record.Id, cancellationToken).ConfigureAwait(false)
                : [];

            if (runScores.Count > 0)
            {
                scoredRuns++;
            }

            foreach (var score in runScores)
            {
                if (score.Kind != RunScoreKind.Binary)
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

            inputTokens += record.Usage?.InputTokens ?? 0;
            outputTokens += record.Usage?.OutputTokens ?? 0;
            totalTokens += record.Usage?.TotalTokens ?? 0;
            cachedInputTokens += record.Usage?.CachedInputTokens ?? 0;
            reasoningTokens += record.Usage?.ReasoningTokens ?? 0;
            audioInputTokens += record.Usage?.AudioInputTokens ?? 0;
            audioOutputTokens += record.Usage?.AudioOutputTokens ?? 0;

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

            if (record.Cost is { } cost)
            {
                if (cost.Source == PricingSource.Unknown)
                {
                    runsWithUnknownPricing++;
                }

                if (cost.Total() is { } runTotal)
                {
                    costSum = (costSum ?? 0) + runTotal;
                }

                currency ??= cost.Currency;
            }

            var runCost = record.Cost?.Total();

            if (record.UserId is { Length: > 0 } breakdownUserId)
            {
                perUser.TryGetValue(breakdownUserId, out var userTally);
                perUser[breakdownUserId] = userTally.Add(record, runCost);
            }

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

            if (record.AgentVersion is { } agentVersion)
            {
                var key = (record.AgentName, agentVersion);
                perVersion.TryGetValue(key, out var versionTally);
                perVersion[key] = new AgentTally(
                    versionTally.TotalRuns + 1,
                    versionTally.FailedRuns + (record.Status == RunStatus.Failed ? 1 : 0),
                    versionTally.TotalTokens + (record.Usage?.TotalTokens ?? 0));
            }

            if (record.ModelId is { Length: > 0 } modelId)
            {
                perModel.TryGetValue(modelId, out var modelTally);
                var modelCost = runCost is { } modelRunCost ? (modelTally.CostSum ?? 0) + modelRunCost : modelTally.CostSum;
                perModel[modelId] = new ModelTally(
                    modelTally.TotalRuns + 1,
                    modelTally.InputTokens + (record.Usage?.InputTokens ?? 0),
                    modelTally.OutputTokens + (record.Usage?.OutputTokens ?? 0),
                    modelTally.TotalTokens + (record.Usage?.TotalTokens ?? 0),
                    modelCost);
            }
        }

        var maxRows = Math.Max(query.MaxAgents, 0);

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
            .Take(maxRows)
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
                .Take(maxRows)],
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
                .Take(maxRows)],
            ByErrorClass = byErrorClass,
        };
    }

    /// <summary>The upper bound applied when selecting an error class's most frequent clusters.</summary>
    private const int TopErrorClusterCount = 3;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(
        ExperimentResultsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var tenantId = query.TenantId ?? _tenantContext.TenantId;
        List<RunRecord> runs;

        lock (_gate)
        {
            runs = [.. _runs.Values];
        }

        var perVariant = new Dictionary<string, VariantTally>(StringComparer.Ordinal);

        foreach (var record in runs)
        {
            if (record.ExperimentId != query.ExperimentId
                || record.Variant is not { Length: > 0 } variant
                || !string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
            {
                continue;
            }

            var scores = record.TenantId is { Length: > 0 } scoreTenantId
                ? await Scores.ListAsync(scoreTenantId, record.Id, cancellationToken).ConfigureAwait(false)
                : [];

            double sum = 0;
            long count = 0;

            foreach (var score in scores)
            {
                if (score.Kind == RunScoreKind.Numeric && score.MessageId is not { Length: > 0 } && score.Value is { } value)
                {
                    sum += value;
                    count++;
                }
            }

            var averageScore = count == 0 ? (double?)null : sum / count;

            perVariant.TryGetValue(variant, out var tally);
            perVariant[variant] = tally.Add(record, averageScore);
        }

        return [.. perVariant.Select(pair => pair.Value.ToResult(pair.Key))];
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(
        RunTimeSeriesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        RunTimeSeriesBucketing.Validate(query.From, query.To, query.Bucket);

        lock (_gate)
        {
            var step = RunTimeSeriesBucketing.StepFor(query.Bucket);
            var buckets = new SortedDictionary<DateTimeOffset, BucketTally>();

            for (var cursor = RunTimeSeriesBucketing.Truncate(query.From, query.Bucket);
                 cursor < query.To;
                 cursor += step)
            {
                buckets[cursor] = default;
            }

            var effectiveTenantId = query.TenantId ?? _tenantContext.TenantId;

            foreach (var record in _runs.Values)
            {
                if (record.StartedAt < query.From || record.StartedAt >= query.To)
                {
                    continue;
                }

                if (!string.Equals(record.TenantId, effectiveTenantId, StringComparison.Ordinal))
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

                if (query.Kind is { } kind && record.Kind != kind)
                {
                    continue;
                }

                var bucket = RunTimeSeriesBucketing.Truncate(record.StartedAt, query.Bucket);
                buckets.TryGetValue(bucket, out var tally);
                buckets[bucket] = tally.Add(record);
            }

            IReadOnlyList<TimeSeriesPoint> points = [.. buckets.Select(static pair => pair.Value.ToPoint(pair.Key))];

            return new ValueTask<IReadOnlyList<TimeSeriesPoint>>(points);
        }
    }

    private void EnsureExpectedTenant(Guid runId, string? expectedTenantId, string suffix)
    {
        if (expectedTenantId is null)
        {
            return;
        }

        if (_runs.TryGetValue(runId, out var run)
            && !string.Equals(run.TenantId, expectedTenantId, StringComparison.Ordinal))
        {
            throw new TraconException(
                $"Run '{runId}' does not belong to the expected tenant ('{expectedTenantId}'). {suffix}");
        }
    }

    private RunRecord? TryGetOwnedRun(Guid runId)
        => _runs.TryGetValue(runId, out var found)
            && string.Equals(found.TenantId, _tenantContext.TenantId, StringComparison.Ordinal)
                ? found
                : null;

    private static bool MatchesLabel(RunRecord record, string? labelKey, string? labelValue)
    {
        if (labelKey is not { Length: > 0 })
        {
            return true;
        }

        if (record.Labels is not { } labels || !labels.TryGetValue(labelKey, out var actual))
        {
            return false;
        }

        return labelValue is null || string.Equals(actual, labelValue, StringComparison.Ordinal);
    }

    private RunRecord WithTreeTotals(RunRecord record)
    {
        var children = 0;
        long input = 0, output = 0, total = 0;
        var sawUsage = record.Usage is not null;
        long? cachedInput = null, reasoning = null, audioInput = null, audioOutput = null;
        var sawCost = false;
        decimal? inputCost = null, outputCost = null, cachedInputCost = null;
        string? costCurrency = null;
        long unknownPricing = 0;

        static long? Accumulate(long? running, long? value)
            => value is null ? running : (running ?? 0) + value.Value;

        void AccumulateUsage(RunUsage usage)
        {
            input += usage.InputTokens ?? 0;
            output += usage.OutputTokens ?? 0;
            total += usage.TotalTokens ?? 0;

            cachedInput = Accumulate(cachedInput, usage.CachedInputTokens);
            reasoning = Accumulate(reasoning, usage.ReasoningTokens);
            audioInput = Accumulate(audioInput, usage.AudioInputTokens);
            audioOutput = Accumulate(audioOutput, usage.AudioOutputTokens);
        }

        void AccumulateCost(RunCost? cost)
        {
            if (cost is null)
            {
                return;
            }

            sawCost = true;
            costCurrency ??= cost.Currency;

            if (cost.Source == PricingSource.Unknown)
            {
                unknownPricing++;
            }

            if (cost.InputCost is { } ic)
            {
                inputCost = (inputCost ?? 0) + ic;
            }

            if (cost.OutputCost is { } oc)
            {
                outputCost = (outputCost ?? 0) + oc;
            }

            if (cost.CachedInputCost is { } cc)
            {
                cachedInputCost = (cachedInputCost ?? 0) + cc;
            }
        }

        if (record.Usage is { } ownUsage)
        {
            AccumulateUsage(ownUsage);
        }

        AccumulateCost(record.Cost);

        foreach (var candidate in _runs.Values)
        {
            if (candidate.ParentRunId == record.Id)
            {
                children++;
            }

            if (candidate.RootRunId != record.Id)
            {
                continue;
            }

            if (candidate.Usage is { } usage)
            {
                sawUsage = true;
                AccumulateUsage(usage);
            }

            AccumulateCost(candidate.Cost);
        }

        return record with
        {
            ChildRunCount = children,
            TreeUsage = sawUsage
                ? new RunUsage
                {
                    InputTokens = input,
                    OutputTokens = output,
                    TotalTokens = total,
                    CachedInputTokens = cachedInput,
                    ReasoningTokens = reasoning,
                    AudioInputTokens = audioInput,
                    AudioOutputTokens = audioOutput,
                }
                : null,
            TreeCost = sawCost
                ? new RunTreeCost
                {
                    InputCost = inputCost,
                    OutputCost = outputCost,
                    CachedInputCost = cachedInputCost,
                    Currency = costCurrency,
                    RunsWithUnknownPricing = unknownPricing,
                }
                : null,
        };
    }

    [SuppressMessage("Design", "CA1031", Justification = "A missing or corrupt snapshot file starts an EMPTY store rather than crashing construction -- the same posture as a fresh database with no schema applied yet.")]
    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var snapshot = JsonSerializer.Deserialize<Snapshot>(json);

            if (snapshot is null)
            {
                return;
            }

            _runs = snapshot.Runs.ToDictionary(static run => run.Id);
            _events = snapshot.Events.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToList());
            _toolInvocations = snapshot.ToolInvocations.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToList());
        }
        catch (Exception)
        {
            // A corrupt or half-written snapshot starts an empty store rather
            // than crashing construction.
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath);

        if (directory is { Length: > 0 })
        {
            Directory.CreateDirectory(directory);
        }

        var snapshot = new Snapshot(
            [.. _runs.Values],
            _events.ToDictionary(static pair => pair.Key, static pair => (IReadOnlyList<RunEvent>)pair.Value),
            _toolInvocations.ToDictionary(static pair => pair.Key, static pair => (IReadOnlyList<ToolInvocationRecord>)pair.Value));

        var json = JsonSerializer.Serialize(snapshot);

        // A temp-file-then-move write keeps a reader from ever observing a
        // half-written file -- the file system's own atomic rename, not a
        // database transaction, but enough for a sample.
        var tempPath = $"{_filePath}.tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private sealed record Snapshot(
        IReadOnlyList<RunRecord> Runs,
        IReadOnlyDictionary<Guid, IReadOnlyList<RunEvent>> Events,
        IReadOnlyDictionary<Guid, IReadOnlyList<ToolInvocationRecord>> ToolInvocations);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct AgentTally(long TotalRuns, long FailedRuns, long TotalTokens);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ModelTally(long TotalRuns, long InputTokens, long OutputTokens, long TotalTokens, decimal? CostSum);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct BreakdownTally(long TotalRuns, long FailedRuns, long TotalTokens, decimal? CostSum)
    {
        public BreakdownTally Add(RunRecord record, decimal? runCost)
            => new(
                TotalRuns + 1,
                FailedRuns + (record.Status == RunStatus.Failed ? 1 : 0),
                TotalTokens + (record.Usage?.TotalTokens ?? 0),
                runCost is { } cost ? (CostSum ?? 0) + cost : CostSum);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ErrorClusterTally(long Count, string SampleMessage, Guid SampleRunId, DateTimeOffset LastSeenAt)
    {
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

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ToolTally(long TotalCalls, long FailedCalls, double TotalDurationMs, long TimedCalls, DateTimeOffset? LastCalledAt)
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
                AverageDurationMs = TimedCalls == 0 ? null : TotalDurationMs / TimedCalls,
                LastCalledAt = LastCalledAt,
            };
    }

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
        public VariantTally Add(RunRecord record, double? averageScore)
        {
            var settled = record.CompletedAt is not null;
            var costSum = CostSum;

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
                ScoreSum + (averageScore ?? 0),
                ScoredRunCount + (averageScore.HasValue ? 1 : 0));
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

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct BucketTally(long Runs, long FailedRuns, long InputTokens, long OutputTokens, decimal? CostSum, double TotalDurationMs, long SettledCount)
    {
        public BucketTally Add(RunRecord record)
        {
            var settled = record.CompletedAt is not null;
            var costSum = CostSum;

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
}
