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
            Status = RunStatus.Running,
            StartedAt = info.StartedAt,
            TenantId = info.TenantId,
            SessionId = info.SessionId,
            ModelId = info.ModelId,
            IsStreaming = info.IsStreaming,
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
        };

        return default;
    }

    /// <inheritdoc />
    public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        _runs.TryGetValue(runId, out var record);
        return new ValueTask<RunRecord?>(record);
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

            matches.Add(record);
        }

        matches.Sort(static (left, right) => right.StartedAt.CompareTo(left.StartedAt));

        var start = Math.Clamp(query.Skip, 0, matches.Count);
        var count = Math.Clamp(query.Take, 0, matches.Count - start);

        return new ValueTask<IReadOnlyList<RunRecord>>(matches.GetRange(start, count));
    }

    /// <inheritdoc />
    public ValueTask<RunStatistics> GetStatisticsAsync(
        RunStatisticsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        long total = 0, completed = 0, failed = 0, canceled = 0, running = 0;
        long inputTokens = 0, outputTokens = 0, totalTokens = 0;
        var perAgent = new Dictionary<string, AgentTally>(StringComparer.Ordinal);
        var perModel = new Dictionary<string, ModelTally>(StringComparer.Ordinal);

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

            total++;

            switch (record.Status)
            {
                case RunStatus.Completed: completed++; break;
                case RunStatus.Failed: failed++; break;
                case RunStatus.Canceled: canceled++; break;
                case RunStatus.Running: running++; break;
                default: break;
            }

            inputTokens += record.Usage?.InputTokens ?? 0;
            outputTokens += record.Usage?.OutputTokens ?? 0;
            totalTokens += record.Usage?.TotalTokens ?? 0;

            perAgent.TryGetValue(record.AgentName, out var tally);
            perAgent[record.AgentName] = new AgentTally(
                tally.TotalRuns + 1,
                tally.FailedRuns + (record.Status == RunStatus.Failed ? 1 : 0),
                tally.TotalTokens + (record.Usage?.TotalTokens ?? 0));

            // Model adi bilinmeyen calistirmalar kirilima girmez ancak
            // toplamlarda sayilir; aksi halde iki rakam birbirini tutmazdi.
            if (record.ModelId is { Length: > 0 } modelId)
            {
                perModel.TryGetValue(modelId, out var modelTally);
                perModel[modelId] = new ModelTally(
                    modelTally.TotalRuns + 1,
                    modelTally.InputTokens + (record.Usage?.InputTokens ?? 0),
                    modelTally.OutputTokens + (record.Usage?.OutputTokens ?? 0),
                    modelTally.TotalTokens + (record.Usage?.TotalTokens ?? 0));
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
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            ByAgent = byAgent,
            ByModel = [.. perModel
                .Select(static pair => new RunModelStatistics
                {
                    ModelId = pair.Key,
                    TotalRuns = pair.Value.TotalRuns,
                    InputTokens = pair.Value.InputTokens,
                    OutputTokens = pair.Value.OutputTokens,
                    TotalTokens = pair.Value.TotalTokens,
                })
                .OrderByDescending(static model => model.TotalRuns)
                .ThenBy(static model => model.ModelId, StringComparer.Ordinal)],
        });
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
    private readonly record struct ModelTally(long TotalRuns, long InputTokens, long OutputTokens, long TotalTokens);

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
