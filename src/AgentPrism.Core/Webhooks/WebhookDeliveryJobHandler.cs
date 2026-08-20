using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Executes a single webhook delivery attempt (<see cref="JobKind.WebhookDelivery"/>).
/// </summary>
/// <remarks>
/// <para>
/// Retrying is left to the queue: a failed attempt throws
/// <see cref="JobRetryException"/>, and the background worker puts the job
/// back with the ladder's delay. A second queue, a second lease, or a second
/// scheduler is not written.
/// </para>
/// <para>
/// The target address is validated <strong>again on every attempt</strong>.
/// A name that was valid at save time may resolve to a private address by
/// delivery time (DNS rebinding).
/// </para>
/// </remarks>
public sealed class WebhookDeliveryJobHandler(
    IWebhookStore store,
    WebhookHttpClient httpClient,
    IOptionsMonitor<AgentPrismWebhookOptions> optionsMonitor,
    IConfiguration? configuration = null,
    TimeProvider? timeProvider = null,
    ILogger<WebhookDeliveryJobHandler>? logger = null,
    IOptionsMonitor<AgentPrismEgressOptions>? egressOptions = null) : IJobHandler
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
            // If the payload is malformed, retrying is pointless; the job ends as failed.
            throw new AgentPrismException("The webhook delivery job does not carry a valid delivery id.");
        }

        var delivery = await store.GetDeliveryAsync(deliveryId, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"No webhook delivery record was found: {deliveryId}.");

        var subscription = await FindSubscriptionAsync(delivery, cancellationToken).ConfigureAwait(false);
        var attempt = context.Job.Attempt;

        if (subscription is null || !subscription.Enabled)
        {
            await DropAsync(delivery, attempt, "The subscription was not found or is disabled.", context, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        // 🚨 SSRF: resolved and validated again on every attempt.
        var verdict = await WebhookUrlValidator
            .ValidateResolvedAsync(
                subscription.Url,
                options,
                egressOptions?.CurrentValue ?? new AgentPrismEgressOptions(),
                cancellationToken)
            .ConfigureAwait(false);

        if (!verdict.IsAllowed)
        {
            // A rejected target is not retried: the result does not change
            // unless the address changes, and every attempt would mean a new DNS query.
            await DropAsync(delivery, attempt, verdict.Reason ?? "The target address was rejected.", context, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        // Checked here as well as where the subscription is saved: a
        // subscription written before the prefix was configured must not
        // silently read an out-of-prefix configuration key. Dropped rather
        // than retried, for the same reason as a rejected address — the
        // verdict cannot change until the record does.
        if (subscription.SecretConfigurationKey is { Length: > 0 } secretKey)
        {
            try
            {
                ConfigurationKeyGuard.RequirePrefix(
                    secretKey,
                    options.AllowedConfigurationPrefix,
                    "secretConfigurationKey");
            }
            catch (AgentPrismException exception)
            {
                await DropAsync(delivery, attempt, exception.Message, context, cancellationToken)
                    .ConfigureAwait(false);

                return;
            }
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
                    "Webhook subscription '{Name}' was disabled after {Threshold} consecutive failed deliveries.",
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

            throw new AgentPrismException($"Webhook delivery failed after {attempt} attempts: {outcome.Error}");
        }

        // The ladder is 1-based: the first delay comes after the first attempt (Attempt == 1).
        var delay = options.RetryDelays[Math.Min(attempt, options.RetryDelays.Count) - 1];

        throw new JobRetryException($"Webhook delivery failed: {outcome.Error}")
        {
            RetryAfter = delay,
        };
    }

    /// <summary>Adds the subscription's extra headers, within the configured limits.</summary>
    /// <remarks>
    /// An extra header may not carry the name of one of AgentPrism's own
    /// headers. <c>TryAddWithoutValidation</c> <em>appends</em> rather than
    /// replaces, so an entry named <c>X-AgentPrism-Signature</c> produced a
    /// second signature value and broke the recipient's verification — the
    /// recipient sees two values and cannot tell which one to check. Reserved
    /// names are dropped, and the drop is logged so an operator can see why
    /// the header never arrived.
    /// </remarks>
    private void AddExtraHeaders(
        HttpRequestMessage request,
        WebhookSubscription subscription,
        AgentPrismWebhookOptions options)
    {
        var added = 0;

        foreach (var (name, value) in subscription.Headers)
        {
            if (WebhookSigner.IsReservedHeader(name))
            {
                logger?.LogWarning(
                    "Webhook subscription '{Subscription}' carries the reserved header '{Header}'; it was not sent.",
                    subscription.Name,
                    name);

                continue;
            }

            if (added == options.MaxExtraHeaders)
            {
                logger?.LogWarning(
                    "Webhook subscription '{Subscription}' carries more than {Limit} extra headers; the rest were not sent.",
                    subscription.Name,
                    options.MaxExtraHeaders);

                break;
            }

            request.Headers.TryAddWithoutValidation(name, value);
            added++;
        }
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
            logger.LogWarning("Webhook delivery dropped ({DeliveryId}): {Reason}", delivery.Id, reason);
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

            // 🚨 The secret is read from configuration, NOT from the database (K-059).
            if (ResolveSecret(subscription, options) is { Length: > 0 } secret)
            {
                request.Headers.TryAddWithoutValidation(
                    WebhookSigner.SignatureHeader,
                    WebhookSigner.Sign(delivery.Payload, timestamp, secret));
            }

            AddExtraHeaders(request, subscription, options);

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
            return new DeliveryOutcome(false, null, $"Timed out ({options.Timeout.TotalSeconds:0}s).");
        }
        catch (HttpRequestException exception)
        {
            return new DeliveryOutcome(false, null, exception.Message);
        }
        catch (AgentPrismException exception)
        {
            // The secret's configuration key sits outside the allowed prefix,
            // or the target was rejected inside the connection callback.
            // Neither changes on a retry, and letting the exception escape
            // would skip the delivery result AND the consecutive-failure
            // counter entirely, so the subscription would never disable itself.
            return new DeliveryOutcome(false, null, exception.Message);
        }
    }

    private string? ResolveSecret(WebhookSubscription subscription, AgentPrismWebhookOptions options)
    {
        if (configuration is null || subscription.SecretConfigurationKey is not { Length: > 0 } key)
        {
            return null;
        }

        // Already dropped in ExecuteAsync if the key sits outside the prefix;
        // repeated here so that no future caller of this method can bypass it.
        ConfigurationKeyGuard.RequirePrefix(key, options.AllowedConfigurationPrefix, "secretConfigurationKey");

        var secret = configuration[key];

        if (string.IsNullOrWhiteSpace(secret) && logger is not null && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "Configuration key '{Key}' for webhook subscription '{Name}' is empty; the request is being sent unsigned.",
                key,
                subscription.Name);
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
            // The response body is only for diagnostics; failing to read it does not affect delivery.
            return string.Empty;
        }
    }

    private readonly record struct DeliveryOutcome(bool Succeeded, int? StatusCode, string? Error);
}
