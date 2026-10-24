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
/// Chat Completions. Microsoft.Extensions.AI.OpenAI has converters
/// (<c>AsOpenAIChatCompletion</c>, <c>AsOpenAIStreamingChatCompletionUpdatesAsync</c>,
/// OPENAI001), measured and not used (2026-10-19): the streaming one writes
/// <c>finish_reason: "stop"</c> on every chunk. The wire format is therefore
/// hand-produced here, and <see cref="ChatCompletionsMessageReader"/> reads the
/// request. The format is documented by OpenAI and stable.
/// </para>
/// <para>
/// Tool calls are <strong>invisible</strong> in the response: the tool loop
/// completes server-side inside Microsoft Agent Framework, and only the
/// resulting text is returned to the client. Use the management API's run
/// events to see tool details.
/// </para>
/// <para>
/// <strong>A tool that needs approval ends the request.</strong> The
/// answer to an approval is the input to the next turn, read from the session,
/// and this endpoint has none: nobody can ever answer it. The run closes
/// <see cref="RunStatus.Failed"/>, and the caller gets <c>409</c> with the error type
/// <c>approval_required</c>, or, on a stream that already answered <c>200</c>, an
/// error frame of that type and no <c>[DONE]</c>. Before, the caller got <c>200</c>
/// with empty content and the run waited for an approval forever.
/// </para>
/// </remarks>
internal static class OpenAIChatCompletionsEndpoints
{
    private const string ObjectCompletion = "chat.completion";
    private const string ObjectChunk = "chat.completion.chunk";

    /// <summary>Connects the Chat Completions endpoint.</summary>
    /// <param name="builder">Endpoint group.</param>
    /// <param name="roles">Resolved role policies.</param>
    /// <param name="prefix">The endpoint path prefix; an embedded file becomes an attachment reference under it.</param>
    /// <param name="idempotencyFilter">The filter that adds <c>Idempotency-Key</c> support.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles, string prefix, IdempotencyFilter idempotencyFilter)
    {
        builder.MapPost("/v1/chat/completions", (
                HttpContext httpContext,
                IAgentCatalog catalog,
                ITenantContext tenantContext,
                IAttachmentStore attachmentStore,
                AttachmentTypeGuard attachmentGuard,
                IAuditActorResolver actorResolver,
                [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                [FromServices] IRunAttributionContext? attributionContext,
                [FromServices] ITraconDrainState drainState,
                CancellationToken cancellationToken)
                => HandleAsync(
                    httpContext,
                    catalog,
                    tenantContext,
                    attachmentStore,
                    attachmentGuard,
                    actorResolver,
                    runAuthorizationHandler,
                    attributionContext,
                    drainState,
                    prefix,
                    cancellationToken))
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
                "streaming and the non-streaming path. A tool that needs approval ends the request: " +
                "this endpoint carries no session, so the approval can never be answered. The run " +
                "closes as Failed and the answer is 409 with the error type 'approval_required', or, " +
                "on a stream, an error frame of that type and no [DONE].")
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
            .Produces<OpenAICompatSupport.OpenAIErrorEnvelope>(StatusCodes.Status409Conflict)
            .Produces<OpenAICompatSupport.OpenAIErrorEnvelope>(StatusCodes.Status502BadGateway)
            .Produces<OpenAICompatSupport.OpenAIErrorEnvelope>(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IAgentCatalog catalog,
        ITenantContext tenantContext,
        IAttachmentStore attachmentStore,
        AttachmentTypeGuard attachmentGuard,
        IAuditActorResolver actorResolver,
        IRunAuthorizationHandler? runAuthorizationHandler,
        IRunAttributionContext? attributionContext,
        ITraconDrainState drainState,
        string prefix,
        CancellationToken cancellationToken)
    {
        // 🚨 Checked BEFORE anything else, like the agent run endpoint (F-431).
        if (OpenAICompatSupport.DrainRefusal(drainState) is { } drainRefusal)
        {
            return drainRefusal;
        }

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

        // F-581: a setting the agent definition owns is refused, never ignored;
        // /v1/responses refuses the same kind through MAF's request mapping.
        if (ChatCompletionsRequestSettings.FindRefused(body) is { Count: > 0 } refused)
        {
            return OpenAICompatSupport.Error(StatusCodes.Status400BadRequest, ChatCompletionsRequestSettings.Describe(refused));
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
                type: TraconErrorTypes.ModelNotFound);
        }

        // F-330: this protocol carries no parameter values; an agent whose
        // required parameter has no default would fail on its first step.
        if (agent.GetService<UnboundParameterGuard>() is { RequiresValues: true } unbound)
        {
            return OpenAICompatSupport.Error(StatusCodes.Status400BadRequest, unbound.Failure().Message);
        }

        // F-337: the attribution is checked before the run starts, as on the
        // agent run endpoint; this protocol's own error shape carries it.
        if (RunAttributionGate.Validate(attributionContext) is { } attributionError)
        {
            return OpenAICompatSupport.Error(StatusCodes.Status400BadRequest, attributionError, type: TraconErrorTypes.InvalidRunAttribution);
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
                type: TraconErrorTypes.RunNotAuthorized);
        }

