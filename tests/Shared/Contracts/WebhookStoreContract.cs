namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IWebhookStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// 🚨 Bu sozlesmede bir <strong>sir alani yoktur</strong>. Depo yalnizca
/// sirrin okunacagi yapilandirma anahtarinin adini tasir (K-059); testler bunu
/// dogrular.
/// </remarks>
public abstract class WebhookStoreContract : TenantIsolationContract<IWebhookStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        var subscription = await Store.SaveSubscriptionAsync(
            Subscription(name) with { Id = Guid.NewGuid(), TenantId = tenantId });

        await Store.CreateDeliveryAsync(Delivery(subscription.Id) with { TenantId = tenantId });

        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        var subscription = await Store.GetSubscriptionAsync(tenantId, (string)key);

        // Olay eslesmesi de kiraciya kilitlidir; aksi halde bir kiracinin olayi
        // digerinin uc noktasina teslim edilirdi.
        var matches = await Store.FindForEventAsync(tenantId, "run.completed");
        matches.Any(item => item.Id == subscription?.Id).ShouldBe(subscription is not null);

        return subscription is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
    {
        var deliveries = await Store.QueryDeliveriesAsync(new WebhookDeliveryQuery { TenantId = tenantId });
        var subscriptions = await Store.ListSubscriptionsAsync(tenantId);

        // Teslimat gecmisi de abonelikle ayni kiraci sinirini tasimalidir.
        deliveries.Count.ShouldBe(subscriptions.Count);

        return subscriptions.Count;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteSubscriptionAsync(tenantId, (string)key);

    private const string Tenant = "test";

    [Fact]
    public async Task Kaydedilen_abonelik_geri_okunur()
    {
        await Store.SaveSubscriptionAsync(Subscription(
            events: ["run.completed", "run.failed"],
            secretKey: "AgentPrism:Webhooks:Secrets:orders"));

        var loaded = await Store.GetSubscriptionAsync(Tenant, "orders");

        loaded.ShouldNotBeNull();
        loaded.Url.ShouldBe("https://example.com/hook");
        loaded.Events.ShouldBe(["run.completed", "run.failed"]);
        loaded.SecretConfigurationKey.ShouldBe("AgentPrism:Webhooks:Secrets:orders");
        loaded.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Basliklar_korunur()
    {
        await Store.SaveSubscriptionAsync(Subscription(
            headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Team"] = "platform",
                ["X-Env"] = "prod",
            }));

        var loaded = await Store.GetSubscriptionAsync(Tenant, "orders");

        loaded.ShouldNotBeNull();
        loaded.Headers.Count.ShouldBe(2);
        loaded.Headers["X-Team"].ShouldBe("platform");
        loaded.Headers["X-Env"].ShouldBe("prod");
    }

    [Fact]
    public async Task Ayni_ad_ikinci_kez_kaydedilince_uzerine_yazilir()
    {
        await Store.SaveSubscriptionAsync(Subscription(url: "https://one.example.com/hook"));
        await Store.SaveSubscriptionAsync(Subscription(url: "https://two.example.com/hook"));

        var all = await Store.ListSubscriptionsAsync(Tenant);

        all.Count.ShouldBe(1);
        all[0].Url.ShouldBe("https://two.example.com/hook");
    }

    [Fact]
    public async Task Olaya_abone_olanlar_bulunur()
    {
        await Store.SaveSubscriptionAsync(Subscription(name: "a", events: ["run.completed"]));
        await Store.SaveSubscriptionAsync(Subscription(name: "b", events: ["run.failed"]));
        await Store.SaveSubscriptionAsync(
            Subscription(name: "c", events: ["run.completed", "quota.threshold"]));

        var matches = await Store.FindForEventAsync(Tenant, "run.completed");

        matches.Select(subscription => subscription.Name).ShouldBe(["a", "c"], ignoreOrder: true);
    }

    [Fact]
    public async Task Devre_disi_abonelik_olay_aramasinda_cikmaz()
    {
        await Store.SaveSubscriptionAsync(
            Subscription(name: "a", events: ["run.completed"], enabled: false));

        (await Store.FindForEventAsync(Tenant, "run.completed")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Olay_adi_tam_eslesir()
    {
        await Store.SaveSubscriptionAsync(Subscription(events: ["run.completed"]));

        // 'run.completed' araniyor; 'run.complete' veya 'run.completed.v2'
        // eslesmemelidir.
        (await Store.FindForEventAsync(Tenant, "run.complete")).ShouldBeEmpty();
        (await Store.FindForEventAsync(Tenant, "run.completed.v2")).ShouldBeEmpty();
        (await Store.FindForEventAsync(Tenant, "run.completed")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Baska_kiracinin_aboneligi_gorunmez()
    {
        await Store.SaveSubscriptionAsync(Subscription());

        (await Store.GetSubscriptionAsync("other", "orders")).ShouldBeNull();
        (await Store.ListSubscriptionsAsync("other")).ShouldBeEmpty();
        (await Store.FindForEventAsync("other", "run.completed")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Silinen_abonelik_geri_okunmaz()
    {
        await Store.SaveSubscriptionAsync(Subscription());

        (await Store.DeleteSubscriptionAsync(Tenant, "orders")).ShouldBeTrue();
        (await Store.GetSubscriptionAsync(Tenant, "orders")).ShouldBeNull();
    }

    [Fact]
    public async Task Abonelik_silinince_teslimleri_de_silinir()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        await Store.CreateDeliveryAsync(Delivery(subscription.Id));

        await Store.DeleteSubscriptionAsync(Tenant, "orders");

        var deliveries = await Store.QueryDeliveriesAsync(new WebhookDeliveryQuery { TenantId = Tenant });

        deliveries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Teslim_kaydi_geri_okunur()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var delivery = await Store.CreateDeliveryAsync(Delivery(subscription.Id));

        var loaded = await Store.GetDeliveryAsync(delivery.Id);

        loaded.ShouldNotBeNull();
        loaded.EventType.ShouldBe("run.completed");
        loaded.Status.ShouldBe(WebhookDeliveryStatus.Pending);
        loaded.Attempt.ShouldBe(0);
    }

    [Fact]
    public async Task Basarili_teslim_sonucu_yazilir()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var delivery = await Store.CreateDeliveryAsync(Delivery(subscription.Id));
        var deliveredAt = new DateTimeOffset(2026, 8, 3, 12, 5, 0, TimeSpan.Zero);

        await Store.RecordDeliveryResultAsync(new WebhookDeliveryResult
        {
            DeliveryId = delivery.Id,
            Status = WebhookDeliveryStatus.Delivered,
            Attempt = 1,
            ResponseCode = 204,
            RecordedAt = deliveredAt,
        });

        var loaded = await Store.GetDeliveryAsync(delivery.Id);

        loaded.ShouldNotBeNull();
        loaded.Status.ShouldBe(WebhookDeliveryStatus.Delivered);
        loaded.ResponseCode.ShouldBe(204);
        loaded.Attempt.ShouldBe(1);
        loaded.DeliveredAt.ShouldBe(deliveredAt);
    }

    [Fact]
    public async Task Basarisiz_teslim_teslim_zamani_yazmaz()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var delivery = await Store.CreateDeliveryAsync(Delivery(subscription.Id));

        await Store.RecordDeliveryResultAsync(new WebhookDeliveryResult
        {
            DeliveryId = delivery.Id,
            Status = WebhookDeliveryStatus.Failed,
            Attempt = 5,
            ResponseCode = 500,
            Error = "HTTP 500",
            RecordedAt = DateTimeOffset.UtcNow,
        });

        var loaded = await Store.GetDeliveryAsync(delivery.Id);

        loaded.ShouldNotBeNull();
        loaded.Status.ShouldBe(WebhookDeliveryStatus.Failed);
        loaded.Error.ShouldBe("HTTP 500");
        loaded.DeliveredAt.ShouldBeNull();
    }

    [Fact]
    public async Task Teslimler_duruma_gore_suzulur()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var first = await Store.CreateDeliveryAsync(Delivery(subscription.Id));
        await Store.CreateDeliveryAsync(Delivery(subscription.Id));

        await Store.RecordDeliveryResultAsync(new WebhookDeliveryResult
        {
            DeliveryId = first.Id,
            Status = WebhookDeliveryStatus.Delivered,
            Attempt = 1,
            RecordedAt = DateTimeOffset.UtcNow,
        });

        var delivered = await Store.QueryDeliveriesAsync(new WebhookDeliveryQuery
        {
            TenantId = Tenant,
            Status = WebhookDeliveryStatus.Delivered,
        });

        delivered.Count.ShouldBe(1);
        delivered[0].Id.ShouldBe(first.Id);
    }

    [Fact]
    public async Task Basarisizlik_sayaci_artar_ve_esikte_abonelik_kapanir()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var now = DateTimeOffset.UtcNow;

        // Esik 3: ilk iki basarisizlik kapatmaz.
        (await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 3, now)).ShouldBeFalse();
        (await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 3, now)).ShouldBeFalse();

        (await Store.GetSubscriptionAsync(Tenant, "orders"))!.Enabled.ShouldBeTrue();

        // Ucuncusu kapatir ve bunu bildirir.
        (await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 3, now)).ShouldBeTrue();

        var loaded = await Store.GetSubscriptionAsync(Tenant, "orders");
        loaded.ShouldNotBeNull();
        loaded.Enabled.ShouldBeFalse();
        loaded.ConsecutiveFailures.ShouldBe(3);
    }

    [Fact]
    public async Task Basarili_teslim_sayaci_sifirlar()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var now = DateTimeOffset.UtcNow;

        await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 5, now);
        await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 5, now);
        await Store.RecordSubscriptionOutcomeAsync(subscription.Id, true, 5, now);

        var loaded = await Store.GetSubscriptionAsync(Tenant, "orders");

        loaded.ShouldNotBeNull();
        loaded.ConsecutiveFailures.ShouldBe(0);
        loaded.Enabled.ShouldBeTrue();
    }

    private static WebhookSubscription Subscription(
        string name = "orders",
        string url = "https://example.com/hook",
        IReadOnlyList<string>? events = null,
        string? secretKey = null,
        IReadOnlyDictionary<string, string>? headers = null,
        bool enabled = true)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Name = name,
            Url = url,
            Events = events ?? ["run.completed"],
            SecretConfigurationKey = secretKey,
            Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Enabled = enabled,
            CreatedAt = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
        };

    private static WebhookDelivery Delivery(Guid subscriptionId)
        => new()
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            TenantId = Tenant,
            EventType = "run.completed",
            Payload = """{"event":"run.completed"}""",
            Status = WebhookDeliveryStatus.Pending,
            CreatedAt = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
        };
}
