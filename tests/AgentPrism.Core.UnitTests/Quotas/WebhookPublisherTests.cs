using System.Text.Json;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Quotas;

/// <summary>Tests of the event publisher.</summary>
public sealed class WebhookPublisherTests
{
    private const string Tenant = "acme";

    [Fact]
    public async Task No_event_is_published_when_there_are_no_subscribers()
    {
        var (publisher, _, jobs) = Build();

        (await publisher.PublishAsync(Tenant, WebhookEvents.RunCompleted, Payload())).ShouldBe(0);
        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Delivery_and_job_are_created_when_a_subscriber_exists()
    {
        var (publisher, store, jobs) = Build();

        await SubscribeAsync(store);

        (await publisher.PublishAsync(Tenant, WebhookEvents.RunCompleted, Payload())).ShouldBe(1);

        var deliveries = await store.QueryDeliveriesAsync(new WebhookDeliveryQuery { TenantId = Tenant });
        deliveries.Count.ShouldBe(1);
        deliveries[0].Status.ShouldBe(WebhookDeliveryStatus.Pending);
        deliveries[0].EventType.ShouldBe(WebhookEvents.RunCompleted);

        // The delivery is written into the SAME queue as phase 17; there is no second queue.
        var queued = await jobs.QueryAsync(new JobQuery { TenantId = Tenant });
        queued.Count.ShouldBe(1);
        queued[0].Kind.ShouldBe(JobKind.WebhookDelivery);
    }

    [Fact]
    public async Task Job_written_to_the_queue_carries_a_serializable_payload()
    {
        // 🚨 Regression (K-166): if Payload is left unassigned, the field
        // becomes default(JsonElement) (ValueKind = Undefined) and
        // JsonElementConverter cannot serialize it — GET /api/jobs would
        // return the whole list with a 500. Unit and functional tests missed
        // this; the sample app caught it.
        var (publisher, store, jobs) = Build();

        await SubscribeAsync(store);
        await publisher.PublishAsync(Tenant, WebhookEvents.RunCompleted, Payload());

        var queued = await jobs.QueryAsync(new JobQuery { TenantId = Tenant });

        queued[0].Payload.ValueKind.ShouldNotBe(JsonValueKind.Undefined);

        // Serialization must actually work; the ValueKind check alone might
        // not catch the same bug again.
        Should.NotThrow(() => JsonSerializer.Serialize(queued[0].Payload));

        // The payload carries the same delivery id as the job item.
        JobPayload.ExtractItems(queued[0].Payload).ShouldBe(
            (await jobs.ListItemsAsync(queued[0].Id)).Select(item => item.Input).ToList());
    }

    [Fact]
    public async Task Retry_limit_matches_the_length_of_the_delay_ladder()
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
    public async Task Nothing_is_created_while_publishing_is_disabled()
    {
        var (publisher, store, jobs) = Build(options => options.Enabled = false);

        await SubscribeAsync(store);

        (await publisher.PublishAsync(Tenant, WebhookEvents.RunCompleted, Payload())).ShouldBe(0);
        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Subscriber_of_a_different_event_receives_no_delivery()
    {
        var (publisher, store, jobs) = Build();

        await SubscribeAsync(store, events: [WebhookEvents.QuotaThreshold]);

        (await publisher.PublishAsync(Tenant, WebhookEvents.RunCompleted, Payload())).ShouldBe(0);
        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Payload_fills_the_envelope_fields()
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
    public async Task Payload_does_not_carry_message_content()
    {
        // K-161: summary only. A recipient that wants the content calls /api/runs/{id}.
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
