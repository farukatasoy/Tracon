using System.Net;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Webhooks;

/// <summary>
/// Phase 119 (BL-027/BL-037): neither the remote target's own response body nor a
/// raw <see cref="HttpRequestException"/> message may reach <c>webhook_deliveries.error</c>.
/// </summary>
public sealed class WebhookDeliveryRedactionTests
{
    [Fact]
    public async Task Remote_target_error_body_is_not_persisted_only_the_status_code_is()
    {
        const string RemoteSecret = "internal-secret-token-should-not-leak";

        var (recorded, _) = await DeliverAsync(new RespondingHandler(
            HttpStatusCode.InternalServerError,
            $$"""{"error":"{{RemoteSecret}}"}"""));

        recorded.Status.ShouldBe(WebhookDeliveryStatus.Pending);
        recorded.Error.ShouldNotBeNull();
        recorded.Error.ShouldContain("HTTP 500");
        recorded.Error.ShouldNotContain(RemoteSecret);
    }

    [Fact]
    public async Task A_network_failure_does_not_leak_the_raw_HttpRequestException_message()
    {
        var (recorded, _) = await DeliverAsync(
            new ThrowingHandler(new HttpRequestException("Connection refused (internal-host:6379)")));

        recorded.Status.ShouldBe(WebhookDeliveryStatus.Pending);
        recorded.Error.ShouldNotBeNull();
        recorded.Error.ShouldContain(nameof(HttpRequestException));
        recorded.Error.ShouldContain("(ref:");
        recorded.Error.ShouldNotContain("internal-host");
    }

    private static async Task<(WebhookDelivery Delivery, HttpRequestMessage? Sent)> DeliverAsync(HttpMessageHandler innerHandler)
    {
        var options = new TraconWebhookOptions { AllowInsecureHttp = false };
        var store = new InMemoryWebhookStore();

        var subscription = await store.SaveSubscriptionAsync(new WebhookSubscription
        {
            Id = TraconId.NewId(),
            TenantId = "default",
            Name = "orders",
            // A public IP literal: the delivery-time SSRF check resolves no DNS for
            // it, so the test never touches the network.
            Url = "https://8.8.8.8/orders",
            Events = [WebhookEvents.RunCompleted],
            SecretConfigurationKey = null,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var delivery = await store.CreateDeliveryAsync(new WebhookDelivery
        {
            Id = TraconId.NewId(),
            SubscriptionId = subscription.Id,
            TenantId = "default",
            EventType = WebhookEvents.RunCompleted,
            Payload = """{"runId":"1"}""",
            Status = WebhookDeliveryStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        using var client = new WebhookHttpClient(innerHandler);

        var handler = new WebhookDeliveryJobHandler(
            store,
            client,
            new StaticOptionsMonitor<TraconWebhookOptions>(options));

        // A retryable outcome throws JobRetryException by design (the queue owns the
        // retry ladder); the delivery record is still written before that happens.
        await Should.ThrowAsync<JobRetryException>(async () => await handler.ExecuteAsync(new JobContext
        {
            Job = new JobRecord
            {
                Id = TraconId.NewId(),
                TenantId = "default",
                HandlerKey = JobHandlerKeys.WebhookDelivery,
                TargetName = "orders",
                Status = JobStatus.Running,
                Payload = default,
                Attempt = 1,
                ScheduledFor = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            Items =
            [
                new JobItemRecord
                {
                    Id = TraconId.NewId(),
                    JobId = Guid.Empty,
                    Seq = 0,
                    Input = delivery.Id.ToString(),
                    Status = JobItemStatus.Pending,
                },
            ],
            ReportItemAsync = (_, _) => ValueTask.CompletedTask,
            IsCancelledAsync = _ => ValueTask.FromResult(false),
        }));

        var recorded = await store.GetDeliveryAsync(delivery.Id);

        return (recorded.ShouldNotBeNull(), null);
    }

    private sealed class RespondingHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
    }

    private sealed class ThrowingHandler(HttpRequestException exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw exception;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
