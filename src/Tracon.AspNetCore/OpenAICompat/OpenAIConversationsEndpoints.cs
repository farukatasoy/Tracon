using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Endpoints compatible with the OpenAI Conversations API.
/// </summary>
/// <remarks>
/// <para>
/// <strong>In Tracon a conversation and a session are the same thing.</strong>
/// A conversation identifier is the identifier of a session in the session
/// store; the <c>conversation</c> field in a <c>/v1/responses</c> call uses
/// the same identifier. This is a deliberate modeling decision: opening a
/// second identifier space would cause the same chat to look different from
/// two different places.
/// </para>
/// <para>
/// <c>POST /v1/conversations</c> is an <strong>identifier reservation</strong>:
/// the identifier is generated and returned, and the session is born on the
/// first <c>/v1/responses</c> call. The reason is that a session is bound to
/// an agent; which agent will be used is not yet known when the conversation
/// is created. As a result, a conversation that has not been used yet returns
/// <strong>empty</strong>, not <c>404</c> — this is the one behavior
/// difference from real OpenAI, and it is documented.
/// </para>
/// <para>
/// <c>POST /v1/conversations/{id}/items</c> is <strong>not supported</strong>:
/// writing a message directly to history requires an agent binding and the
/// chat history provider's write path. The correct way to add a message is a
/// <c>/v1/responses</c> call.
/// </para>
/// </remarks>
internal static class OpenAIConversationsEndpoints
{
    private const string ConversationIdPrefix = "conv_";

    /// <summary>Connects the Conversations endpoints.</summary>
    /// <param name="builder">Endpoint group.</param>
    /// <param name="roles">Resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapPost("/v1/conversations", CreateAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconOpenAICreateConversation")
            .WithTags("Tracon", "OpenAI")
            .WithSummary("Generates a new conversation identifier.")
            .WithDescription(
                "An identifier reservation: the session is born on the first /v1/responses call. " +
                "The returned identifier is used directly in the 'conversation' field.")
            .Produces<ConversationResource>(StatusCodes.Status200OK)
            .Produces<OpenAICompatSupport.OpenAIErrorEnvelope>(StatusCodes.Status400BadRequest);

