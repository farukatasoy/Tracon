using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Webhook subscription and delivery endpoints.</summary>
/// <remarks>
/// <para>
/// None of these endpoints accept or return a <strong>secret</strong>.
/// The contract has <em>no secret field at all</em>: the request body carries
/// only <see cref="WebhookSaveRequest.SecretConfigurationKey"/> (the NAME of
/// the key). An extra <c>secret</c> field sent by the client is not
/// bound and is silently ignored.
/// </para>
/// <para>
/// All dependencies are explicitly marked with <c>[FromServices]</c>.
/// </para>
/// </remarks>
internal static class WebhookEndpoints
{
    /// <summary>Maps the webhook endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/webhooks", ListAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("TraconListWebhooks")
            .WithTags("Tracon", "Webhooks")
            .WithSummary("Lists a tenant's webhook subscriptions.")
            .WithDescription(
                "The response carries no signing secret; only the NAME of the signing key is " +
                "returned. 'headerConfigurationKeys' carries configuration key NAMES and is " +
                "returned as stored. Extra headers are returned with their NAMES only: every " +
                "header value is replaced with '***'.");

        builder.MapGet("/api/webhooks/{name}", GetAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("TraconGetWebhook")
            .WithTags("Tracon", "Webhooks")
            .WithSummary("Gets a single subscription.")
            .WithDescription(
                "As in the list, no signing secret is returned — only the configuration key its " +
                "value is read from at delivery time. A secret is never stored in the database " +
                "and never leaves through this API. 'headerConfigurationKeys' is returned as " +
                "stored (key names only). Extra header values are replaced with '***'. " +
                "An unknown name returns 404.");

        builder.MapPut("/api/webhooks/{name}", SaveAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("TraconSaveWebhook")
            .WithTags("Tracon", "Webhooks")
            .WithSummary("Creates or updates a webhook subscription.")
            .Accepts<WebhookSaveRequest>("application/json")
            .WithDescription(
                "The address passes an SSRF check: only https is accepted (http only when " +
                "AllowInsecureHttp is enabled and only to loopback targets). Private network " +
                "addresses are re-checked again at delivery time. 'secretConfigurationKey' " +
                "must be under the configured allowed prefix and inside the tenant's own key " +
                "space — '{prefix}{tenantId}:...'; a flat name directly under the prefix belongs " +
                "to the default tenant (400 otherwise). A credential header (Authorization, " +
                "X-Api-Key, Cookie, any name ending in '-key') is declared in " +
                "'headerConfigurationKeys' as the NAME of the configuration key its value is read " +
                "from, under the same key space rule; the same header in plain 'headers' is " +
                "rejected with 400. Plain header values are stored and sent as given, but the " +
                "saved subscription comes back with every one replaced by '***', and a header " +
                "whose value is '***' is rejected with 400. A header name may appear only once " +
                "across both maps (case-insensitive), may not be one of Tracon's own X-Tracon-* " +
                "headers in 'headerConfigurationKeys', and both maps together may carry at most " +
                "MaxExtraHeaders entries (400 otherwise). 'headers' or 'headerConfigurationKeys' " +
                "left out or null keeps the stored map; '{}' removes it. Every other field is " +
                "replaced. Changing 'url' without sending the maps keeps them, so the stored " +
                "headers and the resolved credential values go to the NEW address.");

        builder.MapDelete("/api/webhooks/{name}", DeleteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("TraconDeleteWebhook")
            .WithTags("Tracon", "Webhooks")
            .WithSummary("Deletes a subscription and its delivery history.")
            .WithDescription(
                "The delivery history cascades with the subscription, so export it first if it " +
                "is needed for an audit; there is no way to recover it afterwards. Events raised " +
                "after the delete match no subscription and are simply not delivered. An unknown " +
                "name returns 404.");

        builder.MapPost("/api/webhooks/{name}/test", TestAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("TraconTestWebhook")
            .WithTags("Tracon", "Webhooks")
            .WithSummary("Sends a test event to the subscription's endpoint.")
            .WithDescription(
                "The event is written to the queue and delivered by a background worker; the " +
                "response reports that it was queued, not the delivery outcome.");

        builder.MapGet("/api/webhooks/{name}/deliveries", ListDeliveriesAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("TraconListWebhookDeliveries")
            .WithTags("Tracon", "Webhooks")
            .WithSummary("Lists a subscription's delivery history.")
            .WithDescription(
                "This is a history table, not a queue: scheduling and retry live in the job " +
                "queue. There is one entry per event, carrying the latest status, the attempt " +
                "count, and the endpoint's last response code — a retry updates that entry " +
                "rather than adding another. Filter with '?status=' to find failures. Paging " +
                "is offset based — 'skip' defaults to 0, 'take' to 50, and 'take' is clamped to " +
                "1..200 instead of being rejected. An unknown subscription name returns 404.");
    }

    private static async Task<Ok<IReadOnlyList<WebhookSubscription>>> ListAsync(
        [FromServices] IWebhookStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var subscriptions = await store.ListSubscriptionsAsync(tenants.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<WebhookSubscription>>([.. subscriptions.Select(MaskHeaders)]);
    }

    private static async Task<Results<Ok<WebhookSubscription>, ProblemHttpResult>> GetAsync(
        string name,
        [FromServices] IWebhookStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var subscription = await store.GetSubscriptionAsync(tenants.TenantId, name, cancellationToken)
            .ConfigureAwait(false);

        return subscription is null ? NotFound(name) : TypedResults.Ok(MaskHeaders(subscription));
    }

    private static async Task<Results<Ok<WebhookSubscription>, ProblemHttpResult>> SaveAsync(
        string name,
        HttpContext httpContext,
        [FromServices] IWebhookStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IOptionsMonitor<TraconWebhookOptions> webhookOptions,
        [FromServices] IOptions<TraconOptions> coreOptions,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] TimeProvider? timeProvider,
        [FromServices] TraconMetrics metrics,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<WebhookSaveRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        var options = webhookOptions.CurrentValue;

        // 🚨 SSRF: checked by scheme at save time. DNS resolution is NOT done
        // here (it would slow down saving and the target might be
        // unreachable at that moment); the actual protection is applied at
        // delivery time, in the connection callback.
        var verdict = WebhookUrlValidator.ValidateFormat(request.Url, options);

        if (!verdict.IsAllowed)
        {
            return Invalid(verdict.Reason ?? "Address invalid.");
        }

        // 🚨 The subscription carries no secret, only the NAME of the key its
        // value is read from (K-059). Without a prefix restriction that name
        // could point at any configuration key in the application, and
        // Tracon would sign deliveries with a value that was never meant
        // to leave the process; without the tenant segment it could name
        // another tenant's signing key. The field itself stays optional.
        if (!string.IsNullOrWhiteSpace(request.SecretConfigurationKey))
        {
            try
            {
                ConfigurationKeyGuard.RequireTenantKey(
                    request.SecretConfigurationKey,
                    options.AllowedConfigurationPrefix,
                    tenants.TenantId,
                    coreOptions.Value.DefaultTenantId,
                    "secretConfigurationKey");
            }
            catch (TraconException exception)
            {
                return Invalid(exception.Message);
            }
        }

        if (request.Events is not { Count: > 0 })
        {
            return Invalid("At least one event ('events') must be selected.");
        }

        // 🚨 A read masks every header value, so a client doing read-modify-
        // write would otherwise store the mask over the real value and the
        // receiver would reject every delivery.
        if (HeaderValueMask.FindMaskedHeader(request.Headers) is { } maskedHeader)
        {
            return Invalid(HeaderValueMask.DescribeRejection(maskedHeader));
        }

        var unknown = request.Events.Where(eventType => !WebhookEvents.IsKnown(eventType)).ToList();

        if (unknown.Count > 0)
        {
            return Invalid(
                $"Unrecognized event: {string.Join(", ", unknown)}. " +
                $"Valid events: {string.Join(", ", WebhookEvents.All)}.");
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var previous = await store.GetSubscriptionAsync(tenants.TenantId, name, cancellationToken)
            .ConfigureAwait(false);

        // A save that leaves a header map out keeps the stored one: the admin
        // panel sends neither map, and a read masks the plain values, so it
        // could not send them back (phase 190).
        var headers = CredentialHeaderSaveRules.Effective(request.Headers, previous?.Headers, StringComparer.OrdinalIgnoreCase);
        var headerKeys = CredentialHeaderSaveRules.Effective(
            request.HeaderConfigurationKeys,
            previous?.HeaderConfigurationKeys,
            StringComparer.OrdinalIgnoreCase);

        if (CredentialHeaderSaveRules.Validate(
                request.Headers,
                request.HeaderConfigurationKeys,
                headers,
                headerKeys,
                options.AllowedConfigurationPrefix,
                tenants.TenantId,
                coreOptions.Value.DefaultTenantId,
                WebhookSigner.IsReservedHeader) is { } rejection)
        {
            return Invalid(rejection.Detail);
        }

        // Counted at save time: a delivery that ran out of room would have to
        // drop a header silently, and a dropped credential header fails every
        // delivery without saying why.
        if (headers.Count + headerKeys.Count > options.MaxExtraHeaders)
        {
            return Invalid(
                $"'headers' and 'headerConfigurationKeys' together carry {headers.Count + headerKeys.Count} " +
                $"headers; at most {options.MaxExtraHeaders} are allowed (Tracon:Webhooks:MaxExtraHeaders).");
        }

        WarnWhenPreservedHeadersMove(
            loggerFactory.CreateLogger("Tracon.WebhookEndpoints"),
            name,
            previous,
            request);

        var saved = await store.SaveSubscriptionAsync(
            new WebhookSubscription
            {
                Id = previous?.Id ?? TraconId.NewId(),
                TenantId = tenants.TenantId,
                Name = name,
                Url = request.Url!,
                Events = request.Events,
                SecretConfigurationKey = string.IsNullOrWhiteSpace(request.SecretConfigurationKey)
                    ? null
                    : request.SecretConfigurationKey,
                Headers = CredentialHeaderSaveRules.Copy(headers, StringComparer.OrdinalIgnoreCase),
                HeaderConfigurationKeys = CredentialHeaderSaveRules.Copy(headerKeys, StringComparer.OrdinalIgnoreCase),
                Enabled = request.Enabled,

                // Saving resets the counter: when an admin fixes the address,
                // the subscription should not remain immediately disabled.
                ConsecutiveFailures = 0,
                CreatedAt = previous?.CreatedAt ?? now,
                UpdatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("Tracon.WebhookEndpoints"),
            metrics,
            tenants.TenantId,
            action: previous is null ? "webhook.create" : "webhook.update",
            entity: $"webhook:{name}",
            before: previous is null ? null : Describe(previous),
            after: Describe(saved),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(MaskHeaders(saved));
    }

    /// <summary>
    /// Logs when a save moves a subscription to another host while keeping its
    /// stored header maps without the request sending them.
    /// </summary>
    /// <remarks>Names only — a value is never logged.</remarks>
    private static void WarnWhenPreservedHeadersMove(
        ILogger logger,
        string name,
        WebhookSubscription? previous,
        WebhookSaveRequest request)
    {
        if (previous is null
            || !Uri.TryCreate(previous.Url, UriKind.Absolute, out var before)
            || !Uri.TryCreate(request.Url, UriKind.Absolute, out var after)
            || string.Equals(before.Host, after.Host, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var kept = new List<string>();

        if (request.Headers is null)
        {
            kept.AddRange(previous.Headers.Keys);
        }

        if (request.HeaderConfigurationKeys is null)
        {
            kept.AddRange(previous.HeaderConfigurationKeys.Keys);
        }

        if (kept.Count > 0 && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "Webhook subscription '{Subscription}' moved from host '{PreviousHost}' to '{Host}' and kept its " +
                "stored headers ({HeaderNames}); they are now sent to the new host.",
                name,
                before.Host,
                after.Host,
                string.Join(", ", kept));
        }
    }

    /// <summary>Returns a subscription the way every response carries it: header names only.</summary>
    /// <param name="subscription">The stored subscription.</param>
    /// <returns>A copy whose header values are all masked; the stored record is not changed.</returns>
    private static WebhookSubscription MaskHeaders(WebhookSubscription subscription)
        => subscription with { Headers = HeaderValueMask.Apply(subscription.Headers) };

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        string name,
        [FromServices] IWebhookStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] TraconMetrics metrics,
        CancellationToken cancellationToken)
    {
        var existing = await store.GetSubscriptionAsync(tenants.TenantId, name, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return NotFound(name);
        }

        await store.DeleteSubscriptionAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("Tracon.WebhookEndpoints"),
            metrics,
            tenants.TenantId,
            action: "webhook.delete",
            entity: $"webhook:{name}",
            before: Describe(existing),
            after: null,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<WebhookTestResponse>, ProblemHttpResult>> TestAsync(
        string name,
        [FromServices] IWebhookStore store,
        [FromServices] IWebhookPublisher publisher,
        [FromServices] ITenantContext tenants,
        [FromServices] TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        var subscription = await store.GetSubscriptionAsync(tenants.TenantId, name, cancellationToken)
            .ConfigureAwait(false);

        if (subscription is null)
        {
            return NotFound(name);
        }

        if (!subscription.Enabled)
        {
            return Invalid($"Subscription '{name}' is disabled; enable it first.");
        }

        // The test event is sent INDEPENDENTLY of the subscription's event
        // list: adding 'test.ping' to the list should not be required just
        // to verify that an endpoint is alive.
        var queued = await PublishTestAsync(publisher, store, subscription, tenants, timeProvider, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new WebhookTestResponse
        {
            Name = name,
            Queued = queued,
            Message = queued
                ? "Test event was queued. Track the result via the 'deliveries' endpoint."
                : "Event publishing is disabled (Tracon:Webhooks:Enabled=false).",
        });
    }

    private static async Task<Results<Ok<IReadOnlyList<WebhookDelivery>>, ProblemHttpResult>> ListDeliveriesAsync(
        string name,
        [FromQuery] WebhookDeliveryStatus? status,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        [FromServices] IWebhookStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var subscription = await store.GetSubscriptionAsync(tenants.TenantId, name, cancellationToken)
            .ConfigureAwait(false);

        if (subscription is null)
        {
            return NotFound(name);
        }

        var deliveries = await store.QueryDeliveriesAsync(
            new WebhookDeliveryQuery
            {
                TenantId = tenants.TenantId,
                SubscriptionId = subscription.Id,
                Status = status,
                Skip = Math.Max(skip ?? 0, 0),
                Take = Math.Clamp(take ?? 50, 1, 200),
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(deliveries);
    }

    private static async ValueTask<bool> PublishTestAsync(
        IWebhookPublisher publisher,
        IWebhookStore store,
        WebhookSubscription subscription,
        ITenantContext tenants,
        TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();

        // If the subscription is not subscribed to the 'test.ping' event, it
        // is temporarily added to the list; the publisher selects
        // subscriptions by event. No permanent change is made — the
        // modified copy is saved only for this call and immediately
        // reverted.
        if (subscription.Events.Contains(WebhookEvents.Test, StringComparer.Ordinal))
        {
            return await publisher
                .PublishAsync(tenants.TenantId, WebhookEvents.Test, BuildTestPayload(now), cancellationToken)
                .ConfigureAwait(false) > 0;
        }

        var original = subscription.Events;

        await store.SaveSubscriptionAsync(
            subscription with { Events = [.. original, WebhookEvents.Test], UpdatedAt = now },
            cancellationToken).ConfigureAwait(false);

        try
        {
            return await publisher
                .PublishAsync(tenants.TenantId, WebhookEvents.Test, BuildTestPayload(now), cancellationToken)
                .ConfigureAwait(false) > 0;
        }
        finally
        {
            await store.SaveSubscriptionAsync(
                subscription with { Events = original, UpdatedAt = now },
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static WebhookEventPayload BuildTestPayload(DateTimeOffset now)
        => new() { OccurredAt = now };

    /// <summary>Summarizes a subscription for the audit trail.</summary>
    /// <remarks>
    /// The summary contains only the NAME of the key. The secret itself is
    /// never written to a record in this process.
    /// </remarks>
    private static string Describe(WebhookSubscription subscription)
    {
        using var buffer = new MemoryStream();
        using var writer = new System.Text.Json.Utf8JsonWriter(buffer);

        writer.WriteStartObject();
        writer.WriteString("name", subscription.Name);
        writer.WriteString("url", subscription.Url);
        writer.WriteStartArray("events");

        foreach (var eventType in subscription.Events)
        {
            writer.WriteStringValue(eventType);
        }

        writer.WriteEndArray();

        if (subscription.SecretConfigurationKey is { } key)
        {
            writer.WriteString("secretConfigurationKey", key);
        }
        else
        {
            writer.WriteNull("secretConfigurationKey");
        }

        // Header NAMES only (phase 190): which headers a subscription sends is
        // part of what changed; a plain value may be a credential the name
        // rule misses, and the trail is plain text (K-779).
        writer.WriteStartArray("headers");

        foreach (var header in subscription.Headers.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteStringValue(header);
        }

        writer.WriteEndArray();

        // Key NAMES only, never a value (K-059); the audit filter leaves a
        // '...ConfigurationKeys' map as it is.
        writer.WriteStartObject("headerConfigurationKeys");

        foreach (var (header, keyName) in subscription.HeaderConfigurationKeys.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteString(header, keyName);
        }

        writer.WriteEndObject();

        writer.WriteBoolean("enabled", subscription.Enabled);
        writer.WriteEndObject();
        writer.Flush();

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static ProblemHttpResult NotFound(string name)
        => TypedResults.Problem(
            title: "Subscription not found",
            detail: $"There is no webhook subscription named '{name}'.",
            statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult Invalid(string detail)
        => TypedResults.Problem(
            title: "Subscription invalid",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>The request body for saving a webhook subscription.</summary>
/// <remarks>
/// This contract has <strong>no secret field</strong>. An extra
/// <c>secret</c> field sent by the client is not bound and is not written
/// anywhere.
/// </remarks>
public sealed record WebhookSaveRequest
{
    /// <summary>The address events are sent to. Only <c>https</c> (or loopback <c>http</c>).</summary>
    public string? Url { get; init; }

    /// <summary>The names of the subscribed events. See <see cref="WebhookEvents"/>.</summary>
    public IReadOnlyList<string>? Events { get; init; }

    /// <summary>
    /// The <strong>name</strong> of the configuration key from which the signing secret is read.
    /// Not the secret itself.
    /// </summary>
    public string? SecretConfigurationKey { get; init; }

    /// <summary>
    /// Additional headers to add to every request. The values are stored as
    /// plain text, so a header whose name looks like a credential
    /// (<c>Authorization</c>, <c>X-Api-Key</c>, <c>Cookie</c>, any name ending
    /// in <c>-key</c>) is rejected with <c>400</c>: declare it in
    /// <c>HeaderConfigurationKeys</c>. Send every header with its real
    /// value on each save: a response masks the values as <c>***</c>, and a
    /// value of <c>***</c> is rejected with <c>400</c>. Absent or
    /// <see langword="null"/> keeps the stored headers; <c>{}</c> removes them.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Credential headers, as a map from the header name to the NAME of the
    /// configuration key its value is read from, for example
    /// <c>{"X-Api-Key": "Tracon:WebhookSecrets:acme:OrdersKey"}</c>. Every name
    /// must sit inside the caller's tenant key space. A response returns the
    /// map unmasked: it carries no value. Absent or <see langword="null"/> keeps
    /// the stored map; <c>{}</c> removes it.
    /// </summary>
    public IReadOnlyDictionary<string, string>? HeaderConfigurationKeys { get; init; }

    /// <summary>Whether the subscription is enabled.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>The response for the test event.</summary>
public sealed record WebhookTestResponse
{
    /// <summary>The subscription name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the event was written to the queue.</summary>
    public required bool Queued { get; init; }

    /// <summary>The description shown to the user.</summary>
    public required string Message { get; init; }
}
