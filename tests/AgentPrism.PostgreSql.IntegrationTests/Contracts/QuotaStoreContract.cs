namespace AgentPrism.PostgreSql.IntegrationTests.Contracts;

/// <summary>
/// <see cref="IQuotaStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Bellek ici depo ile PostgreSQL deposu ayni senaryolari gecmelidir. Ozellikle
/// iki kural kritiktir: kapsam benzersizligi (<c>COALESCE(agent_name, '')</c>)
/// ve tuketim artirmanin atomikligi (<c>ON CONFLICT DO UPDATE</c>).
/// </remarks>
public abstract class QuotaStoreContract : IAsyncLifetime
{
    private const string Tenant = "test";

    private static readonly DateOnly Today = new(2026, 8, 3);

    private static readonly IReadOnlyDictionary<QuotaPeriod, DateOnly> Periods =
        new Dictionary<QuotaPeriod, DateOnly>
        {
            [QuotaPeriod.Daily] = Today,
            [QuotaPeriod.Monthly] = new(2026, 8, 1),
        };

    /// <summary>Test edilen depo.</summary>
    protected IQuotaStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir depo uretir.</summary>
    protected abstract ValueTask<IQuotaStore> CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Store = await CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Turetilmis sinifin kendi kaynaklarini birakmasi icin kanca.</summary>
    protected virtual ValueTask OnDisposeAsync() => default;

