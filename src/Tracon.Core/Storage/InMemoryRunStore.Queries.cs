using System.Diagnostics.CodeAnalysis;

namespace Tracon;

/// <summary>
/// Single-run lookup, filtered queries, tenant ownership and tree totals.
/// </summary>
internal sealed partial class InMemoryRunStore
{
    /// <inheritdoc />
    public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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
}
