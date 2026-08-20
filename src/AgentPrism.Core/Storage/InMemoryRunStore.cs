using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AgentPrism;

/// <summary>
/// A store that keeps run records and their events in process memory.
/// </summary>
/// <remarks>
/// <para>
/// Events are append-only and stored by sequence number. Replay
/// (<see cref="ReadEventsAsync"/>) behaves the same as with persistent stores.
/// </para>
/// <para>
/// <strong>Limits:</strong> process lifetime, single node, and unbounded
/// memory growth. <see cref="MaxRuns"/> automatically drops the oldest runs.
/// Use <c>AgentPrism.PostgreSql</c> in production.
/// </para>
/// </remarks>
public sealed class InMemoryRunStore : IRunStore
{
    private readonly ConcurrentDictionary<Guid, RunRecord> _runs = new();
    private readonly ConcurrentDictionary<Guid, List<RunEvent>> _events = new();
    private readonly ConcurrentDictionary<Guid, List<ToolInvocationRecord>> _toolInvocations = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _heartbeats = new();
    private readonly ConcurrentQueue<Guid> _insertionOrder = new();
    private readonly IRunScoreStore _scores;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a new in-memory run store.</summary>
    /// <param name="scores">
    /// The score store used in summary computation (<see cref="GetStatisticsAsync"/>).
    /// If not given, creates a private instance of its own -- this is to
    /// avoid breaking tests that use the parameterless <c>new InMemoryRunStore()</c>.
    /// When resolved through DI, it gets the shared singleton instance
    /// registered by <c>AddAgentPrism()</c>, so scores written by the HTTP
    /// layer appear in the summary.
    /// </param>
    /// <param name="tenantContext">
    /// The current tenant's context. If not given, the store behaves as
    /// single-tenant.
    /// </param>
    public InMemoryRunStore(IRunScoreStore? scores = null, ITenantContext? tenantContext = null)
    {
        _scores = scores ?? new InMemoryRunScoreStore();
        _tenantContext = tenantContext ?? FixedTenantContext.Default;
    }

    /// <summary>
    /// The upper bound on the number of runs kept in memory. When exceeded,
    /// the oldest run and its events are dropped.
    /// </summary>
    public int MaxRuns { get; init; } = 1_000;