        // F-580: a part is handed to the model or refused by name, never dropped.
        if (!ChatCompletionsMessageReader.TryRead(body, out var messages, out var error))
        {
            return OpenAICompatSupport.Error(StatusCodes.Status400BadRequest, error);
        }

        // 🚨 The same gate as /v1/responses (F-580 class scan): an embedded image,
        // audio clip or file passes the attachment type guard (size limit, type
        // sniffed from the bytes, allow list) and becomes an attachment reference
        // before the agent sees it. Without it this surface would hand the model
        // any bytes under any declared type. The endpoint is stateless, so the
        // attachment has no session; retention removes it as an orphan.
        if (await AttachmentIngestion.ReplaceEmbeddedDataAsync(
                messages,
                prefix,
                tenantContext.TenantId,
                sessionId: null,
                actorResolver.Resolve(),
                attachmentStore,
                attachmentGuard,
                cancellationToken).ConfigureAwait(false) is { } ingestionError)
        {
            return OpenAICompatSupport.Error(StatusCodes.Status400BadRequest, ingestionError);
        }

        var completionId = OpenAICompatSupport.CreateId("chatcmpl-");

        // Fixed here so a run that stops at an approval can be closed by its id (F-448).
        var runId = TraconId.NewId();

        if (OpenAICompatSupport.ReadStreamFlag(body))
        {
            return new ChatCompletionsStream(agent, messages, completionId, agentName, runId, tenantContext.TenantId);
        }

        try
        {
            // The session is deliberately not passed: Chat Completions is stateless.
            var response = await agent
                .RunAsync(messages, session: null, new TraconRunOptions { RunId = runId }, cancellationToken)
                .ConfigureAwait(false);

            if (ChildRunApproval.Describe(response.Messages) is { } pending)
            {
                await CloseUnanswerableRunAsync(httpContext, runId, tenantContext.TenantId).ConfigureAwait(false);

                return OpenAICompatSupport.Error(
                    StatusCodes.Status409Conflict,
                    ApprovalRequiredMessage(agentName, pending),
                    type: TraconErrorTypes.ApprovalRequired);
            }

            return Results.Json(
                new ChatCompletion(
                    completionId,
                    ObjectCompletion,
                    OpenAICompatSupport.UnixNow(),
                    agentName,
                    [new ChatChoice(
                        0,
                        new ChatMessagePayload("assistant", response.Text),
                        OpenAICompatSupport.ToChatFinishReason(response.FinishReason))],
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
                type: TraconErrorTypes.UpstreamError);
        }
    }

