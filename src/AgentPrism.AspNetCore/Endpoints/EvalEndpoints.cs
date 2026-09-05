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
/// Endpoints for eval suite/case management, run triggering, and result viewing.
/// </summary>
/// <remarks>
/// All dependencies other than <see cref="IEvalStore"/> are marked
/// <strong>explicitly</strong> with <c>[FromServices]</c> — the rationale is the
/// same as <see cref="SchedulingEndpoints"/>. Triggering a run uses the existing
/// job queue (<see cref="IJobStore"/>, <see cref="JobHandlerKeys.Eval"/>); there is no separate execution path.
/// </remarks>
internal static class EvalEndpoints
{
    /// <summary>Maps the eval endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/evals", ListSuitesAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("AgentPrismListEvalSuites")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Lists a tenant's eval suites.")
            .WithDescription(
                "Each entry is a suite's definition — the agent under test and its check " +
                "definitions — without the cases or the past runs; read those from the cases and " +
                "runs endpoints. The response is not paged.");

        builder.MapGet("/api/evals/{name}", GetSuiteAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("AgentPrismGetEvalSuite")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Gets a single eval suite.")
            .WithDescription(
                "The suite carries its checks, which are stored together with it rather than as " +
                "separate rows, because a suite's checks are always read and written as one " +
                "unit. Cases and runs are separate endpoints. Suites are scoped to the calling " +
                "tenant, and an unknown name returns 404.");

        builder.MapPut("/api/evals/{name}", SaveSuiteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.EvalsAdmin)
            .WithName("AgentPrismSaveEvalSuite")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Creates or updates an eval suite.")
            .Accepts<EvalSuiteSaveRequest>("application/json")
            .WithDescription("Check definitions are declarative; an unknown check type turns into an error at run time.");

        builder.MapDelete("/api/evals/{name}", DeleteSuiteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.EvalsAdmin)
            .WithName("AgentPrismDeleteEvalSuite")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Deletes an eval suite (together with its cases and runs).")
            .WithDescription(
                "The cases and every past eval run cascade with the suite, so the score history " +
                "used to compare agent versions disappears with it — export it first if it " +
                "matters. The agent runs those evals produced stay in the run history and are " +
                "still readable there. An unknown name returns 404.");

        builder.MapGet("/api/evals/{name}/cases", ListCasesAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("AgentPrismListEvalCases")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Lists a suite's cases.")
            .WithDescription(
                "Cases come back in their stored order, and that order is their identity: a case " +
                "is addressed by its sequence number, so reordering the list changes which case " +
                "a past result refers to. An unknown suite name returns 404, while a suite with " +
                "no cases returns an empty list.");

        builder.MapPut("/api/evals/{name}/cases", SaveCasesAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.EvalsAdmin)
            .WithName("AgentPrismSaveEvalCases")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Replaces all of a suite's cases with the given list.")
            .WithDescription(
                "This is a full replacement, not an append: cases missing from the body are " +
                "removed, so send the complete list every time. Sequence numbers are assigned " +
                "from the body's order, which means reordering the list re-numbers the cases and " +
                "past results then line up with different cases. Every case needs a non-empty " +
                "'query'; one that does not fails the whole request with 400 and nothing is " +
                "written. An unknown suite name returns 404.")
            .Accepts<IReadOnlyList<EvalCaseInput>>("application/json");

        builder.MapDelete("/api/evals/{name}/cases", ClearCasesAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.EvalsAdmin)
            .WithName("AgentPrismClearEvalCases")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Deletes all of a suite's cases.")
            .WithDescription(
                "The suite itself survives with its checks intact; only the cases go. Past eval " +
                "runs and their per-case results are kept, but they then point at cases that no " +
                "longer exist. The call is idempotent — clearing an already empty suite still " +
                "answers 204. An unknown suite name returns 404.");

        builder.MapPost("/api/evals/{name}/cases/from-run/{runId:guid}", PromoteRunToCaseAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.EvalsAdmin)
            .WithName("AgentPrismPromoteRunToEvalCase")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Promotes a run to an eval case in a single request.")
            .Accepts<EvalCasePromotionRequest>(true, "application/json")
            .WithDescription(
                "The query is read from the run's own session; runs without a session " +
                "cannot be promoted. If the same run is promoted a second time, the " +
                "existing case is returned (200, not 201).");

        builder.MapPost("/api/evals/{name}/run", TriggerRunAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismTriggerEvalRun")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Runs an eval suite now.")
            .Accepts<EvalRunTriggerRequest>(true, "application/json")
            .WithDescription(
                "Each case runs in a new session on the agent being evaluated and produces " +
                "its own 'runs' row. The run is queued as a job; results are processed in the background.");

        builder.MapGet("/api/evals/{name}/runs", ListRunsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("AgentPrismListEvalRuns")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Lists a suite's past runs.")
            .WithDescription(
                "Each entry is one execution of the whole suite with its aggregate outcome; the " +
                "per-case results live behind the single eval-run endpoint. Comparing entries " +
                "over time is how a regression between agent versions is spotted. Paging is " +
                "offset based, with 'skip' defaulting to 0 and 'take' to 50. An unknown suite " +
                "name returns 404.");

        builder.MapGet("/api/evals/runs/{id:guid}", GetRunAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("AgentPrismGetEvalRun")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Gets a single eval run and its per-case results.")
            .WithDescription(
                "This is the endpoint to poll after triggering a suite: the eval run is queued " +
                "and processed in the background, and its results fill in as cases complete. " +
                "Each result names the agent run it came from, so a failing check can be traced " +
                "to the exact conversation. Per-case results are a retention target, so an old " +
                "eval run may keep its summary while its details are gone. An unknown id, or one " +
                "belonging to another tenant, returns 404.");

        builder.MapGet("/api/evaluation/online", GetOnlineEvaluationSummaryAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("AgentPrismGetOnlineEvaluationSummary")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Returns a summary of the online evaluation window.")
            .WithDescription(
                "Returns the average judge score, sample count, and judge cost within the " +
                "window. The summary is in-memory (it resets when the process restarts); for " +
                "an authoritative result, the 'run_scores' table can be queried directly.");

        builder.MapPost("/api/runs/{runId:guid}/judge", JudgeRunAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismJudgeRun")
            .WithTags("AgentPrism", "Evals")
            .WithSummary("Manually has judge(s) score a run.")
            .WithDescription(
                "This SKIPS the sampling decision; it is for calibration and debugging. " +
                "If no IRunJudge is registered, or the run's input/output cannot be read, " +
                "an empty list is returned. Judging both READS the run and WRITES a score for " +
                "it, so a registered IRunAuthorizationHandler is asked for both; either denial " +
                "returns 404, identical to a run that does not exist.");
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
            // Checks are validated at save time: an unknown type name is reported
            // immediately, before a run starts.
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
        [FromServices] IRunStore runs,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
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

        // Promotion COPIES the run's recorded input into an eval case, which
        // then stays readable through the evals API - so it is a read of that
        // run and passes the same gate. A run that is missing or belongs to
        // another tenant is left to the promoter, which already answers with
        // the identical "run not found" body.
        if (runAuthorizationHandler is not null &&
            await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false) is { } sourceRun &&
            string.Equals(sourceRun.TenantId, tenants.TenantId, StringComparison.Ordinal) &&
            await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler, tenants, runId, sourceRun.AgentName, sourceRun.SessionId,
                    attributionContext, RunAccess.Read, RunNotFoundForPromotion(runId), cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
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
                    after: AuditPayload.Write(writer =>
                    {
                        writer.WriteString("suiteId", suite.Id);
                        writer.WriteString("runId", runId);
                        writer.WriteString("sourceKind", outcome.Case.SourceKind.ToString());
                    }),
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
                HandlerKey = JobHandlerKeys.Eval,
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

    private static async Task<Results<Ok<JudgeRunResponse>, ProblemHttpResult>> JudgeRunAsync(
        Guid runId,
        [FromServices] IRunStore runs,
        [FromServices] OnlineEvalJobHandler jobHandler,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        CancellationToken cancellationToken)
    {
        var run = await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // "Does not exist" and "belongs to another tenant" return the SAME 404;
        // a distinct message would leak existence (same rationale as
        // RunEndpoints.SaveFeedbackAsync).
        if (run is null || !string.Equals(run.TenantId, tenants.TenantId, StringComparison.Ordinal))
        {
            return RunNotFoundForPromotion(runId);
        }

        // 🚨 Judging does BOTH things the gate distinguishes: it reads the
        // run's recorded input and output, and it writes a score for that run.
        // Asking for only one of them would leave the other reachable through
        // this route while it is refused on '/api/runs/{id}' next door
        // (phase 147).
        foreach (var access in new[] { RunAccess.Read, RunAccess.Feedback })
        {
            if (await RunAuthorizationGate
                    .CheckRunResourceAsync(
                        runAuthorizationHandler, tenants, runId, run.AgentName, run.SessionId,
                        attributionContext, access, RunNotFoundForPromotion(runId), cancellationToken)
                    .ConfigureAwait(false) is { } authorizationProblem)
            {
                return authorizationProblem;
            }
        }

        var (scores, failures) = await jobHandler.JudgeRunAsync(run, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (scores.Count == 0 && failures.Count > 0)
        {
            return TypedResults.Problem(
                title: "Manual scoring failed",
                detail: string.Join("; ", failures.Select(static failure => $"{failure.JudgeName} ({failure.ErrorType})")),
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
            after: AuditPayload.Write(writer =>
            {
                writer.WriteNumber("scoredBy", scores.Count);
                writer.WriteNumber("failed", failures.Count);
            }),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new JudgeRunResponse { Scores = scores, Failures = failures });
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

    // "Does not exist" and "belongs to another tenant" return the SAME 404;
    // a distinct message would leak existence (same rationale as
    // RunEndpoints.SaveFeedbackAsync).
    private static ProblemHttpResult RunNotFoundForPromotion(Guid runId)
        => TypedResults.Problem(
            title: "Run not found",
            detail: $"There is no run with id '{runId}'.",
            statusCode: StatusCodes.Status404NotFound);
}
