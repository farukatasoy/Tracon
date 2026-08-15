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
/// OpenAI Responses API ile uyumlu calistirma ucu.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Neden MAF'in <c>MapOpenAIResponses()</c> ucu kullanilmiyor?</strong>
/// O uc, depolamayi <c>IConversationStorage</c>, <c>IResponsesService</c> ve
/// <c>IAgentConversationIndex</c> aracilariyla yapar; bu uc arayuz de
/// <c>Microsoft.Agents.AI.Hosting.OpenAI</c> icinde <strong>internal</strong>'dir
/// (olculdu, 1.16.0-alpha.260730.1). Tuketici bir derleme bu tipleri adlandiramaz,
/// dolayisiyla kayit sirasi ne olursa olsun MAF'in bellek ici uygulamalarinin
/// yerine gecemez. O yolu kullanmak kalicilik, kiraci yalitimi, denetim izi ve
/// yeniden oynatma vaatlerinin tumunu sessizce kaybettirirdi.
/// </para>
/// <para>
/// Bunun yerine paketin <em>public</em> yardimcisi <see cref="OpenAIResponses"/>
/// kullanilir: govde cozumleme ve OpenAI bicimli yanit uretimi MAF'a, agent
/// cozumleme ve kalicilik AgentPrism'e aittir. Kablo bicimi MAF'tan geldigi icin
/// stok OpenAI SDK'lari ile uyum korunur.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-036.
/// </para>
/// </remarks>
internal static class OpenAIResponsesEndpoints
{
    /// <summary>Responses ucunu baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="sessionStore">Oturum kaliciligi icin kullanilacak depo.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    /// <param name="prefix">Ek referanslari icin kullanilacak yol oneki.</param>
    /// <param name="idempotencyFilter">Faz 43 — <c>Idempotency-Key</c> destegi.</param>
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
            .WithSummary("OpenAI Responses API ile uyumlu calistirma ucu.")
            .WithDescription(
                "Agent, 'model' alanindan secilir; bulunamazsa 'metadata.entity_id' denenir. " +
                "'conversation' verilirse oturum o kimlikle, verilmezse uretilen yanit kimligiyle " +
                "saklanir; boylece 'previous_response_id' ile zincirleme calisir.")
            // Govdedeki 'stream' bayragina gore ikisinden biri: JSON govde (ham
            // JsonElement, sema MAF'in OpenAIResponses.WriteResponse'undan gelir
            // ve derleme zamaninda tipli degildir) veya SSE. Ayni statu kodu icin
            // IKINCI bir .Produces cagrisi BIRINCIYI EZER (olculdu); ikisi tek
            // cagriya additionalContentTypes ile yazilmalidir.
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

        // Konusma kimligi verilmisse oturum o kimlikle saklanir ve turlar arasinda
        // sabit kalir. Verilmemisse yeni yanit kimligiyle saklanir; istemci bir
        // sonraki cagriyi 'previous_response_id' ile zincirler.
        var saveId = runRequest.ConversationId ?? responseId;
        var loadId = OpenAIResponses.GetSessionStoreId(runRequest) ?? saveId;

        // 'conversation' ve 'previous_response_id' guvenilmez girdidir; yuklemeden
        // once kiraci sahipligi dogrulanir.
        if (!await OpenAICompatSupport
                .IsOwnedByTenantAsync(sessions, tenantContext, loadId, cancellationToken)
                .ConfigureAwait(false))
        {
            return OpenAICompatSupport.Error(
                StatusCodes.Status404NotFound,
                $"'{loadId}' was not found.",
                type: "not_found_error");
        }

