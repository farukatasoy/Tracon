using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Webhooks;

/// <summary>
/// The delivery handler that <c>AddTracon</c> registers reads the host's
/// <c>Tracon:Egress</c> settings before it sends.
/// </summary>
/// <remarks>
/// 🚨 F-388: the registration's factory did not pass
/// <c>IOptionsMonitor&lt;TraconEgressOptions&gt;</c>, so the handler fell back
/// to <c>new TraconEgressOptions()</c>. A host that opened private-network
/// targets with <c>Tracon:Egress:AllowPrivateNetworkTargets</c> alone saved the
/// subscription (the save path read the setting) and then saw every delivery
/// <c>Dropped</c>. The site says either of the two settings is enough. A test
/// that builds the handler by hand cannot see this: it has to come from DI.
/// </remarks>
public sealed class WebhookDeliveryEgressOptionTests
{
    [Fact]
    public async Task The_registered_handler_honors_the_egress_private_network_setting()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:Egress:AllowPrivateNetworkTargets"] = "true",
            })
            .Build();

        var recipient = new AcceptingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // Registered first: AddTracon uses TryAddSingleton, so this client
        // wins and the test never opens a socket.
        services.AddSingleton(new WebhookHttpClient(recipient));
        services.AddTracon(configuration.GetSection("Tracon"));

        await using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<IWebhookStore>();
        var subscription = await store.SaveSubscriptionAsync(new WebhookSubscription
        {
            Id = TraconId.NewId(),
            TenantId = "default",
            Name = "internal",

            // A private IP literal: the delivery-time check resolves no DNS for it.
            Url = "https://10.20.30.40/hooks",
            Events = [WebhookEvents.RunCompleted],
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

        await using (var scope = provider.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<WebhookDeliveryJobHandler>();

            await handler.ExecuteAsync(BuildContext(delivery), CancellationToken.None);
        }

        var recorded = (await store.GetDeliveryAsync(delivery.Id)).ShouldNotBeNull();

        recorded.Status.ShouldBe(WebhookDeliveryStatus.Delivered, recorded.Error);
        recipient.Calls.ShouldBe(1);
    }

    private static JobContext BuildContext(WebhookDelivery delivery)
        => new()
        {
            Job = new JobRecord
            {
                Id = TraconId.NewId(),
                TenantId = "default",
                HandlerKey = JobHandlerKeys.WebhookDelivery,
                TargetName = "internal",
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
        };

    /// <summary>Answers every delivery with <c>204</c> and counts the calls.</summary>
    private sealed class AcceptingHandler : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
