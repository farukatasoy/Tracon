using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Webhook abonelik ve teslim uclari (Faz 21).</summary>
/// <remarks>
/// <para>
/// 🚨 Bu uclarin hicbiri bir <strong>sir</strong> kabul etmez veya dondurmez.
/// Sozlesmede sir alani <em>hic yoktur</em>: istek govdesi yalnizca
/// <see cref="WebhookSaveRequest.SecretConfigurationKey"/> (anahtarin ADI)
/// tasir (K-059). Fazladan gonderilen bir <c>secret</c> alani baglanmaz ve
/// sessizce yok sayilir.
/// </para>
/// <para>
/// Tum bagimliliklar <c>[FromServices]</c> ile acikca isaretlenir (Faz 9 dersi).
/// </para>
/// </remarks>
internal static class WebhookEndpoints
{
    /// <summary>Webhook uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/webhooks", ListAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismListWebhooks")
            .WithSummary("Bir kiracinin webhook aboneliklerini listeler.")
            .WithDescription("Yanit hicbir sir tasimaz; yalnizca imzalama anahtarinin ADI doner.");

        builder.MapGet("/api/webhooks/{name}", GetAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismGetWebhook")
            .WithSummary("Tek bir aboneligi getirir.");

        builder.MapPut("/api/webhooks/{name}", SaveAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismSaveWebhook")
            .WithSummary("Webhook aboneligi olusturur veya gunceller.")
            .WithDescription(
                "Adres SSRF denetiminden gecer: yalnizca https kabul edilir (http yalnizca " +
                "AllowInsecureHttp acikken ve loopback hedeflerine). Ozel ag adresleri teslim " +
                "aninda da yeniden denetlenir.");

        builder.MapDelete("/api/webhooks/{name}", DeleteAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismDeleteWebhook")
            .WithSummary("Bir aboneligi ve teslim gecmisini siler.");

        builder.MapPost("/api/webhooks/{name}/test", TestAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismTestWebhook")
            .WithSummary("Aboneligin ucuna bir sinama olayi gonderir.")
            .WithDescription(
                "Olay kuyruga yazilir ve arka plan iscisi teslim eder; yanit teslimin " +
                "sonucunu degil, kuyruga alindigini bildirir.");

        builder.MapGet("/api/webhooks/{name}/deliveries", ListDeliveriesAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismListWebhookDeliveries")
            .WithSummary("Bir aboneligin teslim gecmisini listeler.");
    }

    private static async Task<Ok<IReadOnlyList<WebhookSubscription>>> ListAsync(
        [FromServices] IWebhookStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var subscriptions = await store.ListSubscriptionsAsync(tenants.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(subscriptions);
    }

    private static async Task<Results<Ok<WebhookSubscription>, ProblemHttpResult>> GetAsync(
        string name,
        [FromServices] IWebhookStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var subscription = await store.GetSubscriptionAsync(tenants.TenantId, name, cancellationToken)
            .ConfigureAwait(false);

        return subscription is null ? NotFound(name) : TypedResults.Ok(subscription);
    }

    private static async Task<Results<Ok<WebhookSubscription>, ProblemHttpResult>> SaveAsync(
        string name,
        [FromBody] WebhookSaveRequest request,
        [FromServices] IWebhookStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IOptionsMonitor<AgentPrismWebhookOptions> webhookOptions,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = webhookOptions.CurrentValue;

        // 🚨 SSRF: kaydetme aninda semaya gore denetlenir. DNS cozumlemesi
        // burada YAPILMAZ (kayit yavaslar ve hedef o an erisilemez olabilir);
        // gercek koruma teslim aninda, baglanti geri cagrisinda uygulanir.
        var verdict = WebhookUrlValidator.ValidateFormat(request.Url, options);

        if (!verdict.IsAllowed)
        {
            return Invalid(verdict.Reason ?? "Adres gecersiz.");
        }

        if (request.Events is not { Count: > 0 })
        {
            return Invalid("En az bir olay ('events') secilmelidir.");
        }

        var unknown = request.Events.Where(eventType => !WebhookEvents.IsKnown(eventType)).ToList();

        if (unknown.Count > 0)
        {
            return Invalid(
                $"Taninmayan olay: {string.Join(", ", unknown)}. " +
                $"Gecerli olaylar: {string.Join(", ", WebhookEvents.All)}.");
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var previous = await store.GetSubscriptionAsync(tenants.TenantId, name, cancellationToken)
            .ConfigureAwait(false);

        var saved = await store.SaveSubscriptionAsync(
            new WebhookSubscription
            {
                Id = previous?.Id ?? AgentPrismId.NewId(),
                TenantId = tenants.TenantId,
                Name = name,
                Url = request.Url!,
                Events = request.Events,
                SecretConfigurationKey = string.IsNullOrWhiteSpace(request.SecretConfigurationKey)
                    ? null
                    : request.SecretConfigurationKey,
                Headers = request.Headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Enabled = request.Enabled,

                // Kaydetmek sayaci sifirlar: yonetici adresi duzelttiginde
                // abonelik hemen kapanmis durumda kalmamalidir.
                ConsecutiveFailures = 0,
                CreatedAt = previous?.CreatedAt ?? now,
                UpdatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("AgentPrism.WebhookEndpoints"),
            tenants.TenantId,
            action: previous is null ? "webhook.create" : "webhook.update",
            entity: $"webhook:{name}",
            before: previous is null ? null : Describe(previous),
            after: Describe(saved),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(saved);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        string name,
        [FromServices] IWebhookStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
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
            loggerFactory.CreateLogger("AgentPrism.WebhookEndpoints"),
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
            return Invalid($"'{name}' aboneligi devre disi; once etkinlestirin.");
        }

        // Sinama olayi, aboneligin olay listesinden BAGIMSIZ gonderilir: bir
        // ucun ayakta oldugunu dogrulamak icin listeye 'test.ping' eklemek
        // gerekmemelidir.
        var queued = await PublishTestAsync(publisher, store, subscription, tenants, timeProvider, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new WebhookTestResponse
        {
            Name = name,
            Queued = queued,
            Message = queued
                ? "Sinama olayi kuyruga yazildi. Sonucu 'deliveries' ucunden izleyin."
                : "Olay yayini kapali (AgentPrism:Webhooks:Enabled=false).",
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

        // Abonelik 'test.ping' olayina abone degilse gecici olarak listeye
        // eklenir; yayinci abonelikleri olaya gore secer. Kalici degisiklik
        // yapilmaz, degistirilmis kopya yalnizca bu cagri icin kaydedilir ve
        // hemen geri alinir.
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

    /// <summary>Bir aboneligi denetim izi icin ozetler.</summary>
    /// <remarks>
    /// Ozette yalnizca anahtarin ADI vardir. Sirrin kendisi hicbir zaman bu
    /// surecte bir kayda yazilmaz (K-059).
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

        writer.WriteBoolean("enabled", subscription.Enabled);
        writer.WriteEndObject();
        writer.Flush();

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static ProblemHttpResult NotFound(string name)
        => TypedResults.Problem(
            title: "Abonelik bulunamadi",
            detail: $"'{name}' adinda bir webhook aboneligi yok.",
            statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult Invalid(string detail)
        => TypedResults.Problem(
            title: "Gecersiz abonelik",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>Bir webhook aboneligini kaydetmek icin istek govdesi.</summary>
/// <remarks>
/// 🚨 Bu sozlesmede <strong>sir alani yoktur</strong>. Istemcinin gonderdigi
/// fazladan bir <c>secret</c> alani baglanmaz ve hicbir yere yazilmaz (K-059).
/// </remarks>
public sealed record WebhookSaveRequest
{
    /// <summary>Olaylarin gonderilecegi adres. Yalnizca <c>https</c> (veya loopback <c>http</c>).</summary>
    public string? Url { get; init; }

    /// <summary>Abone olunan olay adlari. Bkz. <see cref="WebhookEvents"/>.</summary>
    public IReadOnlyList<string>? Events { get; init; }

    /// <summary>
    /// Imzalama sirrinin okunacagi yapilandirma anahtarinin <strong>adi</strong>.
    /// Sirrin kendisi degildir.
    /// </summary>
    public string? SecretConfigurationKey { get; init; }

    /// <summary>Her istege eklenecek ek basliklar.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Abonelik etkin mi.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>Sinama olayinin yaniti.</summary>
public sealed record WebhookTestResponse
{
    /// <summary>Abonelik adi.</summary>
    public required string Name { get; init; }

    /// <summary>Olay kuyruga yazildi mi.</summary>
    public required bool Queued { get; init; }

    /// <summary>Kullaniciya gosterilecek aciklama.</summary>
    public required string Message { get; init; }
}
