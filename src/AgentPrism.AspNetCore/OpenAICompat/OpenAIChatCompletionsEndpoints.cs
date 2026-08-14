using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// OpenAI Chat Completions API ile uyumlu calistirma ucu.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Bu uc durumsuzdur.</strong> Chat Completions sozlesmesinde konusma
/// gecmisini istemci tasir: her istek tum mesaj listesini gonderir. Bu yuzden
/// oturum acilmaz ve sohbet gecmisi saglayicisi devreye girmez; aksi halde gecmis
/// iki kez yonetilir ve mesajlar cift gorunurdu.
/// </para>
/// <para>
/// <c>Microsoft.Agents.AI.Hosting.OpenAI</c> paketi Responses API'si icin public
/// bir yazici yardimcisi (<c>OpenAIResponses</c>) sunar, Chat Completions icin
/// sunmaz — o yoldaki tum model tipleri internal'dir. Bu yuzden kablo bicimi
/// burada elle uretilir. Bicim OpenAI tarafindan belgelenmis ve kararlidir.
/// </para>
/// <para>
/// Tool cagrilari yanitta <strong>gorunmez</strong>: tool dongusu sunucu tarafinda
/// Microsoft Agent Framework icinde tamamlanir, istemciye yalnizca sonuc metni doner.
/// Tool ayrintilarini gormek icin yonetim API'sindeki calistirma olaylari kullanilir.
/// </para>
/// </remarks>
internal static class OpenAIChatCompletionsEndpoints
{
    private const string ObjectCompletion = "chat.completion";
    private const string ObjectChunk = "chat.completion.chunk";