        builder.MapGet("/v1/conversations/{conversationId}", RetrieveAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconOpenAIGetConversation")
            .WithTags("Tracon", "OpenAI")
            .WithSummary("Returns a conversation's metadata.")
            .WithDescription(
                "A conversation identifier is a reservation, so an id that has never carried a " +
                "call is still valid and answers 200 with the current time as its creation time. " +
                "404 therefore means 'not yours', not 'never used': an identifier owned by " +
                "another tenant is reported as missing rather than forbidden, so the API does " +
                "not confirm that it exists.")
            .Produces<ConversationResource>(StatusCodes.Status200OK)
            .Produces<OpenAICompatSupport.OpenAIErrorEnvelope>(StatusCodes.Status404NotFound);

        builder.MapDelete("/v1/conversations/{conversationId}", DeleteAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconOpenAIDeleteConversation")
            .WithTags("Tracon", "OpenAI")
            .WithSummary("Deletes a conversation and the session underneath it.")
            .WithDescription(
                "Following the OpenAI shape, the response is 200 with a 'deleted' flag rather " +
                "than 204: the flag is false when the identifier was valid but no session had " +
                "been created for it yet, so a client can tell a real deletion from a no-op. " +
                "An identifier owned by another tenant returns 404.")
            .Produces<DeletedResource>(StatusCodes.Status200OK)
            .Produces<OpenAICompatSupport.OpenAIErrorEnvelope>(StatusCodes.Status404NotFound);

        builder.MapGet("/v1/conversations/{conversationId}/items", ListItemsAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconOpenAIListConversationItems")
            .WithTags("Tracon", "OpenAI")
            .WithSummary("Lists a conversation's messages in the OpenAI item format.")
            .WithDescription(
                "Items come back oldest first, and one stored message can expand into several " +
                "items — a reply plus its tool calls, for example. '?limit=' trims the list from " +
                "the end and sets 'has_more' to true, which is computed from the real total " +
                "before trimming, so a truncated list never looks complete. There is no cursor " +
                "paging: 'first_id' and 'last_id' describe the returned window only. A " +
                "conversation with no history returns an empty list, and an identifier owned by " +
                "another tenant returns 404.")
            .Produces<ItemListResource>(StatusCodes.Status200OK)
            .Produces<OpenAICompatSupport.OpenAIErrorEnvelope>(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> CreateAsync(
        HttpContext httpContext,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        // The body is optional; OpenAI accepts 'metadata'.
        //
        // Read as raw text; ContentLength is NOT TRUSTED: chunked transfer
        // carries no header and ContentLength is null — measured, the
        // client's metadata was silently getting dropped. The distinction
        // between an empty body and a malformed body is made explicitly here;
        // a malformed body is not silently ignored.
        JsonElement body = default;

        var raw = await new StreamReader(httpContext.Request.Body)
            .ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                body = document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                return OpenAICompatSupport.Error(
                    StatusCodes.Status400BadRequest,
                    $"Body could not be parsed: {ex.Message}");
            }
        }

        var metadata = ReadMetadata(body);
        var conversation = new ConversationResource(
            ConversationIdPrefix + TraconId.NewId().ToString("N"),
            "conversation",
            OpenAICompatSupport.UnixNow(),
            metadata);

        return Results.Json(conversation, OpenAICompatSupport.JsonOptions, statusCode: StatusCodes.Status200OK);
    }

    private static async Task<IResult> RetrieveAsync(
        string conversationId,
        HttpContext httpContext,
        ISessionStore sessions,
        ITenantContext tenantContext,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        [FromServices] IOptionsMonitor<TraconSessionOwnershipOptions>? sessionOwnershipOptions,
        CancellationToken cancellationToken)
    {
        // The ownership check runs BEFORE GetAsync, independent of the tenant;
        // because GetAsync is already filtered by the ambient tenant, it
        // CANNOT SEE another tenant's record and could never trigger the
        // check (HATA-S2-005).
        if (!await OpenAICompatSupport
                .IsOwnedByTenantAsync(sessions, tenantContext, conversationId, cancellationToken)
                .ConfigureAwait(false))
        {
            return NotFound(conversationId);
        }

        // 🚨 The consumer's own handler is asked HERE too, not only on the
        // /api/sessions route: this surface reaches the very same sessions
        // under a different name, and phase 148 already had to come back for it
        // once with the ownership boundary. Asked AFTER the tenant is settled
        // and BEFORE any state is read (K-684), and the gate's own problem body
        // is deliberately DISCARDED — this endpoint answers in the OpenAI error
        // shape, byte for byte the "wrong tenant" answer above.
        //
        // 🚨 What that byte equality does and does NOT buy, on THIS endpoint:
        // it keeps a refusal indistinguishable from another tenant's
        // conversation, which is the comparison that matters. It does not hide
        // existence, and it cannot: an identifier nobody has used yet answers
        // 200 here, because a conversation id is a RESERVATION and the session
        // is born on the first /v1/responses call (see the type remarks). So on
        // this surface "404" already means "not yours" rather than "never
        // existed" — the endpoint's own WithDescription says so — and the
        // identity-hiding 404 of /api/sessions/{id} has no equivalent to
        // preserve here.
        if (await RunAuthorizationGate
                .CheckSessionAsync(runAuthorizationHandler, tenantContext, conversationId, attributionContext, SessionAccess.Read, httpContext, cancellationToken)
                .ConfigureAwait(false) is not null)
        {
            return NotFound(conversationId);
        }

        // 🚨 The OpenAI-compatible surface reaches the SAME sessions under a
        // different name, so it needs the same ownership boundary - phase 148.
        // Leaving it out made `/api/sessions/{id}` answer 404 for another
        // user's session while `/v1/conversations/{id}/items` returned its
        // whole history: one door locked, the one beside it open. The answer
        // is this endpoint's OWN "not found", byte for byte the tenant check's
        // — the same equality, and the same limit, as the handler gate above.
        if (await SessionOwnershipGate
                .DeniesAsync(sessionOwnershipOptions, attributionContext, sessions, conversationId, httpContext, cancellationToken)
                .ConfigureAwait(false))
        {
            return NotFound(conversationId);
        }

        var record = await sessions.GetAsync(conversationId, cancellationToken).ConfigureAwait(false);

        // If there is no record the conversation has not been used yet; the identifier is valid and an empty result is returned.
        var conversation = new ConversationResource(
            conversationId,
            "conversation",
            (record?.CreatedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(),
            Metadata: null);

        return Results.Json(conversation, OpenAICompatSupport.JsonOptions, statusCode: StatusCodes.Status200OK);
    }

    private static async Task<IResult> DeleteAsync(
        string conversationId,
        HttpContext httpContext,
        ISessionStore sessions,
        AgentSessionManager manager,
        ITenantContext tenantContext,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        [FromServices] IOptionsMonitor<TraconSessionOwnershipOptions>? sessionOwnershipOptions,
        CancellationToken cancellationToken)
    {
        if (!await OpenAICompatSupport
                .IsOwnedByTenantAsync(sessions, tenantContext, conversationId, cancellationToken)
                .ConfigureAwait(false))
        {
            return NotFound(conversationId);
        }

        // Same handler gate as RetrieveAsync above, with the access this
        // endpoint actually performs. Checked BEFORE the delete: a denial must
        // leave the conversation, and the session under it, untouched.
        if (await RunAuthorizationGate
                .CheckSessionAsync(runAuthorizationHandler, tenantContext, conversationId, attributionContext, SessionAccess.Delete, httpContext, cancellationToken)
                .ConfigureAwait(false) is not null)
        {
            return NotFound(conversationId);
        }

        // Same boundary as RetrieveAsync above, and the same "not found" body.
        if (await SessionOwnershipGate
                .DeniesAsync(sessionOwnershipOptions, attributionContext, sessions, conversationId, httpContext, cancellationToken)
                .ConfigureAwait(false))
        {
            return NotFound(conversationId);
        }

        var record = await sessions.GetAsync(conversationId, cancellationToken).ConfigureAwait(false);

        var deleted = record is not null &&
                      await manager.DeleteSessionAsync(conversationId, cancellationToken).ConfigureAwait(false);

        return Results.Json(
            new DeletedResource(conversationId, "conversation.deleted", deleted),
            OpenAICompatSupport.JsonOptions,
            statusCode: StatusCodes.Status200OK);
    }

    private static async Task<IResult> ListItemsAsync(
        string conversationId,
        HttpContext httpContext,
        ISessionStore sessions,
        IAgentCatalog catalog,
        ChatHistoryProvider chatHistory,
        ITenantContext tenantContext,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        [FromServices] IOptionsMonitor<TraconSessionOwnershipOptions>? sessionOwnershipOptions,
        ILoggerFactory loggerFactory,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (!await OpenAICompatSupport
                .IsOwnedByTenantAsync(sessions, tenantContext, conversationId, cancellationToken)
                .ConfigureAwait(false))
        {
            return NotFound(conversationId);
        }

        // Same handler gate as RetrieveAsync above; reading a conversation's
        // items IS reading the session's chat history.
        if (await RunAuthorizationGate
                .CheckSessionAsync(runAuthorizationHandler, tenantContext, conversationId, attributionContext, SessionAccess.Read, httpContext, cancellationToken)
                .ConfigureAwait(false) is not null)
        {
            return NotFound(conversationId);
        }

        // Same boundary as RetrieveAsync above, and the same "not found" body.
        if (await SessionOwnershipGate
                .DeniesAsync(sessionOwnershipOptions, attributionContext, sessions, conversationId, httpContext, cancellationToken)
                .ConfigureAwait(false))
        {
            return NotFound(conversationId);
        }

        var record = await sessions.GetAsync(conversationId, cancellationToken).ConfigureAwait(false);

        var messages = record is null
            ? null
            : await ChatHistoryReader
                .ReadAsync(record, catalog, chatHistory, loggerFactory, cancellationToken)
                .ConfigureAwait(false);

        var items = new List<ItemResource>();

        foreach (var message in messages ?? [])
        {
            items.AddRange(ToItems(message));
        }

        // 🚨 HasMore used to be written as a fixed 'false'; truncated items
        // appeared to silently vanish. The real total is measured BEFORE trimming.
        var hasMore = false;

        if (limit is { } max && max > 0 && items.Count > max)
        {
            items = items[..max];
            hasMore = true;
        }

        var list = new ItemListResource(
            "list",
            items,
            items.Count == 0 ? null : items[0].Id,
            items.Count == 0 ? null : items[^1].Id,
            hasMore);

        return Results.Json(list, OpenAICompatSupport.JsonOptions, statusCode: StatusCodes.Status200OK);
    }

    /// <summary>
    /// Converts a <see cref="ChatMessage"/> into OpenAI item resources.
    /// </summary>
    /// <remarks>
    /// A single message can produce more than one item: the model can both
    /// call a tool and return text. Order is preserved.
    /// </remarks>
    private static IEnumerable<ItemResource> ToItems(ChatMessage message)
    {
        var isAssistant = message.Role == ChatRole.Assistant;
        var textParts = new List<ItemContent>();

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    textParts.Add(new ItemContent(isAssistant ? "output_text" : "input_text", text.Text));
                    break;

                case FunctionCallContent call:
                    yield return new ItemResource(
                        Id: "fc_" + TraconId.NewId().ToString("N"),
                        Type: "function_call",
                        Status: "completed",
                        Role: null,
                        Content: null,
                        CallId: call.CallId,
                        Name: call.Name,
                        Arguments: FormatArguments(call),
                        Output: null);
                    break;

                case FunctionResultContent result:
                    yield return new ItemResource(
                        Id: "fco_" + TraconId.NewId().ToString("N"),
                        Type: "function_call_output",
                        Status: result.Exception is null ? "completed" : "incomplete",
                        Role: null,
                        Content: null,
                        CallId: result.CallId,
                        Name: null,
                        Arguments: null,
                        Output: result.Exception?.Message ?? result.Result?.ToString());
                    break;

                default:
                    break;
            }
        }

        if (textParts.Count > 0)
        {
            yield return new ItemResource(
                Id: "msg_" + TraconId.NewId().ToString("N"),
                Type: "message",
                Status: "completed",
                Role: message.Role.Value,
                Content: textParts,
                CallId: null,
                Name: null,
                Arguments: null,
                Output: null);
        }
    }

    private static string? FormatArguments(FunctionCallContent call)
        => call.Arguments is null || call.Arguments.Count == 0
            ? null
            : JsonSerializer.Serialize(call.Arguments, AIJsonUtilities.DefaultOptions);

    private static IResult NotFound(string conversationId)
        => OpenAICompatSupport.Error(
            StatusCodes.Status404NotFound,
            $"'{conversationId}' was not found.",
            type: "not_found_error");

    private static Dictionary<string, string>? ReadMetadata(JsonElement body)
    {
        if (body.ValueKind is not JsonValueKind.Object ||
            !body.TryGetProperty("metadata", out var metadata) ||
            metadata.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var property in metadata.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.String)
            {
                result[property.Name] = property.Value.GetString()!;
            }
        }

        return result.Count == 0 ? null : result;
    }

    private sealed record ConversationResource(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("object")] string Object,
        [property: JsonPropertyName("created_at")] long CreatedAt,
        [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string>? Metadata);

    private sealed record DeletedResource(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("object")] string Object,
        [property: JsonPropertyName("deleted")] bool Deleted);

    private sealed record ItemListResource(
        [property: JsonPropertyName("object")] string Object,
        [property: JsonPropertyName("data")] IReadOnlyList<ItemResource> Data,
        [property: JsonPropertyName("first_id")] string? FirstId,
        [property: JsonPropertyName("last_id")] string? LastId,
        [property: JsonPropertyName("has_more")] bool HasMore);

    private sealed record ItemResource(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("content")] IReadOnlyList<ItemContent>? Content,
        [property: JsonPropertyName("call_id")] string? CallId,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("arguments")] string? Arguments,
        [property: JsonPropertyName("output")] string? Output);

    private sealed record ItemContent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text);
}
