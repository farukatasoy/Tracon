using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AgentPrism;

/// <summary>
/// Calistirma kayitlarini ve olaylarini surec bellegi icinde tutan depo.
/// </summary>
/// <remarks>
/// <para>
/// Olaylar append-only'dir ve sira numarasina gore saklanir. Yeniden oynatma
/// (<see cref="ReadEventsAsync"/>) kalici depolarla ayni davranisi gosterir.
/// </para>
/// <para>
/// <strong>Sinirlari:</strong> surec omru, tek dugum ve sinirsiz bellek buyumesi.
/// <see cref="MaxRuns"/> ile en eski calistirmalar otomatik dusurulur.
/// Uretimde <c>AgentPrism.PostgreSql</c> kullanin.
/// </para>
/// </remarks>
public sealed class InMemoryRunStore : IRunStore
{
    private readonly ConcurrentDictionary<Guid, RunRecord> _runs = new();
    private readonly ConcurrentDictionary<Guid, List<RunEvent>> _events = new();
    private readonly ConcurrentDictionary<Guid, List<ToolInvocationRecord>> _toolInvocations = new();
    private readonly ConcurrentQueue<Guid> _insertionOrder = new();

    /// <summary>
    /// Bellekte tutulacak ust calistirma sayisi. Asilinca en eski calistirma
    /// ve olaylari dusurulur.
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
            Status = RunStatus.Running,
            StartedAt = info.StartedAt,
            TenantId = info.TenantId,
            SessionId = info.SessionId,
            ModelId = info.ModelId,
            IsStreaming = info.IsStreaming,
            ParentRunId = info.ParentRunId,
            RootRunId = info.RootRunId,
            Depth = info.Depth,
            AgentVersion = info.AgentVersion,
            ExperimentId = info.ExperimentId,
            Variant = info.Variant,
        };

        _runs[record.Id] = record;
        _events[record.Id] = [];
        _toolInvocations[record.Id] = [];
        _insertionOrder.Enqueue(record.Id);

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
                $"'{runEvent.RunId}' kimlikli calistirma bulunamadi. Olay eklemeden once StartRunAsync cagrilmalidir.");
        }

        lock (log)
        {
            log.Add(runEvent);
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        if (!_runs.TryGetValue(completion.RunId, out var existing))
        {
            throw new AgentPrismException($"'{completion.RunId}' kimlikli calistirma bulunamadi.");
        }

        _runs[completion.RunId] = existing with
        {
            Status = completion.Status,
            CompletedAt = completion.CompletedAt,
            EventCount = completion.EventCount,
            Usage = completion.Usage,
            Error = completion.Error,
            Cost = completion.Cost,
        };

        return default;
    }

    /// <inheritdoc />
    public ValueTask UpdateRunCostAsync(Guid runId, RunCost? cost, CancellationToken cancellationToken = default)
    {
        // Calistirma dusurulmusse (MaxRuns) cagri sessizce atilir: bu bir bakim
        // ucudur ve calistirmayi kesmemelidir.
        if (_runs.TryGetValue(runId, out var existing))
        {
            _runs[runId] = existing with { Cost = cost };
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        _runs.TryGetValue(runId, out var record);

        return new ValueTask<RunRecord?>(record is null ? null : WithTreeTotals(record));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

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

            if (query.TenantId is { } tenantId && !string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.SessionId is { } sessionId && !string.Equals(record.SessionId, sessionId, StringComparison.Ordinal))
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

            // Ebeveyn filtresi kok filtresini bilerek gecersiz kilar: ikisi
            // mantiksal olarak celisir ve sessizce bos liste donmek hata
            // ayiklanmasi zor bir davranistir.
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
    /// Kayda alt calistirma sayisini ve agac toplamini ekler.
    /// </summary>
    /// <remarks>
    /// Degerler saklanmaz, okumada hesaplanir. Saklansaydi her alt calistirmanin
    /// tamamlanmasi ustteki her kaydi guncellemek zorunda kalir ve kayit yolu
    /// derinlikle birlikte pahalilasirdi.
    /// </remarks>
    private RunRecord WithTreeTotals(RunRecord record)
    {
        var children = 0;
        long input = 0, output = 0, total = 0;
        var sawUsage = record.Usage is not null;

        input += record.Usage?.InputTokens ?? 0;
        output += record.Usage?.OutputTokens ?? 0;
        total += record.Usage?.TotalTokens ?? 0;

        var sawCost = false;
        decimal? inputCost = null;
        decimal? outputCost = null;
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
                input += usage.InputTokens ?? 0;
                output += usage.OutputTokens ?? 0;
                total += usage.TotalTokens ?? 0;
            }

            AccumulateCost(candidate.Cost);
        }

        return record with
        {
            ChildRunCount = children,
            TreeUsage = sawUsage
                ? new RunUsage { InputTokens = input, OutputTokens = output, TotalTokens = total }
                : null,
            TreeCost = sawCost
                ? new RunTreeCost
                {
                    InputCost = inputCost,
                    OutputCost = outputCost,
                    Currency = costCurrency,
                    RunsWithUnknownPricing = unknownPricing,
                }
                : null,
        };
    }

    /// <inheritdoc />
    public ValueTask<RunStatistics> GetStatisticsAsync(
        RunStatisticsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        long total = 0, completed = 0, failed = 0, canceled = 0, running = 0, awaitingInput = 0;
        long inputTokens = 0, outputTokens = 0, totalTokens = 0;
        var perAgent = new Dictionary<string, AgentTally>(StringComparer.Ordinal);
        var perModel = new Dictionary<string, ModelTally>(StringComparer.Ordinal);
        var perVersion = new Dictionary<(string AgentName, int Version), AgentTally>();
        decimal? costSum = null;
        string? currency = null;
        long runsWithUnknownPricing = 0;

        foreach (var record in _runs.Values)
        {
            if (query.AgentName is { } agentName && !string.Equals(record.AgentName, agentName, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.TenantId is { } tenantId && !string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.StartedAfter is { } after && record.StartedAt <= after)
            {
                continue;
            }

            // Eval vaka calistirmalari sentetik test cagrilaridir, gercek
            // trafik degildir; ozeti kirletmemesi icin haric tutulur
            // (docs/18-DEGERLENDIRME.md, acik soru 4).
            if (record.Kind == RunKind.Eval)
            {
                continue;
            }

            total++;

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

                currency ??= cost.Currency;
            }

            perAgent.TryGetValue(record.AgentName, out var tally);
            perAgent[record.AgentName] = new AgentTally(
                tally.TotalRuns + 1,
                tally.FailedRuns + (record.Status == RunStatus.Failed ? 1 : 0),
                tally.TotalTokens + (record.Usage?.TotalTokens ?? 0));

            // Surumu bilinmeyen calistirmalar kirilima girmez ancak toplamlarda
            // sayilir; aksi halde iki rakam birbirini tutmazdi.
            if (record.AgentVersion is { } agentVersion)
            {
                var key = (record.AgentName, agentVersion);
                perVersion.TryGetValue(key, out var versionTally);
                perVersion[key] = new AgentTally(
                    versionTally.TotalRuns + 1,
                    versionTally.FailedRuns + (record.Status == RunStatus.Failed ? 1 : 0),
                    versionTally.TotalTokens + (record.Usage?.TotalTokens ?? 0));
            }

            // Model adi bilinmeyen calistirmalar kirilima girmez ancak
            // toplamlarda sayilir; aksi halde iki rakam birbirini tutmazdi.
            if (record.ModelId is { Length: > 0 } modelId)
            {
                perModel.TryGetValue(modelId, out var modelTally);
                var modelCost = record.Cost is { InputCost: not null } or { OutputCost: not null }
                    ? (modelTally.CostSum ?? 0) + (record.Cost!.InputCost ?? 0) + (record.Cost!.OutputCost ?? 0)
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

        return new ValueTask<RunStatistics>(new RunStatistics
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
            TotalCost = costSum,
            Currency = currency,
            RunsWithUnknownPricing = runsWithUnknownPricing,
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
        });
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(
        ExperimentResultsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var perVariant = new Dictionary<string, VariantTally>(StringComparer.Ordinal);

        foreach (var record in _runs.Values)
        {
            if (record.ExperimentId != query.ExperimentId || record.Variant is not { Length: > 0 } variant)
            {
                continue;
            }

            if (query.TenantId is { } tenantId && !string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
            {
                continue;
            }

            perVariant.TryGetValue(variant, out var tally);
            perVariant[variant] = tally.Add(record);
        }

        return new ValueTask<IReadOnlyList<ExperimentVariantResult>>(
        [
            .. perVariant.Select(pair => pair.Value.ToResult(pair.Key)),
        ]);
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

        // Bos kovalar da donmelidir (K-152 — zaman serisi grafiginde kesinti,
        // "veri yok" degil "sifir" gibi gorunmeli). PostgreSql'in generate_series'i
        // burada onceden tohumlamayla karsilanir.
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

            if (query.TenantId is { } tenantId && !string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
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

            // Bilerek Eval/Workflow calistirmalarini haric TUTMAZ —
            // GetStatisticsAsync'in aksine (bkz. docs/KARARLAR.md K-152).
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

        // Calistirma dusurulmusse (MaxRuns) cagri sessizce atilir: kayit
        // gozlemlenebilirlik icindir ve calistirmayi kesmemelidir.
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
        if (!_toolInvocations.TryGetValue(runId, out var log))
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

            if (query.TenantId is { } tenantId && !string.Equals(run.TenantId, tenantId, StringComparison.Ordinal))
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
        if (!_events.TryGetValue(runId, out var log))
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

    /// <summary>Bir agent icin biriken sayaclar. Yalnizca ozet hesabinda kullanilir.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct AgentTally(long TotalRuns, long FailedRuns, long TotalTokens);

    /// <summary>Bir model icin biriken sayaclar.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ModelTally(
        long TotalRuns,
        long InputTokens,
        long OutputTokens,
        long TotalTokens,
        decimal? CostSum);

    /// <summary>Bir deney kolu icin biriken sayaclar.</summary>
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
        string? Currency)
    {
        public VariantTally Add(RunRecord record)
        {
            var settled = record.CompletedAt is { } completedAt;
            var costSum = CostSum;

            if (record.Cost is { InputCost: not null } or { OutputCost: not null })
            {
                costSum = (costSum ?? 0) + (record.Cost!.InputCost ?? 0) + (record.Cost!.OutputCost ?? 0);
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
                Currency ?? record.Cost?.Currency);
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
            };
    }

    /// <summary>Bir zaman kovasi icin biriken sayaclar.</summary>
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

            if (record.Cost is { InputCost: not null } or { OutputCost: not null })
            {
                costSum = (costSum ?? 0) + (record.Cost!.InputCost ?? 0) + (record.Cost!.OutputCost ?? 0);
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

    /// <summary>Bir tool icin biriken sayaclar.</summary>
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
                // Payda sureli cagrilardir: sure bildirmeyen cagrilari paydaya
                // katmak ortalamayi yapay olarak dusururdu.
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
        }
    }
}
