using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

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
    private const int DefaultDiffPageSize = 50;
    private const int MaxDiffPageSize = 500;
    private const int MaxRunScoreRows = 500;
    private const int MaxRunScoreSeriesDays = 90;

    /// <summary>Maps the eval endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/evals", ListSuitesAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("TraconListEvalSuites")
            .WithTags("Tracon", "Evals")
            .WithSummary("Lists a tenant's eval suites.")
            .WithDescription(
                "Each entry is a suite's definition — the agent under test and its check " +
                "definitions — without the cases or the past runs; read those from the cases and " +
                "runs endpoints. The response is not paged.");

        builder.MapGet("/api/evals/{name}", GetSuiteAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("TraconGetEvalSuite")
            .WithTags("Tracon", "Evals")
            .WithSummary("Gets a single eval suite.")
            .WithDescription(
                "The suite carries its checks, which are stored together with it rather than as " +
                "separate rows, because a suite's checks are always read and written as one " +
                "unit. Cases and runs are separate endpoints. Suites are scoped to the calling " +
                "tenant, and an unknown name returns 404.");

        builder.MapPut("/api/evals/{name}", SaveSuiteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.EvalsAdmin)
            .WithName("TraconSaveEvalSuite")
            .WithTags("Tracon", "Evals")
            .WithSummary("Creates or updates an eval suite.")
            .Accepts<EvalSuiteSaveRequest>("application/json")
            .WithDescription("Check definitions are declarative; an unknown check type turns into an error at run time.");

        builder.MapDelete("/api/evals/{name}", DeleteSuiteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.EvalsAdmin)
            .WithName("TraconDeleteEvalSuite")
            .WithTags("Tracon", "Evals")
            .WithSummary("Deletes an eval suite (together with its cases and runs).")
            .WithDescription(
                "The cases and every past eval run cascade with the suite, so the score history " +
                "used to compare agent versions disappears with it — export it first if it " +
                "matters. The agent runs those evals produced stay in the run history and are " +
                "still readable there. An unknown name returns 404.");

        builder.MapGet("/api/evals/{name}/cases", ListCasesAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("TraconListEvalCases")
            .WithTags("Tracon", "Evals")
            .WithSummary("Lists a suite's cases.")
            .WithDescription(
                "Cases come back in their stored order. Each carries an 'id' that is its " +
                "identity for life — send it back on a replace to keep the case, and the " +
                "run-to-run diff behind '--baseline' will read it as the same case. The " +
                "sequence number is position, not identity: reordering the list re-numbers the " +
                "cases without changing which case is which. An unknown suite name returns 404, " +
                "while a suite with no cases returns an empty list.");

        builder.MapPut("/api/evals/{name}/cases", SaveCasesAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.EvalsAdmin)
            .WithName("TraconSaveEvalCases")
            .WithTags("Tracon", "Evals")
            .WithSummary("Replaces all of a suite's cases with the given list.")
            .WithDescription(
                "This is a full replacement, not an append: cases missing from the body are " +
                "removed, so send the complete list every time. Send each kept case back with " +
                "the 'id' it was listed with — a case holds that id for its whole life and the " +
                "run-to-run diff behind '--baseline' is matched on it, so a case that arrives " +
                "without one is a NEW case and an unchanged case sent without its id reads as " +
                "one removed and another added. An id that does not belong to this suite, or " +
                "that appears twice, fails the whole request with 400. 'parameters' travels the " +
                "same way: it is part of the case, and a parameterized agent's case sent " +
                "without it then fails the missing-parameter check at run time. Promotion data " +
                "is the server's own and follows the id: keep the case, keep its origin. " +
                "Sequence numbers are assigned from the body's order, which means reordering " +
                "the list re-numbers the cases. Every case needs a non-empty 'query'; one that " +
                "does not fails the whole request with 400 and nothing is written. An unknown " +
                "suite name returns 404.")
            .Accepts<IReadOnlyList<EvalCaseInput>>("application/json");

        builder.MapDelete("/api/evals/{name}/cases", ClearCasesAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.EvalsAdmin)
            .WithName("TraconClearEvalCases")
            .WithTags("Tracon", "Evals")
            .WithSummary("Deletes all of a suite's cases.")
            .WithDescription(
                "The suite itself survives with its checks intact; only the cases go. Past eval " +
                "runs and their per-case results are kept, but they then point at cases that no " +
                "longer exist. The call is idempotent — clearing an already empty suite still " +
                "answers 204. An unknown suite name returns 404.");

        builder.MapPost("/api/evals/{name}/cases/from-run/{runId:guid}", PromoteRunToCaseAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.EvalsAdmin)
            .WithName("TraconPromoteRunToEvalCase")
            .WithTags("Tracon", "Evals")
            .WithSummary("Promotes a run to an eval case in a single request.")
            .Accepts<EvalCasePromotionRequest>(true, "application/json")
            .WithDescription(
                "The query is read from the run's own session; runs without a session " +
                "cannot be promoted. If the same run is promoted a second time, the " +
                "existing case is returned (200, not 201).");

        builder.MapPost("/api/evals/{name}/run", TriggerRunAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconTriggerEvalRun")
            .WithTags("Tracon", "Evals")
            .WithSummary("Runs an eval suite now.")
            .Accepts<EvalRunTriggerRequest>(true, "application/json")
            .WithDescription(
                "Each case runs in a new session on the agent being evaluated and produces " +
                "its own 'runs' row. The run is queued as a job; results are processed in the background.");

        builder.MapGet("/api/evals/{name}/runs", ListRunsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("TraconListEvalRuns")
            .WithTags("Tracon", "Evals")
            .WithSummary("Lists a suite's past runs.")
            .WithDescription(
                "Each entry is one execution of the whole suite with its aggregate outcome; the " +
                "per-case results live behind the single eval-run endpoint. To find the " +
                "regression between two of these entries, hand both to the eval-run diff " +
                "endpoint: it aligns them case by case instead of leaving the comparison to " +
                "the caller. Paging is offset based, with 'skip' defaulting to 0 and 'take' " +
                "to 50. An unknown suite name returns 404.");

        builder.MapGet("/api/evals/runs/{id:guid}", GetRunAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("TraconGetEvalRun")
            .WithTags("Tracon", "Evals")
            .WithSummary("Gets a single eval run and its per-case results.")
            .WithDescription(
                "This is the endpoint to poll after triggering a suite: the eval run is queued " +
                "and processed in the background, and its results fill in as cases complete. " +
                "Each result names the agent run it came from, so a failing check can be traced " +
                "to the exact conversation. Per-case results are a retention target, so an old " +
                "eval run may keep its summary while its details are gone. An unknown id, or one " +
                "belonging to another tenant, returns 404.");

        builder.MapGet("/api/evals/runs/{id:guid}/diff", DiffRunsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("TraconDiffEvalRuns")
            .WithTags("Tracon", "Evals")
            .WithSummary("Compares two eval runs of the same suite, case by case.")
            .WithDescription(
                "The run in the path is the candidate; 'baseline' names the run it is judged " +
                "against. Every case lands in exactly one bucket - Regressed, Fixed, " +
                "StillFailing, Unchanged, Added or Removed - and each entry names both sides' " +
                "agent run, so a regression is one click from the two conversations that " +
                "produced it. Cases added to or dropped from the suite are their own buckets " +
                "and are never counted as regressions. Paging is offset based over the aligned " +
                "cases, regressions first; the counters always describe the whole comparison. " +
                "Both runs must have completed and must measure the same suite, otherwise 400. " +
                "If retention has removed either run's per-case results the answer is 409, " +
                "never an empty diff: an empty diff would read as 'nothing changed'. An " +
                "unknown id, or one belonging to another tenant, returns 404.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        builder.MapGet("/api/evaluation/online", GetOnlineEvaluationSummaryAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("TraconGetOnlineEvaluationSummary")
            .WithTags("Tracon", "Evals")
            .WithSummary("Returns a summary of the online evaluation window.")
            .WithDescription(
                "Returns the average judge score, sample count, and judge cost within the " +
                "window. The summary is in-memory (it resets when the process restarts); for " +
                "an authoritative result that survives a restart, use " +
                "'GET /api/evaluation/scores/summary' instead.");

        builder.MapGet("/api/evaluation/scores/summary", GetRunScoreSummaryAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.EvalsRead)
            .WithName("TraconGetRunScoreSummary")
            .WithTags("Tracon", "Evals")
            .WithSummary("Aggregates run and message scores by name, author, source, and agent.")
            .WithDescription(
                "A query over the scores already written, not a counter -- unlike " +
                "'/api/evaluation/online', the result survives a process restart. Each " +
                "breakdown groups by (name, kind): a 1-5 star rating and a 0-100 numeric " +
                "score sharing a name never average together. 'messageId' is never a " +
                "breakdown dimension; use 'target' (run, message, or both) instead. " +
                "'bucket' (hour, day, or week, UTC) adds a trend series; omitting it costs " +
                "nothing extra. A bucketed series with no 'from' defaults to the last 90 " +
                "days, since a series has no other bound the way a breakdown does. Every " +
                "breakdown, and the categories inside one categorical score's entry, is " +
                "capped at 'maxRows'. 400 if 'from' is at or after 'to', or 'maxRows' is " +
                "out of range.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        builder.MapPost("/api/runs/{runId:guid}/judge", JudgeRunAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconJudgeRun")
            .WithTags("Tracon", "Evals")
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
        catch (TraconException exception)
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

        var existing = (await store.ListCasesAsync(suite.Id, cancellationToken).ConfigureAwait(false))
            .ToDictionary(static item => item.Id);

        if (IdentityError(cases, existing) is { } identityError)
        {
            return InvalidSuite(identityError);
        }

        var converted = cases
            .Select((input, seq) =>
            {
                // Promotion data is the server's own record of where a case came
                // from; a client never sends it and must not be able to. It
                // travels with the identity instead: keep the case, keep its
                // origin.
                var kept = input.Id is { } id ? existing[id] : null;

                return new EvalCase
                {
                    Id = input.Id ?? Guid.Empty,
                    SuiteId = default,
                    Seq = seq,
                    Query = input.Query,
                    ExpectedOutput = input.ExpectedOutput,
                    ExpectedTools = input.ExpectedTools,
                    Context = input.Context,
                    Parameters = input.Parameters,
                    SourceRunId = kept?.SourceRunId,
                    SourceKind = kept?.SourceKind,
                    PromotedAt = kept?.PromotedAt,
                };
            })
            .ToArray();

        var saved = await store.ReplaceCasesAsync(suite.Id, converted, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(saved);
    }

    /// <summary>
    /// Checks the identifiers in a replace body, or <see langword="null"/> when
    /// they are sound.
    /// </summary>
    /// <remarks>
    /// Both rules fail the whole request rather than the one entry, the same
    /// way an empty <c>query</c> does: a replace writes the suite's entire case
    /// list, and a partially honoured one leaves a list nobody asked for.
    /// Silently treating an unrecognised identifier as a new case is what the
    /// caller is trying to avoid by sending it at all — the diff behind
    /// <c>--baseline</c> is matched on identity, so a quietly reassigned one
    /// reads as a case removed and another added.
    /// </remarks>
    private static string? IdentityError(
        IReadOnlyList<EvalCaseInput> cases, Dictionary<Guid, EvalCase> existing)
    {
        var seen = new HashSet<Guid>();

        foreach (var input in cases)
        {
            if (input.Id is not { } id)
            {
                continue;
            }

            if (!existing.ContainsKey(id))
            {
                return FormattableString.Invariant(
                    $"Case id '{id}' does not belong to this suite. Send the id a case was returned with to keep it, or omit 'id' to create a new case.");
            }

            if (!seen.Add(id))
            {
                return FormattableString.Invariant(
                    $"Case id '{id}' appears more than once. Each case in the list needs its own id, and a copy of an existing case is a new case: omit its 'id'.");
            }
        }

        return null;
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
        [FromServices] TraconMetrics metrics,
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
                    loggerFactory.CreateLogger("Tracon.EvalEndpoints"),
                    metrics,
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
        [FromServices] IOptionsMonitor<TraconSchedulingOptions> schedulingOptions,
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
                Id = TraconId.NewId(),
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
                Id = TraconId.NewId(),
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

    private static async Task<Results<Ok<EvalRunDiff>, ProblemHttpResult>> DiffRunsAsync(
        Guid id,
        [FromQuery] Guid baseline,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        [FromServices] IEvalStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var query = new EvalRunDiffQuery
        {
            TenantId = tenants.TenantId,
            BaselineRunId = baseline,
            CandidateRunId = id,
            Skip = Math.Max(0, skip ?? 0),
            Take = Math.Clamp(take ?? DefaultDiffPageSize, 1, MaxDiffPageSize),
        };

        EvalRunDiff? diff;

        try
        {
            diff = await store.DiffRunsAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (EvalRunDiffUnavailableException exception)
        {
            // 🚨 Never softened into an empty 200: a diff with no entries reads
            // as "nothing changed" and turns a CI gate green over a regression.
            // The message is the store's own and is not translated (K-232).
            return TypedResults.Problem(
                title: "Eval runs cannot be compared",
                detail: exception.Message,
                statusCode: exception.Reason switch
                {
                    EvalRunDiffUnavailableReason.DifferentSuites => StatusCodes.Status400BadRequest,
                    EvalRunDiffUnavailableReason.RunNotCompleted => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status409Conflict,
                });
        }

        // Either run being unknown, or belonging to another tenant, is the same
        // 404 - a distinct message would leak existence.
        return diff is null ? RunNotFound(id) : TypedResults.Ok(diff);
    }

    private static async Task<Ok<OnlineEvaluationSummary>> GetOnlineEvaluationSummaryAsync(
        [FromServices] OnlineEvalSummaryService summaryService,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var summary = await summaryService.GetSummaryAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(summary);
    }

    private static async Task<Results<Ok<RunScoreSummary>, ProblemHttpResult>> GetRunScoreSummaryAsync(
        [FromServices] IRunScoreStore scores,
        [FromServices] ITenantContext tenants,
        [FromServices] TimeProvider? timeProvider,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? scoreName,
        string? agentName,
        string? source,
        string? author,
        RunScoreTarget? target,
        RunScoreBucket? bucket,
        int? maxRows,
        CancellationToken cancellationToken)
    {
        if (from is { } lowerBound && to is { } upperBound && lowerBound >= upperBound)
        {
            return TypedResults.Problem(
                title: "Range invalid",
                detail: "'from' must be before 'to'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (maxRows is { } requestedMaxRows && (requestedMaxRows < 1 || requestedMaxRows > MaxRunScoreRows))
        {
            return TypedResults.Problem(
                title: "'maxRows' out of range",
                detail: $"'maxRows' must be between 1 and {MaxRunScoreRows}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // A bucketed series has no natural upper bound the way a breakdown
        // does (MaxRows caps the groups INSIDE one bucket, not the number of
        // buckets) -- an unbounded 'from' would let a long-lived tenant's
        // series grow forever. 'from' is left alone when the caller gave it
        // (even one far in the past); this default only kicks in when a
        // bucket was asked for and no range was given at all.
        var effectiveFrom = from ?? (bucket is null
            ? null
            : (timeProvider ?? TimeProvider.System).GetUtcNow().AddDays(-MaxRunScoreSeriesDays));

        var summary = await scores.SummarizeAsync(
            new RunScoreQuery
            {
                TenantId = tenants.TenantId,
                From = effectiveFrom,
                To = to,
                ScoreName = scoreName,
                AgentName = agentName,
                Source = source,
                Author = author,
                Target = target ?? RunScoreTarget.Any,
                Bucket = bucket,
                MaxRows = maxRows ?? 20,
            },
            cancellationToken).ConfigureAwait(false);

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
        [FromServices] TraconMetrics metrics,
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
            loggerFactory.CreateLogger("Tracon.EvalEndpoints"),
            metrics,
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
