using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// OpenAI Conversations API ile uyumlu uclar.
/// </summary>
/// <remarks>
/// <para>
/// <strong>AgentPrism'de konusma ile oturum ayni seydir.</strong> Bir konusma kimligi,
/// oturum deposundaki bir oturumun kimligidir; <c>/v1/responses</c> cagrisindaki
/// <c>conversation</c> alani da ayni kimligi kullanir. Bu bilincli bir modelleme
/// karari: ikinci bir kimlik uzayi acmak, ayni sohbetin iki farkli yerden farkli
/// gorunmesine yol acardi. Gerekce: <c>docs/KARARLAR.md</c>, karar K-043.
/// </para>
/// <para>
/// <c>POST /v1/conversations</c> bir <strong>kimlik rezervasyonudur</strong>: kimlik
/// uretilir ve dondurulur, oturum ilk <c>/v1/responses</c> cagrisinda dogar. Bunun
/// sebebi bir oturumun bir agent'a baglı olmasidir; konusma olusturulurken hangi
/// agent'in kullanilacagi henuz bilinmez. Sonuc olarak henuz kullanilmamis bir
/// konusma <c>404</c> degil, <strong>bos</strong> doner — gercek OpenAI'den tek
/// davranis farki budur ve dokumante edilmistir.
/// </para>
/// <para>
/// <c>POST /v1/conversations/{id}/items</c> <strong>desteklenmez</strong>: gecmise
/// dogrudan mesaj yazmak bir agent baglantisi ve sohbet gecmisi saglayicisinin
/// yazma yolunu gerektirir. Mesaj eklemenin dogru yolu bir <c>/v1/responses</c>
/// cagrisidir.
/// </para>
/// </remarks>
internal static class OpenAIConversationsEndpoints
{
    private const string ConversationIdPrefix = "conv_";

    /// <summary>Conversations uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    public static void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/v1/conversations", CreateAsync)
            .WithName("AgentPrismOpenAICreateConversation")
            .WithSummary("Yeni bir konusma kimligi uretir.")
            .WithDescription(
                "Kimlik rezervasyonudur: oturum ilk /v1/responses cagrisinda dogar. " +
                "Donen kimlik dogrudan 'conversation' alaninda kullanilir.");

        builder.MapGet("/v1/conversations/{conversationId}", RetrieveAsync)
            .WithName("AgentPrismOpenAIGetConversation")
            .WithSummary("Bir konusmanin ustverisini dondurur.");

        builder.MapDelete("/v1/conversations/{conversationId}", DeleteAsync)
            .WithName("AgentPrismOpenAIDeleteConversation")
            .WithSummary("Bir konusmayi ve altindaki oturumu siler.");

        builder.MapGet("/v1/conversations/{conversationId}/items", ListItemsAsync)
            .WithName("AgentPrismOpenAIListConversationItems")
            .WithSummary("Bir konusmanin mesajlarini OpenAI oge bicimiyle listeler.");
    }

    private static async Task<IResult> CreateAsync(
        HttpContext httpContext,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        // Govde istege baglidir; OpenAI 'metadata' kabul eder.
        //
        // Ham metin olarak okunur, ContentLength'e GUVENILMEZ: parcali (chunked)
        // aktarimda baslik gelmez ve ContentLength null olur — olculdu, istemcinin
        // gonderdigi metadata sessizce dusuyordu. Bos govde ile bozuk govde
        // ayrimi burada acikca yapilir; bozuk govde sessizce yok sayilmaz.
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
                    $"Govde cozumlenemedi: {ex.Message}");
            }
        }

        var metadata = ReadMetadata(body);
        var conversation = new ConversationResource(
            ConversationIdPrefix + AgentPrismId.NewId().ToString("N"),
            "conversation",
            OpenAICompatSupport.UnixNow(),
            metadata);

        return Results.Json(conversation, OpenAICompatSupport.JsonOptions, statusCode: StatusCodes.Status200OK);
    }

    private static async Task<IResult> RetrieveAsync(
        string conversationId,
        ISessionStore sessions,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        var record = await sessions.GetAsync(conversationId, cancellationToken).ConfigureAwait(false);

        if (record is not null && !IsOwnedByTenant(record, tenantContext))
        {
            return NotFound(conversationId);
        }

        // Kayit yoksa konusma henuz kullanilmamistir; kimlik gecerlidir ve bos doner.
        var conversation = new ConversationResource(
            conversationId,
            "conversation",
            (record?.CreatedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(),
            Metadata: null);

        return Results.Json(conversation, OpenAICompatSupport.JsonOptions, statusCode: StatusCodes.Status200OK);
    }

    private static async Task<IResult> DeleteAsync(
        string conversationId,
        ISessionStore sessions,
        AgentSessionManager manager,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        var record = await sessions.GetAsync(conversationId, cancellationToken).ConfigureAwait(false);

        if (record is not null && !IsOwnedByTenant(record, tenantContext))
        {
            return NotFound(conversationId);
        }

        var deleted = record is not null &&
                      await manager.DeleteSessionAsync(conversationId, cancellationToken).ConfigureAwait(false);

        return Results.Json(
            new DeletedResource(conversationId, "conversation.deleted", deleted),
            OpenAICompatSupport.JsonOptions,
            statusCode: StatusCodes.Status200OK);
    }

    private static async Task<IResult> ListItemsAsync(
        string conversationId,
        ISessionStore sessions,
        IAgentCatalog catalog,
        ChatHistoryProvider chatHistory,
        ITenantContext tenantContext,
        ILoggerFactory loggerFactory,
        int? limit,
        CancellationToken cancellationToken)
    {
        var record = await sessions.GetAsync(conversationId, cancellationToken).ConfigureAwait(false);

        if (record is not null && !IsOwnedByTenant(record, tenantContext))
        {
            return NotFound(conversationId);
        }

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

        if (limit is { } max && max > 0 && items.Count > max)
        {
            items = items[..max];
        }

        var list = new ItemListResource(
            "list",
            items,
            items.Count == 0 ? null : items[0].Id,
            items.Count == 0 ? null : items[^1].Id,
            HasMore: false);

        return Results.Json(list, OpenAICompatSupport.JsonOptions, statusCode: StatusCodes.Status200OK);
    }

    /// <summary>
    /// Bir <see cref="ChatMessage"/> nesnesini OpenAI oge kaynaklarina cevirir.
    /// </summary>
    /// <remarks>
    /// Tek bir mesaj birden fazla oge uretebilir: model hem tool cagirip hem metin
    /// donebilir. Sira korunur.
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
                        Id: "fc_" + AgentPrismId.NewId().ToString("N"),
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
                        Id: "fco_" + AgentPrismId.NewId().ToString("N"),
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
                Id: "msg_" + AgentPrismId.NewId().ToString("N"),
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

    private static bool IsOwnedByTenant(SessionRecord record, ITenantContext tenantContext)
        => record.TenantId is null ||
           string.Equals(record.TenantId, tenantContext.TenantId, StringComparison.Ordinal);

    private static IResult NotFound(string conversationId)
        => OpenAICompatSupport.Error(
            StatusCodes.Status404NotFound,
            $"'{conversationId}' bulunamadi.",
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
