using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
            return OpenAICompatSupport.Error(StatusCodes.Status400BadRequest, $"Govde cozumlenemedi: {ex.Message}");
        }

        var agentName = OpenAICompatSupport.ReadAgentName(body);

        if (string.IsNullOrEmpty(agentName))
        {
            return OpenAICompatSupport.Error(
                StatusCodes.Status400BadRequest,
                "Agent secilmedi. 'model' alanina agent adini yazin veya " +
                $"'metadata.{OpenAICompatSupport.EntityIdKey}' kullanin. " +
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
                $"'{agentName}' adinda bir agent yok. " +
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
            return OpenAICompatSupport.Error(StatusCodes.Status400BadRequest, $"Istek cozumlenemedi: {ex.Message}");
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
                $"'{loadId}' bulunamadi.",
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

            return Results.Json(
                OpenAIResponses.WriteResponse(response, responseId, runRequest.ConversationId),
                statusCode: StatusCodes.Status200OK);
        }
        catch (Exception ex) when (ex is AgentPrismException or InvalidOperationException or HttpRequestException)
        {
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
            ? "Katalogda hic agent yok."
            : $"Kayitli agent'lar: {string.Join(", ", descriptors.Select(static descriptor => descriptor.Name))}.";
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
