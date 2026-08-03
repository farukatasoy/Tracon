using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Quotas;

/// <summary>Kota denetimi ve tuketim muhasebesinin testleri.</summary>
public sealed class QuotaEnforcerTests
{
    private const string Tenant = "acme";
    private const string Agent = "support";

    [Fact]
    public async Task Kural_yoksa_izin_verilir()
    {
        var (enforcer, _, _) = Build();

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Sinir_altinda_izin_verilir()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxRuns: 5);
        await RecordAsync(enforcer, clock, runs: 4);

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Sinira_ulasinca_reddedilir()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxRuns: 3);
        await RecordAsync(enforcer, clock, runs: 3);

        var decision = await enforcer.CheckAsync(Tenant, Agent);

        decision.IsAllowed.ShouldBeFalse();
        decision.Metric.ShouldBe(QuotaMetric.Runs);
        decision.Limit.ShouldBe(3);
        decision.Used.ShouldBe(3);
        decision.ResetsAt.ShouldNotBeNull();
        decision.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Token_kotasi_ayri_uygulanir()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxTokens: 1000);
        await RecordAsync(enforcer, clock, runs: 1, tokens: 1200);

        var decision = await enforcer.CheckAsync(Tenant, Agent);

        decision.IsAllowed.ShouldBeFalse();
        decision.Metric.ShouldBe(QuotaMetric.Tokens);
    }

    [Fact]
    public async Task Fiyati_tanimsiz_calistirma_para_kotasina_katilmaz()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxCost: 1.0m);

        // Cost = null: fiyat tanimsiz. Sifir olarak da eklenmez; para kotasi
        // bu calistirmayi hic gormez ve asilmaz.
        await RecordAsync(enforcer, clock, runs: 1, tokens: 5000, cost: null);

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Para_kotasi_fiyat_tanimliyken_uygulanir()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxCost: 1.0m);
        await RecordAsync(enforcer, clock, runs: 1, cost: 1.5m);

        var decision = await enforcer.CheckAsync(Tenant, Agent);

        decision.IsAllowed.ShouldBeFalse();
        decision.Metric.ShouldBe(QuotaMetric.Cost);
    }

    [Fact]
    public async Task Kiraci_geneli_kural_baska_agentin_tuketimini_de_sayar()
    {
        var (enforcer, store, clock) = Build();

        // agentName = null: kural kiracinin tumune uygulanir.
        await SaveQuotaAsync(store, agentName: null, maxRuns: 2);

        await RecordAsync(enforcer, clock, runs: 1, agentName: "billing");
        await RecordAsync(enforcer, clock, runs: 1, agentName: "support");

        // Iki farkli agent, ayni kiraci geneli sayaci.
        (await enforcer.CheckAsync(Tenant, "anything")).IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task Agent_kurali_baska_agenti_etkilemez()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, agentName: "support", maxRuns: 1);
        await RecordAsync(enforcer, clock, runs: 1, agentName: "support");

        (await enforcer.CheckAsync(Tenant, "support")).IsAllowed.ShouldBeFalse();
        (await enforcer.CheckAsync(Tenant, "billing")).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Baska_kiracinin_tuketimi_sayilmaz()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxRuns: 1);
        await RecordAsync(enforcer, clock, runs: 1, tenantId: "other");

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Donem_donunce_sayac_sifirlanir()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxRuns: 1);
        await RecordAsync(enforcer, clock, runs: 1);

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeFalse();

        // Ertesi gun: yeni donem, yeni sayac. Eski satir silinmez, yalnizca
        // artik sorgulanmaz.
        clock.Advance(TimeSpan.FromDays(1));

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Devre_disi_kural_uygulanmaz()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxRuns: 1, enabled: false);
        await RecordAsync(enforcer, clock, runs: 5);

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Kota_kapaliyken_hicbir_sey_reddedilmez()
    {
        var (enforcer, store, clock) = Build(options => options.Enabled = false);

        await SaveQuotaAsync(store, maxRuns: 1);
        await RecordAsync(enforcer, clock, runs: 10);

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Esik_asilinca_olay_yayilir()
    {
        var publisher = new RecordingPublisher();
        var (enforcer, store, clock) = Build(publisher: publisher);

        await SaveQuotaAsync(store, maxRuns: 10);

        // %80 esigi: 8/10.
        await RecordAsync(enforcer, clock, runs: 8);

        publisher.Events.Count.ShouldBe(1);
        publisher.Events[0].EventType.ShouldBe(WebhookEvents.QuotaThreshold);
        publisher.Events[0].Payload.Quota.ShouldNotBeNull();
        publisher.Events[0].Payload.Quota!.ThresholdPercent.ShouldBe(80);
        publisher.Events[0].Payload.Quota!.Metric.ShouldBe(QuotaMetric.Runs);
    }

    [Fact]
    public async Task Ayni_esik_donem_icinde_bir_kez_yayilir()
    {
        var publisher = new RecordingPublisher();
        var (enforcer, store, clock) = Build(publisher: publisher);

        await SaveQuotaAsync(store, maxRuns: 10);

        await RecordAsync(enforcer, clock, runs: 8);   // %80 -> olay
        await RecordAsync(enforcer, clock, runs: 1);   // %90 -> olay YOK

        publisher.Events.Count(e => e.Payload.Quota?.ThresholdPercent == 80).ShouldBe(1);
    }

    [Fact]
    public async Task Yuzde_yuz_esigi_ayrica_yayilir()
    {
        var publisher = new RecordingPublisher();
        var (enforcer, store, clock) = Build(publisher: publisher);

        await SaveQuotaAsync(store, maxRuns: 10);

        await RecordAsync(enforcer, clock, runs: 8);    // %80
        await RecordAsync(enforcer, clock, runs: 2);    // %100

        publisher.Events.Select(e => e.Payload.Quota!.ThresholdPercent)
            .ShouldBe([80, 100], ignoreOrder: true);
    }

    [Fact]
    public async Task Depo_hata_verirse_varsayilan_olarak_izin_verilir()
    {
        var enforcer = new QuotaEnforcer(
            new ThrowingQuotaStore(),
            Options(new AgentPrismQuotaOptions()),
            timeProvider: new ManualTimeProvider());

        // Veritabani gecici olarak erisilemezse hizmet durmaz.
        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Siki_kurulumda_depo_hatasi_reddedilir()
    {
        var enforcer = new QuotaEnforcer(
            new ThrowingQuotaStore(),
            Options(new AgentPrismQuotaOptions { AllowOnStoreFailure = false }),
            timeProvider: new ManualTimeProvider());

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task Tuketim_yazimi_hata_verirse_istisna_sizmaz()
    {
        var enforcer = new QuotaEnforcer(
            new ThrowingQuotaStore(),
            Options(new AgentPrismQuotaOptions()),
            timeProvider: new ManualTimeProvider());

        // Gozlemlenebilirlik islevselligi bozmaz: tamamlanmis bir calistirma
        // sayac yazilamadi diye geriye donuk bozulmaz.
        await Should.NotThrowAsync(() => enforcer.RecordAsync(new QuotaConsumption
        {
            TenantId = Tenant,
            AgentName = Agent,
            OccurredAt = DateTimeOffset.UtcNow,
        }).AsTask());
    }

    private static (QuotaEnforcer Enforcer, IQuotaStore Store, ManualTimeProvider Clock) Build(
        Action<AgentPrismQuotaOptions>? configure = null,
        IWebhookPublisher? publisher = null)
    {
        var options = new AgentPrismQuotaOptions();
        configure?.Invoke(options);

        var store = new InMemoryQuotaStore();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero));

        return (new QuotaEnforcer(store, Options(options), publisher, clock), store, clock);
    }

    private static StaticOptionsMonitor<AgentPrismQuotaOptions> Options(AgentPrismQuotaOptions options)
        => new(options);

    private static async Task SaveQuotaAsync(
        IQuotaStore store,
        string? agentName = Agent,
        long? maxRuns = null,
        long? maxTokens = null,
        decimal? maxCost = null,
        bool enabled = true)
        => await store.SaveAsync(new QuotaDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            AgentName = agentName,
            Period = QuotaPeriod.Daily,
            MaxRuns = maxRuns,
            MaxTokens = maxTokens,
            MaxCost = maxCost,
            Enabled = enabled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

    private static async Task RecordAsync(
        QuotaEnforcer enforcer,
        ManualTimeProvider clock,
        long runs = 1,
        long tokens = 0,
        decimal? cost = null,
        string agentName = Agent,
        string tenantId = Tenant)
        => await enforcer.RecordAsync(new QuotaConsumption
        {
            TenantId = tenantId,
            AgentName = agentName,
            Runs = runs,
            Tokens = tokens,
            Cost = cost,
            OccurredAt = clock.GetUtcNow(),
        });

    private sealed class RecordingPublisher : IWebhookPublisher
    {
        public List<(string EventType, WebhookEventPayload Payload)> Events { get; } = [];

        public ValueTask<int> PublishAsync(
            string tenantId,
            string eventType,
            WebhookEventPayload payload,
            CancellationToken cancellationToken = default)
        {
            Events.Add((eventType, payload));

            return new ValueTask<int>(1);
        }
    }

    private sealed class ThrowingQuotaStore : IQuotaStore
    {
        public ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask<QuotaDefinition?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask<QuotaDefinition> SaveAsync(QuotaDefinition definition, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(QuotaUsageQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask AddUsageAsync(
            QuotaConsumption consumption,
            IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
