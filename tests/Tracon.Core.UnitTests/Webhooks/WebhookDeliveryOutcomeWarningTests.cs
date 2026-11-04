using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Webhooks;

/// <summary>
/// The "was disabled" warning of a webhook subscription is written for the delivery that switched the subscription
/// off, and for no other.
/// </summary>
/// <remarks>
/// The handler takes the answer of <see cref="IWebhookStore.RecordSubscriptionOutcomeAsync"/> as the fact "this call
/// disabled the subscription". A store that answers <see langword="true"/> for every failure of a subscription that is
/// already off made the handler write the warning again for each failed delivery, with a threshold the delivery did not
/// reach. The store contract pins the answer; this pins what the handler does with it.
/// </remarks>
public sealed class WebhookDeliveryOutcomeWarningTests
{
    private const string TenantId = "default";

    [Fact]
    public async Task A_store_that_answers_false_produces_no_disabled_warning()
    {
        var logger = new CollectingLogger();

        await RunFinalFailedDeliveryAsync(disabledByThisCall: false, logger);

        logger.DisabledWarnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_store_that_answers_true_produces_one_disabled_warning_that_names_the_subscription_and_threshold()
    {
        var logger = new CollectingLogger();

        await RunFinalFailedDeliveryAsync(disabledByThisCall: true, logger);

        var warning = logger.DisabledWarnings.ShouldHaveSingleItem();

        warning.ShouldContain("'orders'");
        warning.ShouldContain($"after {new TraconWebhookOptions().DisableAfterConsecutiveFailures} consecutive failed deliveries");
    }

    private static async Task RunFinalFailedDeliveryAsync(bool disabledByThisCall, CollectingLogger logger)
    {
        var store = new FixedOutcomeWebhookStore(new InMemoryWebhookStore(), disabledByThisCall);
        var delivery = await ArrangeDeliveryAsync(store);
        using var client = new WebhookHttpClient(new Recipient(HttpStatusCode.InternalServerError));
        var options = new TraconWebhookOptions();
        var handler = new WebhookDeliveryJobHandler(
            store,
            client,
            new StaticOptionsMonitor<TraconWebhookOptions>(options),
            Options.Create(new TraconOptions()),
            logger: logger);
        var lastAttempt = options.RetryDelays.Count;

        // The last failed attempt fails the job, after the outcome is recorded and the warning decided.
        await Should.ThrowAsync<TraconException>(
            async () => await handler.ExecuteAsync(BuildContext(delivery, lastAttempt), TestContext.Current.CancellationToken));

        store.OutcomeCalls.ShouldBe(1);
    }

    private static async Task<WebhookDelivery> ArrangeDeliveryAsync(IWebhookStore store)
    {
        var subscription = await store.SaveSubscriptionAsync(new WebhookSubscription
        {
            Id = TraconId.NewId(),
            TenantId = TenantId,
            Name = "orders",

            // A public IP literal: the delivery-time SSRF check resolves no DNS for it, and the recipient below never
            // opens a socket.
            Url = "https://8.8.8.8/orders",
            Events = [WebhookEvents.RunCompleted],
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        return await store.CreateDeliveryAsync(new WebhookDelivery
        {
            Id = TraconId.NewId(),
            SubscriptionId = subscription.Id,
            TenantId = TenantId,
            EventType = WebhookEvents.RunCompleted,
            Payload = """{"runId":"1"}""",
            Status = WebhookDeliveryStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static JobContext BuildContext(WebhookDelivery delivery, int attempt)
        => new()
        {
            Job = new JobRecord
            {
                Id = TraconId.NewId(),
                TenantId = TenantId,
                HandlerKey = JobHandlerKeys.WebhookDelivery,
                TargetName = "orders",
                Status = JobStatus.Running,
                Attempt = attempt,
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
            ReportItemAsync = static (_, _) => ValueTask.CompletedTask,
            IsCancelledAsync = static _ => ValueTask.FromResult(false),
        };

    private sealed class Recipient(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }

    /// <summary>A webhook store whose answer to "did this call disable the subscription" is fixed. Every other call goes through.</summary>
    private sealed class FixedOutcomeWebhookStore(IWebhookStore inner, bool answer) : IWebhookStore
    {
        private int _outcomeCalls;

        public int OutcomeCalls => Volatile.Read(ref _outcomeCalls);

        public ValueTask<bool> RecordSubscriptionOutcomeAsync(
            Guid subscriptionId,
            bool succeeded,
            int disableThreshold,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _outcomeCalls);

            return ValueTask.FromResult(answer);
        }

        public ValueTask<IReadOnlyList<WebhookSubscription>> ListSubscriptionsAsync(string tenantId, CancellationToken cancellationToken = default)
            => inner.ListSubscriptionsAsync(tenantId, cancellationToken);

        public ValueTask<WebhookSubscription?> GetSubscriptionAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.GetSubscriptionAsync(tenantId, name, cancellationToken);

        public ValueTask<IReadOnlyList<WebhookSubscription>> FindForEventAsync(string tenantId, string eventType, CancellationToken cancellationToken = default)
            => inner.FindForEventAsync(tenantId, eventType, cancellationToken);

        public ValueTask<WebhookSubscription> SaveSubscriptionAsync(WebhookSubscription subscription, CancellationToken cancellationToken = default)
            => inner.SaveSubscriptionAsync(subscription, cancellationToken);

        public ValueTask<bool> DeleteSubscriptionAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.DeleteSubscriptionAsync(tenantId, name, cancellationToken);

        public ValueTask<WebhookDelivery> CreateDeliveryAsync(WebhookDelivery delivery, CancellationToken cancellationToken = default)
            => inner.CreateDeliveryAsync(delivery, cancellationToken);

        public ValueTask<WebhookDelivery?> GetDeliveryAsync(Guid deliveryId, CancellationToken cancellationToken = default)
            => inner.GetDeliveryAsync(deliveryId, cancellationToken);

        public ValueTask RecordDeliveryResultAsync(WebhookDeliveryResult result, CancellationToken cancellationToken = default)
            => inner.RecordDeliveryResultAsync(result, cancellationToken);

        public ValueTask<IReadOnlyList<WebhookDelivery>> QueryDeliveriesAsync(WebhookDeliveryQuery query, CancellationToken cancellationToken = default)
            => inner.QueryDeliveriesAsync(query, cancellationToken);
    }

    private sealed class CollectingLogger : ILogger<WebhookDeliveryJobHandler>
    {
        private readonly List<string> _warnings = [];

        /// <summary>Gets the warnings that say a subscription was disabled; the handler writes others (a failed send).</summary>
        public IEnumerable<string> DisabledWarnings => _warnings.Where(static warning => warning.Contains("was disabled", StringComparison.Ordinal));

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (logLevel == LogLevel.Warning)
            {
                _warnings.Add(formatter(state, exception));
            }
        }
    }
}
