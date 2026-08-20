using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>The source-generated JSON context for a webhook delivery job payload.</summary>
/// <remarks>
/// It is required for AOT compatibility. <c>AgentPrism.Core</c> does not use
/// reflection-based serialization.
/// </remarks>
[JsonSerializable(typeof(string[]))]
internal sealed partial class WebhookJobPayloadJsonContext : JsonSerializerContext;

/// <summary>
/// Publishes an event to its matching subscriptions. It creates the delivery record
/// and writes the delivery to the job queue.
/// </summary>
/// <remarks>
/// <para>
/// Delivery does <strong>not occur inside the request</strong>. A slow or unavailable
/// recipient must not slow the main run path, so publishing writes only two rows,
/// the delivery record and queue job, then returns.
/// </para>
/// <para>
/// The observability rule applies. If publishing fails, the caller is unaffected,
/// the error is logged, and the method returns <c>0</c>.
/// </para>
/// </remarks>
public sealed class WebhookPublisher(
    IWebhookStore store,
    IJobStore jobStore,
    IOptionsMonitor<AgentPrismWebhookOptions> optionsMonitor,
    TimeProvider? timeProvider = null,
    ILogger<WebhookPublisher>? logger = null) : IWebhookPublisher
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public async ValueTask<int> PublishAsync(
        string tenantId,
        string eventType,
        WebhookEventPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(payload);

        var options = optionsMonitor.CurrentValue;

        if (!options.Enabled)
        {
            return 0;
        }

        try
        {
            var subscriptions = await store
                .FindForEventAsync(tenantId, eventType, cancellationToken)
                .ConfigureAwait(false);

            if (subscriptions.Count == 0)
            {
                return 0;
            }

            var now = _clock.GetUtcNow();
            var published = 0;

            foreach (var subscription in subscriptions)
            {
                var deliveryId = AgentPrismId.NewId();

                var body = JsonSerializer.Serialize(
                    payload with
                    {
                        Event = eventType,
                        DeliveryId = deliveryId.ToString(),
                        TenantId = tenantId,
                        OccurredAt = payload.OccurredAt ?? now,
                    },
                    WebhookEventPayloadJsonContext.Default.WebhookEventPayload);

                await store.CreateDeliveryAsync(
                    new WebhookDelivery
                    {
                        Id = deliveryId,
                        SubscriptionId = subscription.Id,
                        TenantId = tenantId,
                        EventType = eventType,
                        Payload = body,
                        Status = WebhookDeliveryStatus.Pending,
                        CreatedAt = now,
                    },
                    cancellationToken).ConfigureAwait(false);

                // Delivery uses the same Phase 17 queue, not a separate one (K-160).
                // The retry limit is the ladder length.
                //
                // Assign the payload. Otherwise, the field becomes `default(JsonElement)`
                // with ValueKind Undefined. `JsonElementConverter.Write` cannot serialize it
                // and throws InvalidOperationException, causing GET /api/jobs to return 500
                // for the full job list. This was detected by running the sample application (K-166).
                var jobPayload = JsonSerializer.SerializeToElement(
                    new[] { deliveryId.ToString() },
                    WebhookJobPayloadJsonContext.Default.StringArray);

                await jobStore.EnqueueAsync(
                    new JobRecord
                    {
                        Id = AgentPrismId.NewId(),
                        TenantId = tenantId,
                        Kind = JobKind.WebhookDelivery,
                        TargetName = subscription.Name,
                        Status = JobStatus.Pending,
                        Payload = jobPayload,
                        ScheduledFor = now,
                        CreatedAt = now,
                        MaxAttempts = Math.Max(1, options.RetryDelays.Count),
                    },
                    [deliveryId.ToString()],
                    cancellationToken).ConfigureAwait(false);

                published++;
            }

            return published;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Could not publish webhook event: {EventType}/{TenantId}.", eventType, tenantId);
            }

            return 0;
        }
    }
}
