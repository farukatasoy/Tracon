namespace AgentPrism.StoreContracts;

/// <summary>
/// Behavior tests for the <see cref="IWebhookStore"/> contract.
/// </summary>
/// <remarks>
/// 🚨 This contract has no <strong>secret field</strong>. The store only carries
/// the name of the configuration key the secret is read from (K-059); tests
/// verify this.
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

        // Event matching is also locked to the tenant; otherwise one tenant's event
        // would be delivered to another tenant's endpoint.
        var matches = await Store.FindForEventAsync(tenantId, "run.completed");
        matches.Any(item => item.Id == subscription?.Id).ShouldBe(subscription is not null);

        return subscription is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
    {
        var deliveries = await Store.QueryDeliveriesAsync(new WebhookDeliveryQuery { TenantId = tenantId });
        var subscriptions = await Store.ListSubscriptionsAsync(tenantId);

        // Delivery history must also respect the same tenant boundary as the subscription.
        deliveries.Count.ShouldBe(subscriptions.Count);

        return subscriptions.Count;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteSubscriptionAsync(tenantId, (string)key);

    private const string Tenant = "test";

    [Fact]
    public async Task Saved_subscription_is_read_back()
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
    public async Task Headers_are_preserved()
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
    public async Task Saving_the_same_name_a_second_time_overwrites_it()
    {
        await Store.SaveSubscriptionAsync(Subscription(url: "https://one.example.com/hook"));
        await Store.SaveSubscriptionAsync(Subscription(url: "https://two.example.com/hook"));

        var all = await Store.ListSubscriptionsAsync(Tenant);

        all.Count.ShouldBe(1);
        all[0].Url.ShouldBe("https://two.example.com/hook");
    }

    [Fact]
    public async Task Subscribers_for_an_event_are_found()
    {
        await Store.SaveSubscriptionAsync(Subscription(name: "a", events: ["run.completed"]));
        await Store.SaveSubscriptionAsync(Subscription(name: "b", events: ["run.failed"]));
        await Store.SaveSubscriptionAsync(
            Subscription(name: "c", events: ["run.completed", "quota.threshold"]));

        var matches = await Store.FindForEventAsync(Tenant, "run.completed");

        matches.Select(subscription => subscription.Name).ShouldBe(["a", "c"], ignoreOrder: true);
    }

    [Fact]
    public async Task Disabled_subscription_does_not_appear_in_event_search()
    {
        await Store.SaveSubscriptionAsync(
            Subscription(name: "a", events: ["run.completed"], enabled: false));

        (await Store.FindForEventAsync(Tenant, "run.completed")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Event_name_matches_exactly()
    {
        await Store.SaveSubscriptionAsync(Subscription(events: ["run.completed"]));

        // Searching for 'run.completed'; 'run.complete' or 'run.completed.v2'
        // must not match.
        (await Store.FindForEventAsync(Tenant, "run.complete")).ShouldBeEmpty();
        (await Store.FindForEventAsync(Tenant, "run.completed.v2")).ShouldBeEmpty();
        (await Store.FindForEventAsync(Tenant, "run.completed")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Another_tenants_subscription_is_not_visible()
    {
        await Store.SaveSubscriptionAsync(Subscription());

        (await Store.GetSubscriptionAsync("other", "orders")).ShouldBeNull();
        (await Store.ListSubscriptionsAsync("other")).ShouldBeEmpty();
        (await Store.FindForEventAsync("other", "run.completed")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleted_subscription_is_not_read_back()
    {
        await Store.SaveSubscriptionAsync(Subscription());

        (await Store.DeleteSubscriptionAsync(Tenant, "orders")).ShouldBeTrue();
        (await Store.GetSubscriptionAsync(Tenant, "orders")).ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_a_subscription_also_deletes_its_deliveries()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        await Store.CreateDeliveryAsync(Delivery(subscription.Id));

        await Store.DeleteSubscriptionAsync(Tenant, "orders");

        var deliveries = await Store.QueryDeliveriesAsync(new WebhookDeliveryQuery { TenantId = Tenant });

        deliveries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delivery_record_is_read_back()
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
    public async Task Successful_delivery_result_is_recorded()
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
    public async Task Failed_delivery_does_not_record_a_delivery_time()
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
    public async Task Deliveries_are_filtered_by_status()
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
    public async Task Failure_counter_increments_and_disables_the_subscription_at_the_threshold()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var now = DateTimeOffset.UtcNow;

        // Threshold 3: the first two failures do not disable it.
        (await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 3, now)).ShouldBeFalse();
        (await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 3, now)).ShouldBeFalse();

        (await Store.GetSubscriptionAsync(Tenant, "orders"))!.Enabled.ShouldBeTrue();

        // The third one disables it and reports that.
        (await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 3, now)).ShouldBeTrue();

        var loaded = await Store.GetSubscriptionAsync(Tenant, "orders");
        loaded.ShouldNotBeNull();
        loaded.Enabled.ShouldBeFalse();
        loaded.ConsecutiveFailures.ShouldBe(3);
    }

    [Fact]
    public async Task Successful_delivery_resets_the_counter()
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
