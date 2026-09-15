using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>Tenant-scoped API key endpoints.</summary>
/// <remarks>
/// <see cref="ApiKeyCreationResult.PlaintextKey"/> is returned only in the
/// response of <see cref="CreateAsync"/>. The listing endpoint never returns
/// the raw value or a hash.
/// </remarks>
internal static class ApiKeyEndpoints
{
    /// <summary>Maps the API key endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/api-keys", ListAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("TraconListApiKeys")
            .WithTags("Tracon", "ApiKeys")
            .WithSummary("Lists a tenant's API keys.")
            .WithDescription("The response carries neither the raw value nor a hash.");

        builder.MapPost("/api/api-keys", CreateAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("TraconCreateApiKey")
            .WithTags("Tracon", "ApiKeys")
            .WithSummary("Generates a new API key.")
            .Accepts<ApiKeyCreateRequest>("application/json")
            .WithDescription(
                "The raw value is returned in the response ONLY ON THIS CALL and " +
                "cannot be produced again. The scope list is closed; an unknown scope " +
                "is rejected. If the request was authenticated with an API key, a scope " +
                "that key does NOT ITSELF CARRY cannot be requested (privilege " +
                "extension/attenuation).");

        builder.MapDelete("/api/api-keys/{id:guid}", RevokeAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("TraconRevokeApiKey")
            .WithTags("Tracon", "ApiKeys")
            .WithSummary("Revokes a key.")
            .WithDescription("The row is NOT deleted; a revocation timestamp is written and stays in the audit trail.");
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
        [FromServices] TraconMetrics metrics,
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

        // Privilege attenuation: if an API key authenticated the request, it
        // cannot generate a new key for a scope it does NOT ITSELF CARRY.
        // Requests arriving with a static token or a user identity (Get()
        // returns null) are not affected by this limit — the role policy
        // is already sufficient.
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

        // 🚨 The raw value is NOT WRITTEN to the audit trail; only the key's
        // NAME and scopes are (the same direction as K-059).
        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("Tracon.ApiKeyEndpoints"),
            metrics,
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
        [FromServices] TraconMetrics metrics,
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
            loggerFactory.CreateLogger("Tracon.ApiKeyEndpoints"),
            metrics,
            tenants.TenantId,
            action: "apikey.revoke",
            entity: $"apikey:{id}",
            before: null,
            after: null,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    /// <summary>Summarizes a key record for the audit trail.</summary>
    /// <remarks>The summary has NO raw value or hash (the same direction).</remarks>
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

/// <summary>The request body for creating an API key.</summary>
public sealed record ApiKeyCreateRequest
{
    /// <summary>Gets the name that lets the operator identify the key.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the set of scopes. Cannot be empty; an unknown value is rejected.</summary>
    public IReadOnlyList<ApiKeyScope>? Scopes { get; init; }

    /// <summary>Gets the expiration time. If not given, the key never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}