        // Govdeye gomulu 'data:' URI'leri (image_url, input_file) MAF tarafindan
        // zaten DataContent'e cevrilmistir; agent'a gonderilmeden once birer ege
        // donusturulur ki sohbet gecmisi kucuk kalsin (docs/14-COK-MODLULUK.md, 14.1).
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
            // 🚨 HATA-S2-003/HATA-S3-005: K-296'nin duzeltmesi yalniz akisli
            // varyantlari (ResponsesStream asagida) kapsamis, bu akissiz kardes
            // yolu KACIRMIS. Dar bir 'when' filtresi (yalniz AgentPrismException/
            // InvalidOperationException/HttpRequestException) gercek saglayici SDK
            // istisnalarini (orn. Anthropic'in AnthropicApiException'i
            // Exception'dan DOGRUDAN turer, HttpRequestException'dan TUREMEZ)
            // yakalamadan kacirir ve ASP.NET Core'un genel isleyicisine sizip ciplak
            // 500 uretirdi. Burada yakalanmayan HICBIR sey yoktur.
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
    /// Onay bekleyen tool cagrilarini <c>output</c> dizisine <c>function_call</c>
    /// ogeleri olarak ekler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🚨 HATA-S2-004/MT-COMPAT-029: <c>OpenAIResponses.WriteResponse</c> (MAF,
    /// alpha paket) bir <c>ToolApprovalRequestContent</c>'i taniMAZ — donusum
    /// tablosu yalniz <c>FunctionCallContent</c>/<c>FunctionResultContent</c>/
    /// bilinen metin-benzeri icerikleri isler (decompile ile dogrulandi,
    /// <c>AgentResponseExtensions.ToItemContent</c>). Onay bekleyen bir cagri bu
    /// yuzden <c>output</c>'tan SESSIZCE dusuyordu; caller'in gordugu tek sey
    /// bos bir dizi ve <c>status: "completed"</c> — cagrinin var oldugunu HIC
    /// bilmiyordu. <c>Response</c>/<c>FunctionToolCallItemResource</c> MAF
    /// icinde <c>internal</c>'dir, guclu tipli bir cozum yazilamaz; bu yuzden
    /// zaten uretilmis JSON, gercek OpenAI Responses API'nin belgelenmis
    /// <c>function_call</c> oge semasiyla (id/type/status/call_id/name/arguments)
    /// BIREBIR ayni sekilde yama uygulanir. Bu, MAF'in NORMAL (onay istemeyen)
    /// bir tool cagrisi icin urettigi ogeyle de ayni bicimdir — SDK acisindan
    /// sIradan bir bekleyen fonksiyon cagrisindan ayirt edilemez, ki dogru
    /// olan da budur: caller standart OpenAI akisini izleyip bir sonraki turda
    /// <c>function_call_output</c> saglayabilir (ya da yonetim API'sine gidip
    /// resmi onay akisini kullanabilir).
    /// </para>
    /// <para>
    /// <c>status</c> alani <em>degistirilmez</em> (hala <c>"completed"</c>) —
    /// bu, MAF'in HER function_call ogesi icin kullandigi degerle tutarlidir
    /// (tool GERCEKTEN calismis olsun ya da olmasin) ve gercek OpenAI Responses
    /// API'sinde de fonksiyon cagirma "requires_action" degil boyle temsil
    /// edilir. Yalniz <c>output</c> dizisi eksiksiz hale gelir.
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
    /// Akisli yaniti yazar.
    /// </summary>
    /// <remarks>
    /// Cerceveler <see cref="OpenAIResponses.WriteResponseStreamAsync"/> tarafindan
    /// zaten tam SSE bicimiyle uretilir (<c>event:</c> + <c>data:</c> + bos satir);
    /// yeniden cerceveleme bozulmaya yol acardi, bu yuzden oldugu gibi yazilir.
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
                // Istemci baglantiyi kesti.
            }
            catch (Exception ex)
            {
                // 🚨 K-296 (bkz. AgentEndpoints.ExecuteStreamingAsync): dar bir istisna
                // filtresi gercek saglayici SDK istisnalarini kacirip baglantiyi 'error'
                // cercevesi UretMEDEN kapatirdi. Burada HER istisna bir cerceveye donusur.
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