    /// <summary>Chat Completions ucunu baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    /// <param name="idempotencyFilter">Faz 43 — <c>Idempotency-Key</c> destegi.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles, IdempotencyFilter idempotencyFilter)
    {
        builder.MapPost("/v1/chat/completions", HandleAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .AddEndpointFilter(idempotencyFilter)
            .WithName("AgentPrismOpenAIChatCompletions")
            .WithTags("AgentPrism", "OpenAI")
            .WithSummary("OpenAI Chat Completions API ile uyumlu calistirma ucu.")
            .WithDescription(
                "Durumsuzdur: gecmisi istemci tasir. Agent, 'model' alanindan secilir; " +
                "bulunamazsa 'metadata.entity_id' denenir.")
            // Govdedeki 'stream' bayragina gore ikisinden biri. Ayni statu kodu
            // icin IKINCI bir .Produces cagrisi BIRINCIYI EZER (olculdu); ikisi
            // tek cagriya additionalContentTypes ile yazilmalidir. Akisli yolda
            // gercek govde ChatCompletionChunk'tir; sema burada ChatCompletion'a
            // yaklastirilir (ASP.NET Core'un metadata modeli ayni statu icin iki
            // farkli tipi ifade edemez).
            .Produces<ChatCompletion>(
                StatusCodes.Status200OK,
                contentType: "application/json",
                additionalContentTypes: ["text/event-stream"])
            .Produces<OpenAICompatSupport.OpenAIErrorEnvelope>(StatusCodes.Status400BadRequest)
            .Produces<OpenAICompatSupport.OpenAIErrorEnvelope>(StatusCodes.Status404NotFound)
            .Produces<OpenAICompatSupport.OpenAIErrorEnvelope>(StatusCodes.Status502BadGateway);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IAgentCatalog catalog,
        CancellationToken cancellationToken)
    {
        JsonElement body;

        try
        {
            body = await httpContext.Request
                .ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return OpenAICompatSupport.Error(StatusCodes.Status400BadRequest, $"Govde cozumlenemedi: {ex.Message}");
        }

        var agentName = OpenAICompatSupport.ReadAgentName(body);

        if (string.IsNullOrEmpty(agentName))
        {
            return OpenAICompatSupport.Error(
                StatusCodes.Status400BadRequest,
                "Agent secilmedi. 'model' alanina agent adini yazin veya " +
                $"'metadata.{OpenAICompatSupport.EntityIdKey}' kullanin.");
        }

        AIAgent? agent;

        try
        {
            agent = await catalog.ResolveAsync(agentName, cancellationToken).ConfigureAwait(false);
        }
        catch (AgentPrismException ex)
        {
            return OpenAICompatSupport.Error(StatusCodes.Status400BadRequest, ex.Message);
        }

        if (agent is null)
        {
            return OpenAICompatSupport.Error(
                StatusCodes.Status404NotFound,
                $"'{agentName}' adinda bir agent yok.",
                type: "model_not_found");
        }

        if (!TryReadMessages(body, out var messages, out var error))
        {
            return OpenAICompatSupport.Error(StatusCodes.Status400BadRequest, error);
        }

        var completionId = OpenAICompatSupport.CreateId("chatcmpl-");

        if (OpenAICompatSupport.ReadStreamFlag(body))
        {
            return new ChatCompletionsStream(agent, messages, completionId, agentName);
        }

        try
        {
            // Oturum bilerek verilmiyor: Chat Completions durumsuzdur.
            var response = await agent
                .RunAsync(messages, session: null, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Json(
                new ChatCompletion(
                    completionId,
                    ObjectCompletion,
                    OpenAICompatSupport.UnixNow(),
                    agentName,
                    [new ChatChoice(0, new ChatMessagePayload("assistant", response.Text), "stop")],
                    ToUsage(response.Usage)),
                OpenAICompatSupport.JsonOptions,
                statusCode: StatusCodes.Status200OK);
        }
        catch (Exception ex) when (ex is AgentPrismException or InvalidOperationException or HttpRequestException)
        {
            return OpenAICompatSupport.Error(StatusCodes.Status502BadGateway, ex.Message, type: "upstream_error");
        }
    }

    /// <summary>
    /// <c>messages</c> dizisini <see cref="ChatMessage"/> listesine cevirir.
    /// </summary>
    /// <remarks>
    /// Icerik hem duz metin hem de <c>{"type":"text","text":"..."}</c> parcalari
    /// bicimindeki dizi olabilir; OpenAI SDK'lari ikisini de uretir.
    /// </remarks>
    private static bool TryReadMessages(
        JsonElement body,
        out List<ChatMessage> messages,
        out string error)
    {
        messages = [];

        if (body.ValueKind is not JsonValueKind.Object ||
            !body.TryGetProperty("messages", out var raw) ||
            raw.ValueKind is not JsonValueKind.Array)
        {
            error = "'messages' alani zorunludur ve bir dizi olmalidir.";
            return false;
        }

        foreach (var item in raw.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.Object ||
                !item.TryGetProperty("role", out var roleElement) ||
                roleElement.ValueKind is not JsonValueKind.String)
            {
                error = "Her mesaj bir 'role' alani tasimalidir.";
                return false;
            }

            var role = roleElement.GetString()!;
            var text = ReadContent(item);

            if (text is null)
            {
                continue;
            }

            messages.Add(new ChatMessage(ToChatRole(role), text));
        }

        if (messages.Count == 0)
        {
            error = "'messages' bos olamaz.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string? ReadContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind is JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind is not JsonValueKind.Array)
        {
            return null;
        }

        var builder = new StringBuilder();

        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind is JsonValueKind.Object &&
                part.TryGetProperty("text", out var partText) &&
                partText.ValueKind is JsonValueKind.String)
            {
                builder.Append(partText.GetString());
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static ChatRole ToChatRole(string role) => role switch
    {
        "system" => ChatRole.System,
        // OpenAI "developer" rolunu "system" yerine gecen yeni ad olarak kullaniyor.
        "developer" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        "tool" => ChatRole.Tool,
        _ => ChatRole.User,
    };

    private static ChatUsage? ToUsage(UsageDetails? usage)
        => usage is null
            ? null
            : new ChatUsage(
                usage.InputTokenCount ?? 0,
                usage.OutputTokenCount ?? 0,
                usage.TotalTokenCount ?? 0);

    /// <summary>Akisli Chat Completions yanitini yazar.</summary>
    private sealed class ChatCompletionsStream(
        AIAgent agent,
        List<ChatMessage> messages,
        string completionId,
        string model) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            var cancellationToken = httpContext.RequestAborted;
            var writer = await SseWriter.StartAsync(httpContext.Response, cancellationToken).ConfigureAwait(false);
            var created = OpenAICompatSupport.UnixNow();

            try
            {
                await WriteChunkAsync(writer, new ChatDelta("assistant", null), finishReason: null, created, cancellationToken)
                    .ConfigureAwait(false);

                var updates = agent.RunStreamingAsync(messages, session: null, cancellationToken: cancellationToken);

                await foreach (var update in updates.ConfigureAwait(false))
                {
                    if (update.Text is { Length: > 0 } text)
                    {
                        await WriteChunkAsync(writer, new ChatDelta(null, text), finishReason: null, created, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                await WriteChunkAsync(writer, new ChatDelta(null, null), "stop", created, cancellationToken)
                    .ConfigureAwait(false);

                // OpenAI akisi bu sabit isaretle biter; SDK'lar bunu bekler.
                await writer.WriteRawAsync("data: [DONE]\n\n", cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Istemci baglantiyi kesti.
            }
            catch (Exception ex)
            {
                // 🚨 K-296 (bkz. AgentEndpoints.ExecuteStreamingAsync): dar bir istisna
                // filtresi gercek saglayici SDK istisnalarini kacirip baglantiyi 'error'
                // cercevesi UretMEDEN kapatirdi. Burada HER istisna bir cerceveye donusur.
                var payload = JsonSerializer.Serialize(
                    new ChatStreamError(new ChatStreamErrorBody(ex.Message, "upstream_error")),
                    OpenAICompatSupport.JsonOptions);

                await writer.WriteRawAsync($"data: {payload}\n\n", CancellationToken.None).ConfigureAwait(false);
            }
        }

        private Task WriteChunkAsync(
            SseWriter writer,
            ChatDelta delta,
            string? finishReason,
            long created,
            CancellationToken cancellationToken)
        {
            var chunk = new ChatCompletionChunk(
                completionId,
                ObjectChunk,
                created,
                model,
                [new ChatChunkChoice(0, delta, finishReason)]);

            return writer.WriteRawAsync(
                $"data: {JsonSerializer.Serialize(chunk, OpenAICompatSupport.JsonOptions)}\n\n",
                cancellationToken);
        }

        private sealed record ChatStreamError([property: JsonPropertyName("error")] ChatStreamErrorBody Error);

        private sealed record ChatStreamErrorBody(
            [property: JsonPropertyName("message")] string Message,
            [property: JsonPropertyName("type")] string Type);
    }

    private sealed record ChatCompletion(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("object")] string Object,
        [property: JsonPropertyName("created")] long Created,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice> Choices,
        [property: JsonPropertyName("usage")] ChatUsage? Usage);

    private sealed record ChatChoice(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("message")] ChatMessagePayload Message,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record ChatMessagePayload(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string? Content);

    private sealed record ChatUsage(
        [property: JsonPropertyName("prompt_tokens")] long PromptTokens,
        [property: JsonPropertyName("completion_tokens")] long CompletionTokens,
        [property: JsonPropertyName("total_tokens")] long TotalTokens);

    private sealed record ChatCompletionChunk(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("object")] string Object,
        [property: JsonPropertyName("created")] long Created,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChunkChoice> Choices);

    private sealed record ChatChunkChoice(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("delta")] ChatDelta Delta,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record ChatDelta(
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("content")] string? Content);
}
