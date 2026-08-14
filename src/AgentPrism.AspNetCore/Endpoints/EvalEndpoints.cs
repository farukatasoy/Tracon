using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Eval takimi/vaka yonetimi, kosu tetikleme ve sonuc goruntuleme uclari (Faz 18).
/// </summary>
/// <remarks>
/// 🚨 <see cref="IEvalStore"/> disindaki tum bagimliliklar <c>[FromServices]</c>
/// ile <strong>acikca</strong> isaretlenir — gerekce <see cref="SchedulingEndpoints"/>
/// ile aynidir. Kosu tetikleme, mevcut is kuyrugunu (<see cref="IJobStore"/>,
/// <see cref="JobKind.Eval"/>) kullanir; ayri bir yurutme yolu yoktur.
/// </remarks>
internal static class EvalEndpoints
{
    /// <summary>Eval uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/evals", ListSuitesAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("AgentPrismListEvalSuites")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Bir kiracinin eval takimlarini listeler.");

        builder.MapGet("/api/evals/{name}", GetSuiteAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("AgentPrismGetEvalSuite")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Tek bir eval takimini getirir.");

        builder.MapPut("/api/evals/{name}", SaveSuiteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.EvalsAdmin)
            .WithName("AgentPrismSaveEvalSuite")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Eval takimi olusturur veya gunceller.")
            .Accepts<EvalSuiteSaveRequest>("application/json")
            .WithDescription("Denetim tanimlari bildirimseldir; bilinmeyen bir denetim turu kosu aninda hataya donusur.");

        builder.MapDelete("/api/evals/{name}", DeleteSuiteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.EvalsAdmin)
            .WithName("AgentPrismDeleteEvalSuite")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Bir eval takimini siler (vakalar ve kosular birlikte).");

        builder.MapGet("/api/evals/{name}/cases", ListCasesAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("AgentPrismListEvalCases")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Bir takimin vakalarini listeler.");

        builder.MapPut("/api/evals/{name}/cases", SaveCasesAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.EvalsAdmin)
            .WithName("AgentPrismSaveEvalCases")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Bir takimin tum vakalarini verilen listeyle degistirir.")
            .Accepts<IReadOnlyList<EvalCaseInput>>("application/json");

        builder.MapDelete("/api/evals/{name}/cases", ClearCasesAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.EvalsAdmin)
            .WithName("AgentPrismClearEvalCases")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Bir takimin tum vakalarini siler.");

        builder.MapPost("/api/evals/{name}/cases/from-run/{runId:guid}", PromoteRunToCaseAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.EvalsAdmin)
            .WithName("AgentPrismPromoteRunToEvalCase")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Bir calistirmayi tek istekle bir eval vakasina terfi ettirir.")
            .Accepts<EvalCasePromotionRequest>(true, "application/json")
            .WithDescription(
                "Sorgu, calistirmanin kendi oturumundan okunur; oturumsuz calistirmalar " +
                "terfi edilemez. Ayni calistirma ikinci kez terfi edilirse mevcut vaka doner " +
                "(201 degil 200).");

        builder.MapPost("/api/evals/{name}/run", TriggerRunAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismTriggerEvalRun")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Bir eval takimini simdi calistirir.")
            .Accepts<EvalRunTriggerRequest>(true, "application/json")
            .WithDescription(
                "Her vaka, olculen agent uzerinde yeni bir oturumda calisir ve kendi 'runs' " +
                "satirini uretir. Kosu is kuyruguna girer; sonuclar arka planda islenir.");

        builder.MapGet("/api/evals/{name}/runs", ListRunsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("AgentPrismListEvalRuns")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Bir takimin gecmis kosularini listeler.");

        builder.MapGet("/api/evals/runs/{id:guid}", GetRunAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("AgentPrismGetEvalRun")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Tek bir eval kosusunu ve vaka bazinda sonuclarini getirir.");

        builder.MapGet("/api/evaluation/online", GetOnlineEvaluationSummaryAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("AgentPrismGetOnlineEvaluationSummary")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Cevrimici degerlendirme penceresinin ozetini dondurur (Faz 49).")
            .WithDescription(
                "Pencere icindeki ortalama yargic puani, ornek sayisi ve yargic maliyetini " +
                "dondurur. Ozet bellek icidir (sureç yeniden baslatilinca sifirlanir); kesin " +
                "sonuc icin 'run_scores' tablosu dogrudan sorgulanabilir.");

        builder.MapPost("/api/runs/{runId:guid}/judge", JudgeRunAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismJudgeRun")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Bir calistirmayi elle yargic(lar)a puanlatir (Faz 49).")
            .WithDescription(
                "Ornekleme kararini ATLAR; kalibrasyon ve hata ayiklama icindir. Hicbir " +
                "IRunJudge kayitli degilse veya calistirmanin girdisi/ciktisi okunamiyorsa " +
                "bos bir liste doner.");
    }

    private static async Task<Ok<IReadOnlyList<EvalSuite>>> ListSuitesAsync(
        [FromServices] IEvalStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var suites = await store.ListSuitesAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(suites);
    }

    private static async Task<Results<Ok<EvalSuite>, ProblemHttpResult>> GetSuiteAsync(
        string name,
        [FromServices] IEvalStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var suite = await store.GetSuiteAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);
        return suite is null ? SuiteNotFound(name) : TypedResults.Ok(suite);
    }

    private static async Task<Results<Ok<EvalSuite>, ProblemHttpResult>> SaveSuiteAsync(
        string name,
        HttpContext httpContext,
        [FromServices] IEvalStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] EvalCheckRegistry checkRegistry,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<EvalSuiteSaveRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (string.IsNullOrWhiteSpace(request.AgentName))
        {
            return InvalidSuite("'agentName' is required.");
        }

        try
        {
            // Denetimler kayit aninda dogrulanir: bilinmeyen bir tur adi kosu
            // baslamadan, hemen geri bildirilir.
            checkRegistry.BuildChecks(request.Checks);
        }
        catch (AgentPrismException exception)
        {
            return InvalidSuite(exception.Message);
        }

        var existing = await store.GetSuiteAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        var suite = new EvalSuite
        {
            Id = existing?.Id ?? Guid.Empty,
            TenantId = tenants.TenantId,
            Name = name,
            Description = request.Description,
            AgentName = request.AgentName,
            Checks = request.Checks,
            CreatedAt = existing?.CreatedAt ?? default,
            UpdatedAt = default,
        };

        var saved = await store.SaveSuiteAsync(suite, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(saved);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteSuiteAsync(
        string name,
        [FromServices] IEvalStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
        => await store.DeleteSuiteAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false)
            ? TypedResults.NoContent()
            : SuiteNotFound(name);

    private static async Task<Results<Ok<IReadOnlyList<EvalCase>>, ProblemHttpResult>> ListCasesAsync(
        string name,
        [FromServices] IEvalStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var suite = await store.GetSuiteAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        if (suite is null)
        {
            return SuiteNotFound(name);
        }

        var cases = await store.ListCasesAsync(suite.Id, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(cases);
    }

    private static async Task<Results<Ok<IReadOnlyList<EvalCase>>, ProblemHttpResult>> SaveCasesAsync(
        string name,
        HttpContext httpContext,
        [FromServices] IEvalStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<IReadOnlyList<EvalCaseInput>>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var cases = bound!;

        var suite = await store.GetSuiteAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        if (suite is null)
        {
            return SuiteNotFound(name);
        }

        if (cases.Any(static input => string.IsNullOrWhiteSpace(input.Query)))
        {
            return InvalidSuite("Each case must have a non-empty 'query' field.");
        }

        var converted = cases
            .Select(static (input, seq) => new EvalCase
            {
                SuiteId = default,
                Seq = seq,
                Query = input.Query,
                ExpectedOutput = input.ExpectedOutput,
                ExpectedTools = input.ExpectedTools,
                Context = input.Context,
            })
            .ToArray();

        var saved = await store.ReplaceCasesAsync(suite.Id, converted, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(saved);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ClearCasesAsync(
        string name,
        [FromServices] IEvalStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var suite = await store.GetSuiteAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        if (suite is null)
        {
            return SuiteNotFound(name);
        }

        await store.ReplaceCasesAsync(suite.Id, [], cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<EvalCase>, Ok<EvalCase>, ProblemHttpResult>> PromoteRunToCaseAsync(
        string name,
        Guid runId,
        HttpContext httpContext,
        [FromServices] IEvalStore evalStore,
        [FromServices] RunToCasePromoter promoter,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var (request, bindError) = await RequestBodyBinding
            .ReadOptionalAsync<EvalCasePromotionRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var suite = await evalStore.GetSuiteAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        if (suite is null)
        {
            return SuiteNotFound(name);
        }

        var outcome = await promoter
            .PromoteAsync(tenants.TenantId, suite, runId, request?.SourceKind, cancellationToken)
            .ConfigureAwait(false);

        switch (outcome.Status)
        {
            case RunPromotionStatus.RunNotFound:
                return RunNotFoundForPromotion(runId);

            case RunPromotionStatus.AmbiguousSource:
                return TypedResults.Problem(
                    title: "Promotion reason could not be determined",
                    detail: $"Run '{runId}' is neither failed nor completed. " +
                             "'sourceKind' must be given explicitly in the body.",
                    statusCode: StatusCodes.Status400BadRequest);

            case RunPromotionStatus.NoQuery:
                return TypedResults.Problem(
                    title: "Run query could not be read",
                    detail: $"The query for run '{runId}' could not be read. Runs without a session, " +
                             "or sessions whose history cannot be read, cannot be promoted.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            case RunPromotionStatus.MultiTurn:
                return TypedResults.Problem(
                    title: "Multi-turn run cannot be promoted",
                    detail: $"The session for run '{runId}' has more than one user turn; " +
                             "it does not fit a single-turn case.",
                    statusCode: StatusCodes.Status409Conflict);

            case RunPromotionStatus.AlreadyExists:
                return TypedResults.Ok(outcome.Case!);

            case RunPromotionStatus.Created:
            default:
                await AuditRecorder.WriteAsync(
                    auditLog,
                    actorResolver,
                    loggerFactory.CreateLogger("AgentPrism.EvalEndpoints"),
                    tenants.TenantId,
                    action: "eval.case.promoted",
                    entity: $"eval_case:{outcome.Case!.Id}",
                    before: null,
                    after: $$"""{"suiteId":"{{suite.Id}}","runId":"{{runId}}","sourceKind":"{{outcome.Case.SourceKind}}"}""",
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Created($"/api/evals/{name}/cases", outcome.Case);
        }
    }

    private static async Task<Results<Ok<EvalRun>, ProblemHttpResult>> TriggerRunAsync(
        string name,
        HttpContext httpContext,
        [FromServices] IEvalStore evalStore,
        [FromServices] IJobStore jobStore,
        [FromServices] ITenantContext tenants,
        [FromServices] IOptionsMonitor<AgentPrismSchedulingOptions> schedulingOptions,
        [FromServices] IAgentCatalog catalog,
        [FromServices] IAgentDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        var (request, bindError) = await RequestBodyBinding
            .ReadOptionalAsync<EvalRunTriggerRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var suite = await evalStore.GetSuiteAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        if (suite is null)
        {
            return SuiteNotFound(name);
        }

        if (request?.AgentVersion is { } requestedVersion)
        {
            var descriptors = await catalog.ListAsync(cancellationToken).ConfigureAwait(false);
            var descriptor = descriptors.FirstOrDefault(
                candidate => string.Equals(candidate.Name, suite.AgentName, StringComparison.Ordinal));

            if (descriptor?.Origin == AgentDefinitionOrigin.Code)
            {
                return TypedResults.Problem(
                    title: "Version cannot be selected",
                    detail: $"'{suite.AgentName}' is defined in code and has no version history.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (await definitions.GetVersionAsync(suite.AgentName, requestedVersion, cancellationToken).ConfigureAwait(false) is null)
            {
                return TypedResults.Problem(
                    title: "Version not found",
                    detail: $"Agent '{suite.AgentName}' has no version {requestedVersion}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var cases = await evalStore.ListCasesAsync(suite.Id, cancellationToken).ConfigureAwait(false);

        if (cases.Count == 0)
        {
            return TypedResults.Problem(
                title: "Run could not be started",
                detail: $"Suite '{name}' has no cases.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var maxItems = schedulingOptions.CurrentValue.MaxItemsPerJob;

        if (cases.Count > maxItems)
        {
            return TypedResults.Problem(
                title: "Run could not be started",
                detail: $"The suite has {cases.Count} cases; at most {maxItems} are supported.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var items = cases.Select(static evalCase => evalCase.Id.ToString()).ToArray();
        var now = DateTimeOffset.UtcNow;

        var job = await jobStore.EnqueueAsync(
            new JobRecord
            {
                Id = AgentPrismId.NewId(),
                TenantId = tenants.TenantId,
                Kind = JobKind.Eval,
                TargetName = suite.AgentName,
                Status = JobStatus.Pending,
                Payload = BuildRunPayload(suite.Name, request?.ModelId, request?.NumRepetitions, request?.AgentVersion),
                ScheduledFor = now,
                CreatedAt = now,
            },
            items,
            cancellationToken).ConfigureAwait(false);

        var run = await evalStore.CreateRunAsync(
            new EvalRun
            {
                Id = AgentPrismId.NewId(),
                TenantId = tenants.TenantId,
                SuiteId = suite.Id,
                JobId = job.Id,
                Status = EvalRunStatus.Pending,
                Total = items.Length,
                StartedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(run);
    }

    private static async Task<Results<Ok<IReadOnlyList<EvalRun>>, ProblemHttpResult>> ListRunsAsync(
        string name,
        [FromServices] IEvalStore store,
        [FromServices] ITenantContext tenants,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        var suite = await store.GetSuiteAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        if (suite is null)
        {
            return SuiteNotFound(name);
        }

        var runs = await store.QueryRunsAsync(
            new EvalRunQuery
            {
                TenantId = tenants.TenantId,
                SuiteId = suite.Id,
                Skip = skip ?? 0,
                Take = take ?? 50,
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(runs);
    }

    private static async Task<Results<Ok<EvalRunDetailResponse>, ProblemHttpResult>> GetRunAsync(
        Guid id,
        [FromServices] IEvalStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var run = await store.GetRunAsync(tenants.TenantId, id, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return RunNotFound(id);
        }

        var results = await store.ListCaseResultsAsync(tenants.TenantId, id, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new EvalRunDetailResponse { Run = run, Results = results });
    }

    private static async Task<Ok<OnlineEvaluationSummary>> GetOnlineEvaluationSummaryAsync(
        [FromServices] OnlineEvalSummaryService summaryService,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var summary = await summaryService.GetSummaryAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(summary);
    }

    private static async Task<Results<Ok<IReadOnlyList<RunScore>>, ProblemHttpResult>> JudgeRunAsync(
        Guid runId,
        [FromServices] IRunStore runs,
        [FromServices] OnlineEvalJobHandler jobHandler,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var run = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // "Yok" ile "baska kiraciya ait" AYNI 404'u doner; ayri bir mesaj varlik
        // sizdirirdi (RunEndpoints.SaveFeedbackAsync ile ayni gerekce).
        if (run is null || !string.Equals(run.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return RunNotFoundForPromotion(runId);
        }

        var (scores, failures) = await jobHandler.JudgeRunAsync(run, cancellationToken).ConfigureAwait(false);

        if (scores.Count == 0 && failures.Count > 0)
        {
            return TypedResults.Problem(
                title: "Manual scoring failed",
                detail: string.Join("; ", failures),
                statusCode: StatusCodes.Status502BadGateway);
        }

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("AgentPrism.EvalEndpoints"),
            tenants.TenantId,
            action: "run.judge.manual",
            entity: $"run:{runId}",
            before: null,
            after: $$"""{"scoredBy":{{scores.Count}},"failed":{{failures.Count}}}""",
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<RunScore>>(scores);
    }

    private static JsonElement BuildRunPayload(string suiteName, string? modelId, int? numRepetitions, int? agentVersion)
        => JsonSerializer.SerializeToElement(new
        {
            suiteName,
            modelId,
            numRepetitions,
            agentVersion,
        });

    private static ProblemHttpResult InvalidSuite(string detail)
        => TypedResults.Problem(title: "Eval suite invalid", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult SuiteNotFound(string name)
        => TypedResults.Problem(
            title: "Eval suite not found",
            detail: $"There is no eval suite named '{name}'.",
            statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult RunNotFound(Guid id)
        => TypedResults.Problem(
            title: "Eval run not found",
            detail: $"There is no eval run with id '{id}'.",
            statusCode: StatusCodes.Status404NotFound);

    // "Yok" ile "baska kiraciya ait" AYNI 404'u doner; ayri bir mesaj varlik
    // sizdirirdi (RunEndpoints.SaveFeedbackAsync ile ayni gerekce).
    private static ProblemHttpResult RunNotFoundForPromotion(Guid runId)
        => TypedResults.Problem(
            title: "Run not found",
            detail: $"There is no run with id '{runId}'.",
            statusCode: StatusCodes.Status404NotFound);
}
