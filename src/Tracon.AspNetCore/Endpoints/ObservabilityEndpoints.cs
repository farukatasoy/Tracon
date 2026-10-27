using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Tracon;

/// <summary>
/// Telemetry endpoints: a run's span tree, tool calls and non-token usage.
/// </summary>
internal static class ObservabilityEndpoints
{
    /// <summary>Maps the telemetry endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/runs/{runId:guid}/trace", async Task<Results<Ok<RunTrace>, ProblemHttpResult>> (
                Guid runId,
                ITraceStore traces,
                [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                [FromServices] IRunAttributionContext? attributionContext,
                ITenantContext tenants,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                // The trace store is scoped to the ambient tenant, so a trace
                // that comes back is already this tenant's; the gate is asked
                // after that, and its denial reuses the SAME "not found" body.
                if (await traces.GetTraceByRunAsync(runId, cancellationToken).ConfigureAwait(false) is not { } trace)
                {
                    return TraceNotFound(runId);
                }

                if (await RunAuthorizationGate
                        .CheckRunResourceAsync(
                            runAuthorizationHandler,
                            tenants,
                            runId,
                            agentName: null,
                            sessionId: null,
                            attributionContext,
                            RunAccess.Read,
                            TraceNotFound(runId),
                            httpContext,
                            cancellationToken)
                        .ConfigureAwait(false) is { } authorizationProblem)
                {
                    return authorizationProblem;
                }

                return TypedResults.Ok(trace);
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconGetRunTrace")
            .WithTags("Tracon", "Runs")
            .WithSummary("Returns a run's span tree.")
            .WithDescription(
                "Spans are written with sampling. Spans for failed runs are always recorded " +
                "by default; successes are recorded at a configurable rate.");

        builder.MapGet("/api/runs/{runId:guid}/tools", async Task<Results<Ok<IReadOnlyList<ToolInvocationRecord>>, ProblemHttpResult>> (
                Guid runId,
                IRunStore runs,
                [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                [FromServices] IRunAttributionContext? attributionContext,
                ITenantContext tenants,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                // 🚨 The run is looked up FIRST, which this endpoint did not do
                // before: it used to answer an unknown run with an empty 200.
                // A denial that returned 404 while a missing run returned
                // '200 []' would be an existence oracle - exactly the side
                // channel K-671 exists to close. So an unknown run now gets the
                // same 404 its siblings (/trace, /input, /feedback) already
                // gave, and a denial is indistinguishable from it.
                if (await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false) is not { } record)
                {
                    return RunNotFound(runId);
                }

                if (await RunAuthorizationGate
                        .CheckRunResourceAsync(
                            runAuthorizationHandler,
                            tenants,
                            runId,
                            record.AgentName,
                            record.SessionId,
                            attributionContext,
                            RunAccess.Read,
                            RunNotFound(runId),
                            httpContext,
                            cancellationToken)
                        .ConfigureAwait(false) is { } authorizationProblem)
                {
                    return authorizationProblem;
                }

                return TypedResults.Ok(
                    await runs.ListToolInvocationsAsync(runId, cancellationToken).ConfigureAwait(false));
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconListRunToolInvocations")
            .WithTags("Tracon", "Runs")
            .WithSummary("Lists a run's tool calls in chronological order.")
            .WithDescription(
                "A call that went through the tool chain (a code tool, an MCP tool, a workspace tool, the " +
                "knowledge search, the conversation memory search) is recorded where it happens, so its row " +
                "exists whether the run streams or not and whether it ends or fails, and its duration is " +
                "measured. A hosted tool, a tool of the framework (such as run_skill_script) and an agent " +
                "called as a tool are outside the chain: their rows come from the messages of the answer, so " +
                "a run that does not stream has a duration for none of them, and has no row for them when it " +
                "fails. A run that does not exist, belongs to another tenant, or is denied by a registered " +
                "IRunAuthorizationHandler returns the same 404.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        builder.MapGet("/api/runs/{runId:guid}/usage", async Task<Results<Ok<IReadOnlyList<NonTokenUsageRecord>>, ProblemHttpResult>> (
                Guid runId,
                IRunStore runs,
                [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                [FromServices] IRunAttributionContext? attributionContext,
                ITenantContext tenants,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                // 🚨 Same order as /tools above, for the same reason (K-671): the run is
                // looked up FIRST, so an unknown run, another tenant's run and a denied
                // run all answer the same 404. An empty 200 for a missing run beside a
                // 404 for a denied one would be an existence oracle.
                if (await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false) is not { } record)
                {
                    return RunNotFound(runId);
                }

                if (await RunAuthorizationGate
                        .CheckRunResourceAsync(
                            runAuthorizationHandler,
                            tenants,
                            runId,
                            record.AgentName,
                            record.SessionId,
                            attributionContext,
                            RunAccess.Read,
                            RunNotFound(runId),
                            httpContext,
                            cancellationToken)
                        .ConfigureAwait(false) is { } authorizationProblem)
                {
                    return authorizationProblem;
                }

                return TypedResults.Ok(
                    await runs.ListNonTokenUsageAsync(runId, cancellationToken).ConfigureAwait(false));
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconListRunNonTokenUsage")
            .WithTags("Tracon", "Runs")
            .WithSummary("Lists the non-token usage items a run's workflow function nodes reported.")
            .WithDescription(
                "Each item is one call of IWorkflowContext.ReportUsageAsync, in the order reported: " +
                "the node's name, the unit and quantity, and the amount when the node reported one. " +
                "The items of the run itself only, not of the runs below it; a tool's measurement stays on " +
                "GET /api/runs/{runId}/tools. The run's total is on the run record (nonTokenCost, " +
                "treeNonTokenCost). A run that does not exist, belongs to another tenant, or is denied by a " +
                "registered IRunAuthorizationHandler returns the same 404.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        builder.MapGet("/api/tools/usage", async Task<Ok<IReadOnlyList<ToolUsage>>> (
                IRunStore runs,
                DateTimeOffset? startedAfter,
                int? maxTools,
                CancellationToken cancellationToken)
                => TypedResults.Ok(await runs.GetToolUsageAsync(
                    new ToolUsageQuery
                    {
                        StartedAfter = startedAfter,
                        MaxTools = maxTools is { } max ? Math.Clamp(max, 1, 200) : 50,
                    },
                    cancellationToken).ConfigureAwait(false)))
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconToolUsage")
            .WithTags("Tracon", "Runs")
            .WithSummary("Returns call count, error rate, and average duration per tool.")
            .WithDescription("The summary is computed by the store itself; it is not a paginated subset.");
    }

    private static ProblemHttpResult TraceNotFound(Guid runId)
        => TypedResults.Problem(
            title: "Trace not found",
            detail: $"There are no recorded spans for run '{runId}'. Span writing is " +
                    "sampled: only a portion of successful runs is recorded " +
                    "(Tracon:Observability:SuccessSampleRatio).",
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>Kept identical to <c>RunEndpoints.NotFound</c>: the two answer the same question.</summary>
    private static ProblemHttpResult RunNotFound(Guid runId)
        => TypedResults.Problem(
            title: "Run not found",
            detail: $"There is no run with id '{runId}'.",
            statusCode: StatusCodes.Status404NotFound);
}
