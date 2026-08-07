
namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IRunStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Bellek ici depo ile PostgreSQL deposu ayni senaryolari gecmelidir.
/// </remarks>
public abstract class RunStoreContract : TenantIsolationContract<IRunStore>
{
    /// <inheritdoc />
    /// <remarks>
    /// Kiraci arayuzde bir parametre degildir; <see cref="ITenantContext"/>'ten
    /// okunur. Bu yuzden her kanca once gecerli kiraciyi ayarlar.
    /// </remarks>
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        AmbientTenant.TenantId = tenantId;

        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId, name));
        await Store.AppendEventAsync(TestData.Event(runId, 0));

        return runId;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;

        var runId = (Guid)key;
        var record = await Store.GetRunAsync(runId);

        // Olay akisi da ayni siniri tasimalidir; kayit gorulmuyorsa olaylari da
        // gorulmemelidir.
        var events = 0;

        await foreach (var runEvent in Store.ReadEventsAsync(runId))
        {
            _ = runEvent;
            events++;
        }

        (events > 0).ShouldBe(record is not null);

        return record is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
    {
        AmbientTenant.TenantId = tenantId;
        return (await Store.QueryRunsAsync(new RunQuery())).Count;
    }

    [Fact]
    public async Task StartRunAsync_ayni_kimlikle_ikinci_kez_cagrilinca_UPSERT_yapar()
    {
        // Faz 46: kuyruga alinan bir calistirma once Queued, isci is'i
        // gercekten alinca AYNI kimlikle tekrar (varsayilan Running) yazilir.
        // Ikinci cagri yeni bir satir ACMAMALI, mevcut satiri GUNCELLEMELIDIR.
        var runId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(runId) with { Status = RunStatus.Queued });

        var queued = await Store.GetRunAsync(runId);
        queued.ShouldNotBeNull();
        queued!.Status.ShouldBe(RunStatus.Queued);

        await Store.StartRunAsync(TestData.Run(runId));

        var running = await Store.GetRunAsync(runId);
        running.ShouldNotBeNull();
        running!.Status.ShouldBe(RunStatus.Running);

        (await Store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false }))
            .Count(record => record.Id == runId).ShouldBe(1);
    }

    [Fact]
    public async Task Olaylar_sira_numarasina_gore_okunur()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        for (var i = 0; i < 5; i++)
        {
            await Store.AppendEventAsync(TestData.Event(runId, i));
        }

        var sequences = new List<long>();

        await foreach (var runEvent in Store.ReadEventsAsync(runId, fromSequence: 2))
        {
            sequences.Add(runEvent.Sequence);
        }

        sequences.ShouldBe([2, 3, 4]);
    }

    [Fact]
    public async Task Olay_alanlari_gidip_gelir()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.AppendEventAsync(new RunEvent
        {
            RunId = runId,
            Sequence = 0,
            Type = RunEventType.ToolInvoking,
            Timestamp = DateTimeOffset.UtcNow,
            Text = "metin",
            ToolName = "get_order_status",
            ToolCallId = "call-1",
            Payload = "orderId=42",
        });

        var events = new List<RunEvent>();

        await foreach (var runEvent in Store.ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        var single = events.ShouldHaveSingleItem();
        single.Type.ShouldBe(RunEventType.ToolInvoking);
        single.Text.ShouldBe("metin");
        single.ToolName.ShouldBe("get_order_status");
        single.ToolCallId.ShouldBe("call-1");
        single.Payload.ShouldBe("orderId=42");
    }

    [Fact]
    public async Task Olmayan_calistirmaya_olay_eklenemez()
        => await Should.ThrowAsync<AgentPrismException>(
            async () => await Store.AppendEventAsync(TestData.Event(AgentPrismId.NewId(), 0)));

    [Fact]
    public async Task Olmayan_calistirma_sonlandirilamaz()
        => await Should.ThrowAsync<AgentPrismException>(
            async () => await Store.CompleteRunAsync(new RunCompletion
            {
                RunId = AgentPrismId.NewId(),
                Status = RunStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow,
            }));

    [Fact]
    public async Task Olmayan_calistirmanin_olaylari_bos_doner()
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in Store.ReadEventsAsync(AgentPrismId.NewId()))
        {
            events.Add(runEvent);
        }

        events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sonlandirma_ozeti_gunceller()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        var completedAt = DateTimeOffset.UtcNow;

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Failed,
            CompletedAt = completedAt,
            EventCount = 7,
            Usage = new RunUsage { InputTokens = 10, OutputTokens = 20, TotalTokens = 30 },
            Error = new RunError { Type = "System.InvalidOperationException", Message = "patladi" },
        });

        var record = await Store.GetRunAsync(runId);

        record.ShouldNotBeNull();
        record.Status.ShouldBe(RunStatus.Failed);
        record.EventCount.ShouldBe(7);
        record.CompletedAt.ShouldNotBeNull();
        record.Usage.ShouldNotBeNull();
        record.Usage.InputTokens.ShouldBe(10);
        record.Usage.OutputTokens.ShouldBe(20);
        record.Usage.TotalTokens.ShouldBe(30);
        record.Error.ShouldNotBeNull();
        record.Error.Type.ShouldBe("System.InvalidOperationException");
        record.Error.Message.ShouldBe("patladi");
    }

    [Fact]
    public async Task Sonlandirma_hata_sinifi_ve_parmak_izini_saklar()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = new RunError
            {
                Type = "content_filtered",
                Message = "yanit filtrelendi",
                Class = RunErrorClass.ContentFiltered,
                Fingerprint = "abc123",
            },
        });

        var record = await Store.GetRunAsync(runId);

        record.ShouldNotBeNull();
        record.Error.ShouldNotBeNull();
        record.Error.Class.ShouldBe(RunErrorClass.ContentFiltered);
        record.Error.Fingerprint.ShouldBe("abc123");
    }

    [Fact]
    public async Task Token_bilgisi_yoksa_null_kalir()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        (await Store.GetRunAsync(runId))!.Usage.ShouldBeNull();
    }

    [Fact]
    public async Task Sorgu_agent_adina_gore_filtreler()
    {
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha"));
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "beta"));
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha"));

        var results = await Store.QueryRunsAsync(new RunQuery { AgentName = "alpha" });

        results.Count.ShouldBe(2);
        results.ShouldAllBe(static run => run.AgentName == "alpha");
    }

    [Fact]
    public async Task Sorgu_duruma_gore_filtreler()
    {
        var completed = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(completed));
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = completed,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        var results = await Store.QueryRunsAsync(new RunQuery { Status = RunStatus.Completed });

        results.ShouldHaveSingleItem().Id.ShouldBe(completed);
    }

    [Fact]
    public async Task Sorgu_en_yeniden_eskiye_siralar()
    {
        var now = DateTimeOffset.UtcNow;

        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now.AddMinutes(-10) });
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now });
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now.AddMinutes(-5) });

        var results = await Store.QueryRunsAsync(new RunQuery());

        results.Count.ShouldBe(3);
        results[0].StartedAt.ShouldBe(now, TimeSpan.FromMilliseconds(1));
        results[^1].StartedAt.ShouldBe(now.AddMinutes(-10), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Sorgu_sayfalama_uygular()
    {
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now.AddMinutes(-i) });
        }

        var page = await Store.QueryRunsAsync(new RunQuery { Skip = 1, Take = 2 });

        page.Count.ShouldBe(2);
        page[0].StartedAt.ShouldBe(now.AddMinutes(-1), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Sorgu_baslangic_zamanina_gore_filtreler()
    {
        var now = DateTimeOffset.UtcNow;

        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now.AddMinutes(-30) });
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now });

        var results = await Store.QueryRunsAsync(new RunQuery { StartedAfter = now.AddMinutes(-10) });

        results.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Olmayan_calistirma_null_doner()
        => (await Store.GetRunAsync(AgentPrismId.NewId())).ShouldBeNull();

    [Fact]
    public async Task Ozet_durumlari_ve_tokenlari_toplar()
    {
        await CompleteRunAsync("alpha", RunStatus.Completed, new RunUsage { InputTokens = 10, OutputTokens = 5, TotalTokens = 15 });
        await CompleteRunAsync("alpha", RunStatus.Failed, new RunUsage { InputTokens = 2, OutputTokens = 1, TotalTokens = 3 });
        await CompleteRunAsync("beta", RunStatus.Canceled, usage: null);
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "beta"));

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.TotalRuns.ShouldBe(4);
        stats.CompletedRuns.ShouldBe(1);
        stats.FailedRuns.ShouldBe(1);
        stats.CanceledRuns.ShouldBe(1);
        stats.RunningRuns.ShouldBe(1);
        stats.InputTokens.ShouldBe(12);
        stats.OutputTokens.ShouldBe(6);
        stats.TotalTokens.ShouldBe(18);
    }

    [Fact]
    public async Task Ozet_eval_calistirmalarini_haric_tutar()
    {
        // Eval vaka calistirmalari sentetik test cagrilaridir; normal
        // istatistikleri kirletmemelidir (docs/18-DEGERLENDIRME.md, acik soru 4).
        await CompleteRunAsync("alpha", RunStatus.Completed, usage: null);

        var evalRunId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(evalRunId, "alpha") with { Kind = RunKind.Eval });
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = evalRunId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { InputTokens = 1_000, OutputTokens = 1_000, TotalTokens = 2_000 },
        });

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.TotalRuns.ShouldBe(1);
        stats.CompletedRuns.ShouldBe(1);
        stats.TotalTokens.ShouldBe(0);
    }

    [Fact]
    public async Task Ozet_hata_oranini_yalnizca_sonuclanmislar_uzerinden_hesaplar()
    {
        await CompleteRunAsync("alpha", RunStatus.Completed, usage: null);
        await CompleteRunAsync("alpha", RunStatus.Failed, usage: null);

        // Devam eden calistirma paydaya girmemelidir.
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha"));

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.ErrorRate.ShouldNotBeNull();
        stats.ErrorRate.Value.ShouldBe(0.5, 0.0001);
    }

    [Fact]
    public async Task Ozet_hic_sonuclanmis_calistirma_yoksa_hata_orani_vermez()
    {
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha"));

        (await Store.GetStatisticsAsync(new RunStatisticsQuery())).ErrorRate.ShouldBeNull();
    }

    [Fact]
    public async Task Ozet_hata_sinifina_gore_kirilim_hesaplar()
    {
        // Ayni sinifta iki farkli kume: "fp-a" iki kez, "fp-b" bir kez gorulur.
        await FailedRunAsync(RunErrorClass.ContentFiltered, "fp-a", "a mesaji");
        await FailedRunAsync(RunErrorClass.ContentFiltered, "fp-a", "a mesaji");
        await FailedRunAsync(RunErrorClass.ContentFiltered, "fp-b", "b mesaji");
        await FailedRunAsync(RunErrorClass.Timeout, "fp-c", "c mesaji");

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());
        var byClass = stats.ByErrorClass.ToDictionary(static entry => entry.Class);

        byClass[RunErrorClass.ContentFiltered].TotalRuns.ShouldBe(3);
        byClass[RunErrorClass.ContentFiltered].TopClusters.Count.ShouldBe(2);

        // En sik kume (fp-a, 2 calistirma) once gelir.
        byClass[RunErrorClass.ContentFiltered].TopClusters[0].Fingerprint.ShouldBe("fp-a");
        byClass[RunErrorClass.ContentFiltered].TopClusters[0].Count.ShouldBe(2);
        byClass[RunErrorClass.ContentFiltered].TopClusters[1].Fingerprint.ShouldBe("fp-b");
        byClass[RunErrorClass.ContentFiltered].TopClusters[1].Count.ShouldBe(1);

        byClass[RunErrorClass.Timeout].TotalRuns.ShouldBe(1);
    }

    [Fact]
    public async Task Ozet_ariza_kumesi_sinif_basina_en_fazla_uc_dondurur()
    {
        for (var i = 0; i < 5; i++)
        {
            await FailedRunAsync(RunErrorClass.ProviderError, $"fp-{i}", $"mesaj {i}");
        }

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());
        var providerError = stats.ByErrorClass.Single(static entry => entry.Class == RunErrorClass.ProviderError);

        providerError.TotalRuns.ShouldBe(5);
        providerError.TopClusters.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Ozet_hata_sinifi_bos_satirlar_unknown_kovasinda_gorunur()
    {
        // Hata sinifi eklenmeden once yazilmis bir satiri simule eder: Class ve
        // Fingerprint BILEREK bos (K-014 -- gecmis kayitlar geriye donuk
        // doldurulmaz). Sorgu cokmemeli ve satir Unknown kovasinda gorunmelidir.
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = new RunError { Type = "AgentPrism.AgentPrismCompilationException", Message = "eski kayit" },
        });

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.ByErrorClass.ShouldContain(static entry => entry.Class == RunErrorClass.Unknown && entry.TotalRuns == 1);
    }

    [Fact]
    public async Task Ozet_agent_kirilimini_calistirma_sayisina_gore_siralar()
    {
        await CompleteRunAsync("az-kullanilan", RunStatus.Completed, usage: null);
        await CompleteRunAsync("cok-kullanilan", RunStatus.Completed, new RunUsage { TotalTokens = 100 });
        await CompleteRunAsync("cok-kullanilan", RunStatus.Failed, new RunUsage { TotalTokens = 50 });

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.ByAgent.Count.ShouldBe(2);
        stats.ByAgent[0].AgentName.ShouldBe("cok-kullanilan");
        stats.ByAgent[0].TotalRuns.ShouldBe(2);
        stats.ByAgent[0].FailedRuns.ShouldBe(1);
        stats.ByAgent[0].TotalTokens.ShouldBe(150);
        stats.ByAgent[1].AgentName.ShouldBe("az-kullanilan");
    }

    [Fact]
    public async Task Ozet_agent_adina_gore_filtreler()
    {
        await CompleteRunAsync("alpha", RunStatus.Completed, usage: null);
        await CompleteRunAsync("beta", RunStatus.Completed, usage: null);

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery { AgentName = "alpha" });

        stats.TotalRuns.ShouldBe(1);
        stats.ByAgent.ShouldHaveSingleItem().AgentName.ShouldBe("alpha");
    }

    [Fact]
    public async Task Ozet_baslangic_zamanina_gore_filtreler()
    {
        var now = DateTimeOffset.UtcNow;

        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha") with { StartedAt = now.AddHours(-2) });
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha") with { StartedAt = now });

        var stats = await Store.GetStatisticsAsync(
            new RunStatisticsQuery { StartedAfter = now.AddHours(-1) });

        stats.TotalRuns.ShouldBe(1);
    }

    [Fact]
    public async Task Ozet_agent_kirilimini_sinirlar()
    {
        await CompleteRunAsync("alpha", RunStatus.Completed, usage: null);
        await CompleteRunAsync("beta", RunStatus.Completed, usage: null);
        await CompleteRunAsync("gamma", RunStatus.Completed, usage: null);

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery { MaxAgents = 2 });

        stats.TotalRuns.ShouldBe(3);
        stats.ByAgent.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Bos_depo_ozeti_sifir_doner()
    {
        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.TotalRuns.ShouldBe(0);
        stats.TotalTokens.ShouldBe(0);
        stats.ByAgent.ShouldBeEmpty();
        stats.ErrorRate.ShouldBeNull();
    }

    // --- Surum ve deney kirilimi (Faz 19.2-19.3) ---

    [Fact]
    public async Task Ozet_surum_kirilimini_hesaplar()
    {
        var v1 = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(v1, "alpha") with { AgentVersion = 1 });
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = v1,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { TotalTokens = 10 },
        });

        var v2A = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(v2A, "alpha") with { AgentVersion = 2 });
        await Store.CompleteRunAsync(new RunCompletion { RunId = v2A, Status = RunStatus.Failed, CompletedAt = DateTimeOffset.UtcNow });

        var v2B = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(v2B, "alpha") with { AgentVersion = 2 });
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = v2B,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { TotalTokens = 20 },
        });

        // Surumu bilinmeyen bir calistirma kirilima girmemelidir.
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha"));

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.ByVersion.Count.ShouldBe(2);

        var version1 = stats.ByVersion.Single(static v => v.Version == 1);
        version1.AgentName.ShouldBe("alpha");
        version1.TotalRuns.ShouldBe(1);
        version1.FailedRuns.ShouldBe(0);
        version1.TotalTokens.ShouldBe(10);

        var version2 = stats.ByVersion.Single(static v => v.Version == 2);
        version2.TotalRuns.ShouldBe(2);
        version2.FailedRuns.ShouldBe(1);
        version2.TotalTokens.ShouldBe(20);
    }

    [Fact]
    public async Task Deney_sonucu_kol_bazinda_ozetlenir()
    {
        var experimentId = Guid.NewGuid();

        var controlRun = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(controlRun, "alpha") with
        {
            AgentVersion = 1,
            ExperimentId = experimentId,
            Variant = "control",
        });
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = controlRun,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { InputTokens = 5, OutputTokens = 5, TotalTokens = 10 },
        });

        var v2Run = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(v2Run, "alpha") with
        {
            AgentVersion = 2,
            ExperimentId = experimentId,
            Variant = "v2",
        });
        await Store.CompleteRunAsync(new RunCompletion { RunId = v2Run, Status = RunStatus.Failed, CompletedAt = DateTimeOffset.UtcNow });

        // Baska bir deneyin calistirmasi bu deneyin sonucuna girmemelidir.
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha") with
        {
            ExperimentId = Guid.NewGuid(),
            Variant = "control",
        });

        var results = await Store.GetExperimentResultsAsync(new ExperimentResultsQuery { ExperimentId = experimentId });

        results.Count.ShouldBe(2);

        var control = results.Single(static r => string.Equals(r.Variant, "control", StringComparison.Ordinal));
        control.Version.ShouldBe(1);
        control.TotalRuns.ShouldBe(1);
        control.CompletedRuns.ShouldBe(1);
        control.TotalTokens.ShouldBe(10);
        control.AverageDurationMs.ShouldNotBeNull();

        var v2 = results.Single(static r => string.Equals(r.Variant, "v2", StringComparison.Ordinal));
        v2.Version.ShouldBe(2);
        v2.FailedRuns.ShouldBe(1);
    }

    [Fact]
    public async Task Deney_sonucu_trafik_almayan_deneyde_bos_doner()
    {
        var results = await Store.GetExperimentResultsAsync(new ExperimentResultsQuery { ExperimentId = Guid.NewGuid() });

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task Agac_alanlari_gidip_gelir()
    {
        var rootId = AgentPrismId.NewId();
        var childId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId));
        await Store.StartRunAsync(TestData.Run(childId, "arastirmaci") with
        {
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });

        var child = await Store.GetRunAsync(childId);

        child.ShouldNotBeNull();
        child.ParentRunId.ShouldBe(rootId);
        child.RootRunId.ShouldBe(rootId);
        child.Depth.ShouldBe(1);

        var root = await Store.GetRunAsync(rootId);

        root.ShouldNotBeNull();
        root.ParentRunId.ShouldBeNull();

        // Kok kaydin root_run_id alani BOS kalir. Kokun kendisine isaret eden bir
        // deger yazmak, "kok mu, alt mi" sorusunu sorguda ikinci bir kosula
        // dondururdu.
        root.RootRunId.ShouldBeNull();
        root.Depth.ShouldBe(0);
        root.ChildRunCount.ShouldBe(1);
    }

    [Fact]
    public async Task Workflow_calistirmasi_ayni_tabloda_yasar()
    {
        // Workflow calistirmalari icin AYRI BIR TABLO YOKTUR (Faz 15). Ayrim
        // `kind` sutunuyla yapilir ve icindeki agent'lar Faz 12'nin agac
        // mekanizmasiyla ayni satirin altina baglanir.
        var workflowRunId = AgentPrismId.NewId();
        var agentRunId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(workflowRunId, "inceleme") with
        {
            Kind = RunKind.Workflow,
            WorkflowName = "inceleme",
        });

        await Store.StartRunAsync(TestData.Run(agentRunId, "yazar") with
        {
            ParentRunId = workflowRunId,
            RootRunId = workflowRunId,
            Depth = 1,
        });

        var workflowRun = await Store.GetRunAsync(workflowRunId);

        workflowRun.ShouldNotBeNull();
        workflowRun.Kind.ShouldBe(RunKind.Workflow);
        workflowRun.WorkflowName.ShouldBe("inceleme");
        workflowRun.ChildRunCount.ShouldBe(1);

        // Agent satirlari varsayilan turu korur; eski kayitlar da boyle okunur.
        var agentRun = await Store.GetRunAsync(agentRunId);

        agentRun.ShouldNotBeNull();
        agentRun.Kind.ShouldBe(RunKind.Agent);
        agentRun.WorkflowName.ShouldBeNull();
    }

    [Fact]
    public async Task Liste_varsayilan_olarak_yalniz_kok_calistirmalari_doner()
    {
        var rootId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId));
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "arastirmaci") with
        {
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });

        var roots = await Store.QueryRunsAsync(new RunQuery());

        roots.ShouldHaveSingleItem().Id.ShouldBe(rootId);

        var all = await Store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false });

        all.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Ebeveyn_filtresi_kok_filtresini_gecersiz_kilar()
    {
        var rootId = AgentPrismId.NewId();
        var childId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId));
        await Store.StartRunAsync(TestData.Run(childId, "arastirmaci") with
        {
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });

        // OnlyRootRuns varsayilan olarak true'dur; ebeveyn filtresi verildiginde
        // bilerek yok sayilir. Sessizce bos liste donmek, hata ayiklanmasi zor bir
        // davranistir.
        var children = await Store.QueryRunsAsync(new RunQuery { ParentRunId = rootId });

        children.ShouldHaveSingleItem().Id.ShouldBe(childId);
    }

    [Fact]
    public async Task Agac_sorgusu_koku_ve_tum_altini_doner()
    {
        var rootId = AgentPrismId.NewId();
        var childId = AgentPrismId.NewId();
        var grandChildId = AgentPrismId.NewId();
        var yabanciId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId));
        await Store.StartRunAsync(TestData.Run(childId, "arastirmaci") with
        {
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });
        await Store.StartRunAsync(TestData.Run(grandChildId, "ozetleyici") with
        {
            ParentRunId = childId,
            RootRunId = rootId,
            Depth = 2,
        });
        await Store.StartRunAsync(TestData.Run(yabanciId, "baska"));

        var tree = await Store.QueryRunsAsync(new RunQuery { RootRunId = rootId, OnlyRootRuns = false });

        tree.Select(static run => run.Id).ShouldBe([rootId, childId, grandChildId], ignoreOrder: true);
    }

    [Fact]
    public async Task Agac_toplami_kokun_ve_altinin_tokenlerini_birlestirir()
    {
        var rootId = AgentPrismId.NewId();
        var childId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId));
        await Store.StartRunAsync(TestData.Run(childId, "arastirmaci") with
        {
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });

        await CompleteAsync(rootId, new RunUsage { InputTokens = 10, OutputTokens = 5, TotalTokens = 15 });
        await CompleteAsync(childId, new RunUsage { InputTokens = 30, OutputTokens = 20, TotalTokens = 50 });

        var root = await Store.GetRunAsync(rootId);

        root.ShouldNotBeNull();
        root.Usage!.TotalTokens.ShouldBe(15);

        // Agac toplami kokun KENDI kullanimini da icerir; ikisi toplanmaz.
        root.TreeUsage!.TotalTokens.ShouldBe(65);
        root.TreeUsage.InputTokens.ShouldBe(40);
        root.TreeUsage.OutputTokens.ShouldBe(25);

        var child = await Store.GetRunAsync(childId);

        // Alti olmayan bir calistirmada agac toplami kendi kullanimina esittir.
        child!.TreeUsage!.TotalTokens.ShouldBe(50);
    }

    [Fact]
    public async Task Token_bildirmeyen_agacta_toplam_bos_kalir()
    {
        var rootId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId));
        await CompleteAsync(rootId, usage: null);

        var root = await Store.GetRunAsync(rootId);

        // Sifir yazmak, "saglayici token bildirmedi" ile "hic token harcanmadi"
        // durumlarini ayirt edilemez hale getirirdi.
        root!.TreeUsage.ShouldBeNull();
    }

    // --- Maliyet (Faz 20) ---

    [Fact]
    public async Task Maliyet_yaziliyor_ve_geri_okunuyor()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId) with { ModelId = "gpt-x" });

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { InputTokens = 100, OutputTokens = 50, TotalTokens = 150 },
            Cost = new RunCost
            {
                InputCost = 0.1234567891m,
                OutputCost = 0.9876543219m,
                Currency = "USD",
                Source = PricingSource.Catalog,
            },
        });

        var record = await Store.GetRunAsync(runId);

        record.ShouldNotBeNull();
        record.Cost.ShouldNotBeNull();
        record.Cost.Source.ShouldBe(PricingSource.Catalog);
        record.Cost.Currency.ShouldBe("USD");
        // numeric(20,10) tam yuvarlamadan gidip gelmelidir.
        record.Cost.InputCost.ShouldBe(0.1234567891m);
        record.Cost.OutputCost.ShouldBe(0.9876543219m);
    }

    [Fact]
    public async Task Fiyat_tanimsizsa_maliyet_alanlari_null_ama_kaynak_unknown_yazilir()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId) with { ModelId = "hic-fiyatlanmamis-model" });

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Cost = new RunCost { Source = PricingSource.Unknown },
        });

        var record = await Store.GetRunAsync(runId);

        record!.Cost.ShouldNotBeNull();
        record.Cost.Source.ShouldBe(PricingSource.Unknown);
        // Sifir DEGIL: bilinmeyen fiyat sifir maliyetle karistirilmamalidir.
        record.Cost.InputCost.ShouldBeNull();
        record.Cost.OutputCost.ShouldBeNull();
    }

    [Fact]
    public async Task Model_bilinmiyorsa_maliyet_hic_yoktur()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        // Cost hic gecilmez: model baglanmamis bir kod agent'i senaryosu.
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        var record = await Store.GetRunAsync(runId);

        // Model hic bilinmiyorsa Cost NULL'dur; bu, "model biliniyor ama fiyat
        // tanimsiz" (Source=Unknown, yine de dolu bir RunCost) durumundan farklidir.
        record!.Cost.ShouldBeNull();
    }

    [Fact]
    public async Task Agac_maliyeti_kendi_maliyetiyle_toplanmiyor_ayri_alanlar()
    {
        var rootId = AgentPrismId.NewId();
        var childId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId) with { ModelId = "gpt-x" });
        await Store.StartRunAsync(TestData.Run(childId, "arastirmaci") with
        {
            ModelId = "gpt-x",
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });

        await CompleteWithCostAsync(rootId, 1m, 1m);
        await CompleteWithCostAsync(childId, 2m, 3m);

        var root = await Store.GetRunAsync(rootId);

        root.ShouldNotBeNull();
        root.Cost!.InputCost.ShouldBe(1m);
        root.Cost.OutputCost.ShouldBe(1m);

        // Agac toplami kokun KENDI maliyetini de icerir; ikisi toplanip
        // ayrica gosterilmez (RunTreeUsage ile ayni desen).
        root.TreeCost!.InputCost.ShouldBe(3m);
        root.TreeCost.OutputCost.ShouldBe(4m);

        var child = await Store.GetRunAsync(childId);

        // Alti olmayan bir calistirmada agac toplami kendi maliyetine esittir.
        child!.TreeCost!.InputCost.ShouldBe(2m);
        child.TreeCost.OutputCost.ShouldBe(3m);
    }

    [Fact]
    public async Task Ozet_maliyeti_toplar_ve_tanimsiz_sayisini_bildirir()
    {
        await CompleteRunWithCostAsync("alpha", "gpt-x", 1m, 1m, PricingSource.Catalog);
        await CompleteRunWithCostAsync("alpha", "gpt-x", 2m, 2m, PricingSource.Catalog);
        await CompleteRunWithCostAsync("beta", "fiyatsiz-model", null, null, PricingSource.Unknown);

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.TotalCost.ShouldBe(6m);
        stats.Currency.ShouldBe("USD");
        stats.RunsWithUnknownPricing.ShouldBe(1);

        var model = stats.ByModel.Single(static m => string.Equals(m.ModelId, "gpt-x", StringComparison.Ordinal));
        model.TotalCost.ShouldBe(6m);
    }

    [Fact]
    public async Task Deney_sonucu_maliyet_iceriyor()
    {
        var experimentId = Guid.NewGuid();

        var controlRun = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(controlRun, "alpha") with
        {
            ModelId = "gpt-x",
            ExperimentId = experimentId,
            Variant = "control",
        });
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = controlRun,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Cost = new RunCost { InputCost = 1m, OutputCost = 2m, Currency = "USD", Source = PricingSource.Catalog },
        });

        var results = await Store.GetExperimentResultsAsync(new ExperimentResultsQuery { ExperimentId = experimentId });

        var control = results.ShouldHaveSingleItem();
        control.TotalCost.ShouldBe(3m);
        control.Currency.ShouldBe("USD");
    }

    [Fact]
    public async Task Zaman_serisi_bos_kovalari_doldurur()
    {
        var now = DateTimeOffset.UtcNow;
        var from = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(-3);
        var to = from.AddHours(3);

        // Kasitli olarak orta kovaya (from+1h) hicbir calistirma dusurulmez.
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = from.AddMinutes(5) });
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = from.AddHours(2).AddMinutes(5) });

        var points = await Store.GetTimeSeriesAsync(new RunTimeSeriesQuery { From = from, To = to, Bucket = TimeSeriesBucket.Hour });

        points.Count.ShouldBe(3);
        points[0].Runs.ShouldBe(1);
        points[1].Runs.ShouldBe(0);
        points[2].Runs.ShouldBe(1);
    }

    [Fact]
    public async Task Zaman_serisi_eval_calistirmalarini_haric_tutmaz()
    {
        // /api/stats'in aksine (K-141), zaman serisi Eval/Workflow calistirmalarini
        // varsayilan olarak DISLAMAZ (bkz. docs/KARARLAR.md K-152).
        var now = DateTimeOffset.UtcNow;
        var from = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(-1);
        var to = from.AddHours(1);

        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = from.AddMinutes(5), Kind = RunKind.Eval });

        var points = await Store.GetTimeSeriesAsync(new RunTimeSeriesQuery { From = from, To = to, Bucket = TimeSeriesBucket.Hour });

        points.ShouldHaveSingleItem().Runs.ShouldBe(1);
    }

    [Fact]
    public async Task Zaman_serisi_kova_sinirini_asinca_hata_verir()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-30);
        var to = DateTimeOffset.UtcNow;

        var exception = await Should.ThrowAsync<AgentPrismException>(async () =>
            await Store.GetTimeSeriesAsync(new RunTimeSeriesQuery { From = from, To = to, Bucket = TimeSeriesBucket.Hour }));

        exception.Message.ShouldContain("500");
    }

    [Fact]
    public async Task Maliyet_yeniden_hesaplama_ucu_satiri_gunceller()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId) with { ModelId = "gpt-x" });
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Cost = new RunCost { Source = PricingSource.Unknown },
        });

        await Store.UpdateRunCostAsync(runId, new RunCost
        {
            InputCost = 5m,
            OutputCost = 5m,
            Currency = "USD",
            Source = PricingSource.Configuration,
        });

        var record = await Store.GetRunAsync(runId);

        record!.Cost!.Source.ShouldBe(PricingSource.Configuration);
        record.Cost.InputCost.ShouldBe(5m);
    }

    private async Task CompleteWithCostAsync(Guid runId, decimal inputCost, decimal outputCost)
        => await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Cost = new RunCost
            {
                InputCost = inputCost,
                OutputCost = outputCost,
                Currency = "USD",
                Source = PricingSource.Catalog,
            },
        });

    private async Task CompleteRunWithCostAsync(
        string agentName,
        string modelId,
        decimal? inputCost,
        decimal? outputCost,
        PricingSource source)
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId, agentName) with { ModelId = modelId });

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Cost = new RunCost { InputCost = inputCost, OutputCost = outputCost, Currency = "USD", Source = source },
        });
    }

    private async Task CompleteAsync(Guid runId, RunUsage? usage)
        => await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = usage,
        });

    private async Task CompleteRunAsync(string agentName, RunStatus status, RunUsage? usage)
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId, agentName));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = status,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = usage,
        });
    }

    private async Task FailedRunAsync(RunErrorClass errorClass, string fingerprint, string message)
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = new RunError
            {
                Type = "test_error",
                Message = message,
                Class = errorClass,
                Fingerprint = fingerprint,
            },
        });
    }

    [Fact]
    public async Task Ozet_ve_zaman_serisi_kiracilar_arasinda_sizmaz()
    {
        var now = DateTimeOffset.UtcNow;
        var from = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(-1);

        AmbientTenant.TenantId = TenantA;
        var experimentId = Guid.NewGuid();
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId) with
        {
            StartedAt = from.AddMinutes(5),
            ExperimentId = experimentId,
            Variant = "control",
        });

        AmbientTenant.TenantId = TenantB;

        (await Store.GetStatisticsAsync(new RunStatisticsQuery())).TotalRuns.ShouldBe(0);
        (await Store.GetTimeSeriesAsync(new RunTimeSeriesQuery
        {
            From = from,
            To = from.AddHours(1),
            Bucket = TimeSeriesBucket.Hour,
        })).Sum(static point => point.Runs).ShouldBe(0);
        (await Store.GetExperimentResultsAsync(new ExperimentResultsQuery { ExperimentId = experimentId })).ShouldBeEmpty();

        AmbientTenant.TenantId = TenantA;

        (await Store.GetStatisticsAsync(new RunStatisticsQuery())).TotalRuns.ShouldBe(1);
        (await Store.GetTimeSeriesAsync(new RunTimeSeriesQuery
        {
            From = from,
            To = from.AddHours(1),
            Bucket = TimeSeriesBucket.Hour,
        })).Sum(static point => point.Runs).ShouldBe(1);
        (await Store.GetExperimentResultsAsync(new ExperimentResultsQuery { ExperimentId = experimentId })).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Calistirma_kaydi_gecerli_kiraciyla_damgalanir()
    {
        // IsolationTests.cs'ten tasindi (Faz 41): artik dort kosumda birden
        // calisir, yalniz PostgreSQL'de degil.
        AmbientTenant.TenantId = TenantA;

        var runId = AgentPrismId.NewId();
        var record = await Store.StartRunAsync(TestData.Run(runId));

        record.TenantId.ShouldBe(TenantA);
        (await Store.GetRunAsync(runId))!.TenantId.ShouldBe(TenantA);
    }
}
