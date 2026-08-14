using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("AgentPrismListAgents")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Kodda ve veritabaninda tanimli tum agent'lari listeler.");

        builder.MapGet("/api/agents/{name}", GetAgentAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("AgentPrismGetAgent")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Bir agent'in katalog ozetini ve varsa kalici tanimini dondurur.");

        builder.MapPost("/api/agents", CreateAgentAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("AgentPrismCreateAgent")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Yeni bir agent tanimi olusturur.")
            .Accepts<AgentDefinitionRequest>("application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        builder.MapPost("/api/agents/validate", ValidateAgentAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("AgentPrismValidateAgent")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Bir tanimi kaydetmeden ve hicbir model cagirmadan derler.")
            .Accepts<AgentDefinitionRequest>("application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        builder.MapPut("/api/agents/{name}", UpdateAgentAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("AgentPrismUpdateAgent")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Bir agent tanimini gunceller ve yeni bir surum uretir.")
            .Accepts<AgentDefinitionRequest>("application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        builder.MapDelete("/api/agents/{name}", DeleteAgentAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("AgentPrismDeleteAgent")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Bir agent tanimini ve surum gecmisini siler.");

        builder.MapGet("/api/agents/{name}/versions", ListVersionsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("AgentPrismListAgentVersions")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Bir tanimin surum gecmisini yeniden eskiye listeler.");

        builder.MapPost("/api/agents/{name}/rollback", RollbackAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("AgentPrismRollbackAgent")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Bir tanimi onceki bir surumun icerigiyle yeni surum olarak yazar.");

        builder.MapGet("/api/agents/{name}/versions/{a:int}/diff/{b:int}", GetVersionDiffAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("AgentPrismGetAgentVersionDiff")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Iki tanim surumunu ham JSON olarak dondurur; diff hesabi arayuzde yapilir.");

        builder.MapPost("/api/agents/{name}/run", async (
                string name,
                AgentRunRequest request,
                IAgentCatalog catalog,
                AgentSessionManager sessions,
                IAttachmentStore attachmentStore,
                IJobStore jobStore,
                IRunStore runStore,
                ITenantContext tenantContext,
                ExperimentAssignmentResolver experimentAssignment,
                IOptionsMonitor<AgentPrismAsyncRunOptions> asyncRunOptions,
                [FromServices] QuotaEnforcer? quotaEnforcer,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                // 🚨 Kota denetimi calistirma BASLAMADAN once yapilir. Devam eden
                // bir calistirma kota asilinca kesilmez (K-162); yalnizca yeni
                // calistirma 429 alir. Kuyruga alma da yeni bir calistirmadir.
                if (await QuotaGate
                        .CheckAsync(quotaEnforcer, tenantContext, name, httpContext, cancellationToken)
                        .ConfigureAwait(false) is { } quotaProblem)
                {
                    return quotaProblem;
                }

                if (WantsAsync(httpContext))
                {
                    return await RunQueuedAsync(
                        name,
                        request,
                        catalog,
                        jobStore,
                        runStore,
                        tenantContext,
                        asyncRunOptions.CurrentValue,
                        prefix,
                        httpContext,
                        cancellationToken).ConfigureAwait(false);
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
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .AddEndpointFilter(idempotencyFilter)
            .WithName("AgentPrismRunAgent")
            .WithTags("AgentPrism", "Agents")
            .WithSummary("Bir agent'i deneme amaciyla calistirir ve yaniti SSE ile akitir.")
            .WithDescription(
                "Kota asilmissa calistirma baslamaz ve 429 doner; ProblemDetails hangi kotanin " +
                "asildigini ve sayacin ne zaman sifirlanacagini tasir. 'Idempotency-Key' basligi " +
                "tasiyan bir istek SSE yerine tek bir JSON yanitla (akissiz) calisir — Faz 43'un " +
                "tekillestirme sozlesmesi akissiz bir yanit gerektirir (docs/43-IDEMPOTENCY-KEY.md). " +
                "'Prefer: respond-async' basligi tasiyan bir istek calistirmayi kuyruga alir ve " +
                "'202 Accepted' + 'Location' doner (Faz 46, docs/46-DAYANIKLI-CALISTIRMA.md). " +
                "Kayitli bir IContentGuard icerigi engellerse akissiz yanit '422' doner ve " +
                "runs.error_type 'content_blocked' olur; AKISLI yanitta durum kodu zaten " +
                "gonderilmis oldugu icin engelleme SSE 'error' olayi olarak gorunur " +
                "(Faz 48, docs/48-GUARDRAILS.md).")
            // Basari yaniti varsayilan olarak SSE'dir (bkz. AgentRunStream); ama
            // 'Idempotency-Key' basligi tasiyan bir istek JSON govde, 'Prefer:
            // respond-async' tasiyan bir istek 202 govde alir.
            .Produces<string>(StatusCodes.Status200OK, contentType: "text/event-stream")
            .Produces<AcceptedRunResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status501NotImplemented);
    }

    /// <summary>
    /// Istek <c>Prefer: respond-async</c> tercihini tasiyor mu (RFC 7240).
    /// </summary>
    /// <remarks>
    /// Baslik TASIMAYAN bir istek icin bu denetim tek bir sozluk aramasidir;
    /// hicbir ek sorgu atilmaz (K1: sessiz maliyet yoktur).
    /// </remarks>
    private static bool WantsAsync(HttpContext httpContext)
        => httpContext.Request.Headers.TryGetValue("Prefer", out var values) &&
           values.Any(static value => value is not null &&
               value.Split(',').Any(static token =>
                   token.Trim().Equals("respond-async", StringComparison.OrdinalIgnoreCase)));

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
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        AgentDefinitionValidator validator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await BindAgentDefinitionRequestAsync(httpContext, cancellationToken).ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        if (await ValidateCallGraphAsync(catalog, request, cancellationToken).ConfigureAwait(false) is { } cycle)
        {
            return cycle;
        }

        if (await ValidateEntitiesAsync(validator, request, cancellationToken).ConfigureAwait(false) is { } entities)
        {
            return entities;
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
        HttpContext httpContext,
        AgentDefinitionValidator validator,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await BindAgentDefinitionRequestAsync(httpContext, cancellationToken).ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

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
        HttpContext httpContext,
        IAgentCatalog catalog,
        IAgentDefinitionStore definitions,
        AgentDefinitionValidator validator,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await BindAgentDefinitionRequestAsync(httpContext, cancellationToken).ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

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

        if (await ValidateEntitiesAsync(validator, request, cancellationToken).ConfigureAwait(false) is { } entities)
        {
            return entities;
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
    /// Calistirmayi kuyruga alir ve <c>202 Accepted</c> doner (Faz 46).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Onay kararlari ve ekler bu surumde desteklenmez: ikisi de canli bir
    /// istemci baglantisi (onay: bir sonraki turun girdisi; ek: yol oneki
    /// gerektiren bir <c>UriContent</c> referansi) varsayar, kuyruktan kosan
    /// bir isin bu baglami yoktur.
    /// </para>
    /// <para>
    /// Burada uretilen calistirma kimligi hem <c>runs</c> satirinin hem
    /// <c>jobs</c> kaydinin kimligidir: ikisi ayni deger tasir. Bu, istemciye
    /// donen <c>Location</c>'in is kuyruktan kosana kadar da anlamli kalmasini
    /// saglar — <c>GET /api/runs/{id}</c> gercek satir henuz yoksa <c>404</c>
    /// degil, az once yazilan <see cref="RunStatus.Queued"/> satirini gorur.
    /// </para>
    /// </remarks>
    private static async Task<IResult> RunQueuedAsync(
        string name,
        AgentRunRequest request,
        IAgentCatalog catalog,
        IJobStore jobStore,
        IRunStore runStore,
        ITenantContext tenantContext,
        AgentPrismAsyncRunOptions options,
        string prefix,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return Results.Problem(
                title: "Kuyruga alma destegi kapali",
                detail: "'Prefer: respond-async' basligi gonderildi ama bu kurulumda kuyruga alma " +
                        "destegi kapali (AgentPrismAsyncRunOptions.Enabled = false).",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.Problem(
                title: "Istek bos",
                detail: "Kuyruga alinan bir calistirmada 'message' zorunludur.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Approvals.Count > 0 || request.AttachmentIds.Count > 0)
        {
            return Results.Problem(
                title: "Desteklenmiyor",
                detail: "Kuyruga alinan ('Prefer: respond-async') bir calistirma onay kararlarini " +
                        "veya ekleri bu surumde desteklemez.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Microsoft.Agents.AI.AIAgent? agent;

        try
        {
            agent = await catalog.ResolveAsync(name, cancellationToken).ConfigureAwait(false);
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

        var runId = AgentPrismId.NewId();
        var now = DateTimeOffset.UtcNow;

        // Yer tutucu satir: isci is'i alip agent'i GERCEKTEN calistirana kadar
        // istemci bu kimlikle GET /api/runs/{id} cagirirsa 404 degil, Queued
        // gormelidir. Ayni kimlikle StartRunAsync isci tarafindan IKINCI kez
        // cagrildiginda (bkz. AgentRunJobHandler) depo bunu bir UPSERT olarak
        // ele alir; yeni bir satir ACILMAZ.
        await runStore.StartRunAsync(
            new RunStartInfo
            {
                RunId = runId,
                AgentName = name,
                Status = RunStatus.Queued,
                StartedAt = now,
                TenantId = tenantContext.TenantId,
                SessionId = request.SessionId,
            },
            cancellationToken).ConfigureAwait(false);

        // 🚨 Job.Id calistirma kimligiyle AYNI verilir (AgentPrismId.NewId()
        // burada TEKRAR cagrilmaz). Faz 17'nin genel deseninde is kimligi ile
        // calistirma kimligi ayridir; burada bilerek birlestirilir, aksi halde
        // isci is'i almadan once GET /api/runs/{id} 404 disinda bir sey
        // dondurmenin ikinci bir yolu (is deposunu tarama) gerekirdi.
        var job = await jobStore.EnqueueAsync(
            new JobRecord
            {
                Id = runId,
                TenantId = tenantContext.TenantId,
                Kind = JobKind.AgentRun,
                TargetName = name,
                Status = JobStatus.Pending,
                Payload = BuildQueuedRunPayload(runId, request.Message, request.SessionId),
                MaxAttempts = options.MaxAttempts,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            cancellationToken).ConfigureAwait(false);

        // 🚨 RFC 7240 tercihi *tavsiye* sayar; sunucu yok sayabilir. AgentPrism
        // bu belirsizligi tasimaz: tercih uygulandiysa yanit hem 202 HEM
        // 'Preference-Applied' tasir (Faz 43'un ayni kuralinin tekrari).
        httpContext.Response.Headers["Preference-Applied"] = "respond-async";

        var location = $"{prefix}/api/runs/{runId}";

        return TypedResults.Accepted(location, new AcceptedRunResponse
        {
            RunId = runId,
            JobId = job.Id,
            Location = location,
            EventsLocation = $"{location}/events",
        });
    }

    /// <summary>
    /// <see cref="JobKind.AgentRun"/> isinin yukunu kurar. Ayristirma
    /// <c>AgentRunJobHandler.ParsePayload</c>'dadir (elle, AOT uyumlu).
    /// </summary>
    private static JsonElement BuildQueuedRunPayload(Guid runId, string message, string? sessionId)
        => JsonSerializer.SerializeToElement(new
        {
            runId = runId.ToString(),
            message,
            sessionId,
        });

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
            catch (Exception ex)
            {
                // 🚨 K-296: SSE basliklari (200, text/event-stream) ZATEN gonderildi.
                // Dar bir 'when' filtresi (yalniz AgentPrismException/InvalidOperationException/
                // HttpRequestException) gercek saglayici SDK istisnalarini (orn. Anthropic'in
                // AnthropicApiException'i Exception'dan DOGRUDAN turer, HttpRequestException'dan
                // TUREMEZ) yakalamadan kacirirdi — baglanti 'error' cercevesi UretMEDEN kapanir
                // ve istemci bunu sessiz basari sanir. Burada yakalanmayan HICBIR sey yoktur:
                // istemciye HER ZAMAN bir 'error' cercevesi ulasir.
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
            catch (AgentPrismContentBlockedException ex)
            {
                // 🚨 Engelleme bir ISTEMCI hatasidir: istek anlasildi ama politika
                // onu gecirmedi ve yeniden denemek ise yaramaz. 502 "yukari akis
                // bozuk" derdi ve istemciyi yeniden denemeye yonlendirirdi.
                // ProblemDetails guard ve kural adini tasir, engellenen metni
                // TASIMAZ (ex.Message de tasimaz).
                await Results.Problem(
                        title: "Icerik engellendi",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["errorType"] = AgentPrismContentBlockedException.ContentBlockedErrorType,
                            ["guard"] = ex.GuardName,
                            ["rule"] = ex.RuleName,
                            ["direction"] = ex.Direction.ToString(),
                        })
                    .ExecuteAsync(httpContext).ConfigureAwait(false);
            }
            catch (AgentPrismSessionConflictException ex)
            {
                // 🚨 HATA-004: ayni YENI oturuma eszamanli iki ilk istek geldiginde
                // kaybeden burada duser. 502 "yukari akis bozuk" derdi; asil sebep
                // istemci tarafi bir yaris kosulu — 409 ve kisa bir yeniden deneme
                // dogru cozumdur.
                await Results.Problem(
                        title: "Oturum catismasi",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status409Conflict,
                        extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["errorType"] = AgentPrismSessionConflictException.SessionConflictErrorType,
                        })
                    .ExecuteAsync(httpContext).ConfigureAwait(false);
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

    /// <summary>
    /// Kaydetmeden once tanimi tam olarak dogrular (K-404) — model, tool,
    /// skill ve cagrilabilir-agent VARLIK denetimleri dahil. Bu denetim
    /// oncesinde yalniz ayri <c>POST /api/agents/validate</c> ucu
    /// cagirilirdi; SAVE yolunun kendisi bilinmeyen bir skill/tool adini
    /// hicbir hata vermeden kaydederdi (HATA-K-001).
    /// </summary>
    private static async ValueTask<ProblemHttpResult?> ValidateEntitiesAsync(
        AgentDefinitionValidator validator,
        AgentDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var report = await validator
            .ValidateAsync(request.ToDefinition(), cancellationToken)
            .ConfigureAwait(false);

        if (report.Valid)
        {
            return null;
        }

        var detail = string.Join(
            " ",
            report.Messages
                .Where(static message => message.Severity == ValidationSeverity.Error)
                .Select(static message => message.Message));

        return TypedResults.Problem(
            title: "Tanim gecersiz",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
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

    /// <summary>
    /// Govdeyi elle okur (minimal API'nin otomatik JSON baglamasi yerine): bir
    /// ayristirma hatasi (ornegin taninmayan bir enum degeri) boylece bu ucun
    /// kendi <c>400</c> sozlesmesine girer, minimal API'nin baglama asamasinda
    /// fillayip yakalanamayan bir <see cref="JsonException"/> ile genel <c>500</c>'e
    /// dusmez (HATA-S1-007).
    /// </summary>
    private static async Task<(AgentDefinitionRequest? Request, ProblemHttpResult? Error)> BindAgentDefinitionRequestAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await httpContext.Request
                .ReadFromJsonAsync<AgentDefinitionRequest>(cancellationToken)
                .ConfigureAwait(false);

            if (request is null)
            {
                return (null, TypedResults.Problem(
                    title: "Gecersiz istek govdesi",
                    detail: "Govde bos olamaz.",
                    statusCode: StatusCodes.Status400BadRequest));
            }

            return (request, null);
        }
        catch (JsonException ex)
        {
            return (null, TypedResults.Problem(
                title: "Gecersiz istek govdesi",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
    }
}