    /// <inheritdoc />
    public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);

        var record = new RunRecord
        {
            Id = info.RunId,
            AgentName = info.AgentName,
            Kind = info.Kind,
            WorkflowName = info.WorkflowName,
            Status = info.Status,
            StartedAt = info.StartedAt,
            TenantId = info.TenantId ?? _tenantContext.TenantId,
            // 🚨 Set BELOW, after the upsert check: on the second StartRunAsync
            // of a queued run the caller (a background worker) usually does not
            // know the user, and overwriting would erase what the first write
            // got right. The SQL providers COALESCE for the same reason.
            UserId = info.UserId,
            Labels = info.Labels,
            SessionId = info.SessionId,
            ModelId = info.ModelId,
            IsStreaming = info.IsStreaming,
            ParentRunId = info.ParentRunId,
            RootRunId = info.RootRunId,
            Depth = info.Depth,
            AgentVersion = info.AgentVersion,
            ExperimentId = info.ExperimentId,
            Variant = info.Variant,
            ReplayOfRunId = info.ReplayOfRunId,
        };

        // 🚨 Phase 46: for a queued run, this method is called TWICE with the
        // SAME identity (first Queued, then again when the worker moves it to
        // Running). This is an UPSERT: if the row already exists, do NOT
        // RESET the event/tool-invocation logs (even if no event has been
        // written yet), and do not enqueue it a SECOND time.
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
            _insertionOrder.Enqueue(record.Id);
        }

        TrimIfNeeded();

        return new ValueTask<RunRecord>(record);
    }

    /// <inheritdoc />
    public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        if (!_events.TryGetValue(runEvent.RunId, out var log))
        {
            throw new AgentPrismException(
                $"No run with id '{runEvent.RunId}' was found. StartRunAsync must be called before adding an event.");
        }

        // EXPECTED tenant check (K-355). SAME behavior as the SQL store;
        // contract tests exercise both implementations against the same assertion.
        EnsureExpectedTenant(runEvent.RunId, runEvent.TenantId, "The event was not written.");

        lock (log)
        {
            log.Add(runEvent);
        }

        return default;
    }

    /// <summary>
    /// Whether the write's target run belongs to the expected tenant.
    /// </summary>
    /// <param name="runId">The run's identity.</param>
    /// <param name="expectedTenantId">The expected tenant. No check is performed if <see langword="null"/>.</param>
    /// <param name="suffix">Text appended to the end of the error message.</param>
    /// <exception cref="AgentPrismException">The tenant does not match.</exception>
    private void EnsureExpectedTenant(Guid runId, string? expectedTenantId, string suffix)
    {
        if (expectedTenantId is null)
        {
            return;
        }

        if (_runs.TryGetValue(runId, out var run)
            && !string.Equals(run.TenantId, expectedTenantId, StringComparison.Ordinal))
        {
            throw new AgentPrismException(
                $"Run '{runId}' does not belong to the expected tenant ('{expectedTenantId}'). {suffix}");
        }
    }

    /// <inheritdoc />
    public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        if (!_runs.TryGetValue(completion.RunId, out var existing))
        {
            throw new AgentPrismException($"No run with id '{completion.RunId}' was found.");
        }

        // EXPECTED tenant check (K-355).
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
        };

        return default;
    }

    /// <inheritdoc />
    public ValueTask UpdateRunCostAsync(
        Guid runId,
        RunCost? cost,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        // If the run was dropped (MaxRuns), the call is silently discarded:
        // this is a maintenance cost and must not interrupt the run.
        if (_runs.TryGetValue(runId, out var existing))
        {
            // If the EXPECTED tenant does not match, the write is SILENTLY
            // skipped — on the SQL side too, the WHERE condition updates zero
            // rows and no error is thrown (K-355).
            if (tenantId is not null
                && !string.Equals(existing.TenantId, tenantId, StringComparison.Ordinal))
            {
                return default;
            }

            _runs[runId] = existing with { Cost = cost };
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

        foreach (var runId in runIds)
        {
            // Meaningful only for Running rows; an identity that does not
            // exist or is in another status is silently skipped (a maintenance signal).
            if (_runs.TryGetValue(runId, out var run) && run.Status == RunStatus.Running)
            {
                _heartbeats[runId] = at;
            }
        }

        return default;
    }

    /// <summary>
    /// The clustering fingerprint for orphaned-run errors. See the rationale
    /// at the usage site.
    /// </summary>
    private const string OrphanedFingerprint = "orphaned";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(
        DateTimeOffset staleBefore,
        int max,
        CancellationToken cancellationToken = default)
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
            var error = new RunError
            {
                Type = "orphaned",
                Message = message,
                Class = RunErrorClass.Infrastructure,

                // A fixed string -- NOT a SHA-256 hash. Every orphaned run is
                // the SAME failure (the process is not responding); there is
                // no need for ErrorFingerprint.Compute to normalize the
                // variable timestamp in the message text, and the SQL stores
                // (a separate assembly, which cannot reach ErrorFingerprint)
                // use this constant verbatim -- behavioral equality between
                // the two implementations is achieved SIMPLY this way.
                Fingerprint = OrphanedFingerprint,
            };

            var updated = record with
            {
                Status = RunStatus.Failed,
                CompletedAt = now,
                Error = error,
                EventCount = record.EventCount + 1,
            };

            // Another path may have modified the record at the same time (for
            // example, the run completed normally right at this moment);
            // even though such a race is very unlikely, TryUpdate provides
            // atomic protection -- if missed, the row is retried on the next pass.
            if (!_runs.TryUpdate(record.Id, updated, record))
            {
                continue;
            }

            _heartbeats.TryRemove(record.Id, out _);

            if (_events.TryGetValue(record.Id, out var log))
            {
                lock (log)
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
            }

            claimed.Add(updated);
        }

        return new ValueTask<IReadOnlyList<RunRecord>>(claimed);
    }

    /// <inheritdoc />
    public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        TryGetOwnedRun(runId, out var record);

        return new ValueTask<RunRecord?>(record is null ? null : WithTreeTotals(record));
    }

    /// <summary>
    /// Says whether a record satisfies a label filter.
    /// </summary>
    /// <remarks>
    /// A <see langword="null"/> key applies no filter. A key with no value
    /// matches any value of that key; the value alone is meaningless without a
    /// key and is ignored, which mirrors the SQL providers.
    /// </remarks>
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

    /// <summary>Adds an optional counter, staying <see langword="null"/> while NEITHER side reported one.</summary>
    private static long? Accumulate(long? running, long? value)
        => value is null ? running : (running ?? 0) + value.Value;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var effectiveTenantId = query.TenantId ?? _tenantContext.TenantId;

        // HATA-S2-001: SessionId is set only on the ROOT run (K-217); a child
        // run's own SessionId is always null. A direct equality check would
        // therefore never match any child run when given together with
        // "includeChildren=true". First collect the identities of the ROOT
        // runs belonging to this session, then match each record against its
        // own tree's ROOT (RootRunId ?? Id).
        HashSet<Guid>? sessionRootIds = null;

        if (query.SessionId is { } sessionId)
        {
            sessionRootIds = [];

            foreach (var candidate in _runs.Values)
            {
                if (string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal)
                    && string.Equals(candidate.TenantId, effectiveTenantId, StringComparison.Ordinal))
                {
                    sessionRootIds.Add(candidate.Id);
                }
            }
        }

        var matches = new List<RunRecord>();

        foreach (var record in _runs.Values)
        {
            if (query.AgentName is { } agentName && !string.Equals(record.AgentName, agentName, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.Status is { } status && record.Status != status)
            {
                continue;
            }

            if (query.Kind is { } kind && record.Kind != kind)
            {
                continue;
            }

            if (query.UserId is { } userId && !string.Equals(record.UserId, userId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!MatchesLabel(record, query.LabelKey, query.LabelValue))
            {
                continue;
            }

            if (!string.Equals(record.TenantId, effectiveTenantId, StringComparison.Ordinal))
            {
                continue;
            }

            if (sessionRootIds is not null && !sessionRootIds.Contains(record.RootRunId ?? record.Id))
            {
                continue;
            }

            if (query.ErrorType is { } errorType && !string.Equals(record.Error?.Type, errorType, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.StartedAfter is { } after && record.StartedAt <= after)
            {
                continue;
            }

            if (query.RootRunId is { } rootRunId
                && record.RootRunId != rootRunId
                && record.Id != rootRunId)
            {
                continue;
            }

            // The parent filter deliberately overrides the root filter: the
            // two are logically contradictory, and silently returning an
            // empty list is a hard-to-debug behavior.
            if (query.ParentRunId is { } parentRunId)
            {
                if (record.ParentRunId != parentRunId)
                {
                    continue;
                }
            }
            else if (query.OnlyRootRuns && record.ParentRunId is not null)
            {
                continue;
            }

            matches.Add(record);
        }

        matches.Sort(static (left, right) => right.StartedAt.CompareTo(left.StartedAt));

        var start = Math.Clamp(query.Skip, 0, matches.Count);
        var count = Math.Clamp(query.Take, 0, matches.Count - start);
        var page = matches.GetRange(start, count);

        for (var index = 0; index < page.Count; index++)
        {
            page[index] = WithTreeTotals(page[index]);
        }

        return new ValueTask<IReadOnlyList<RunRecord>>(page);
    }

    /// <summary>
    /// Adds the child run count and tree total to the record.
    /// </summary>
    /// <remarks>
    /// Values are not stored; they are computed at read time. If stored,
    /// every child run's completion would have to update every record above
    /// it, and the write path would grow more expensive with depth.
    /// </remarks>
    /// <summary>Whether the run belongs to the current tenant.</summary>
    /// <param name="runId">The run's identity.</param>
    /// <returns><see langword="true"/> if the record exists and the tenant matches.</returns>
    private bool IsOwnedByCurrentTenant(Guid runId) => TryGetOwnedRun(runId, out _);

    /// <summary>Returns the run only if it belongs to the current tenant.</summary>
    /// <param name="runId">The run's identity.</param>
    /// <param name="record">The record found; <see langword="null"/> if not owned.</param>
    /// <returns><see langword="true"/> if the record was found.</returns>
    private bool TryGetOwnedRun(Guid runId, [NotNullWhen(true)] out RunRecord? record)
    {
        if (_runs.TryGetValue(runId, out var found)
            && string.Equals(found.TenantId, _tenantContext.TenantId, StringComparison.Ordinal))
        {
            record = found;
            return true;
        }

        record = null;
        return false;
    }

    private RunRecord WithTreeTotals(RunRecord record)
    {
        var children = 0;
        long input = 0, output = 0, total = 0;
        var sawUsage = record.Usage is not null;

        // 🚨 The four breakdown counters stay NULL-PRESERVING while the three
        // totals above sum from zero: a tree in which no provider ever reported
        // cache usage must report "not measured", not "measured zero".
        long? cachedInput = null, reasoning = null, audioInput = null, audioOutput = null;

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

        if (record.Usage is { } ownUsage)
        {
            AccumulateUsage(ownUsage);
        }

        var sawCost = false;
        decimal? inputCost = null;
        decimal? outputCost = null;
        decimal? cachedInputCost = null;
        string? costCurrency = null;
        long unknownPricing = 0;

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

            // 🚨 A THIRD addend of the tree total, not a subset of InputCost: each
            // run's own InputCost already excludes its cached tokens. Leaving it
            // out under-reports every tree that hit the prompt cache.
            if (cost.CachedInputCost is { } cc)
            {
                cachedInputCost = (cachedInputCost ?? 0) + cc;
            }
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
            // (docs/18-DEGERLENDIRME.md, open question 4).
            if (record.Kind == RunKind.Eval)
            {
                continue;
            }

            total++;

            // Fetching a run's scores one by one (N+1) is acceptable in the
            // in-memory store; the production path is the single-query JOIN
            // in the SQL providers. See docs/31-GERI-BILDIRIM-VE-PUANLAMA.md.
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
    public ValueTask RecordToolInvocationAsync(
        ToolInvocationRecord invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        // EXPECTED tenant check (K-355).
        EnsureExpectedTenant(invocation.RunId, invocation.TenantId, "The tool invocation was not written.");

        // If the run was dropped (MaxRuns), the call is silently discarded:
        // the record is for observability and must not interrupt the run.
        if (_toolInvocations.TryGetValue(invocation.RunId, out var log))
        {
            lock (log)
            {
                log.Add(invocation);
            }
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        if (!IsOwnedByCurrentTenant(runId) || !_toolInvocations.TryGetValue(runId, out var log))
        {
            return new ValueTask<IReadOnlyList<ToolInvocationRecord>>([]);
        }

        ToolInvocationRecord[] snapshot;

        lock (log)
        {
            snapshot = [.. log];
        }

        Array.Sort(snapshot, static (left, right) => left.CreatedAt.CompareTo(right.CreatedAt));

        return new ValueTask<IReadOnlyList<ToolInvocationRecord>>(snapshot);
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

    /// <inheritdoc />
    public async IAsyncEnumerable<RunEvent> ReadEventsAsync(
        Guid runId,
        long fromSequence = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!IsOwnedByCurrentTenant(runId) || !_events.TryGetValue(runId, out var log))
        {
            yield break;
        }

        RunEvent[] snapshot;

        lock (log)
        {
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

    private void TrimIfNeeded()
    {
        while (_runs.Count > MaxRuns && _insertionOrder.TryDequeue(out var oldest))
        {
            _runs.TryRemove(oldest, out _);
            _events.TryRemove(oldest, out _);
            _toolInvocations.TryRemove(oldest, out _);
            _heartbeats.TryRemove(oldest, out _);
        }
    }
}