    [Fact]
    public async Task Kaydedilen_kural_geri_okunur()
    {
        var saved = await Store.SaveAsync(Quota(maxRuns: 100, maxTokens: 5000, maxCost: 12.5m));

        var loaded = await Store.GetAsync(Tenant, saved.Id);

        loaded.ShouldNotBeNull();
        loaded.MaxRuns.ShouldBe(100);
        loaded.MaxTokens.ShouldBe(5000);
        loaded.MaxCost.ShouldBe(12.5m);
        loaded.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Bos_sinirlar_null_olarak_korunur()
    {
        var saved = await Store.SaveAsync(Quota(maxRuns: 10));

        var loaded = await Store.GetAsync(Tenant, saved.Id);

        loaded.ShouldNotBeNull();
        loaded.MaxRuns.ShouldBe(10);
        loaded.MaxTokens.ShouldBeNull();
        loaded.MaxCost.ShouldBeNull();
    }

    [Fact]
    public async Task Ayni_kapsam_ikinci_kez_kaydedilince_uzerine_yazilir()
    {
        // 🚨 PostgreSQL'de NULL'lar birbirine esit sayilmaz; duz bir UNIQUE
        // kisiti agent_name NULL olan ayni kuralin sinirsiz kez eklenmesine
        // izin verirdi. Benzersizlik COALESCE(agent_name, '') ile kurulur.
        await Store.SaveAsync(Quota(agentName: null, maxRuns: 10));
        await Store.SaveAsync(Quota(agentName: null, maxRuns: 20));

        var all = await Store.ListAsync(Tenant);

        all.Count.ShouldBe(1);
        all[0].MaxRuns.ShouldBe(20);
    }

    [Fact]
    public async Task Farkli_agent_ayri_kural_olur()
    {
        await Store.SaveAsync(Quota(agentName: null, maxRuns: 10));
        await Store.SaveAsync(Quota(agentName: "support", maxRuns: 5));

        (await Store.ListAsync(Tenant)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Farkli_donem_ayri_kural_olur()
    {
        await Store.SaveAsync(Quota(period: QuotaPeriod.Daily, maxRuns: 10));
        await Store.SaveAsync(Quota(period: QuotaPeriod.Monthly, maxRuns: 200));

        (await Store.ListAsync(Tenant)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Baska_kiracinin_kurali_gorunmez()
    {
        var saved = await Store.SaveAsync(Quota(maxRuns: 10));

        (await Store.GetAsync("other", saved.Id)).ShouldBeNull();
        (await Store.ListAsync("other")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Silinen_kural_geri_okunmaz()
    {
        var saved = await Store.SaveAsync(Quota(maxRuns: 10));

        (await Store.DeleteAsync(Tenant, saved.Id)).ShouldBeTrue();
        (await Store.GetAsync(Tenant, saved.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Olmayan_kurali_silmek_false_doner()
        => (await Store.DeleteAsync(Tenant, Guid.NewGuid())).ShouldBeFalse();

    [Fact]
    public async Task Tuketim_hem_agent_hem_kiraci_sayacini_artirir()
    {
        await Store.AddUsageAsync(Consumption(runs: 1, tokens: 100, cost: 0.5m), Periods);

        var usage = await Store.GetUsageAsync(new QuotaUsageQuery { TenantId = Tenant });

        // Iki donem x iki kapsam (agent + kiraci geneli) = dort satir.
        usage.Count.ShouldBe(4);

        var agentDaily = Find(usage, "support", QuotaPeriod.Daily);
        agentDaily.Runs.ShouldBe(1);
        agentDaily.Tokens.ShouldBe(100);
        agentDaily.Cost.ShouldBe(0.5m);

        // Bos ad = kiraci geneli sayaci.
        var tenantDaily = Find(usage, string.Empty, QuotaPeriod.Daily);
        tenantDaily.Runs.ShouldBe(1);
        tenantDaily.Tokens.ShouldBe(100);
    }

    [Fact]
    public async Task Ard_arda_tuketim_toplanir()
    {
        await Store.AddUsageAsync(Consumption(runs: 1, tokens: 100, cost: 0.5m), Periods);
        await Store.AddUsageAsync(Consumption(runs: 1, tokens: 250, cost: 1.25m), Periods);
        await Store.AddUsageAsync(Consumption(runs: 1, tokens: 50, cost: 0.25m), Periods);

        var usage = await Store.GetUsageAsync(new QuotaUsageQuery { TenantId = Tenant });
        var daily = Find(usage, "support", QuotaPeriod.Daily);

        daily.Runs.ShouldBe(3);
        daily.Tokens.ShouldBe(400);
        daily.Cost.ShouldBe(2.0m);
    }

    [Fact]
    public async Task Eszamanli_artirma_hicbir_artisi_kaybetmez()
    {
        // 🚨 ON CONFLICT DO UPDATE atomiktir. Okuma-degistir-yaz dizisi olsaydi
        // ayni anda biten calistirmalardan bazilari sessizce yutulurdu.
        const int Concurrency = 20;

        await Task.WhenAll(Enumerable.Range(0, Concurrency).Select(_ =>
            Store.AddUsageAsync(Consumption(runs: 1, tokens: 10), Periods).AsTask()));

        var usage = await Store.GetUsageAsync(new QuotaUsageQuery { TenantId = Tenant });
        var daily = Find(usage, "support", QuotaPeriod.Daily);

        daily.Runs.ShouldBe(Concurrency);
        daily.Tokens.ShouldBe(Concurrency * 10);
    }

    [Fact]
    public async Task Fiyati_tanimsiz_tuketim_para_toplamini_bozmaz()
    {
        await Store.AddUsageAsync(Consumption(runs: 1, tokens: 100, cost: 2.0m), Periods);

        // Cost = null: fiyat tanimsiz. Toplam degismemelidir; NULL eklemek
        // toplami tumuyle NULL yapardi.
        await Store.AddUsageAsync(Consumption(runs: 1, tokens: 100, cost: null), Periods);

        var usage = await Store.GetUsageAsync(new QuotaUsageQuery { TenantId = Tenant });
        var daily = Find(usage, "support", QuotaPeriod.Daily);

        daily.Runs.ShouldBe(2);
        daily.Cost.ShouldBe(2.0m);
    }

    [Fact]
    public async Task Ayri_donem_ayri_sayac_tutar()
    {
        await Store.AddUsageAsync(Consumption(runs: 1), Periods);

        var nextDay = new Dictionary<QuotaPeriod, DateOnly>
        {
            [QuotaPeriod.Daily] = Today.AddDays(1),
            [QuotaPeriod.Monthly] = new(2026, 8, 1),
        };

        await Store.AddUsageAsync(Consumption(runs: 1), nextDay);

        var usage = await Store.GetUsageAsync(new QuotaUsageQuery { TenantId = Tenant });

        // Gunluk sayac bolundu, aylik sayac birikti.
        Find(usage, "support", QuotaPeriod.Daily, Today).Runs.ShouldBe(1);
        Find(usage, "support", QuotaPeriod.Daily, Today.AddDays(1)).Runs.ShouldBe(1);
        Find(usage, "support", QuotaPeriod.Monthly).Runs.ShouldBe(2);
    }

    [Fact]
    public async Task Kullanim_agent_adina_gore_suzulur()
    {
        await Store.AddUsageAsync(Consumption(runs: 1, agentName: "support"), Periods);
        await Store.AddUsageAsync(Consumption(runs: 1, agentName: "billing"), Periods);

        var filtered = await Store.GetUsageAsync(
            new QuotaUsageQuery { TenantId = Tenant, AgentName = "support" });

        filtered.ShouldAllBe(record => record.AgentName == "support");
        filtered.Count.ShouldBe(2);  // gunluk + aylik
    }

    [Fact]
    public async Task Kullanim_doneme_gore_suzulur()
    {
        await Store.AddUsageAsync(Consumption(runs: 1), Periods);

        var filtered = await Store.GetUsageAsync(
            new QuotaUsageQuery { TenantId = Tenant, Period = QuotaPeriod.Daily });

        filtered.ShouldAllBe(record => record.Period == QuotaPeriod.Daily);
    }

    [Fact]
    public async Task Baska_kiracinin_tuketimi_gorunmez()
    {
        await Store.AddUsageAsync(Consumption(runs: 1), Periods);

        (await Store.GetUsageAsync(new QuotaUsageQuery { TenantId = "other" })).ShouldBeEmpty();
    }

    private static QuotaUsageRecord Find(
        IReadOnlyList<QuotaUsageRecord> usage,
        string agentName,
        QuotaPeriod period,
        DateOnly? periodStart = null)
        => usage.First(record =>
            string.Equals(record.AgentName, agentName, StringComparison.Ordinal)
            && record.Period == period
            && (periodStart is null || record.PeriodStart == periodStart));

    private static QuotaDefinition Quota(
        string? agentName = "support",
        QuotaPeriod period = QuotaPeriod.Daily,
        long? maxRuns = null,
        long? maxTokens = null,
        decimal? maxCost = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            AgentName = agentName,
            Period = period,
            MaxRuns = maxRuns,
            MaxTokens = maxTokens,
            MaxCost = maxCost,
            Enabled = true,
            CreatedAt = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
        };

    private static QuotaConsumption Consumption(
        long runs = 1,
        long tokens = 0,
        decimal? cost = null,
        string agentName = "support")
        => new()
        {
            TenantId = Tenant,
            AgentName = agentName,
            Runs = runs,
            Tokens = tokens,
            Cost = cost,
            OccurredAt = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
        };
}
