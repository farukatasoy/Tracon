using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Tracon;

/// <summary>
/// Shared helpers for OpenAI-compatible endpoints: error shape, agent
/// selection, and ownership checks for untrusted session identifiers.
/// </summary>
internal static class OpenAICompatSupport
{
    /// <summary>
    /// Key the agent name is looked for under in <c>metadata</c>. This is
    /// Microsoft's DevUI convention.
    /// </summary>
    public const string EntityIdKey = "entity_id";

    /// <summary>
    /// Extracts the agent name from the request body.
    /// </summary>
    /// <param name="body">Raw request body.</param>
    /// <returns>The agent name; <see langword="null"/> if not found.</returns>
    /// <remarks>
    /// <para>
    /// The <c>model</c> field is checked first: this lets stock OpenAI SDKs
    /// work without writing an extra field
    /// (<c>client.responses.create(model="support", ...)</c>).
    /// </para>
    /// <para>
    /// If not found, <c>metadata.entity_id</c> is tried, for compatibility with DevUI.
    /// </para>
    /// </remarks>
    public static string? ReadAgentName(JsonElement body)
    {
        if (body.ValueKind is JsonValueKind.Object &&
            body.TryGetProperty("metadata", out var metadata) &&
            metadata.ValueKind is JsonValueKind.Object &&
            metadata.TryGetProperty(EntityIdKey, out var entityId) &&
            entityId.ValueKind is JsonValueKind.String &&
            entityId.GetString() is { Length: > 0 } fromMetadata)
        {
            return fromMetadata;
        }

        if (body.ValueKind is JsonValueKind.Object &&
            body.TryGetProperty("model", out var model) &&
            model.ValueKind is JsonValueKind.String &&
            model.GetString() is { Length: > 0 } fromModel)
        {
            return fromModel;
        }

        return null;
    }

    /// <summary>Reads the <c>stream</c> flag from the body.</summary>
    /// <param name="body">Raw request body.</param>
    /// <returns><see langword="true"/> if streaming is requested.</returns>
    public static bool ReadStreamFlag(JsonElement body)
        => body.ValueKind is JsonValueKind.Object &&
           body.TryGetProperty("stream", out var stream) &&
           stream.ValueKind is JsonValueKind.True;

    /// <summary>
    /// Produces an OpenAI-shaped error response.
    /// </summary>
    /// <param name="statusCode">HTTP status code.</param>
    /// <param name="message">Description to show the user.</param>
    /// <param name="type">OpenAI error type.</param>
    /// <returns>A writable result.</returns>
    /// <remarks>
    /// These endpoints <strong>do not use</strong> <c>ProblemDetails</c>.
    /// OpenAI SDKs parse the error body in the <c>{"error":{"message":...}}</c>
    /// shape; returning <c>ProblemDetails</c> would produce a meaningless error
    /// on the client. The management API (<c>/api/*</c>) continues to use
    /// <c>ProblemDetails</c>.
    /// </remarks>
    public static IResult Error(int statusCode, string message, string type = "invalid_request_error")
        => Results.Json(
            new OpenAIErrorEnvelope(new OpenAIErrorBody(message, type)),
            JsonOptions,
            contentType: "application/json",
            statusCode: statusCode);

    /// <summary>
    /// Checks whether an untrusted session identifier belongs to the current tenant.
    /// </summary>
    /// <param name="store">Session store.</param>
    /// <param name="tenantContext">Tenant context.</param>
    /// <param name="sessionId">Identifier coming from the client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="false"/> if the identifier belongs to another tenant. If
    /// there is no record, returns <see langword="true"/>: the identifier has
    /// not been used yet and a new session will be opened.
    /// </returns>
    /// <remarks>
    /// <c>conversation</c> and <c>previous_response_id</c> come from the
    /// client and are treated as <strong>untrusted</strong>. The check happens
    /// here, in the HTTP layer.
    /// <para>
    /// <see cref="ISessionStore.GetAsync"/> is NOT USED: it is filtered by
    /// the ambient tenant, so it can never correctly answer the cross-tenant
    /// question — another tenant's record is NEVER VISIBLE from this context
    /// and the result would always be "no record, allow it".
    /// <see cref="ISessionStore.GetOwnerTenantIdAsync"/> is used instead; it
    /// does NOT APPLY a tenant filter.
    /// </para>
    /// </remarks>
    public static async ValueTask<bool> IsOwnedByTenantAsync(
        ISessionStore store,
        ITenantContext tenantContext,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var ownerTenantId = await store.GetOwnerTenantIdAsync(sessionId, cancellationToken).ConfigureAwait(false);

        return ownerTenantId is null ||
               string.Equals(ownerTenantId, tenantContext.TenantId, StringComparison.Ordinal);
    }

    /// <summary>Current time in Unix seconds.</summary>
    /// <returns>Epoch seconds.</returns>
    public static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Serialization settings for OpenAI-compatible outputs.</summary>
    /// <remarks>
    /// The OpenAI wire format uses <c>snake_case</c>; property names are
    /// therefore written explicitly with <c>JsonPropertyName</c> rather than
    /// left to the default naming policy.
    /// </remarks>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Helper that generates a string identifier.</summary>
    /// <param name="prefix">Identifier prefix.</param>
    /// <returns>A unique identifier.</returns>
    public static string CreateId(string prefix)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}{Guid.NewGuid():N}");

    /// <summary>
    /// Outer envelope of an OpenAI-compatible error body. Internal so it can be
    /// attached to response schema metadata.
    /// </summary>
    internal sealed record OpenAIErrorEnvelope(OpenAIErrorBody Error);

    /// <summary>OpenAI-compatible error body.</summary>
    internal sealed record OpenAIErrorBody(string Message, string Type);
}
