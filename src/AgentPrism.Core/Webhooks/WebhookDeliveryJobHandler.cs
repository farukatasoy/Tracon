using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Tek bir webhook teslim denemesini yurutur (<see cref="JobKind.WebhookDelivery"/>).
/// </summary>
/// <remarks>
/// <para>
/// Yeniden deneme Faz 17'nin kuyruguna birakilir: basarisiz bir deneme
/// <see cref="JobRetryException"/> firlatir ve arka plan iscisi merdivendeki
/// gecikmeyle isi geri koyar. Ikinci bir kuyruk, ikinci bir kiralama veya
/// ikinci bir zamanlayici yazilmaz (K-160).
/// </para>
/// <para>
/// 🚨 Hedef adres <strong>her denemede yeniden</strong> dogrulanir. Kaydetme
/// aninda gecerli olan bir ad, teslim aninda ozel bir adrese cozumlenebilir
/// (DNS yeniden baglama).
/// </para>
/// </remarks>
public sealed class WebhookDeliveryJobHandler(
    IWebhookStore store,
    WebhookHttpClient httpClient,
    IOptionsMonitor<AgentPrismWebhookOptions> optionsMonitor,
    IConfiguration? configuration = null,
    TimeProvider? timeProvider = null,
    ILogger<WebhookDeliveryJobHandler>? logger = null) : IJobHandler
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public JobKind Kind => JobKind.WebhookDelivery;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = optionsMonitor.CurrentValue;

        if (context.Items.Count == 0 || !Guid.TryParse(context.Items[0].Input, out var deliveryId))
        {
            // Yuk bozuksa yeniden denemek anlamsizdir; is basarisiz biter.
            throw new AgentPrismException("Webhook teslim isi gecerli bir teslim kimligi tasimiyor.");
        }

        var delivery = await store.GetDeliveryAsync(deliveryId, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"Webhook teslim kaydi bulunamadi: {deliveryId}.");

        var subscription = await FindSubscriptionAsync(delivery, cancellationToken).ConfigureAwait(false);
        var attempt = context.Job.Attempt;

        if (subscription is null || !subscription.Enabled)
        {
            await DropAsync(delivery, attempt, "Abonelik bulunamadi veya devre disi.", context, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        // 🚨 SSRF: her denemede yeniden cozumlenir ve denetlenir.
        var verdict = await WebhookUrlValidator
            .ValidateResolvedAsync(subscription.Url, options, cancellationToken)
            .ConfigureAwait(false);

        if (!verdict.IsAllowed)
        {
            // Reddedilen bir hedef yeniden denenmez: adres degismedikce sonuc
            // degismez ve her deneme yeni bir DNS sorgusu demektir.
            await DropAsync(delivery, attempt, verdict.Reason ?? "Hedef adres reddedildi.", context, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        var outcome = await SendAsync(subscription, delivery, options, cancellationToken).ConfigureAwait(false);
        var now = _clock.GetUtcNow();

        if (outcome.Succeeded)
        {
            await store.RecordDeliveryResultAsync(
                new WebhookDeliveryResult
                {
                    DeliveryId = delivery.Id,
                    Status = WebhookDeliveryStatus.Delivered,
                    Attempt = attempt,
                    ResponseCode = outcome.StatusCode,
                    RecordedAt = now,
                },
                cancellationToken).ConfigureAwait(false);

            await store
                .RecordSubscriptionOutcomeAsync(subscription.Id, succeeded: true, options.DisableAfterConsecutiveFailures, now, cancellationToken)
                .ConfigureAwait(false);

            await context.ReportItemAsync(
                new JobItemResult
                {
                    JobId = context.Job.Id,
                    Seq = context.Items[0].Seq,
                    Status = JobItemStatus.Completed,
                },
                cancellationToken).ConfigureAwait(false);

            return;
        }

        var maxAttempts = Math.Max(1, options.RetryDelays.Count);
        var isFinalAttempt = attempt >= maxAttempts;

        await store.RecordDeliveryResultAsync(
            new WebhookDeliveryResult
            {
                DeliveryId = delivery.Id,
                Status = isFinalAttempt ? WebhookDeliveryStatus.Failed : WebhookDeliveryStatus.Pending,
                Attempt = attempt,
                ResponseCode = outcome.StatusCode,
                Error = outcome.Error,
                RecordedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        if (isFinalAttempt)
        {
            var disabled = await store
                .RecordSubscriptionOutcomeAsync(subscription.Id, succeeded: false, options.DisableAfterConsecutiveFailures, now, cancellationToken)
                .ConfigureAwait(false);

            if (disabled && logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    "Webhook aboneligi '{Name}' ust uste {Threshold} basarisiz teslimden sonra devre disi birakildi.",
                    subscription.Name,
                    options.DisableAfterConsecutiveFailures);
            }

            await context.ReportItemAsync(
                new JobItemResult
                {
                    JobId = context.Job.Id,
                    Seq = context.Items[0].Seq,
                    Status = JobItemStatus.Failed,
                    Error = outcome.Error,
                },
                cancellationToken).ConfigureAwait(false);

            throw new AgentPrismException($"Webhook teslimi {attempt} denemede basarisiz oldu: {outcome.Error}");
        }

        // Merdiven 1 tabanlidir: ilk denemeden (Attempt == 1) sonra ilk gecikme.
        var delay = options.RetryDelays[Math.Min(attempt, options.RetryDelays.Count) - 1];

        throw new JobRetryException($"Webhook teslimi basarisiz oldu: {outcome.Error}")
        {
            RetryAfter = delay,
        };
    }

    private async ValueTask<WebhookSubscription?> FindSubscriptionAsync(
        WebhookDelivery delivery,
        CancellationToken cancellationToken)
    {
        var subscriptions = await store
            .ListSubscriptionsAsync(delivery.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return subscriptions.FirstOrDefault(subscription => subscription.Id == delivery.SubscriptionId);
    }

    private async ValueTask DropAsync(
        WebhookDelivery delivery,
        int attempt,
        string reason,
        JobContext context,
        CancellationToken cancellationToken)
    {
        await store.RecordDeliveryResultAsync(
            new WebhookDeliveryResult
            {
                DeliveryId = delivery.Id,
                Status = WebhookDeliveryStatus.Dropped,
                Attempt = attempt,
                Error = reason,
                RecordedAt = _clock.GetUtcNow(),
            },
            cancellationToken).ConfigureAwait(false);

        await context.ReportItemAsync(
            new JobItemResult
            {
                JobId = context.Job.Id,
                Seq = context.Items[0].Seq,
                Status = JobItemStatus.Failed,
                Error = reason,
            },
            cancellationToken).ConfigureAwait(false);

        if (logger is not null && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning("Webhook teslimi dusuruldu ({DeliveryId}): {Reason}", delivery.Id, reason);
        }
    }

    private async ValueTask<DeliveryOutcome> SendAsync(
        WebhookSubscription subscription,
        WebhookDelivery delivery,
        AgentPrismWebhookOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
            {
                Content = new StringContent(delivery.Payload, Encoding.UTF8),
            };

            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

            var timestamp = _clock.GetUtcNow();

            request.Headers.TryAddWithoutValidation(WebhookSigner.EventHeader, delivery.EventType);
            request.Headers.TryAddWithoutValidation(WebhookSigner.DeliveryHeader, delivery.Id.ToString());
            request.Headers.TryAddWithoutValidation(
                WebhookSigner.TimestampHeader,
                timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

            // 🚨 Sir veritabanindan DEGIL, yapilandirmadan okunur (K-059).
            if (ResolveSecret(subscription) is { Length: > 0 } secret)
            {
                request.Headers.TryAddWithoutValidation(
                    WebhookSigner.SignatureHeader,
                    WebhookSigner.Sign(delivery.Payload, timestamp, secret));
            }

            foreach (var (name, value) in subscription.Headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await httpClient
                .SendAsync(request, options.Timeout, cancellationToken)
                .ConfigureAwait(false);

            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                return new DeliveryOutcome(true, statusCode, null);
            }

            var body = await ReadCappedAsync(response, options.MaxResponseBytes, cancellationToken).ConfigureAwait(false);

            return new DeliveryOutcome(false, statusCode, $"HTTP {statusCode}: {body}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DeliveryOutcome(false, null, $"Zaman asimi ({options.Timeout.TotalSeconds:0} sn).");
        }
        catch (HttpRequestException exception)
        {
            return new DeliveryOutcome(false, null, exception.Message);
        }
    }

    private string? ResolveSecret(WebhookSubscription subscription)
    {
        if (configuration is null || subscription.SecretConfigurationKey is not { Length: > 0 } key)
        {
            return null;
        }

        var secret = configuration[key];

        if (string.IsNullOrWhiteSpace(secret) && logger is not null && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "Webhook aboneligi '{Name}' icin '{Key}' yapilandirma anahtari bos; istek imzalanmadan gonderiliyor.",
                subscription.Name,
                key);
        }

        return secret;
    }

    private static async ValueTask<string> ReadCappedAsync(
        HttpResponseMessage response,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[Math.Max(1, maxBytes)];
            var read = 0;

            while (read < buffer.Length)
            {
                var chunk = await stream
                    .ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken)
                    .ConfigureAwait(false);

                if (chunk == 0)
                {
                    break;
                }

                read += chunk;
            }

            return Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Yanit govdesi yalnizca tanilamadir; okunamamasi teslimi etkilemez.
            return string.Empty;
        }
    }

    private readonly record struct DeliveryOutcome(bool Succeeded, int? StatusCode, string? Error);
}
