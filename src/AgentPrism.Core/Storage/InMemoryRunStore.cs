using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
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
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _heartbeats = new();
    private readonly ConcurrentQueue<Guid> _insertionOrder = new();
    private readonly IRunScoreStore _scores;
    private readonly ITenantContext _tenantContext;

    /// <summary>Yeni bir bellek ici calistirma deposu olusturur.</summary>
    /// <param name="scores">
    /// Ozet hesabinda kullanilan puan deposu (<see cref="GetStatisticsAsync"/>).
    /// Verilmezse ozel bir orneği kendisi olusturur -- bu, parametresiz
    /// <c>new InMemoryRunStore()</c> kullanan testleri bozmamak icindir. DI
    /// uzerinden cozumlendiğinde <c>AddAgentPrism()</c>'in kaydettigi paylasilan
    /// tekil orneği alir, boylece HTTP katmaninin yazdigi puanlar ozette gorunur.
    /// </param>
    /// <param name="tenantContext">
    /// Gecerli kiracinin baglami. Verilmezse depo tek kiracili davranir
    /// (Faz 41).
    /// </param>
    public InMemoryRunStore(IRunScoreStore? scores = null, ITenantContext? tenantContext = null)
    {
        _scores = scores ?? new InMemoryRunScoreStore();
        _tenantContext = tenantContext ?? FixedTenantContext.Default;
    }

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
            Status = info.Status,
            StartedAt = info.StartedAt,
            TenantId = info.TenantId ?? _tenantContext.TenantId,
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

        // 🚨 Faz 46: kuyruga alinan bir calistirma icin bu metot AYNI kimlikle
        // IKI kez cagrilir (once Queued, sonra isci Running'e gecirirken). Bu bir
        // UPSERT'tir: satir zaten varsa olay/tool-cagri gunluklerini SIFIRLAMA
        // (araya hicbir olay yazilmamis olsa da) ve siraya IKINCI kez ekleme.
        var isNew = !_runs.ContainsKey(record.Id);

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
                $"'{runEvent.RunId}' kimlikli calistirma bulunamadi. Olay eklemeden once StartRunAsync cagrilmalidir.");
        }

        // BEKLENEN kiraci denetimi (K-355). SQL deposuyla AYNI davranis; sozlesme
        // testleri iki uygulamayi da ayni iddiayla sinar.
        EnsureExpectedTenant(runEvent.RunId, runEvent.TenantId, "Olay yazilmadi.");

        lock (log)
        {
            log.Add(runEvent);
        }

        return default;
    }

    /// <summary>
    /// Yazmanin hedef calistirmasi beklenen kiraciya ait mi.
    /// </summary>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <param name="expectedTenantId">Beklenen kiraci. <see langword="null"/> ise denetim yapilmaz.</param>
    /// <param name="suffix">Hata mesajinin sonuna eklenecek aciklama.</param>
    /// <exception cref="AgentPrismException">Kiraci uyusmuyorsa.</exception>
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
                $"'{runId}' kimlikli calistirma beklenen kiraciya ('{expectedTenantId}') ait degil. {suffix}");
        }
    }

    /// <inheritdoc />
    public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        if (!_runs.TryGetValue(completion.RunId, out var existing))
        {
            throw new AgentPrismException($"'{completion.RunId}' kimlikli calistirma bulunamadi.");
        }

        // BEKLENEN kiraci denetimi (K-355).
        EnsureExpectedTenant(completion.RunId, completion.TenantId, "Calistirma sonlandirilmadi.");

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
    public ValueTask UpdateRunCostAsync(
        Guid runId,
        RunCost? cost,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        // Calistirma dusurulmusse (MaxRuns) cagri sessizce atilir: bu bir bakim
        // ucudur ve calistirmayi kesmemelidir.
        if (_runs.TryGetValue(runId, out var existing))
        {
            // BEKLENEN kiraci uyusmuyorsa yazma SESSIZCE atlanir — SQL tarafinda
            // da WHERE kosulu sifir satir gunceller ve hata firlatilmaz (K-355).
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
            // Yalniz Running satirlari icin anlamlidir; var olmayan veya
            // baska durumdaki bir kimlik sessizce atlanir (bakim sinyali).
            if (_runs.TryGetValue(runId, out var run) && run.Status == RunStatus.Running)
            {
                _heartbeats[runId] = at;
            }
        }

        return default;
    }

    /// <summary>
    /// Oksuz calistirma hatalarinin kumeleme parmak izi. Bkz. kullanim yerindeki
    /// gerekce.
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
            var message = $"Calistirma yuruten surec yanit vermiyor; son isaret: {lastSeen:O}.";
            var error = new RunError
            {
                Type = "orphaned",
                Message = message,
                Class = RunErrorClass.Infrastructure,

                // Sabit bir dize -- bir SHA-256 hash DEGIL. Butun oksuz
                // calistirmalar AYNI arizadir (surec yanit vermiyor); mesaj
                // metnindeki degisken zaman damgasini normallestirmek icin
                // ErrorFingerprint.Compute'a ihtiyac yoktur ve SQL depolari
                // (ayri bir derleme, ErrorFingerprint'e erisemez) bu sabiti
                // birebir aynen kullanir -- iki uygulama arasinda davranis
                // esitligi boylece SADE bir sekilde saglanir.
                Fingerprint = OrphanedFingerprint,
            };

            var updated = record with
            {
                Status = RunStatus.Failed,
                CompletedAt = now,
                Error = error,
                EventCount = record.EventCount + 1,
            };

            // Baska bir yol ayni anda kaydi degistirmis olabilir (ornek:
            // calistirma tam bu sirada normal sekilde tamamlandi); byle bir
            // yaris cok dusuk ihtimalli olsa da TryUpdate atomik korumayi
            // saglar -- kacirilirsa satir bir sonraki turda tekrar denenir.
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

            if (query.Kind is { } kind && record.Kind != kind)
            {
                continue;
            }

            if (!string.Equals(record.TenantId, query.TenantId ?? _tenantContext.TenantId, StringComparison.Ordinal))
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
    /// <summary>Calistirma gecerli kiraciya ait mi.</summary>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <returns>Kayit var ve kiraci esliyorsa <see langword="true"/>.</returns>
    private bool IsOwnedByCurrentTenant(Guid runId) => TryGetOwnedRun(runId, out _);

    /// <summary>Calistirmayi yalnizca gecerli kiraciya aitse dondurur.</summary>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <param name="record">Bulunan kayit; sahiplik yoksa <see langword="null"/>.</param>
    /// <returns>Kayit bulunduysa <see langword="true"/>.</returns>
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
    public async ValueTask<RunStatistics> GetStatisticsAsync(
        RunStatisticsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        long total = 0, completed = 0, failed = 0, canceled = 0, running = 0, awaitingInput = 0;
        long inputTokens = 0, outputTokens = 0, totalTokens = 0;
        long scoredRuns = 0, binaryScores = 0, positiveBinaryScores = 0;
        var perAgent = new Dictionary<string, AgentTally>(StringComparer.Ordinal);
        var perModel = new Dictionary<string, ModelTally>(StringComparer.Ordinal);
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

            // Eval vaka calistirmalari sentetik test cagrilaridir, gercek
            // trafik degildir; ozeti kirletmemesi icin haric tutulur
            // (docs/18-DEGERLENDIRME.md, acik soru 4).
            if (record.Kind == RunKind.Eval)
            {
                continue;
            }

            total++;

            // Bir calistirmanin puanlarini tek tek cekmek (N+1) bellek ici
            // depoda kabul edilebilir; uretim yolu SQL saglayicilarindaki
            // tek sorguluk JOIN'dir. Bkz. docs/31-GERI-BILDIRIM-VE-PUANLAMA.md.
            // TenantId nadiren bos olabilir (RunRecord.TenantId nullable'dir);
            // bos ise bu calistirma icin hicbir puan yazilamamis demektir.
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

            // Hata kirilimi: hata sinifi eklenmeden once yazilmis satirlar
            // (Class == null) Unknown kovasina duser (K-014 -- geriye donuk
            // doldurma yapilmaz).
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
            ByErrorClass = byErrorClass,
        };
    }

    /// <summary>Bir hata sinifinin en sik uc kumesini secerken kesilen ust sinir.</summary>
    private const int TopErrorClusterCount = 3;

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

            if (!string.Equals(record.TenantId, query.TenantId ?? _tenantContext.TenantId, StringComparison.Ordinal))
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

        // BEKLENEN kiraci denetimi (K-355).
        EnsureExpectedTenant(invocation.RunId, invocation.TenantId, "Tool cagrisi yazilmadi.");

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

    /// <summary>Bir hata parmak izi kumesi icin biriken sayaclar.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ErrorClusterTally(long Count, string SampleMessage, Guid SampleRunId, DateTimeOffset LastSeenAt)
    {
        /// <summary>
        /// En son goruntuyu gunceller. "Son" burada calistirmanin baslangic
        /// zamanidir (<see cref="RunRecord.StartedAt"/>) -- kayitta hatanin
        /// kendisine ozgu ayri bir "olustu" zaman damgasi yoktur.
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
            _heartbeats.TryRemove(oldest, out _);
        }
    }
}
