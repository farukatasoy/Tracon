using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Webhook teslim isinin yuku icin kaynak uretilmis JSON baglami.</summary>
/// <remarks>
/// AOT uyumlulugu icin gereklidir: <c>AgentPrism.Core</c> yansimaya dayanan
/// serilestirme kullanmaz.
/// </remarks>
[JsonSerializable(typeof(string[]))]
internal sealed partial class WebhookJobPayloadJsonContext : JsonSerializerContext;

/// <summary>
/// Bir olayi ilgili aboneliklere yayar: teslim kaydini olusturur ve teslimi
/// Faz 17'nin is kuyruguna yazar.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Teslim <strong>istek icinde yapilmaz</strong>. Yavas veya erisilemeyen
/// bir alici ana calistirma yolunu yavaslatmamalidir; bu yuzden yayin yalnizca
/// iki satir yazar (teslim kaydi + kuyruk isi) ve doner.
/// </para>
/// <para>
/// Gozlemlenebilirlik kurali gecerlidir: yayin basarisiz olursa cagiran
/// etkilenmez, hata loglanir ve <c>0</c> doner.
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

                // Teslim, ayri bir kuyruk degil, Faz 17'nin AYNI kuyrugudur
                // (K-160). Deneme siniri merdivenin uzunlugudur.
                //
                // 🚨 Payload ATANMALIDIR. Atanmazsa alan `default(JsonElement)`
                // olur (ValueKind = Undefined) ve `JsonElementConverter.Write`
                // onu serilestiremeyip `InvalidOperationException` firlatir —
                // GET /api/jobs tum is listesini 500 ile dondururdu. Ornek
                // uygulama calistirilinca yakalandi (K-166).
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
                logger.LogWarning(exception, "Webhook olayi yayilamadi: {EventType}/{TenantId}.", eventType, tenantId);
            }

            return 0;
        }
    }
}
