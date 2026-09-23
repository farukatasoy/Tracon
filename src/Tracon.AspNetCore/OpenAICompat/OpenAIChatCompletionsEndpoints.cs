using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Run endpoint compatible with the OpenAI Chat Completions API.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This endpoint is stateless.</strong> In the Chat Completions
/// contract the client carries the conversation history: every request sends
/// the full message list. No session is therefore opened and the chat history
/// provider does not get involved; otherwise history would be managed twice
/// and messages would appear duplicated.
/// </para>
/// <para>
/// The <c>Microsoft.Agents.AI.Hosting.OpenAI</c> package offers a public
/// writer helper (<c>OpenAIResponses</c>) for the Responses API, but not for
/// Chat Completions — every model type on that path is internal. The wire
/// format is therefore hand-produced here. The format is documented by OpenAI
/// and stable.
/// </para>
/// <para>
/// Tool calls are <strong>invisible</strong> in the response: the tool loop
/// completes server-side inside Microsoft Agent Framework, and only the
/// resulting text is returned to the client. Use the management API's run
/// events to see tool details.
/// </para>
/// </remarks>
internal static class OpenAIChatCompletionsEndpoints
{
    private const string ObjectCompletion = "chat.completion";
    private const string ObjectChunk = "chat.completion.chunk";

    /// <summary>Connects the Chat Completions endpoint.</summary>
    /// <param name="builder">Endpoint group.</param>
    /// <param name="roles">Resolved role policies.</param>
    /// <param name="idempotencyFilter">The filter that adds <c>Idempotency-Key</c> support.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles, IdempotencyFilter idempotencyFilter)
    {
        builder.MapPost("/v1/chat/completions", HandleAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .AddEndpointFilter(idempotencyFilter)
            .WithName("TraconOpenAIChatCompletions")
            .WithTags("Tracon", "OpenAI")
            .WithSummary("Run endpoint compatible with the OpenAI Chat Completions API.")
            // 🚨 JsonElement, not object (Phase 159, MEASURED): an `object` body
            // generates `object body` on the typed client, and the client
            // serializes through a source-generated JsonSerializerContext - so
            // any natural caller input (an anonymous type, a POCO) throws
            // NotSupportedException at run time, because only the registered
            // root types resolve. JsonElement IS one of them, and it is what a
            // caller of an OpenAI-shaped endpoint holds anyway. The document is
            // unchanged in meaning: JsonElement's own schema component is the
            // same empty "any JSON" shape an `object` body produced inline.
            .Accepts<JsonElement>("application/json")
            .WithDescription(
                "Stateless: the client carries history. The agent is selected from the " +
                "'model' field; if not found, 'metadata.entity_id' is tried. If a registered " +
                "IRunAuthorizationHandler denies the caller, the response is 403 on BOTH the " +
                "streaming and the non-streaming path.")
            // One of two shapes, depending on the 'stream' flag in the body.
            // For the same status code, a SECOND .Produces call OVERWRITES the
            // FIRST (measured); the two must be written in a single call using
            // additionalContentTypes. On the streaming path the real body is a
            // ChatCompletionChunk; the schema here is approximated to
            // ChatCompletion (ASP.NET Core's metadata model cannot express two
            // different types for the same status).
            .Produces<ChatCompletion>(
                StatusCodes.Status200OK,
                contentType: "application/json",
                additionalContentTypes: ["text/event-stream"])
            .Produces<OpenAICompatSupport.OpenAIErrorEnvelope>(StatusCodes.Status400BadRequest)
            .Produces<OpenAICompatSupport.OpenAIErrorEnvelope>(StatusCodes.Status403Forbidden)
            .Produces<OpenAICompatSupport.OpenAIErrorEnvelope>(StatusCodes.Status404NotFound)
            .Produces<OpenAICompatSupport.OpenAIErrorEnvelope>(StatusCodes.Status502BadGateway);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IAgentCatalog catalog,
        ITenantContext tenantContext,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
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
            return OpenAICompatSupport.Error(StatusCodes.Status400BadRequest, $"Body could not be parsed: {ex.Message}");
        }

        var agentName = OpenAICompatSupport.ReadAgentName(body);

        if (string.IsNullOrEmpty(agentName))
        {
            return OpenAICompatSupport.Error(
                StatusCodes.Status400BadRequest,
                "No agent selected. Put the agent name in the 'model' field, or use " +
                $"'metadata.{OpenAICompatSupport.EntityIdKey}'.");
        }

        AIAgent? agent;

        try
        {
            agent = await catalog.ResolveAsync(agentName, culture: null, cancellationToken).ConfigureAwait(false);
        }
        catch (TraconException ex)
        {
            return OpenAICompatSupport.Error(StatusCodes.Status400BadRequest, ex.Message);
        }

        if (agent is null)
        {
            return OpenAICompatSupport.Error(
                StatusCodes.Status404NotFound,
                $"There is no agent named '{agentName}'.",
                type: "model_not_found");
        }

        // 🚨 This is the SIXTH run-starting surface and phase 139 missed it:
        // the endpoint resolves an agent from the catalog and calls RunAsync /
        // RunStreamingAsync a few lines below. The gate is asked BEFORE the
        // streaming branch, so both the streaming and the non-streaming path
        // are covered by this one call - a check inside either branch would
        // leave the other one open (phase 147, F-195).
        //
        // Chat Completions is stateless, so there is no session to name.
        if (await RunAuthorizationGate
                .CheckRunAsync(runAuthorizationHandler, tenantContext, agentName, sessionId: null, attributionContext, httpContext, cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return OpenAICompatSupport.Error(
                StatusCodes.Status403Forbidden,
                authorizationProblem.ProblemDetails.Detail ?? "The registered IRunAuthorizationHandler denied this run.",
                type: "run_not_authorized");
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
            // The session is deliberately not passed: Chat Completions is stateless.
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
        // 🚨 A provider timeout arrives as an OperationCanceledException while
        // nobody cancelled anything (HttpClient's own deadline). Without this
        // filter it skipped the 502 mapping below and the caller got a bare,
        // empty success. Same class as AgentEndpoints; Phase 157.
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 🚨 HATA-S2-003/HATA-S3-005: K-296's fix covered only the streaming
            // variant (ChatCompletionsStream below); it MISSED this non-streaming
            // sibling path. A narrow 'when' filter (only TraconException/
            // InvalidOperationException/HttpRequestException) would let real
            // provider SDK exceptions slip through uncaught and leak into
            // ASP.NET Core's generic handler, producing a bare 500. Nothing goes
            // uncaught here.
            var correlationId = SafeErrorText.NewCorrelationId();

            httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Tracon.OpenAICompat")
                .LogError(ex, "Chat Completions run for agent '{AgentName}' failed. (ref: {CorrelationId})", agentName, correlationId);

            return OpenAICompatSupport.Error(
                StatusCodes.Status502BadGateway,
                SafeErrorText.ForPersistence(ex, correlationId),
                type: "upstream_error");
        }
    }

    /// <summary>
    /// Converts the <c>messages</c> array into a <see cref="ChatMessage"/> list.
    /// </summary>
    /// <remarks>
    /// Content can be either plain text or an array of
    /// <c>{"type":"text","text":"..."}</c> parts; OpenAI SDKs produce both.
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
            error = "'messages' is required and must be an array.";
            return false;
        }

