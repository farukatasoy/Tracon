using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Agent katalogu ve tanim yonetimi uclari.
/// </summary>
/// <remarks>
/// Kodda tanimli agent'lar salt okunurdur. Ad cakismasinda kod kazanir
/// (karar K-003); veritabanina yazilan ayni adli bir tanim hicbir zaman
/// cozulmezdi. Bu yuzden yazma uclari boyle bir istegi sessizce kabul etmek
/// yerine <c>409 Conflict</c> dondurur.
/// </remarks>
internal static class AgentEndpoints
{
    /// <summary>Agent uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    /// <param name="prefix">
    /// Ek referanslarini kurarken kullanilacak yol oneki (bkz. <see cref="AttachmentUriReference"/>).
    /// </param>
    /// <param name="idempotencyFilter">Faz 43 — yalniz <c>/api/agents/{name}/run</c> ucuna eklenir.</param>
    public static void Map(
        IEndpointRouteBuilder builder,
        AgentPrismRolePolicies roles,
        string prefix,
        IdempotencyFilter idempotencyFilter)
    {
        builder.MapGet("/api/agents", async Task<Ok<IReadOnlyList<AgentDescriptor>>> (
                IAgentCatalog catalog,
                CancellationToken cancellationToken)
                => TypedResults.Ok(await catalog.ListAsync(cancellationToken).ConfigureAwait(false)))
            .RequireRole(roles.Reader)
            .WithName("AgentPrismListAgents")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Kodda ve veritabaninda tanimli tum agent'lari listeler.");

        builder.MapGet("/api/agents/{name}", GetAgentAsync)
            .RequireRole(roles.Reader)
            .WithName("AgentPrismGetAgent")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Bir agent'in katalog ozetini ve varsa kalici tanimini dondurur.");

        builder.MapPost("/api/agents", CreateAgentAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismCreateAgent")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Yeni bir agent tanimi olusturur.");

        builder.MapPost("/api/agents/validate", ValidateAgentAsync)
            .RequireRole(roles.Operator)
            .WithName("AgentPrismValidateAgent")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Bir tanimi kaydetmeden ve hicbir model cagirmadan derler.");

        builder.MapPut("/api/agents/{name}", UpdateAgentAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismUpdateAgent")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Bir agent tanimini gunceller ve yeni bir surum uretir.");

        builder.MapDelete("/api/agents/{name}", DeleteAgentAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismDeleteAgent")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Bir agent tanimini ve surum gecmisini siler.");

        builder.MapGet("/api/agents/{name}/versions", ListVersionsAsync)
            .RequireRole(roles.Reader)
            .WithName("AgentPrismListAgentVersions")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Bir tanimin surum gecmisini yeniden eskiye listeler.");

        builder.MapPost("/api/agents/{name}/rollback", RollbackAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismRollbackAgent")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Bir tanimi onceki bir surumun icerigiyle yeni surum olarak yazar.");

        builder.MapGet("/api/agents/{name}/versions/{a:int}/diff/{b:int}", GetVersionDiffAsync)
            .RequireRole(roles.Reader)
            .WithName("AgentPrismGetAgentVersionDiff")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Iki tanim surumunu ham JSON olarak dondurur; diff hesabi arayuzde yapilir.");

        builder.MapPost("/api/agents/{name}/run", async (
                string name,
                AgentRunRequest request,
                IAgentCatalog catalog,
                AgentSessionManager sessions,
                IAttachmentStore attachmentStore,
                ITenantContext tenantContext,
                ExperimentAssignmentResolver experimentAssignment,
                [FromServices] QuotaEnforcer? quotaEnforcer,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                // 🚨 Kota denetimi calistirma BASLAMADAN once yapilir. Devam eden
                // bir calistirma kota asilinca kesilmez (K-162); yalnizca yeni
                // calistirma 429 alir.
                if (await QuotaGate
                        .CheckAsync(quotaEnforcer, tenantContext, name, httpContext, cancellationToken)
                        .ConfigureAwait(false) is { } quotaProblem)
                {
                    return quotaProblem;
                }

                return await RunAsync(
                    name,
                    request,
                    catalog,
                    sessions,
                    attachmentStore,
                    tenantContext,
                    experimentAssignment,
                    prefix,
                    httpContext,
                    cancellationToken).ConfigureAwait(false);
            })
            .RequireRole(roles.Operator)
            .AddEndpointFilter(idempotencyFilter)
            .WithName("AgentPrismRunAgent")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Bir agent'i deneme amaciyla calistirir ve yaniti SSE ile akitir.")
            .WithDescription(
                "Kota asilmissa calistirma baslamaz ve 429 doner; ProblemDetails hangi kotanin " +
                "asildigini ve sayacin ne zaman sifirlanacagini tasir. 'Idempotency-Key' basligi " +
                "tasiyan bir istek SSE yerine tek bir JSON yanitla (akissiz) calisir — Faz 43'un " +
                "tekillestirme sozlesmesi akissiz bir yanit gerektirir (docs/43-IDEMPOTENCY-KEY.md).")
            // Basari yaniti varsayilan olarak SSE'dir (bkz. AgentRunStream); ama
            // 'Idempotency-Key' basligi tasiyan bir istek JSON govde alir.
            .Produces<string>(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<Results<Ok<AgentVersionDiffResponse>, ProblemHttpResult>> GetVersionDiffAsync(
        string name,
        int a,
        int b,
        IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        var left = await definitions.GetVersionAsync(name, a, cancellationToken).ConfigureAwait(false);
        var right = await definitions.GetVersionAsync(name, b, cancellationToken).ConfigureAwait(false);

        if (left is null || right is null)
        {
            return TypedResults.Problem(
                title: "Surum bulunamadi",
                detail: $"'{name}' agent'inin {(left is null ? a : b)} numarali surumu yok.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(new AgentVersionDiffResponse { Left = left, Right = right });
    }

    private static async Task<Results<Ok<AgentDetailResponse>, ProblemHttpResult>> GetAgentAsync(
        string name,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        var descriptor = await FindDescriptorAsync(catalog, name, cancellationToken).ConfigureAwait(false);

        if (descriptor is null)
        {
            return NotFound(name);
        }

        var definition = await definitions.GetAsync(name, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new AgentDetailResponse
        {
            Descriptor = descriptor,
            Definition = definition,
            IsEditable = descriptor.Origin == AgentDefinitionOrigin.Database,
        });
    }

    private static async Task<Results<Created<AgentDefinition>, ProblemHttpResult>> CreateAgentAsync(
        AgentDefinitionRequest request,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        if (await ValidateCallGraphAsync(catalog, request, cancellationToken).ConfigureAwait(false) is { } cycle)
        {
            return cycle;
        }

        if (await FindDescriptorAsync(catalog, request.Name, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return TypedResults.Problem(
                title: "Agent adi kullanimda",
                detail: existing.Origin == AgentDefinitionOrigin.Code
                    ? $"'{request.Name}' kodda tanimli bir agent'tir ve yonetim API'sinden degistirilemez. " +
                      "Ad cakismasinda kod kazandigi icin ayni adla yazilan bir tanim hicbir zaman cozulmezdi."
                    : $"'{request.Name}' adinda bir tanim zaten var. Guncellemek icin PUT kullanin.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var saved = await definitions
            .SaveAsync(request.ToDefinition(), cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"{httpContext.Request.Path}/{Uri.EscapeDataString(saved.Name)}", saved);
    }

    /// <summary>
    /// Bir tanimi kaydetmeden ve hicbir model cagirmadan derler.
    /// </summary>
    /// <remarks>
    /// Dogrulama basarisizligi bir HTTP hatasi degildir: istek gecerliyse yanit
    /// her zaman <c>200</c>'dur, sonuc <see cref="AgentValidationReport.Valid"/>
    /// alaninda tasinir. Yalnizca govde ayristirilamiyorsa (bu ucun kendi ismi/model
    /// alani denetimi) <c>400</c> donulur — bu, agin hatasiyla dogrulama hatasini
    /// ayirt etmek isteyen bir CI'in karsilastigi tek gercek istek hatasidir.
    /// </remarks>
    private static async Task<Results<Ok<AgentValidationReport>, ProblemHttpResult>> ValidateAgentAsync(
        AgentDefinitionRequest request,
        AgentDefinitionValidator validator,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var report = await validator
            .ValidateAsync(request.ToDefinition(), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(report);
    }

    private static async Task<Results<Ok<AgentDefinition>, ProblemHttpResult>> UpdateAgentAsync(
        string name,
        AgentDefinitionRequest request,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(name, request.Name, StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                title: "Ad uyusmuyor",
                detail: $"Yoldaki ad '{name}', govdedeki ad '{request.Name}'. Agent adi degistirilemez; " +
                        "yeni bir ad icin yeni bir tanim olusturun.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        if (await GuardCodeAgentAsync(catalog, name, cancellationToken).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        if (await ValidateCallGraphAsync(catalog, request, cancellationToken).ConfigureAwait(false) is { } cycle)
        {
            return cycle;
        }

        if (await definitions.GetAsync(name, cancellationToken).ConfigureAwait(false) is null)
        {
            return NotFound(name);
        }

        var saved = await definitions
            .SaveAsync(request.ToDefinition(), cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(saved);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAgentAsync(
        string name,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        if (await GuardCodeAgentAsync(catalog, name, cancellationToken).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        return await definitions.DeleteAsync(name, cancellationToken).ConfigureAwait(false)
            ? TypedResults.NoContent()
            : NotFound(name);
    }

    private static async Task<Results<Ok<IReadOnlyList<AgentDefinition>>, ProblemHttpResult>> ListVersionsAsync(
        string name,
        IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        if (await definitions.GetAsync(name, cancellationToken).ConfigureAwait(false) is null)
        {
            return NotFound(name);
        }

        return TypedResults.Ok(
            await definitions.ListVersionsAsync(name, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Results<Ok<AgentDefinition>, ProblemHttpResult>> RollbackAsync(
        string name,
        AgentRollbackRequest request,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        if (await GuardCodeAgentAsync(catalog, name, cancellationToken).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        try
        {
            return TypedResults.Ok(
                await definitions.RollbackAsync(name, request.Version, cancellationToken).ConfigureAwait(false));
        }
        catch (AgentPrismException ex)
        {
            return TypedResults.Problem(
                title: "Geri alinamadi",
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound);
        }
    }

    private static async Task<IResult> RunAsync(
        string name,
        AgentRunRequest request,
        IAgentCatalog catalog,
        AgentSessionManager sessions,
        IAttachmentStore attachmentStore,
        ITenantContext tenantContext,
        ExperimentAssignmentResolver experimentAssignment,
        string prefix,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Onay kararlari tek basina gecerli bir istektir: kullanici bekleyen bir
        // tool cagrisini onaylarken yeni bir mesaj yazmaz.
        if (string.IsNullOrWhiteSpace(request.Message) && request.Approvals.Count == 0 && request.AttachmentIds.Count == 0)
        {
            return Results.Problem(
                title: "Istek bos",
                detail: "'message', 'attachmentIds' veya 'approvals' alanlarindan biri zorunludur.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Approvals.Count > 0 && string.IsNullOrWhiteSpace(request.SessionId))
        {
            return Results.Problem(
                title: "Onay icin oturum gerekli",
                detail: "Bekleyen onay istegi oturum gecmisinde yasar; 'sessionId' zorunludur.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Ek sahipligi akis baslamadan once dogrulanir: yanit basladiktan sonra
        // duzgun bir ProblemDetails donduremeyiz.
        var attachments = new List<AttachmentDescriptor>(request.AttachmentIds.Count);

        foreach (var attachmentId in request.AttachmentIds)
        {
            if (await attachmentStore.GetAsync(tenantContext.TenantId, attachmentId, cancellationToken)
                    .ConfigureAwait(false) is not { } descriptor)
            {
                return Results.Problem(
                    title: "Ek bulunamadi",
                    detail: $"'{attachmentId}' kimlikli bir ek yok veya bu kiraciya ait degil.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            attachments.Add(descriptor);
        }

        // Kimlik burada uretilir (AgentRunStream.ExecuteAsync icinde degil): deney
        // atama anahtari (oturum kimligi ?? calistirma kimligi) akis baslamadan
        // once bilinmelidir.
        var runId = AgentPrismId.NewId();
        var assignmentKey = request.SessionId ?? runId.ToString("D");

        var assignment = await experimentAssignment
            .ResolveAsync(tenantContext.TenantId, name, assignmentKey, cancellationToken)
            .ConfigureAwait(false);

        Microsoft.Agents.AI.AIAgent? agent;

        // Cozumleme bildirimsel bir tanimi derler; bilinmeyen tool veya saglayici
        // burada hata verir. Yanit henuz baslamadigi icin duzgun bir ProblemDetails
        // dondurebiliyoruz - akis basladiktan sonra bu mumkun olmaz.
        try
        {
            agent = assignment is null
                ? await catalog.ResolveAsync(name, cancellationToken).ConfigureAwait(false)
                : await catalog.ResolveAsync(name, assignment.Version, cancellationToken).ConfigureAwait(false);
        }
        catch (AgentPrismException ex)
        {
            return Results.Problem(
                title: "Agent derlenemedi",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (agent is null)
        {
            return Results.Problem(
                title: "Agent bulunamadi",
                detail: $"'{name}' adinda bir agent yok.",
                statusCode: StatusCodes.Status404NotFound);
        }

        // 🚨 Faz 43: 'Idempotency-Key' tasiyan bir istek akissiz calisir. Saklanan
        // yanit tekilleştirilebilir olmalidir; bir SSE govdesini saklamak
        // (zamanlama bilgisi kaybi, ongorulemez boyut) bu fazin kapsami disidir
        // (docs/43-IDEMPOTENCY-KEY.md, bolum 43.4). Bu yuzden IdempotencyFilter
        // yerine burada, akis SECIMI aninda karar verilir: filtre akisli bir
        // istegi hicbir zaman GORMEZ, cunku baslik tasiyan istek zaten akissizdir.
        var streaming = !httpContext.Request.Headers.ContainsKey(IdempotencyFilter.HeaderName);

        return new AgentRunStream(agent, name, request, sessions, attachments, prefix, runId, assignment, streaming);
    }

    /// <summary>
    /// Deneme calistirmasinin yanitini yazar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Varsayilan olarak <paramref name="streaming"/> actir ve yanit SSE'dir; ayri
    /// bir <see cref="IResult"/> olarak yazilir cunku akis basladiktan sonra durum
    /// kodu degistirilemez, hata durumunda <c>event: error</c> cercevesi gonderilir.
    /// </para>
    /// <para>
    /// Ilk cerceve <c>run</c>'dir ve calistirma kimligini tasir. Kimlik
    /// <see cref="AgentPrismRunOptions"/> ile <em>cagiran tarafindan</em> uretilir;
    /// aksi halde calistirma kaydini yazan sarmalayici kendi kimligini uretir ve
    /// akan yanit hicbir zaman <c>/api/runs/{id}</c> kaydiyla iliskilendirilemezdi.
    /// </para>
    /// <para>
    /// 🚨 <paramref name="streaming"/> kapaliysa (Faz 43, <c>Idempotency-Key</c>)
    /// yanit tek bir JSON govdedir: baslıklar/durum kodu henuz gonderilmedigi
    /// icin bir hata gercek bir HTTP durum koduyla (502) donebilir — SSE dalinin
    /// aksine burada <c>event: error</c> cercevesine gerek yoktur.
    /// </para>
    /// </remarks>
    private sealed class AgentRunStream(
        Microsoft.Agents.AI.AIAgent agent,
        string agentName,
        AgentRunRequest request,
        AgentSessionManager sessions,
        IReadOnlyList<AttachmentDescriptor> attachments,
        string prefix,
        Guid runId,
        ExperimentAssignment? assignment,
        bool streaming) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            if (streaming)
            {
                await ExecuteStreamingAsync(httpContext).ConfigureAwait(false);
            }
            else
            {
                await ExecuteBufferedAsync(httpContext).ConfigureAwait(false);
            }
        }

        private async Task ExecuteStreamingAsync(HttpContext httpContext)
        {
            var cancellationToken = httpContext.RequestAborted;
            var writer = await SseWriter.StartAsync(httpContext.Response, cancellationToken).ConfigureAwait(false);

            Microsoft.Agents.AI.AgentSession? session = null;
            long sequence = 0;

            try
            {
                if (!string.IsNullOrWhiteSpace(request.SessionId))
                {
                    session = await sessions
                        .GetOrCreateSessionAsync(agent, request.SessionId, cancellationToken)
                        .ConfigureAwait(false);
                }

                var messages = await BuildMessagesAsync(httpContext, session, cancellationToken)
                    .ConfigureAwait(false);

                await writer.WriteEventAsync(
                    sequence++,
                    "run",
                    JsonSerializer.Serialize(new AgentRunAccepted(runId, request.SessionId), JsonOptions),
                    cancellationToken).ConfigureAwait(false);

                var updates = agent.RunStreamingAsync(
                    messages,
                    session,
                    new AgentPrismRunOptions
                    {
                        RunId = runId,
                        AgentVersion = assignment?.Version,
                        ExperimentId = assignment?.ExperimentId,
                        Variant = assignment?.Variant,
                    },
                    cancellationToken);

                await foreach (var update in updates.ConfigureAwait(false))
                {
                    var payload = JsonSerializer.Serialize(update, AIJsonUtilities.DefaultOptions);
                    await writer.WriteEventAsync(sequence++, "update", payload, cancellationToken).ConfigureAwait(false);
                }

                if (session is not null)
                {
                    await sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
                }

                await writer.WriteEventAsync(
                    sequence,
                    "done",
                    JsonSerializer.Serialize(new AgentRunCompleted(request.SessionId), JsonOptions),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Istemci baglantiyi kesti. Yazacak kimse kalmadi.
            }
            catch (Exception ex) when (ex is AgentPrismException or InvalidOperationException or HttpRequestException)
            {
                await writer.WriteEventAsync(
                    sequence,
                    "error",
                    JsonSerializer.Serialize(new AgentRunFailed(ex.GetType().Name, ex.Message), JsonOptions),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task ExecuteBufferedAsync(HttpContext httpContext)
        {
            var cancellationToken = httpContext.RequestAborted;

            Microsoft.Agents.AI.AgentSession? session = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(request.SessionId))
                {
                    session = await sessions
                        .GetOrCreateSessionAsync(agent, request.SessionId, cancellationToken)
                        .ConfigureAwait(false);
                }

                var messages = await BuildMessagesAsync(httpContext, session, cancellationToken)
                    .ConfigureAwait(false);

                var response = await agent.RunAsync(
                    messages,
                    session,
                    new AgentPrismRunOptions
                    {
                        RunId = runId,
                        AgentVersion = assignment?.Version,
                        ExperimentId = assignment?.ExperimentId,
                        Variant = assignment?.Variant,
                    },
                    cancellationToken).ConfigureAwait(false);

                if (session is not null)
                {
                    await sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
                }

                await Results.Json(
                        new AgentRunResult(runId, request.SessionId, response),
                        AIJsonUtilities.DefaultOptions,
                        statusCode: StatusCodes.Status200OK)
                    .ExecuteAsync(httpContext).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Istemci baglantiyi kesti.
            }
            catch (Exception ex) when (ex is AgentPrismException or InvalidOperationException or HttpRequestException)
            {
                await Results.Problem(
                        title: "Agent calistirilamadi",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status502BadGateway)
                    .ExecuteAsync(httpContext).ConfigureAwait(false);
            }
        }

        private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// Gonderilecek mesajlari kurar: varsa onay yanitlari, varsa kullanici mesaji.
        /// </summary>
        /// <remarks>
        /// Onay yanitlari kullanici mesajindan ONCE gelir. Microsoft Agent Framework
        /// bekleyen cagriyi yanitlamadan yeni bir kullanici mesajini isleyemez;
        /// ters sira, modelin yanitlanmamis bir onay istegiyle karsilasmasina yol acardi.
        /// </remarks>
        private async ValueTask<List<ChatMessage>> BuildMessagesAsync(
            HttpContext httpContext,
            Microsoft.Agents.AI.AgentSession? session,
            CancellationToken cancellationToken)
        {
            var messages = new List<ChatMessage>(2);

            if (request.Approvals.Count > 0 && session is not null)
            {
                var services = httpContext.RequestServices;
                var loggerFactory = services.GetRequiredService<ILoggerFactory>();

                var approvalMessage = await ToolApprovalResolver.BuildResponseMessageAsync(
                    request.Approvals,
                    agent,
                    agentName,
                    session,
                    services.GetRequiredService<Microsoft.Agents.AI.ChatHistoryProvider>(),
                    services.GetRequiredService<IToolApprovalRuleStore>(),
                    services.GetRequiredService<ITenantContext>(),
                    services.GetRequiredService<IAuditLog>(),
                    services.GetRequiredService<IAuditActorResolver>(),
                    loggerFactory.CreateLogger(typeof(ToolApprovalResolver).FullName!),
                    cancellationToken).ConfigureAwait(false);

                if (approvalMessage is not null)
                {
                    messages.Add(approvalMessage);
                }
            }

            var contents = new List<AIContent>();

            if (!string.IsNullOrWhiteSpace(request.Message))
            {
                contents.Add(new TextContent(request.Message));
            }

            // Ikili icerik burada TASINMAZ: yalniz kucuk bir UriContent referansi
            // eklenir. Gercek baytlar model cagrisindan hemen once, saglayiciya
            // gonderilmeden ONCE cozulur (bkz. AttachmentResolvingChatClient).
            foreach (var attachment in attachments)
            {
                contents.Add(new UriContent(AttachmentUriReference.Create(prefix, attachment.Id), attachment.MediaType));
            }

            if (contents.Count > 0)
            {
                messages.Add(new ChatMessage(ChatRole.User, contents));
            }

            return messages;
        }

        private sealed record AgentRunAccepted(Guid RunId, string? SessionId);

        private sealed record AgentRunCompleted(string? SessionId);

        private sealed record AgentRunFailed(string Type, string Message);

        /// <summary>Akissiz (Idempotency-Key) calistirmanin JSON yaniti.</summary>
        private sealed record AgentRunResult(Guid RunId, string? SessionId, Microsoft.Agents.AI.AgentResponse Response);
    }

    private static async ValueTask<AgentDescriptor?> FindDescriptorAsync(
        IAgentCatalog catalog,
        string name,
        CancellationToken cancellationToken)
    {
        foreach (var descriptor in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(descriptor.Name, name, StringComparison.Ordinal))
            {
                return descriptor;
            }
        }

        return null;
    }

    /// <summary>
    /// Tanimin cagri grafigini denetler ve sorunluysa <c>400</c> uretir.
    /// </summary>
    /// <remarks>
    /// Denetim <strong>kaydetme aninda</strong> yapilir. Calisma anina birakilsaydi
    /// kullanici hatayi ancak agent'i calistirdiginda ve derinlik sayaci dolduktan
    /// sonra - yani token harcadiktan sonra - gorurdu.
    /// </remarks>
    private static async ValueTask<ProblemHttpResult?> ValidateCallGraphAsync(
        IAgentCatalog catalog,
        AgentDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CallableAgentNames.Count == 0)
        {
            return null;
        }

        var descriptors = await catalog.ListAsync(cancellationToken).ConfigureAwait(false);

        return AgentCallGraph.Validate(request.Name, request.CallableAgentNames, descriptors) is { } problem
            ? TypedResults.Problem(
                title: "Cagri grafigi gecersiz",
                detail: problem,
                statusCode: StatusCodes.Status400BadRequest)
            : null;
    }

    private static async ValueTask<ProblemHttpResult?> GuardCodeAgentAsync(
        IAgentCatalog catalog,
        string name,
        CancellationToken cancellationToken)
    {
        var descriptor = await FindDescriptorAsync(catalog, name, cancellationToken).ConfigureAwait(false);

        return descriptor?.Origin == AgentDefinitionOrigin.Code
            ? TypedResults.Problem(
                title: "Kodda tanimli agent degistirilemez",
                detail: $"'{name}' kodda tanimlidir. Kod tanimlari derleme zamaninda dogrulanir ve " +
                        "yonetim API'sinden degistirilemez; degisiklik icin uygulama kodunu guncelleyin.",
                statusCode: StatusCodes.Status409Conflict)
            : null;
    }

    private static ProblemHttpResult? Validate(AgentDefinitionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.Problem(
                title: "Agent adi bos",
                detail: "'name' alani zorunludur.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Model?.Provider) || string.IsNullOrWhiteSpace(request.Model.Model))
        {
            return TypedResults.Problem(
                title: "Model baglantisi eksik",
                detail: "'model.provider' ve 'model.model' alanlari zorunludur.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }

    private static ProblemHttpResult NotFound(string name)
        => TypedResults.Problem(
            title: "Agent bulunamadi",
            detail: $"'{name}' adinda bir agent yok.",
            statusCode: StatusCodes.Status404NotFound);
}
