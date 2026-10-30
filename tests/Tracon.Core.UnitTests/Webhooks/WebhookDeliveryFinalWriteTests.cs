using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Webhooks;

/// <summary>
/// A webhook the recipient accepted is not delivered again because Tracon could
/// not record that it was delivered.
/// </summary>
/// <remarks>
/// The writes that record a delivery's result sat in the same handler
/// body as the successful send, with nothing between them and the worker. One
/// store error after the recipient answered <c>2xx</c> failed the attempt, the
/// worker put the job back, and the next attempt sent the same delivery again.
/// the rule: a store error after the work belongs to the store; the result
/// write is tried again, and the work is never repeated for it.
/// </remarks>
public sealed class WebhookDeliveryFinalWriteTests
{
    private const string TenantId = "default";

    [Fact]
    public async Task A_store_error_after_a_successful_delivery_does_not_deliver_it_again()
    {
        var store = new FlakyResultWebhookStore(new InMemoryWebhookStore()) { DeliveredWriteFailuresLeft = 1 };
        var delivery = await ArrangeDeliveryAsync(store);
        var recipient = new CountingRecipient();
        using var client = new WebhookHttpClient(recipient);
        var handler = CreateHandler(store, client, TimeSpan.FromMinutes(5));

        var jobs = new InMemoryJobStore();
        var job = await EnqueueAsync(jobs, delivery);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.WebhookDelivery, handler));

        using var worker = new JobWorkerBackgroundService(
            jobs,
            new InMemoryJobScheduleStore(),
            host.Registry,
            host.Scopes,
            new StaticOptionsMonitor<TraconSchedulingOptions>(new TraconSchedulingOptions
            {
                MaxConcurrentJobs = 1,
                PollInterval = TimeSpan.FromMilliseconds(5),
            }),
            new SchemaReadyGate([]),
            new NotDraining(),
            logger: NullLogger<JobWorkerBackgroundService>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            var finished = await WaitUntil.ValueAsync(
                async () => await jobs.GetAsync(TenantId, job.Id, TestContext.Current.CancellationToken),
                static current => current?.Status is JobStatus.Completed or JobStatus.Failed,
                "the delivery job to finish");

            recipient.Requests.ShouldBe(1);
            finished!.Status.ShouldBe(JobStatus.Completed);
            (await store.GetDeliveryAsync(delivery.Id, TestContext.Current.CancellationToken))!.Status
                .ShouldBe(WebhookDeliveryStatus.Delivered);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_delivery_already_recorded_as_delivered_is_not_sent_again()
    {
        var store = new FlakyResultWebhookStore(new InMemoryWebhookStore());
        var delivery = await ArrangeDeliveryAsync(store);

        // An earlier attempt reached the recipient and recorded it, then failed
        // before it could finish the job; the worker leased the job again.
        await store.RecordDeliveryResultAsync(
            new WebhookDeliveryResult
            {
                DeliveryId = delivery.Id,
                Status = WebhookDeliveryStatus.Delivered,
                Attempt = 1,
                ResponseCode = 200,
                RecordedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        var recipient = new CountingRecipient();
        using var client = new WebhookHttpClient(recipient);
        var handler = CreateHandler(store, client, TimeSpan.FromMinutes(5));
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(BuildContext(delivery, attempt: 2, reported), TestContext.Current.CancellationToken);

        recipient.Requests.ShouldBe(0);
        reported.Single().Status.ShouldBe(JobItemStatus.Completed);
    }

    [Fact]
    public async Task A_result_that_cannot_be_recorded_in_time_is_logged_and_the_delivery_is_not_repeated()
    {
        var store = new FlakyResultWebhookStore(new InMemoryWebhookStore()) { DeliveredWriteFailuresLeft = int.MaxValue };
        var delivery = await ArrangeDeliveryAsync(store);
        var recipient = new CountingRecipient();
        using var client = new WebhookHttpClient(recipient);

        // A third of the lease bounds the tries: 200 ms here.
        var handler = CreateHandler(store, client, TimeSpan.FromMilliseconds(600));
        var reported = new List<JobItemResult>();

        // Throwing here would hand the job back to the worker, and the next
        // attempt would find the delivery still Pending and send it again.
        await handler.ExecuteAsync(BuildContext(delivery, attempt: 1, reported), TestContext.Current.CancellationToken);

        recipient.Requests.ShouldBe(1);
        reported.Single().Status.ShouldBe(JobItemStatus.Completed);
        (await store.GetDeliveryAsync(delivery.Id, TestContext.Current.CancellationToken))!.Status
            .ShouldBe(WebhookDeliveryStatus.Pending);
    }

    /// <summary>
    /// A store error while recording a FAILED send is the store's too. It
    /// used to escape the handler as a raw error: the worker put the job back with no
    /// pause, so the recipient was called again at once instead of after the ladder's
    /// delay.
    /// </summary>
    [Fact]
    public async Task A_store_error_after_a_failed_send_keeps_the_ladder_delay()
    {
        var store = new FlakyResultWebhookStore(new InMemoryWebhookStore()) { FailedPathWriteFailuresLeft = 1 };
        var delivery = await ArrangeDeliveryAsync(store);
        var recipient = new CountingRecipient(HttpStatusCode.InternalServerError);
        using var client = new WebhookHttpClient(recipient);
        var handler = CreateHandler(store, client, TimeSpan.FromMinutes(5));

        var retry = await Should.ThrowAsync<JobRetryException>(
            async () => await handler.ExecuteAsync(
                BuildContext(delivery, attempt: 1, []),
                TestContext.Current.CancellationToken));

        retry.RetryAfter.ShouldBe(new TraconWebhookOptions().RetryDelays[0]);
        recipient.Requests.ShouldBe(1);
        (await store.GetDeliveryAsync(delivery.Id, TestContext.Current.CancellationToken))!.Attempt.ShouldBe(1);
    }

    /// <summary>
    /// On the last attempt the failed result is recorded through the same
    /// retry, so the delivery ends <see cref="WebhookDeliveryStatus.Failed"/>. One
    /// store error used to leave it <see cref="WebhookDeliveryStatus.Pending"/> for
    /// good, with no attempt left to finish it.
    /// </summary>
    [Fact]
    public async Task A_store_error_on_the_last_failed_attempt_still_records_the_delivery_as_failed()
    {
        var store = new FlakyResultWebhookStore(new InMemoryWebhookStore()) { FailedPathWriteFailuresLeft = 1 };
        var delivery = await ArrangeDeliveryAsync(store);
        var recipient = new CountingRecipient(HttpStatusCode.InternalServerError);
        using var client = new WebhookHttpClient(recipient);
        var handler = CreateHandler(store, client, TimeSpan.FromMinutes(5));
        var reported = new List<JobItemResult>();
        var lastAttempt = new TraconWebhookOptions().RetryDelays.Count;

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await handler.ExecuteAsync(
                BuildContext(delivery, attempt: lastAttempt, reported),
                TestContext.Current.CancellationToken));

        failure.Message.ShouldContain($"after {lastAttempt} attempts");
        (await store.GetDeliveryAsync(delivery.Id, TestContext.Current.CancellationToken))!.Status
            .ShouldBe(WebhookDeliveryStatus.Failed);
        reported.Single().Status.ShouldBe(JobItemStatus.Failed);
    }

    private static async Task<WebhookDelivery> ArrangeDeliveryAsync(IWebhookStore store)
    {
        var subscription = await store.SaveSubscriptionAsync(new WebhookSubscription
        {
            Id = TraconId.NewId(),
            TenantId = TenantId,
            Name = "orders",

            // A public IP literal: the delivery-time SSRF check resolves no DNS
            // for it, and the counting transport below never opens a socket.
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

    private static async Task<JobRecord> EnqueueAsync(InMemoryJobStore jobs, WebhookDelivery delivery)
    {
        var now = DateTimeOffset.UtcNow;

        return await jobs.EnqueueAsync(
            new JobRecord
            {
                Id = TraconId.NewId(),
                TenantId = TenantId,
                HandlerKey = JobHandlerKeys.WebhookDelivery,
                TargetName = "orders",
                Status = JobStatus.Pending,
                ScheduledFor = now,
                CreatedAt = now,
                MaxAttempts = new TraconWebhookOptions().RetryDelays.Count,
            },
            [delivery.Id.ToString()],
            TestContext.Current.CancellationToken);
    }

    private static WebhookDeliveryJobHandler CreateHandler(IWebhookStore store, WebhookHttpClient client, TimeSpan leaseDuration)
        => new(
            store,
            client,
            new StaticOptionsMonitor<TraconWebhookOptions>(new TraconWebhookOptions()),
            Options.Create(new TraconOptions()),
            logger: NullLogger<WebhookDeliveryJobHandler>.Instance,
            schedulingOptions: new StaticOptionsMonitor<TraconSchedulingOptions>(
                new TraconSchedulingOptions { LeaseDuration = leaseDuration }));

    private static JobContext BuildContext(WebhookDelivery delivery, int attempt, List<JobItemResult> reported)
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
            ReportItemAsync = (result, _) =>
            {
                reported.Add(result);
                return ValueTask.CompletedTask;
            },
            IsCancelledAsync = _ => ValueTask.FromResult(false),
        };

    /// <summary>A recipient that answers with one status (<c>200</c> by default) and counts what reached it.</summary>
    private sealed class CountingRecipient(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);

            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    /// <summary>
    /// A webhook store that refuses to record a delivery as delivered while
    /// failures are left. Every other call goes through.
    /// </summary>
    private sealed class FlakyResultWebhookStore(IWebhookStore inner) : IWebhookStore
    {
        private int _deliveredWriteFailuresLeft;
        private int _failedPathWriteFailuresLeft;

        public int DeliveredWriteFailuresLeft
        {
            get => Volatile.Read(ref _deliveredWriteFailuresLeft);
            init => _deliveredWriteFailuresLeft = value;
        }

        /// <summary>Gets how many times a Pending or Failed result write is refused.</summary>
        public int FailedPathWriteFailuresLeft
        {
            get => Volatile.Read(ref _failedPathWriteFailuresLeft);
            init => _failedPathWriteFailuresLeft = value;
        }

        public ValueTask RecordDeliveryResultAsync(WebhookDeliveryResult result, CancellationToken cancellationToken = default)
        {
            if (result.Status == WebhookDeliveryStatus.Delivered
                    ? TryTakeFailure(ref _deliveredWriteFailuresLeft)
                    : TryTakeFailure(ref _failedPathWriteFailuresLeft))
            {
                throw new InvalidOperationException("The webhook store is unavailable.");
            }

            return inner.RecordDeliveryResultAsync(result, cancellationToken);
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

        public ValueTask<bool> RecordSubscriptionOutcomeAsync(
            Guid subscriptionId,
            bool succeeded,
            int disableThreshold,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
            => inner.RecordSubscriptionOutcomeAsync(subscriptionId, succeeded, disableThreshold, updatedAt, cancellationToken);

        public ValueTask<WebhookDelivery> CreateDeliveryAsync(WebhookDelivery delivery, CancellationToken cancellationToken = default)
            => inner.CreateDeliveryAsync(delivery, cancellationToken);

        public ValueTask<WebhookDelivery?> GetDeliveryAsync(Guid deliveryId, CancellationToken cancellationToken = default)
            => inner.GetDeliveryAsync(deliveryId, cancellationToken);

        public ValueTask<IReadOnlyList<WebhookDelivery>> QueryDeliveriesAsync(WebhookDeliveryQuery query, CancellationToken cancellationToken = default)
            => inner.QueryDeliveriesAsync(query, cancellationToken);

        private static bool TryTakeFailure(ref int failuresLeft)
        {
            while (true)
            {
                var left = Volatile.Read(ref failuresLeft);

                if (left <= 0)
                {
                    return false;
                }

                if (left == int.MaxValue || Interlocked.CompareExchange(ref failuresLeft, left - 1, left) == left)
                {
                    return true;
                }
            }
        }
    }

    private sealed class NotDraining : ITraconDrainState
    {
        public bool IsDraining => false;
    }
}
