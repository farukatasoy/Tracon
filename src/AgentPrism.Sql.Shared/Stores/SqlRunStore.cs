using System.Data.Common;
using System.Runtime.CompilerServices;

namespace AgentPrism;

/// <summary>
/// Calistirma kayitlarini ve olay akisini PostgreSQL'de saklayan depo.
/// </summary>
/// <remarks>
/// <para>
/// Olaylar <em>append-only</em>'dir (karar K-014). Sira numarasini
/// <see cref="RunEventWriter"/> uretir; depo yalnizca yazar.
/// </para>
/// <para>
/// Davranis sozlesmesi <see cref="InMemoryRunStore"/> ile birebir aynidir ve ortak
/// sozlesme testleriyle korunur. Tum islemler <see cref="ITenantContext.TenantId"/>
/// ile sinirlidir.
/// </para>
/// </remarks>
internal sealed class SqlRunStore : IRunStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Yeni bir calistirma deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SqlRunStore(
        SqlStoreContext context,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _context = context;
        _sql = context.Sql;
        _tenantContext = tenantContext;
    }

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    /// <remarks>
    /// 🚨 Faz 46: <c>_sql.InsertRun</c> bir UPSERT'tir (<c>id</c> uzerinde
    /// catisirsa GUNCELLER). Kuyruga alinan bir calistirma icin bu metot AYNI
    /// <see cref="RunStartInfo.RunId"/> ile iki kez cagrilir — once
    /// <see cref="RunStatus.Queued"/> ile (HTTP katmani), sonra isci is'i
    /// gercekten calistirirken (bu kez varsayilan <see cref="RunStatus.Running"/>
    /// ile). Duz bir INSERT olsaydi ikinci cagri birincil anahtar catismasi
    /// uretirdi.
    /// </remarks>
    public async ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
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

        var command = CreateCommand(_sql.InsertRun);
        DbHelpers.Add(command, "id", record.Id);
        DbHelpers.Add(command, "tenant_id", record.TenantId!);
        DbHelpers.Add(command, "agent_name", record.AgentName);
        AddNullableText(command, "session_id", record.SessionId);
        AddNullableText(command, "model_id", record.ModelId);
        DbHelpers.Add(command, "status", (short)record.Status);
        Dialect.AddTimestamp(command, "started_at", record.StartedAt);
        DbHelpers.Add(command, "is_streaming", record.IsStreaming);
        AddNullableUuid(command, "parent_run_id", record.ParentRunId);
        AddNullableUuid(command, "root_run_id", record.RootRunId);
        DbHelpers.Add(command, "kind", (short)record.Kind);
        AddNullableText(command, "workflow_name", record.WorkflowName);
        AddNullableInt32(command, "agent_version", record.AgentVersion);
        AddNullableUuid(command, "experiment_id", record.ExperimentId);
        AddNullableText(command, "variant", record.Variant);
        AddNullableUuid(command, "replay_of_run_id", record.ReplayOfRunId);

        // Derinlik smallint sutunudur; kaynagi butcenin MaxDepth degeridir ve
        // hicbir kurulumda short sinirina yaklasmaz.
        DbHelpers.Add(command, "depth", (short)Math.Clamp(record.Depth, 0, short.MaxValue));

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        return record;
    }

    /// <inheritdoc />
    public async ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        var command = CreateCommand(_sql.InsertRunEvent);
        DbHelpers.Add(command, "run_id", runEvent.RunId);
        DbHelpers.Add(command, "seq", runEvent.Sequence);
        DbHelpers.Add(command, "type", (short)runEvent.Type);
        AddNullableText(command, "text", runEvent.Text);
        AddNullableText(command, "tool_name", runEvent.ToolName);
        AddNullableText(command, "tool_call_id", runEvent.ToolCallId);
        AddNullableText(command, "payload", runEvent.Payload);
        Dialect.AddTimestamp(command, "created_at", runEvent.Timestamp);

        // 🚨 BEKLENEN kiraci; ambient kiraci DEGIL. RunStartInfo.TenantId ambient
        // kiraciyi bilerek ezebildigi icin (workflow ve is kuyrugu boyle calisir)
        // ambient ile suzmek mesru yazmalari dusururdu. NULL ise denetim yok.
        // Gerekce: K-355.
        AddNullableText(command, "tenant_id", runEvent.TenantId);

        int affected;

        try
        {
            affected = await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (DbException ex) when (Dialect.IsForeignKeyViolation(ex))
        {
            throw new AgentPrismException(
                $"'{runEvent.RunId}' kimlikli calistirma bulunamadi. " +
                "Olay eklemeden once StartRunAsync cagrilmalidir.",
                ex);
        }

        if (affected == 0)
        {
            throw new AgentPrismException(
                $"'{runEvent.RunId}' kimlikli calistirma bulunamadi veya beklenen kiraciya " +
                $"('{runEvent.TenantId}') ait degil. Olay yazilmadi.");
        }
    }

    /// <inheritdoc />
    public async ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        var command = CreateCommand(_sql.UpdateRunCompletion);
        DbHelpers.Add(command, "id", completion.RunId);
        DbHelpers.Add(command, "status", (short)completion.Status);
        Dialect.AddTimestamp(command, "completed_at", completion.CompletedAt);
        DbHelpers.Add(command, "event_count", completion.EventCount);
        AddNullableInt64(command, "input_tokens", completion.Usage?.InputTokens);
        AddNullableInt64(command, "output_tokens", completion.Usage?.OutputTokens);
        AddNullableInt64(command, "total_tokens", completion.Usage?.TotalTokens);
        AddNullableText(command, "error_type", completion.Error?.Type);
        AddNullableText(command, "error_message", completion.Error?.Message);
        Dialect.AddInt16(command, "error_class", completion.Error?.Class is { } errorClass ? (short)errorClass : null);
        AddNullableText(command, "error_fingerprint", completion.Error?.Fingerprint);
        AddNullableDecimal(command, "input_cost", completion.Cost?.InputCost);
        AddNullableDecimal(command, "output_cost", completion.Cost?.OutputCost);
        AddNullableText(command, "cost_currency", completion.Cost?.Currency);
        Dialect.AddInt16(command, "pricing_source", completion.Cost is { } cost ? (short)cost.Source : null);

        // BEKLENEN kiraci (K-355). NULL ise denetim yok.
        AddNullableText(command, "tenant_id", completion.TenantId);

        var affected = await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new AgentPrismException(
                $"'{completion.RunId}' kimlikli calistirma bulunamadi" +
                (completion.TenantId is null
                    ? "."
                    : $" veya beklenen kiraciya ('{completion.TenantId}') ait degil."));
        }
    }

    /// <inheritdoc />
    public async ValueTask UpdateRunCostAsync(
        Guid runId,
        RunCost? cost,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.UpdateRunCost);
        DbHelpers.Add(command, "id", runId);
        AddNullableDecimal(command, "input_cost", cost?.InputCost);
        AddNullableDecimal(command, "output_cost", cost?.OutputCost);
        AddNullableText(command, "cost_currency", cost?.Currency);
        Dialect.AddInt16(command, "pricing_source", cost is { } value ? (short)value.Source : null);

        // BEKLENEN kiraci (K-355). Bakim ucu (POST /api/stats/recalculate-costs)
        // kimlikleri kiraciya gore SUZULMUS bir sorgudan alir ve ayni kiraciyi
        // buraya da tasir; boylece iki asamali yolda yaris kalmaz.
        AddNullableText(command, "tenant_id", tenantId);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Kimlikler cagiran surecin KENDI IRunCancellationRegistry defterinden gelir ve zaten " +
        "o surecin gercekten yuruttugu calistirmalarla sinirlidir; bakim sinyalidir, veri okumaz.")]
    public async ValueTask TouchHeartbeatAsync(
        IReadOnlyCollection<Guid> runIds,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runIds);

        // Tek bir toplu UPDATE (WHERE id IN (dizi)) SQLite/SQL Server'da dizi
        // parametresini JSON metnine cevirmeyi gerektirirdi; uuid harf
        // buyuklugu (K-191) ve dizi serilestirmesinin (System.Text.Json,
        // kucuk harf) UYUSMAMASI riski tasirdi. Bu surecte AYNI ANDA suren
        // calistirma sayisi kucuktur (IRunCancellationRegistry.ActiveRunIds);
        // dongude N tekil UPDATE, bu riski almadan ayni sonucu verir.
        foreach (var runId in runIds)
        {
            var command = CreateCommand(_sql.TouchRunHeartbeat);
            DbHelpers.Add(command, "id", runId);
            Dialect.AddTimestamp(command, "at", at);
            DbHelpers.Add(command, "status_running", (short)RunStatus.Running);

            await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Bir bakim isidir ve BUTUN kiracilarin oksuz satirlarini tarar; ambient kiraciyla " +
        "suzmek diger kiracilarin satirlarini sonsuza dek Running birakirdi.")]
    public async ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(
        DateTimeOffset staleBefore,
        int max,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var command = CreateCommand(_sql.ClaimOrphanedRuns);
        DbHelpers.Add(command, "status_failed", (short)RunStatus.Failed);
        Dialect.AddTimestamp(command, "now", now);
        DbHelpers.Add(command, "error_class", (short)RunErrorClass.Infrastructure);
        DbHelpers.Add(command, "error_fingerprint", OrphanedFingerprint);
        DbHelpers.Add(command, "status_running", (short)RunStatus.Running);
        Dialect.AddTimestamp(command, "stale_before", staleBefore);
        DbHelpers.Add(command, "max", Math.Max(max, 0));

        var claimed = await DbHelpers.ReadListAsync(command, ReadOrphanedRun, cancellationToken).ConfigureAwait(false);

        // RunEventWriter o surecte artik yoktur; olayi biz yaziyoruz. Bir
        // sonraki tur bu run_id'yi bir daha GORMEZ (status artik Failed'dir),
        // bu yuzden N ayri INSERT (birleştirilmiş tek bir yazma yerine) burada
        // sicak yol maliyeti degildir -- MaxRunsPerScan ile sinirli, dakikada
        // bir kosan bir bakim isidir.
        foreach (var record in claimed)
        {
            var eventCommand = CreateCommand(_sql.InsertOrphanRunEvent);
            DbHelpers.Add(eventCommand, "run_id", record.Id);
            DbHelpers.Add(eventCommand, "type", (short)RunEventType.RunFailed);
            AddNullableText(eventCommand, "text", record.Error?.Message);
            Dialect.AddTimestamp(eventCommand, "created_at", now);

            await DbHelpers.ExecuteAsync(eventCommand, cancellationToken).ConfigureAwait(false);
        }

        return claimed;
    }

    /// <summary>
    /// Oksuz calistirma hatalarinin kumeleme parmak izi. Sabit bir dize --
    /// bir SHA-256 hash DEGIL; gerekce <see cref="InMemoryRunStore"/>'daki
    /// ayni adli sabitle aynidir (AgentPrism.Core'un ErrorFingerprint'i bu
    /// derlemeden erisilemez, K-176).
    /// </summary>
    private const string OrphanedFingerprint = "orphaned";

    /// <inheritdoc />
    public async ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectRun);
        DbHelpers.Add(command, "id", runId);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);

        return await DbHelpers.ReadSingleAsync(command, ReadRun, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(
        RunQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectRuns);
        DbHelpers.Add(command, "tenant_id", query.TenantId ?? _tenantContext.TenantId);
        AddNullableText(command, "agent_name", query.AgentName);
        Dialect.AddInt16(command, "status", (short?)query.Status);
        Dialect.AddInt16(command, "kind", (short?)query.Kind);
        AddNullableText(command, "session_id", query.SessionId);
        AddNullableText(command, "error_type", query.ErrorType);
        Dialect.AddTimestamp(command, "started_after", query.StartedAfter);
        AddNullableUuid(command, "parent_run_id", query.ParentRunId);
        AddNullableUuid(command, "root_run_id", query.RootRunId);
        DbHelpers.Add(command, "only_root_runs", query.OnlyRootRuns);
        DbHelpers.Add(command, "skip", Math.Max(query.Skip, 0));
        DbHelpers.Add(command, "take", Math.Max(query.Take, 0));

        return await DbHelpers.ReadListAsync(command, ReadRun, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<RunStatistics> GetStatisticsAsync(
        RunStatisticsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectRunStatistics);
        DbHelpers.Add(command, "tenant_id", query.TenantId ?? _tenantContext.TenantId);
        AddNullableText(command, "agent_name", query.AgentName);
        Dialect.AddTimestamp(command, "started_after", query.StartedAfter);
        DbHelpers.Add(command, "max_agents", Math.Max(query.MaxAgents, 0));
        DbHelpers.Add(command, "status_running", (short)RunStatus.Running);
        DbHelpers.Add(command, "status_completed", (short)RunStatus.Completed);
        DbHelpers.Add(command, "status_failed", (short)RunStatus.Failed);
        DbHelpers.Add(command, "status_canceled", (short)RunStatus.Canceled);
        DbHelpers.Add(command, "status_awaiting", (short)RunStatus.AwaitingInput);
        DbHelpers.Add(command, "kind_eval", (short)RunKind.Eval);
        DbHelpers.Add(command, "pricing_source_unknown", (short)PricingSource.Unknown);
        DbHelpers.Add(command, "kind_binary", (short)RunScoreKind.Binary);
        DbHelpers.Add(command, "top_clusters", (long)TopErrorClusterCount);

        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using (reader.ConfigureAwait(false))
            {
                // Birinci sonuc kumesi: toplam ozet. Toplama sorgusu her zaman
                // tam olarak bir satir dondurur.
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return EmptyStatistics;
                }

                var total = reader.GetInt64(0);
                var completed = reader.GetInt64(1);
                var failed = reader.GetInt64(2);
                var canceled = reader.GetInt64(3);
                var running = reader.GetInt64(4);
                var awaitingInput = reader.GetInt64(5);
                var inputTokens = reader.GetInt64(6);
                var outputTokens = reader.GetInt64(7);
                var totalTokens = reader.GetInt64(8);
                var totalCost = DbHelpers.GetNullableDecimal(reader, 9);
                var currency = DbHelpers.GetNullableString(reader, 10);
                var runsWithUnknownPricing = reader.GetInt64(11);
                var scoredRuns = reader.GetInt64(12);
                var positiveRate = reader.IsDBNull(13) ? (double?)null : reader.GetDouble(13);

                // Ikinci sonuc kumesi: agent kirilimi.
                var byAgent = new List<RunAgentStatistics>();

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        byAgent.Add(new RunAgentStatistics
                        {
                            AgentName = reader.GetString(0),
                            TotalRuns = reader.GetInt64(1),
                            FailedRuns = reader.GetInt64(2),
                            TotalTokens = reader.GetInt64(3),
                        });
                    }
                }

                // Ucuncu sonuc kumesi: model kirilimi. model_id NULL olan
                // calistirmalar sorguda elenir; toplamlarda ise sayilirlar.
                var byModel = new List<RunModelStatistics>();

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        byModel.Add(new RunModelStatistics
                        {
                            ModelId = reader.GetString(0),
                            TotalRuns = reader.GetInt64(1),
                            InputTokens = reader.GetInt64(2),
                            OutputTokens = reader.GetInt64(3),
                            TotalTokens = reader.GetInt64(4),
                            TotalCost = DbHelpers.GetNullableDecimal(reader, 5),
                        });
                    }
                }

                // Dorduncu sonuc kumesi: surum kirilimi. agent_version NULL olan
                // calistirmalar sorguda elenir; toplamlarda ise sayilirlar.
                var byVersion = new List<RunVersionStatistics>();

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        byVersion.Add(new RunVersionStatistics
                        {
                            AgentName = reader.GetString(0),
                            Version = reader.GetInt32(1),
                            TotalRuns = reader.GetInt64(2),
                            FailedRuns = reader.GetInt64(3),
                            TotalTokens = reader.GetInt64(4),
                        });
                    }
                }

                // Besinci sonuc kumesi: hata sinifi kirilimi (Faz 44).
                var errorTotals = new List<(RunErrorClass Class, long TotalRuns)>();

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        errorTotals.Add(((RunErrorClass)reader.GetInt16(0), reader.GetInt64(1)));
                    }
                }

                // Altinci sonuc kumesi: sinif basina en sik uc parmak izi kumesi,
                // sinifa gore SIRALI doner (SQL metni bunu garanti eder).
                var clustersByClass = new Dictionary<RunErrorClass, List<RunErrorCluster>>();

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var errorClass = (RunErrorClass)reader.GetInt16(0);

                        if (!clustersByClass.TryGetValue(errorClass, out var clusters))
                        {
                            clusters = [];
                            clustersByClass[errorClass] = clusters;
                        }

                        clusters.Add(new RunErrorCluster
                        {
                            Fingerprint = reader.GetString(1),
                            Count = reader.GetInt64(2),
                            SampleMessage = reader.GetString(3),
                            SampleRunId = reader.GetGuid(4),
                            LastSeenAt = DbHelpers.GetTimestamp(reader, 5),
                        });
                    }
                }

                var byErrorClass = errorTotals
                    .Select(pair => new RunErrorStatistics
                    {
                        Class = pair.Class,
                        TotalRuns = pair.TotalRuns,
                        TopClusters = clustersByClass.TryGetValue(pair.Class, out var clusters)
                            ? clusters
                            : [],
                    })
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
                    TotalCost = totalCost,
                    Currency = currency,
                    RunsWithUnknownPricing = runsWithUnknownPricing,
                    ScoredRuns = scoredRuns,
                    PositiveRate = positiveRate,
                    ByAgent = byAgent,
                    ByModel = byModel,
                    ByVersion = byVersion,
                    ByErrorClass = byErrorClass,
                };
            }
        }
    }

    /// <summary>Bir hata sinifinin en sik uc kumesini secerken kesilen ust sinir.</summary>
    private const int TopErrorClusterCount = 3;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(
        RunTimeSeriesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        RunTimeSeriesBucketing.Validate(query.From, query.To, query.Bucket);

        var command = CreateCommand(_sql.SelectRunTimeSeries);
        DbHelpers.Add(command, "tenant_id", query.TenantId ?? _tenantContext.TenantId);
        Dialect.AddTimestamp(command, "from_ts", query.From);
        Dialect.AddTimestamp(command, "to_ts", query.To);
        DbHelpers.Add(command, "bucket_unit", query.Bucket == TimeSeriesBucket.Hour ? "hour" : "day");
        Dialect.AddInterval(command, "bucket_step", RunTimeSeriesBucketing.StepFor(query.Bucket));
        DbHelpers.Add(command, "status_failed", (short)RunStatus.Failed);
        AddNullableText(command, "agent_name", query.AgentName);
        AddNullableText(command, "model_id", query.ModelId);
        Dialect.AddInt16(command, "kind", (short?)query.Kind);

        return await DbHelpers.ReadListAsync(command, ReadTimeSeriesPoint, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RunEvent> ReadEventsAsync(
        Guid runId,
        long fromSequence = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectRunEvents);
        DbHelpers.Add(command, "run_id", runId);
        DbHelpers.Add(command, "from_sequence", fromSequence);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);

        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    yield return ReadEvent(reader);
                }
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask RecordToolInvocationAsync(
        ToolInvocationRecord invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var command = CreateCommand(_sql.InsertToolInvocation);
        DbHelpers.Add(command, "id", invocation.Id);
        DbHelpers.Add(command, "run_id", invocation.RunId);
        DbHelpers.Add(command, "tool_name", invocation.ToolName);
        AddNullableText(command, "tool_call_id", invocation.ToolCallId);
        AddNullableText(command, "source", invocation.Source);
        AddNullableText(command, "arguments", invocation.Arguments);
        AddNullableText(command, "result", invocation.Result);
        // Sure `integer` sutununda milisaniye olarak saklanir; 24 gunden
        // uzun bir tool cagrisi gercekci degildir ve tasma olusmaz.
        Dialect.AddInt32(command, "duration_ms", invocation.Duration is { } duration
                ? (int)Math.Clamp(duration.TotalMilliseconds, 0, int.MaxValue)
                : null);
        AddNullableText(command, "error", invocation.Error);
        Dialect.AddTimestamp(command, "created_at", invocation.CreatedAt);

        // Token DISI olcum (Faz 28). Cagrilarin cogunlugu olcum tasimaz ve bes
        // sutun da NULL kalir. Fiyat tanimsizsa `cost` SIFIR degil NULL yazilir.
        AddNullableText(command, "usage_unit", invocation.Usage?.Unit);
        Dialect.AddDecimal(command, "usage_quantity", invocation.Usage?.Quantity);
        Dialect.AddNullableBoolean(command, "usage_estimated", invocation.Usage?.IsEstimated);
        Dialect.AddDecimal(command, "cost", invocation.Usage?.Cost);
        AddNullableText(command, "cost_currency", invocation.Usage?.Currency);

        // BEKLENEN kiraci (K-355). NULL ise denetim yok.
        AddNullableText(command, "tenant_id", invocation.TenantId);

        int affected;

        try
        {
            affected = await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (DbException ex) when (Dialect.IsForeignKeyViolation(ex))
        {
            throw new AgentPrismException(
                $"'{invocation.RunId}' kimlikli calistirma bulunamadi. " +
                "Tool cagrisi kaydetmeden once StartRunAsync cagrilmalidir.",
                ex);
        }

        if (affected == 0)
        {
            throw new AgentPrismException(
                $"'{invocation.RunId}' kimlikli calistirma bulunamadi veya beklenen kiraciya " +
                $"('{invocation.TenantId}') ait degil. Tool cagrisi yazilmadi.");
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectToolInvocations);
        DbHelpers.Add(command, "run_id", runId);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);

        return await DbHelpers
            .ReadListAsync(command, ReadToolInvocation, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(
        ToolUsageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectToolUsage);
        DbHelpers.Add(command, "tenant_id", query.TenantId ?? _tenantContext.TenantId);
        Dialect.AddTimestamp(command, "started_after", query.StartedAfter);
        DbHelpers.Add(command, "max_tools", Math.Max(query.MaxTools, 0));

        return await DbHelpers
            .ReadListAsync(command, ReadToolUsage, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(
        ExperimentResultsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectExperimentResults);
        DbHelpers.Add(command, "tenant_id", query.TenantId ?? _tenantContext.TenantId);
        DbHelpers.Add(command, "experiment_id", query.ExperimentId);
        DbHelpers.Add(command, "status_completed", (short)RunStatus.Completed);
        DbHelpers.Add(command, "status_failed", (short)RunStatus.Failed);
        DbHelpers.Add(command, "status_canceled", (short)RunStatus.Canceled);
        DbHelpers.Add(command, "score_kind_numeric", (short)RunScoreKind.Numeric);

        return await DbHelpers
            .ReadListAsync(command, ReadExperimentVariantResult, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Hic satir donmeyen ozet sorgusu icin notr sonuc.</summary>
    private static RunStatistics EmptyStatistics { get; } = new()
    {
        TotalRuns = 0,
        CompletedRuns = 0,
        FailedRuns = 0,
        CanceledRuns = 0,
        RunningRuns = 0,
    };

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static RunEvent ReadEvent(DbDataReader reader)
        => new()
        {
            RunId = reader.GetGuid(0),
            Sequence = reader.GetInt64(1),
            Type = (RunEventType)reader.GetInt16(2),
            Text = DbHelpers.GetNullableString(reader, 3),
            ToolName = DbHelpers.GetNullableString(reader, 4),
            ToolCallId = DbHelpers.GetNullableString(reader, 5),
            Payload = DbHelpers.GetNullableString(reader, 6),
            Timestamp = DbHelpers.GetTimestamp(reader, 7),
        };

    /// <summary>
    /// <c>ClaimOrphanedRuns</c>'in RETURNING/OUTPUT sutunlarini okur. Bilerek
    /// <see cref="ReadRun"/>'dan AYRIDIR: o, agac toplamlarinin LATERAL/OUTER
    /// APPLY birlestirmesine dayanir; oksuz kapama bu maliyeti gerektirmez --
    /// dogan satirin ne kullanimi ne maliyeti vardir (henuz hic tamamlanmamis
    /// bir calistirma).
    /// </summary>
    private static RunRecord ReadOrphanedRun(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            AgentName = reader.GetString(2),
            SessionId = DbHelpers.GetNullableString(reader, 3),
            Status = (RunStatus)reader.GetInt16(4),
            StartedAt = DbHelpers.GetTimestamp(reader, 5),
            CompletedAt = reader.IsDBNull(6) ? null : DbHelpers.GetTimestamp(reader, 6),
            IsStreaming = reader.GetBoolean(7),
            ModelId = DbHelpers.GetNullableString(reader, 8),
            Kind = (RunKind)reader.GetInt16(9),
            WorkflowName = DbHelpers.GetNullableString(reader, 10),
            AgentVersion = reader.IsDBNull(11) ? null : reader.GetInt32(11),
            ExperimentId = reader.IsDBNull(12) ? null : reader.GetGuid(12),
            Variant = DbHelpers.GetNullableString(reader, 13),
            ReplayOfRunId = reader.IsDBNull(14) ? null : reader.GetGuid(14),
            ParentRunId = reader.IsDBNull(15) ? null : reader.GetGuid(15),
            RootRunId = reader.IsDBNull(16) ? null : reader.GetGuid(16),
            Depth = reader.GetInt16(17),
            EventCount = reader.GetInt64(18),
            Error = new RunError
            {
                Type = reader.GetString(19),
                Message = DbHelpers.GetNullableString(reader, 20) ?? string.Empty,
                Class = reader.IsDBNull(21) ? null : (RunErrorClass)reader.GetInt16(21),
                Fingerprint = DbHelpers.GetNullableString(reader, 22),
            },
        };

    private static RunRecord ReadRun(DbDataReader reader)
    {
        var errorType = DbHelpers.GetNullableString(reader, 12);
        var ownUsage = ReadUsage(reader);
        var ownCost = ReadCost(reader);

        return new RunRecord
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            AgentName = reader.GetString(2),
            SessionId = DbHelpers.GetNullableString(reader, 3),
            Status = (RunStatus)reader.GetInt16(4),
            StartedAt = DbHelpers.GetTimestamp(reader, 5),
            CompletedAt = reader.IsDBNull(6) ? null : DbHelpers.GetTimestamp(reader, 6),
            IsStreaming = reader.GetBoolean(7),
            Usage = ownUsage,
            EventCount = reader.GetInt64(11),
            ModelId = DbHelpers.GetNullableString(reader, 14),
            ParentRunId = reader.IsDBNull(15) ? null : reader.GetGuid(15),
            RootRunId = reader.IsDBNull(16) ? null : reader.GetGuid(16),
            Depth = reader.GetInt16(17),
            ChildRunCount = reader.IsDBNull(18) ? 0 : reader.GetInt32(18),
            TreeUsage = ReadTreeUsage(reader, ownUsage),
            Kind = (RunKind)reader.GetInt16(23),
            WorkflowName = DbHelpers.GetNullableString(reader, 24),
            AgentVersion = reader.IsDBNull(25) ? null : reader.GetInt32(25),
            ExperimentId = reader.IsDBNull(26) ? null : reader.GetGuid(26),
            Variant = DbHelpers.GetNullableString(reader, 27),
            Cost = ownCost,
            TreeCost = ReadTreeCost(reader, ownCost),

            // 🚨 39: Faz 47'de SONA eklendi. Eski satirlarda NULL'dur; bu satir
            // bir yeniden oynatma degildir demektir.
            ReplayOfRunId = reader.IsDBNull(39) ? null : reader.GetGuid(39),

            // 🚨 37-38: HER ZAMAN sona eklenen sutunlar (Faz 44). Eski
            // satirlarda error_class NULL'dur -- Unknown kovasina duser
            // (RunStatistics.ByErrorClass, K-014 -- geriye donuk doldurma yok).
            Error = errorType is null
                ? null
                : new RunError
                {
                    Type = errorType,
                    Message = DbHelpers.GetNullableString(reader, 13) ?? string.Empty,
                    Class = reader.IsDBNull(37) ? null : (RunErrorClass)reader.GetInt16(37),
                    Fingerprint = DbHelpers.GetNullableString(reader, 38),
                },
        };
    }

    /// <summary>Agacin token toplamini kaydin kendi kullanimiyla birlestirir.</summary>
    /// <remarks>
    /// Alt sorgu yalnizca <em>altindaki</em> calistirmalari toplar; kaydin kendi
    /// kullanimi buraya eklenir. Ne kayitta ne agacta kullanim varsa deger
    /// <see langword="null"/> kalir: sifir yazmak, "saglayici token bildirmedi"
    /// ile "hic token harcanmadi" durumlarini ayirt edilemez hale getirirdi.
    /// </remarks>
    private static RunUsage? ReadTreeUsage(DbDataReader reader, RunUsage? ownUsage)
    {
        var descendantRows = reader.IsDBNull(22) ? 0 : reader.GetInt64(22);

        if (descendantRows == 0 && ownUsage is null)
        {
            return null;
        }

        return new RunUsage
        {
            InputTokens = (reader.IsDBNull(19) ? 0 : reader.GetInt64(19)) + (ownUsage?.InputTokens ?? 0),
            OutputTokens = (reader.IsDBNull(20) ? 0 : reader.GetInt64(20)) + (ownUsage?.OutputTokens ?? 0),
            TotalTokens = (reader.IsDBNull(21) ? 0 : reader.GetInt64(21)) + (ownUsage?.TotalTokens ?? 0),
        };
    }

    private static RunUsage? ReadUsage(DbDataReader reader)
    {
        if (reader.IsDBNull(8) && reader.IsDBNull(9) && reader.IsDBNull(10))
        {
            return null;
        }

        return new RunUsage
        {
            InputTokens = reader.IsDBNull(8) ? null : reader.GetInt64(8),
            OutputTokens = reader.IsDBNull(9) ? null : reader.GetInt64(9),
            TotalTokens = reader.IsDBNull(10) ? null : reader.GetInt64(10),
        };
    }

    /// <summary>
    /// Kaydin kendi maliyetini okur. <c>pricing_source</c> NULL ise model hic
    /// bilinmiyordu demektir (<see cref="PricingSource.Unknown"/>'dan farkli):
    /// bu durumda <see langword="null"/> doner.
    /// </summary>
    private static RunCost? ReadCost(DbDataReader reader)
    {
        if (reader.IsDBNull(31))
        {
            return null;
        }

        return new RunCost
        {
            InputCost = DbHelpers.GetNullableDecimal(reader, 28),
            OutputCost = DbHelpers.GetNullableDecimal(reader, 29),
            Currency = DbHelpers.GetNullableString(reader, 30),
            Source = (PricingSource)reader.GetInt16(31),
        };
    }

    /// <summary>Agacin maliyet toplamini kaydin kendi maliyetiyle birlestirir.</summary>
    /// <remarks>Ayni gerekce <see cref="ReadTreeUsage"/> ile.</remarks>
    private static RunTreeCost? ReadTreeCost(DbDataReader reader, RunCost? ownCost)
    {
        var pricedDescendants = reader.IsDBNull(36) ? 0 : reader.GetInt64(36);

        if (pricedDescendants == 0 && ownCost is null)
        {
            return null;
        }

        var inputCost = DbHelpers.GetNullableDecimal(reader, 32);
        var outputCost = DbHelpers.GetNullableDecimal(reader, 33);

        if (ownCost?.InputCost is { } ownInput)
        {
            inputCost = (inputCost ?? 0) + ownInput;
        }

        if (ownCost?.OutputCost is { } ownOutput)
        {
            outputCost = (outputCost ?? 0) + ownOutput;
        }

        var unknownPricing = (reader.IsDBNull(35) ? 0 : reader.GetInt64(35))
            + (ownCost?.Source == PricingSource.Unknown ? 1 : 0);

        return new RunTreeCost
        {
            InputCost = inputCost,
            OutputCost = outputCost,
            Currency = DbHelpers.GetNullableString(reader, 34) ?? ownCost?.Currency,
            RunsWithUnknownPricing = unknownPricing,
        };
    }

    private static ToolInvocationRecord ReadToolInvocation(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            RunId = reader.GetGuid(1),
            ToolName = reader.GetString(2),
            ToolCallId = DbHelpers.GetNullableString(reader, 3),
            Source = DbHelpers.GetNullableString(reader, 4),
            Arguments = DbHelpers.GetNullableString(reader, 5),
            Result = DbHelpers.GetNullableString(reader, 6),
            Duration = reader.IsDBNull(7) ? null : TimeSpan.FromMilliseconds(reader.GetInt32(7)),
            Error = DbHelpers.GetNullableString(reader, 8),
            CreatedAt = DbHelpers.GetTimestamp(reader, 9),
            Usage = ReadToolCallUsage(reader),
        };

    /// <summary>
    /// Token disi olcum sutunlarini okur (indeksler 10-14).
    /// </summary>
    /// <remarks>
    /// Birim bos ise cagri hic olcum bildirmemistir ve nesne kurulmaz — bos bir
    /// <see cref="ToolCallUsage"/> dondurmek "olculdu ama sifir" anlamina gelirdi.
    /// Mantiksal deger <c>DbHelpers.ToBoolean</c> ile okunur: SQLite mantiksal tip
    /// tasimaz ve <c>long</c> (0/1) dondurur (K-195).
    /// </remarks>
    private static ToolCallUsage? ReadToolCallUsage(DbDataReader reader)
    {
        if (DbHelpers.GetNullableString(reader, 10) is not { Length: > 0 } unit)
        {
            return null;
        }

        return new ToolCallUsage
        {
            Unit = unit,
            Quantity = DbHelpers.GetNullableDecimal(reader, 11) ?? 0m,
            IsEstimated = !reader.IsDBNull(12) && DbHelpers.ToBoolean(reader.GetValue(12)),
            Cost = DbHelpers.GetNullableDecimal(reader, 13),
            Currency = DbHelpers.GetNullableString(reader, 14),
        };
    }

    private static ToolUsage ReadToolUsage(DbDataReader reader)
        => new()
        {
            ToolName = reader.GetString(0),
            TotalCalls = reader.GetInt64(1),
            FailedCalls = reader.GetInt64(2),
            AverageDurationMs = reader.IsDBNull(3) ? null : reader.GetDouble(3),
            LastCalledAt = reader.IsDBNull(4) ? null : DbHelpers.GetTimestamp(reader, 4),
        };

    private static TimeSeriesPoint ReadTimeSeriesPoint(DbDataReader reader)
        => new()
        {
            Bucket = DbHelpers.GetTimestamp(reader, 0),
            Runs = reader.GetInt64(1),
            FailedRuns = reader.GetInt64(2),
            InputTokens = reader.GetInt64(3),
            OutputTokens = reader.GetInt64(4),
            Cost = DbHelpers.GetNullableDecimal(reader, 5),
            AverageDurationMs = reader.IsDBNull(6) ? null : reader.GetDouble(6),
        };

    private static ExperimentVariantResult ReadExperimentVariantResult(DbDataReader reader)
        => new()
        {
            Variant = reader.GetString(0),
            Version = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
            TotalRuns = reader.GetInt64(2),
            CompletedRuns = reader.GetInt64(3),
            FailedRuns = reader.GetInt64(4),
            CanceledRuns = reader.GetInt64(5),
            InputTokens = reader.GetInt64(6),
            OutputTokens = reader.GetInt64(7),
            TotalTokens = reader.GetInt64(8),
            AverageDurationMs = reader.IsDBNull(9) ? null : reader.GetDouble(9),
            TotalCost = DbHelpers.GetNullableDecimal(reader, 10),
            Currency = DbHelpers.GetNullableString(reader, 11),
            AverageScore = reader.IsDBNull(12) ? null : reader.GetDouble(12),
        };

    private void AddNullableText(DbCommand command, string name, string? value)
        => Dialect.AddText(command, name, value);

    private void AddNullableUuid(DbCommand command, string name, Guid? value)
        => Dialect.AddUuid(command, name, value);

    private void AddNullableInt64(DbCommand command, string name, long? value)
        => Dialect.AddInt64(command, name, value);

    private void AddNullableInt32(DbCommand command, string name, int? value)
        => Dialect.AddInt32(command, name, value);

    private void AddNullableDecimal(DbCommand command, string name, decimal? value)
        => Dialect.AddDecimal(command, name, value);
}
