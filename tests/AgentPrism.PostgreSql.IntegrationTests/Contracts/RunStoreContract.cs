using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests.Contracts;

/// <summary>
/// <see cref="IRunStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Bellek ici depo ile PostgreSQL deposu ayni senaryolari gecmelidir.
/// </remarks>
public abstract class RunStoreContract : IAsyncLifetime
{
    /// <summary>Test edilen depo.</summary>
    protected IRunStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir depo uretir.</summary>
    /// <returns>Kullanima hazir depo.</returns>
    protected abstract ValueTask<IRunStore> CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Store = await CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Turetilmis sinifin kendi kaynaklarini birakmasi icin kanca.</summary>
    /// <returns>Tamamlanma gorevi.</returns>
    protected virtual ValueTask OnDisposeAsync() => default;

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
}
