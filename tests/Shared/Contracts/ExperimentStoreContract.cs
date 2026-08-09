namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IExperimentStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Bu testler <strong>her uygulama icin</strong> calistirilir. Bellek ici depo ile
/// PostgreSQL deposu arasindaki davranis farki hatadir; bu sinif o farki yakalar.
/// </remarks>
public abstract class ExperimentStoreContract : TenantIsolationContract<IExperimentStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        await Store.SaveAsync(Experiment(name, tenantId));
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId, (string)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(tenantId)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (string)key);

    private const string TenantId = "default";

    [Fact]
    public async Task Kayit_ve_getirme_gidip_gelir()
    {
        var saved = await Store.SaveAsync(Experiment("d1"));

        var fetched = await Store.GetAsync(TenantId, "d1");

        fetched.ShouldNotBeNull();
        fetched.AgentName.ShouldBe(saved.AgentName);
        fetched.Status.ShouldBe(ExperimentStatus.Draft);
        fetched.Variants.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Draft_deney_guncellenebilir()
    {
        await Store.SaveAsync(Experiment("d1"));

        var updated = await Store.SaveAsync(Experiment("d1") with
        {
            Variants =
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 30 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 70 },
            ],
        });

        updated.Variants.Single(static v => string.Equals(v.Name, "v2", StringComparison.Ordinal)).Weight.ShouldBe(70);
    }

    [Fact]
    public async Task Calisan_deney_guncellenemez()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.SaveAsync(Experiment("d1")));
    }

    [Fact]
    public async Task Baslatma_calisan_deneyi_dondurur()
    {
        await Store.SaveAsync(Experiment("d1"));

        var started = await Store.StartAsync(TenantId, "d1");

        started.Status.ShouldBe(ExperimentStatus.Running);
        started.StartedAt.ShouldNotBeNull();

        var running = await Store.GetRunningAsync(TenantId, "agent-a");
        running.ShouldNotBeNull();
        running!.Name.ShouldBe("d1");
    }

    [Fact]
    public async Task Ayni_agent_icin_ikinci_baslatma_reddedilir()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        await Store.SaveAsync(Experiment("d2"));

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.StartAsync(TenantId, "d2"));
    }

    [Fact]
    public async Task Durdurma_deneyi_durdurur_ve_calisan_slotu_bosaltir()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        var stopped = await Store.StopAsync(TenantId, "d1");

        stopped.Status.ShouldBe(ExperimentStatus.Stopped);
        stopped.EndedAt.ShouldNotBeNull();

        (await Store.GetRunningAsync(TenantId, "agent-a")).ShouldBeNull();

        // Slot bosaldigi icin baska bir deney ayni agent'ta baslatilabilir.
        await Store.SaveAsync(Experiment("d2"));
        var secondStart = await Store.StartAsync(TenantId, "d2");
        secondStart.Status.ShouldBe(ExperimentStatus.Running);
    }

    [Fact]
    public async Task Calismayan_deney_durdurulamaz()
    {
        await Store.SaveAsync(Experiment("d1"));

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.StopAsync(TenantId, "d1"));
    }

    [Fact]
    public async Task Olmayan_deney_baslatilamaz()
        => await Should.ThrowAsync<AgentPrismException>(async () => await Store.StartAsync(TenantId, "yok"));

    [Fact]
    public async Task Calisan_deney_silinemez()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.DeleteAsync(TenantId, "d1"));
    }

    [Fact]
    public async Task Draft_deney_silinebilir()
    {
        await Store.SaveAsync(Experiment("d1"));

        (await Store.DeleteAsync(TenantId, "d1")).ShouldBeTrue();
        (await Store.GetAsync(TenantId, "d1")).ShouldBeNull();
    }

    [Fact]
    public async Task Olmayan_deney_silinirken_false_doner()
        => (await Store.DeleteAsync(TenantId, "yok")).ShouldBeFalse();

    [Fact]
    public async Task Listeleme_yalnizca_o_kiraciyi_getirir()
    {
        await Store.SaveAsync(Experiment("d1", tenantId: "kiraci-a"));
        await Store.SaveAsync(Experiment("d2", tenantId: "kiraci-b"));

        var listA = await Store.ListAsync("kiraci-a");

        listA.ShouldHaveSingleItem().Name.ShouldBe("d1");
    }

    [Fact]
    public async Task Calisan_deney_yoksa_null_doner()
        => (await Store.GetRunningAsync(TenantId, "agent-a")).ShouldBeNull();

    [Fact]
    public async Task Kanarya_kurali_tanimlanip_okunur()
    {
        await Store.SaveAsync(Experiment("d1"));

        var updated = await Store.SetCanaryPolicyAsync(TenantId, "d1", CanaryPolicy());

        updated.Canary.ShouldNotBeNull();
        updated.Canary!.CanaryVariant.ShouldBe("v2");
        updated.Canary.MaxErrorRateDelta.ShouldBe(0.1);

        var fetched = await Store.GetAsync(TenantId, "d1");
        fetched!.Canary.ShouldNotBeNull();
        fetched.Canary!.RampSteps.ShouldBe([5, 25, 50, 100]);
    }

    [Fact]
    public async Task Kanarya_kurali_null_ile_kaldirilir()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.SetCanaryPolicyAsync(TenantId, "d1", CanaryPolicy());

        var cleared = await Store.SetCanaryPolicyAsync(TenantId, "d1", null);

        cleared.Canary.ShouldBeNull();
        (await Store.GetAsync(TenantId, "d1"))!.Canary.ShouldBeNull();
    }

    [Fact]
    public async Task Kanarya_kurali_Draft_deneyde_de_tanimlanabilir()
    {
        await Store.SaveAsync(Experiment("d1"));

        // 🚨 SetCanaryPolicyAsync SaveAsync'in aksine duruma bagli DEGILDIR.
        var updated = await Store.SetCanaryPolicyAsync(TenantId, "d1", CanaryPolicy());

        updated.Status.ShouldBe(ExperimentStatus.Draft);
        updated.Canary.ShouldNotBeNull();
    }

    [Fact]
    public async Task Kanarya_kurali_deneyi_duzenleme_Draft_kisitindan_MUAF_tutulur()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        // SaveAsync Running'de reddedilir ama SetCanaryPolicyAsync reddedilmez.
        var updated = await Store.SetCanaryPolicyAsync(TenantId, "d1", CanaryPolicy());

        updated.Status.ShouldBe(ExperimentStatus.Running);
        updated.Canary.ShouldNotBeNull();
    }

    [Fact]
    public async Task Kanarya_rampasi_calisan_deneyde_agirligi_gunceller_ve_Running_kalir()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        var advanced = await Store.AdvanceCanaryRampAsync(
            TenantId,
            "d1",
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 75 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 25 },
            ]);

        advanced.Status.ShouldBe(ExperimentStatus.Running);
        advanced.Variants.Single(static v => string.Equals(v.Name, "v2", StringComparison.Ordinal)).Weight.ShouldBe(25);
    }

    [Fact]
    public async Task Kanarya_rampasi_calismayan_deneyde_reddedilir()
    {
        await Store.SaveAsync(Experiment("d1"));

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.AdvanceCanaryRampAsync(
            TenantId,
            "d1",
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 75 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 25 },
            ]));
    }

    [Fact]
    public async Task Geri_alma_deneyi_durdurur_agirliklari_dondurur_ve_nedeni_yazar()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        var rolledBack = await Store.RollbackCanaryAsync(
            TenantId,
            "d1",
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 100 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 0 },
            ],
            "hata orani esigin uzerinde");

        rolledBack.Status.ShouldBe(ExperimentStatus.Stopped);
        rolledBack.EndedAt.ShouldNotBeNull();
        rolledBack.RollbackReason.ShouldBe("hata orani esigin uzerinde");
        rolledBack.Variants.Single(static v => string.Equals(v.Name, "v2", StringComparison.Ordinal)).Weight.ShouldBe(0);

        (await Store.GetRunningAsync(TenantId, "agent-a")).ShouldBeNull();
    }

    [Fact]
    public async Task ListRunningWithCanaryAsync_yalniz_kanarya_tanimli_calisan_deneyleri_getirir()
    {
        // d1: kanaryasiz Running -- listede OLMAMALI.
        await Store.SaveAsync(Experiment("d1", tenantId: "kanarya-a"));
        await Store.StartAsync("kanarya-a", "d1");

        // d2: kanaryali Draft -- listede OLMAMALI (Running degil).
        await Store.SaveAsync(Experiment("d2", tenantId: "kanarya-a"));
        await Store.SetCanaryPolicyAsync("kanarya-a", "d2", CanaryPolicy());

        // d3: kanaryali VE Running -- listede OLMALI, baska bir kiracida bile.
        await Store.SaveAsync(Experiment("d3", tenantId: "kanarya-b"));
        await Store.SetCanaryPolicyAsync("kanarya-b", "d3", CanaryPolicy());
        await Store.StartAsync("kanarya-b", "d3");

        var running = await Store.ListRunningWithCanaryAsync();

        running.ShouldContain(experiment => string.Equals(experiment.Name, "d3", StringComparison.Ordinal));
        running.ShouldNotContain(experiment => string.Equals(experiment.Name, "d1", StringComparison.Ordinal));
        running.ShouldNotContain(experiment => string.Equals(experiment.Name, "d2", StringComparison.Ordinal));
    }

    private static CanaryPolicy CanaryPolicy()
        => new()
        {
            CanaryVariant = "v2",
            MaxErrorRateDelta = 0.1,
            MinScore = 60,
            MinSampleSize = 20,
            RampSteps = [5, 25, 50, 100],
            RampInterval = TimeSpan.FromHours(1),
        };

    private static Experiment Experiment(string name, string tenantId = TenantId)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            AgentName = "agent-a",
            Variants =
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 50 },
            ],
        };

    [Fact]
    public async Task Deney_yasam_dongusu_kiracilar_arasinda_sizmaz()
    {
        await Store.SaveAsync(Experiment("kampanya", "tenant-a"));

        // Baslatma, durdurma ve "calisan deneyi bul" ayni siniri tasimalidir.
        await Should.ThrowAsync<AgentPrismException>(async () => await Store.StartAsync("tenant-b", "kampanya"));

        await Store.StartAsync("tenant-a", "kampanya");

        (await Store.GetRunningAsync("tenant-b", "agent-a")).ShouldBeNull();
        (await Store.GetRunningAsync("tenant-a", "agent-a")).ShouldNotBeNull();

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.StopAsync("tenant-b", "kampanya"));

        await Store.StopAsync("tenant-a", "kampanya");

        (await Store.GetRunningAsync("tenant-a", "agent-a")).ShouldBeNull();
    }
}