        foreach (var item in raw.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.Object ||
                !item.TryGetProperty("role", out var roleElement) ||
                roleElement.ValueKind is not JsonValueKind.String)
            {
                error = "Every message must have a 'role' field.";
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
            error = "'messages' cannot be empty.";
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
        // OpenAI uses the "developer" role as the new name replacing "system".
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

    /// <summary>Writes the streaming Chat Completions response.</summary>
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

                // The OpenAI stream ends with this fixed marker; SDKs expect it.
                await writer.WriteRawAsync("data: [DONE]\n\n", cancellationToken).ConfigureAwait(false);
            }
            // Same filter, same reason as the non-streaming branch above.
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                // The client disconnected.
            }
            catch (Exception ex)
            {
                // 🚨 K-296 (see AgentEndpoints.ExecuteStreamingAsync): a narrow
                // exception filter would let real provider SDK exceptions slip
                // through and close the connection WITHOUT producing an 'error'
                // frame. Here EVERY exception turns into a frame.
                var correlationId = SafeErrorText.NewCorrelationId();

                httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Tracon.OpenAICompat")
                    .LogError(ex, "Streaming Chat Completions run for agent '{AgentName}' failed. (ref: {CorrelationId})", model, correlationId);

                var payload = JsonSerializer.Serialize(
                    new ChatStreamError(new ChatStreamErrorBody(SafeErrorText.ForPersistence(ex, correlationId), "upstream_error")),
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
