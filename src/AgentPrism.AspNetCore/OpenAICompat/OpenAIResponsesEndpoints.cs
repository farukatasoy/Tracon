using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Run endpoint compatible with the OpenAI Responses API.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why isn't MAF's <c>MapOpenAIResponses()</c> endpoint used?</strong>
/// That endpoint does its storage through the <c>IConversationStorage</c>,
/// <c>IResponsesService</c>, and <c>IAgentConversationIndex</c> abstractions;
/// those interfaces are also <strong>internal</strong> inside
/// <c>Microsoft.Agents.AI.Hosting.OpenAI</c> (measured, 1.16.0-alpha.260730.1).
/// A consumer assembly cannot name these types, so no matter the registration
/// order it cannot replace MAF's in-memory implementations. Using that path
/// would silently lose all of the persistence, tenant isolation, audit trail,
/// and replay guarantees.
/// </para>
/// <para>
/// The package's <em>public</em> helper <see cref="OpenAIResponses"/> is used
/// instead: body parsing and OpenAI-shaped response generation belong to MAF,
/// agent resolution and persistence belong to AgentPrism. Because the wire
/// format comes from MAF, compatibility with stock OpenAI SDKs is preserved.
/// Rationale: <c>docs/KARARLAR.md</c>, decision K-036.
/// </para>
/// </remarks>
internal static class OpenAIResponsesEndpoints
{
    /// <summary>Connects the Responses endpoint.</summary>
    /// <param name="builder">Endpoint group.</param>
    /// <param name="sessionStore">Store to use for session persistence.</param>
    /// <param name="roles">Resolved role policies.</param>
    /// <param name="prefix">Path prefix to use for attachment references.</param>
    /// <param name="idempotencyFilter">Phase 43 — <c>Idempotency-Key</c> support.</param>
    public static void Map(
        IEndpointRouteBuilder builder,
        AgentSessionStore sessionStore,
        AgentPrismRolePolicies roles,
        string prefix,
        IdempotencyFilter idempotencyFilter)
    {
        builder.MapPost("/v1/responses", (
                HttpContext httpContext,
                IAgentCatalog catalog,
                ISessionStore sessions,
                ITenantContext tenantContext,
                IAttachmentStore attachmentStore,
                AttachmentTypeGuard attachmentGuard,
                IAuditActorResolver actorResolver,
                CancellationToken cancellationToken)
                => HandleAsync(
                    httpContext,
                    catalog,
                    sessionStore,
                    sessions,
                    tenantContext,
                    attachmentStore,
                    attachmentGuard,
                    actorResolver,
                    prefix,
                    cancellationToken))
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .AddEndpointFilter(idempotencyFilter)
            .WithName("AgentPrismOpenAIResponses")
            .WithTags("AgentPrism", "OpenAI")
            .WithSummary("Run endpoint compatible with the OpenAI Responses API.")
            .WithDescription(
                "The agent is selected from the 'model' field; if not found, 'metadata.entity_id' is tried. " +
                "If 'conversation' is given the session is stored under that identifier; if not, under the " +
                "generated response identifier, so chaining with 'previous_response_id' works.")
            // One of two shapes, depending on the 'stream' flag in the body:
            // a JSON body (raw JsonElement, the schema comes from MAF's
            // OpenAIResponses.WriteResponse and is not typed at compile time)
            // or SSE. For the same status code, a SECOND .Produces call
            // OVERWRITES the FIRST (measured); the two must be written in a
            // single call using additionalContentTypes.
            .Produces<JsonElement>(
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
        AgentSessionStore sessionStore,
        ISessionStore sessions,
        ITenantContext tenantContext,
        IAttachmentStore attachmentStore,
        AttachmentTypeGuard attachmentGuard,
        IAuditActorResolver actorResolver,
        string prefix,
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
                $"'metadata.{OpenAICompatSupport.EntityIdKey}'. " +
                await KnownAgentsAsync(catalog, cancellationToken).ConfigureAwait(false));
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
                $"There is no agent named '{agentName}'. " +
                await KnownAgentsAsync(catalog, cancellationToken).ConfigureAwait(false),
                type: "model_not_found");
        }

        OpenAIResponsesRunRequest runRequest;

        try
        {
            runRequest = OpenAIResponses.ToAgentRunRequest(body);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return OpenAICompatSupport.Error(StatusCodes.Status400BadRequest, $"Request could not be parsed: {ex.Message}");
        }

        var responseId = OpenAIResponses.CreateResponseId();

        // If a conversation identifier is given, the session is stored under
        // that identifier and stays fixed across turns. If not given, it is
        // stored under the new response identifier; the client chains the next
        // call with 'previous_response_id'.
        var saveId = runRequest.ConversationId ?? responseId;
        var loadId = OpenAIResponses.GetSessionStoreId(runRequest) ?? saveId;

        // 'conversation' and 'previous_response_id' are untrusted input;
        // tenant ownership is verified before loading.
        if (!await OpenAICompatSupport
                .IsOwnedByTenantAsync(sessions, tenantContext, loadId, cancellationToken)
                .ConfigureAwait(false))
        {
            return OpenAICompatSupport.Error(
                StatusCodes.Status404NotFound,
                $"'{loadId}' was not found.",
                type: "not_found_error");
        }

        // 'data:' URIs embedded in the body (image_url, input_file) have
        // already been converted to DataContent by MAF; before being sent to
        // the agent they are each converted into an attachment so the chat
        // history stays small (docs/14-COK-MODLULUK.md, 14.1).
        if (await AttachmentIngestion.ReplaceEmbeddedDataAsync(
                runRequest.Messages,
                prefix,
                tenantContext.TenantId,
                saveId,
                actorResolver.Resolve(),
                attachmentStore,
                attachmentGuard,
                cancellationToken).ConfigureAwait(false) is { } ingestionError)
        {
            return OpenAICompatSupport.Error(StatusCodes.Status400BadRequest, ingestionError);
        }

        var session = await sessionStore
            .GetSessionAsync(agent, loadId, cancellationToken).ConfigureAwait(false);

        if (OpenAICompatSupport.ReadStreamFlag(body))
        {
            return new ResponsesStream(agent, sessionStore, runRequest, session, responseId, saveId);
        }

        try
        {
            var response = await agent
                .RunAsync(runRequest.Messages, session, runRequest.Options, cancellationToken)
                .ConfigureAwait(false);

            await sessionStore.SaveSessionAsync(agent, saveId, session, cancellationToken).ConfigureAwait(false);

            var responseJson = OpenAIResponses.WriteResponse(response, responseId, runRequest.ConversationId);

            return Results.Json(
                AppendPendingApprovalOutputItems(responseJson, response.Messages),
                statusCode: StatusCodes.Status200OK);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 🚨 HATA-S2-003/HATA-S3-005: K-296's fix covered only the streaming
            // variant (ResponsesStream below); it MISSED this non-streaming
            // sibling path. A narrow 'when' filter (only AgentPrismException/
            // InvalidOperationException/HttpRequestException) would let real
            // provider SDK exceptions (e.g. Anthropic's AnthropicApiException
            // derives DIRECTLY from Exception, NOT from HttpRequestException)
            // slip through uncaught and leak into ASP.NET Core's generic
            // handler, producing a bare 500. Nothing goes uncaught here.
            return OpenAICompatSupport.Error(
                StatusCodes.Status502BadGateway,
                ex.Message,
                type: "upstream_error");
        }
    }

    private static async ValueTask<string> KnownAgentsAsync(IAgentCatalog catalog, CancellationToken cancellationToken)
    {
        var descriptors = await catalog.ListAsync(cancellationToken).ConfigureAwait(false);

        return descriptors.Count == 0
            ? "The catalog has no agents."
            : $"Registered agents: {string.Join(", ", descriptors.Select(static descriptor => descriptor.Name))}.";
    }

    /// <summary>
    /// Appends tool calls pending approval to the <c>output</c> array as
    /// <c>function_call</c> items.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🚨 HATA-S2-004/MT-COMPAT-029: <c>OpenAIResponses.WriteResponse</c> (MAF,
    /// alpha package) does NOT recognize a <c>ToolApprovalRequestContent</c> —
    /// its conversion table only handles <c>FunctionCallContent</c>/
    /// <c>FunctionResultContent</c>/known text-like content (verified by
    /// decompiling <c>AgentResponseExtensions.ToItemContent</c>). A call
    /// pending approval was therefore SILENTLY dropped from <c>output</c>; all
    /// the caller saw was an empty array and <c>status: "completed"</c> — it
    /// had NO idea the call even existed. <c>Response</c>/
    /// <c>FunctionToolCallItemResource</c> are <c>internal</c> inside MAF, so
    /// a strongly-typed fix cannot be written; instead the already-produced
    /// JSON is patched to be BYTE-FOR-BYTE identical to the real OpenAI
    /// Responses API's documented <c>function_call</c> item schema
    /// (id/type/status/call_id/name/arguments). This is also the exact shape
    /// MAF produces for a NORMAL (non-approval) tool call — indistinguishable
    /// from the SDK's point of view from an ordinary pending function call,
    /// which is the correct behavior: the caller learns that the call exists.
    /// </para>
    /// <para>
    /// 🚨 <strong>The caller cannot answer that call over this endpoint.</strong>
    /// Measured on 2026-08-18 against <c>Microsoft.Agents.AI.Hosting.OpenAI</c>
    /// 1.16.0-alpha.260730.1: <see cref="OpenAIResponses.ToAgentRunRequest"/>
    /// deserializes <em>every</em> item of the <c>input</c> array into its
    /// internal <c>Responses.Models.InputMessage</c>, which declares <c>role</c>
    /// and <c>content</c> as required. There is no polymorphic dispatch on the
    /// item's <c>type</c>, so a <c>function_call_output</c> (and a
    /// <c>function_call</c>) item fails with
    /// <c>JsonException: ... was missing required properties including: 'role',
    /// 'content'</c>, which this endpoint answers with <c>400</c>. The tool-call
    /// round trip therefore exists on the <em>output</em> side only. To answer a
    /// pending call, use the management approval API
    /// (<c>POST /api/approvals/{id}/decide</c>, Phase 55).
    /// </para>
    /// <para>
    /// The <c>status</c> field is <em>not changed</em> (still
    /// <c>"completed"</c>) — this is consistent with the value MAF uses for
    /// EVERY function_call item (whether the tool ACTUALLY ran or not), and
    /// the real OpenAI Responses API represents a pending function call this
    /// way too, not as "requires_action". Only the <c>output</c> array becomes
    /// complete.
    /// </para>
    /// </remarks>
    private static JsonElement AppendPendingApprovalOutputItems(JsonElement responseJson, IEnumerable<ChatMessage> messages)
    {
        List<FunctionCallContent>? pending = null;

        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is ToolApprovalRequestContent { ToolCall: FunctionCallContent call })
                {
                    (pending ??= []).Add(call);
                }
            }
        }

        if (pending is null)
        {
            return responseJson;
        }

        var root = JsonNode.Parse(responseJson.GetRawText())!.AsObject();
        var output = root["output"]?.AsArray() ?? [];
        root["output"] = output;

        foreach (var call in pending)
        {
            output.Add(new JsonObject
            {
                ["id"] = $"fc_{Guid.NewGuid():N}",
                ["type"] = "function_call",
                ["status"] = "completed",
                ["call_id"] = call.CallId,
                ["name"] = call.Name,
                ["arguments"] = JsonSerializer.Serialize(call.Arguments, OpenAICompatSupport.JsonOptions),
            });
        }

        return JsonSerializer.SerializeToElement(root);
    }

    /// <summary>
    /// Writes the streaming response.
    /// </summary>
    /// <remarks>
    /// Frames are already produced in the full SSE format by
    /// <see cref="OpenAIResponses.WriteResponseStreamAsync"/> (<c>event:</c> +
    /// <c>data:</c> + a blank line); re-framing them would cause corruption,
    /// so they are written as is.
    /// </remarks>
    private sealed class ResponsesStream(
        AIAgent agent,
        AgentSessionStore sessionStore,
        OpenAIResponsesRunRequest request,
        AgentSession session,
        string responseId,
        string saveId) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            var cancellationToken = httpContext.RequestAborted;
            var writer = await SseWriter.StartAsync(httpContext.Response, cancellationToken).ConfigureAwait(false);

            try
            {
                var updates = agent.RunStreamingAsync(
                    request.Messages,
                    session,
                    request.Options,
                    cancellationToken);

                var frames = OpenAIResponses.WriteResponseStreamAsync(
                    updates,
                    responseId,
                    request.ConversationId,
                    cancellationToken);

                await foreach (var frame in frames.ConfigureAwait(false))
                {
                    await writer.WriteRawAsync(frame, cancellationToken).ConfigureAwait(false);
                }

                await sessionStore.SaveSessionAsync(agent, saveId, session, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The client disconnected.
            }
            catch (Exception ex)
            {
                // 🚨 K-296 (see AgentEndpoints.ExecuteStreamingAsync): a narrow
                // exception filter would let real provider SDK exceptions slip
                // through and close the connection WITHOUT producing an 'error'
                // frame. Here EVERY exception turns into a frame.
                var payload = JsonSerializer.Serialize(
                    new ResponsesStreamError("error", ex.Message),
                    OpenAICompatSupport.JsonOptions);

                await writer.WriteRawAsync($"event: error\ndata: {payload}\n\n", CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        private sealed record ResponsesStreamError(string Type, string Message);
    }
}
