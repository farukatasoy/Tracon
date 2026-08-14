using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>Kiraci bazli API anahtari uclari (Faz 53).</summary>
/// <remarks>
/// 🚨 <see cref="ApiKeyCreationResult.PlaintextKey"/> yalnizca
/// <see cref="CreateAsync"/>'in yanitinda doner. Listeleme ucu hicbir zaman ham
/// deger veya ozet dondurmez (bolum 53.2).
/// </remarks>
internal static class ApiKeyEndpoints
{
    /// <summary>API anahtari uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/api-keys", ListAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("AgentPrismListApiKeys")
            .WithTags("AgentPrism", "ApiKeys")
            .WithSummary("Bir kiracinin API anahtarlarini listeler.")
            .WithDescription("Yanit ne ham degeri ne de ozeti tasir.");

        builder.MapPost("/api/api-keys", CreateAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("AgentPrismCreateApiKey")
            .WithTags("AgentPrism", "ApiKeys")
            .WithSummary("Yeni bir API anahtari uretir.")
            .Accepts<ApiKeyCreateRequest>("application/json")
            .WithDescription(
                "Ham deger yanitta YALNIZCA BU CAGRIDA doner ve bir daha " +
                "uretilemez. Kapsam listesi kapalidir; bilinmeyen bir kapsam reddedilir. " +
                "Istek bir API anahtariyla dogrulandiysa, o anahtarin KENDI TASIMADIGI " +
                "bir kapsam istenemez (yetki uzatma/attenuation, bolum 53.3).");

        builder.MapDelete("/api/api-keys/{id:guid}", RevokeAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("AgentPrismRevokeApiKey")
            .WithTags("AgentPrism", "ApiKeys")
            .WithSummary("Bir anahtari iptal eder.")
            .WithDescription("Satir SILINMEZ; iptal damgasi yazilir ve denetim izinde kalir.");
    }

    private static async Task<Ok<IReadOnlyList<ApiKeyRecord>>> ListAsync(
        [FromServices] IApiKeyStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var keys = await store.ListAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(keys);
    }

    private static async Task<Results<Ok<ApiKeyCreationResult>, ProblemHttpResult>> CreateAsync(
        HttpContext httpContext,
        [FromServices] IApiKeyStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<ApiKeyCreateRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Invalid("'name' cannot be empty.");
        }

        if (request.Scopes is not { Count: > 0 })
        {
            return Invalid("At least one scope ('scopes') must be selected.");
        }

        // Yetki uzatma (attenuation): istegi dogrulayan bir API anahtariysa,
        // o anahtarin KENDI TASIMADIGI bir kapsam icin yeni anahtar uretemez.
        // Statik token veya kullanici kimligiyle gelen istekler (Get() null
        // doner) bu sinirdan etkilenmez — rol politikasi zaten yeterlidir.
        if (ApiKeyRequestContext.Get(httpContext) is { } caller)
        {
            var ungranted = request.Scopes.Where(scope => !caller.Scopes.Contains(scope)).ToList();

            if (ungranted.Count > 0)
            {
                return Invalid(
                    $"Cannot request scope(s) this key does not carry: {string.Join(", ", ungranted)}.");
            }
        }

        var created = await store.CreateAsync(
            new ApiKeyDraft
            {
                TenantId = tenants.TenantId,
                Name = request.Name,
                Scopes = request.Scopes,
                ExpiresAt = request.ExpiresAt,
            },
            cancellationToken).ConfigureAwait(false);

        // 🚨 Ham deger denetim izine YAZILMAZ; yalnizca anahtarin ADI ve
        // kapsamlari (K-059'un ayni yonu).
        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("AgentPrism.ApiKeyEndpoints"),
            tenants.TenantId,
            action: "apikey.create",
            entity: $"apikey:{created.Record.Id}",
            before: null,
            after: Describe(created.Record),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(created);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeAsync(
        Guid id,
        [FromServices] IApiKeyStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var revoked = await store.RevokeAsync(tenants.TenantId, id, cancellationToken).ConfigureAwait(false);

        if (!revoked)
        {
            return NotFound(id);
        }

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("AgentPrism.ApiKeyEndpoints"),
            tenants.TenantId,
            action: "apikey.revoke",
            entity: $"apikey:{id}",
            before: null,
            after: null,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    /// <summary>Bir anahtar kaydini denetim izi icin ozetler.</summary>
    /// <remarks>Ozette ham deger veya ozet YOKTUR (K-059'un ayni yonu).</remarks>
    private static string Describe(ApiKeyRecord record)
    {
        using var buffer = new MemoryStream();
        using var writer = new System.Text.Json.Utf8JsonWriter(buffer);

        writer.WriteStartObject();
        writer.WriteString("name", record.Name);
        writer.WriteString("keyPrefix", record.KeyPrefix);
        writer.WriteStartArray("scopes");

        foreach (var scope in record.Scopes)
        {
            writer.WriteStringValue(scope.ToString());
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static ProblemHttpResult NotFound(Guid id)
        => TypedResults.Problem(
            title: "Key not found",
            detail: $"There is no API key with id '{id}'.",
            statusCode: StatusCodes.Status404NotFound);

    private static Results<Ok<ApiKeyCreationResult>, ProblemHttpResult> Invalid(string detail)
        => TypedResults.Problem(
            title: "Invalid request",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>Bir API anahtari olusturmak icin istek govdesi.</summary>
public sealed record ApiKeyCreateRequest
{
    /// <summary>Operatorun anahtari tanimasi icin ad.</summary>
    public string? Name { get; init; }

    /// <summary>Kapsam kumesi. Bos olamaz; bilinmeyen bir deger reddedilir.</summary>
    public IReadOnlyList<ApiKeyScope>? Scopes { get; init; }

    /// <summary>Sure sonu. Verilmezse anahtar suresizdir.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}