    /// <summary>The text a caller gets when the run stopped at a tool approval.</summary>
    /// <param name="agentName">The agent.</param>
    /// <param name="pending">The names of the tools that wait for approval.</param>
    /// <returns>The error message.</returns>
    private static string ApprovalRequiredMessage(string agentName, string pending)
        => $"Agent '{agentName}' asked for approval to call '{pending}'. The Chat Completions endpoint carries " +
           "no session, so the approval can never be answered, and the run was closed as failed. Run an agent " +
           "with tools that need approval through /v1/responses with a conversation, or through " +
           "POST /api/agents/{name}/run with a 'sessionId'.";

    /// <summary>Closes a run that stopped at an approval nobody can answer.</summary>
    /// <param name="httpContext">The request, for the run store and the logger.</param>
    /// <param name="runId">The run.</param>
    /// <param name="tenantId">The tenant the run belongs to.</param>
    /// <returns>A task that completes when the row is written or the failure is logged.</returns>
    /// <remarks>
    /// Not the request's token: a caller that disconnects right after the answer must not
    /// leave the run waiting for an approval forever.
    /// </remarks>
    private static ValueTask CloseUnanswerableRunAsync(HttpContext httpContext, Guid runId, string tenantId)
        => SessionlessApprovalRun.CloseAsFailedAsync(
            httpContext.RequestServices.GetService<IRunStore>(),
            runId,
            tenantId,
            DateTimeOffset.UtcNow,
            httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Tracon.OpenAICompat"),
            CancellationToken.None);

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
        string model,
        Guid runId,
        string tenantId) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            var cancellationToken = httpContext.RequestAborted;
            var writer = await SseWriter.StartAsync(httpContext.Response, cancellationToken).ConfigureAwait(false);
            // Stops the writer's keep-alive loop before the response ends (F-325).
            await using var writerScope = writer.ConfigureAwait(false);
            var created = OpenAICompatSupport.UnixNow();

            try
            {
                await WriteChunkAsync(writer, new ChatDelta("assistant", null), finishReason: null, created, cancellationToken)
                    .ConfigureAwait(false);

                // A harness loop hands over its final iteration only (K-889): the
                // client reads this stream as the answer, not as a draft forming.
                var updates = AgentAnswerStream.RunAsync(
                    agent,
                    messages,
                    session: null,
                    new TraconRunOptions { RunId = runId },
                    cancellationToken);
                List<string>? pending = null;

                // The LAST reported reason is the answer's: the tool loop streams a tool
                // turn's update with that turn's own reason before the next turn
                // (phase 207.4; the same rule the run's detector applies).
                ChatFinishReason? finishReason = null;

                await foreach (var update in updates.ConfigureAwait(false))
                {
                    if (ChildRunApproval.Describe(update.Contents) is { } names)
                    {
                        (pending ??= []).Add(names);
                    }

                    if (update.FinishReason is { } reported)
                    {
                        finishReason = reported;
                    }

                    if (update.Text is { Length: > 0 } text)
                    {
                        await WriteChunkAsync(writer, new ChatDelta(null, text), finishReason: null, created, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                // F-448: the stream already said 200, so the refusal is an error frame,
                // the same shape as a failure's, and the stream does not say it finished.
                if (pending is not null)
                {
                    await CloseUnanswerableRunAsync(httpContext, runId, tenantId).ConfigureAwait(false);

                    await WriteErrorAsync(
                        writer,
                        ApprovalRequiredMessage(model, string.Join(", ", pending.Distinct(StringComparer.Ordinal))),
                        TraconErrorTypes.ApprovalRequired,
                        cancellationToken).ConfigureAwait(false);

                    return;
                }

                await WriteChunkAsync(
                    writer,
                    new ChatDelta(null, null),
                    OpenAICompatSupport.ToChatFinishReason(finishReason),
                    created,
                    cancellationToken).ConfigureAwait(false);

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

                await WriteErrorAsync(writer, SafeErrorText.ForPersistence(ex, correlationId), TraconErrorTypes.UpstreamError, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        private static Task WriteErrorAsync(SseWriter writer, string message, string type, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(
                new ChatStreamError(new ChatStreamErrorBody(message, type)),
                OpenAICompatSupport.JsonOptions);

            return writer.WriteRawAsync($"data: {payload}\n\n", cancellationToken);
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
