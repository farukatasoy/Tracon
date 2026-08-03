using System.Text.Json;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Quotas;

/// <summary>Olay yayincisinin testleri.</summary>
public sealed class WebhookPublisherTests
{
    private const string Tenant = "acme";

    [Fact]
    public async Task Abone_yoksa_hicbir_sey_yayilmaz()
    {
        var (publisher, _, jobs) = Build();

        (await publisher.PublishAsync(Tenant, WebhookEvents.RunCompleted, Payload())).ShouldBe(0);
        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Abone_varsa_teslim_ve_is_olusur()
    {
        var (publisher, store, jobs) = Build();

        await SubscribeAsync(store);

        (await publisher.PublishAsync(Tenant, WebhookEvents.RunCompleted, Payload())).ShouldBe(1);

        var deliveries = await store.QueryDeliveriesAsync(new WebhookDeliveryQuery { TenantId = Tenant });
        deliveries.Count.ShouldBe(1);
        deliveries[0].Status.ShouldBe(WebhookDeliveryStatus.Pending);
        deliveries[0].EventType.ShouldBe(WebhookEvents.RunCompleted);

        // Teslim, Faz 17'nin AYNI kuyruguna yazilir; ikinci bir kuyruk yoktur.
        var queued = await jobs.QueryAsync(new JobQuery { TenantId = Tenant });
        queued.Count.ShouldBe(1);
        queued[0].Kind.ShouldBe(JobKind.WebhookDelivery);
    }

    [Fact]
    public async Task Kuyruga_yazilan_is_serilestirilebilir_bir_yuk_tasir()
    {
        // 🚨 Regresyon (K-166): Payload atanmazsa alan default(JsonElement)
        // olur (ValueKind = Undefined) ve JsonElementConverter onu
        // serilestiremez — GET /api/jobs tum listeyi 500 ile dondururdu.
        // Birim ve fonksiyonel testler yakalamadi; ornek uygulama yakaladi.
        var (publisher, store, jobs) = Build();

        await SubscribeAsync(store);
        await publisher.PublishAsync(Tenant, WebhookEvents.RunCompleted, Payload());

        var queued = await jobs.QueryAsync(new JobQuery { TenantId = Tenant });

        queued[0].Payload.ValueKind.ShouldNotBe(JsonValueKind.Undefined);

        // Serilestirme gercekten calismalidir; ValueKind denetimi tek basina
        // ayni hatayi bir daha yakalamayabilir.
        Should.NotThrow(() => JsonSerializer.Serialize(queued[0].Payload));

        // Yuk, is ogesiyle ayni teslim kimligini tasir.
        JobPayload.ExtractItems(queued[0].Payload).ShouldBe(
            (await jobs.ListItemsAsync(queued[0].Id)).Select(item => item.Input).ToList());
    }

    [Fact]
    public async Task Deneme_siniri_merdivenin_uzunlugudur()
    {
        var (publisher, store, jobs) = Build(options =>
        {
            options.RetryDelays.Clear();
            options.RetryDelays.Add(TimeSpan.FromMinutes(1));
            options.RetryDelays.Add(TimeSpan.FromMinutes(5));
        });

        await SubscribeAsync(store);
        await publisher.PublishAsync(Tenant, WebhookEvents.RunCompleted, Payload());

        var queued = await jobs.QueryAsync(new JobQuery { TenantId = Tenant });

        queued[0].MaxAttempts.ShouldBe(2);
    }

    [Fact]
    public async Task Yayin_kapaliyken_hicbir_sey_olusmaz()
    {
        var (publisher, store, jobs) = Build(options => options.Enabled = false);

        await SubscribeAsync(store);

        (await publisher.PublishAsync(Tenant, WebhookEvents.RunCompleted, Payload())).ShouldBe(0);
        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Baska_olaya_abone_olan_teslim_almaz()
    {
        var (publisher, store, jobs) = Build();

        await SubscribeAsync(store, events: [WebhookEvents.QuotaThreshold]);

        (await publisher.PublishAsync(Tenant, WebhookEvents.RunCompleted, Payload())).ShouldBe(0);
        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Yuk_zarf_alanlarini_doldurur()
    {
        var (publisher, store, _) = Build();

        await SubscribeAsync(store);
        await publisher.PublishAsync(Tenant, WebhookEvents.RunCompleted, Payload());

        var delivery = (await store.QueryDeliveriesAsync(new WebhookDeliveryQuery { TenantId = Tenant }))[0];
        using var document = JsonDocument.Parse(delivery.Payload);

        document.RootElement.GetProperty("event").GetString().ShouldBe(WebhookEvents.RunCompleted);
        document.RootElement.GetProperty("tenantId").GetString().ShouldBe(Tenant);
        document.RootElement.GetProperty("deliveryId").GetString().ShouldBe(delivery.Id.ToString());
        document.RootElement.GetProperty("run").GetProperty("runId").GetString().ShouldBe("run-1");
    }

    [Fact]
    public async Task Yuk_mesaj_icerigi_tasimaz()
    {
        // K-161: yalnizca ozet. Icerik isteyen alici /api/runs/{id} cagirir.
        var (publisher, store, _) = Build();

        await SubscribeAsync(store);
        await publisher.PublishAsync(Tenant, WebhookEvents.RunCompleted, Payload());

        var delivery = (await store.QueryDeliveriesAsync(new WebhookDeliveryQuery { TenantId = Tenant }))[0];

        delivery.Payload.ShouldNotContain("messages");
        delivery.Payload.ShouldNotContain("\"text\"");
    }

    private static (IWebhookPublisher Publisher, IWebhookStore Store, IJobStore Jobs) Build(
        Action<AgentPrismWebhookOptions>? configure = null)
    {
        var options = new AgentPrismWebhookOptions();
        configure?.Invoke(options);

        var store = new InMemoryWebhookStore();
        var jobs = new InMemoryJobStore();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero));

        return (
            new WebhookPublisher(store, jobs, new StaticMonitor<AgentPrismWebhookOptions>(options), clock),
            store,
            jobs);
    }

    private static async Task SubscribeAsync(IWebhookStore store, IReadOnlyList<string>? events = null)
        => await store.SaveSubscriptionAsync(new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Name = "orders",
            Url = "https://example.com/hook",
            Events = events ?? [WebhookEvents.RunCompleted],
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

    private static WebhookEventPayload Payload()
        => new()
        {
            Run = new WebhookRunSummary
            {
                RunId = "run-1",
                AgentName = "support",
                Status = "Completed",
                InputTokens = 10,
                OutputTokens = 5,
            },
        };

    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
