using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Telemetry endpoints: a run's span tree and tool calls.
/// </summary>
internal static class ObservabilityEndpoints
{
    /// <summary>Maps the telemetry endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/runs/{runId:guid}/trace", async Task<Results<Ok<RunTrace>, ProblemHttpResult>> (
                Guid runId,
                ITraceStore traces,
                [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                [FromServices] IRunAttributionContext? attributionContext,
                ITenantContext tenants,
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
                            cancellationToken)
                        .ConfigureAwait(false) is { } authorizationProblem)
                {
                    return authorizationProblem;
                }

                return TypedResults.Ok(trace);
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismGetRunTrace")
            .WithTags("AgentPrism", "Runs")
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
            .WithName("AgentPrismListRunToolInvocations")
            .WithTags("AgentPrism", "Runs")
            .WithSummary("Lists a run's tool calls in chronological order.")
            .WithDescription(
                "Duration is measured only for streaming runs: in a non-streaming run all " +
                "messages arrive at once, so the true duration between call and result cannot be read. " +
                "A run that does not exist, belongs to another tenant, or is denied by a registered " +
                "IRunAuthorizationHandler returns the same 404.")
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
            .WithName("AgentPrismToolUsage")
            .WithTags("AgentPrism", "Runs")
            .WithSummary("Returns call count, error rate, and average duration per tool.")
            .WithDescription("The summary is computed by the store itself; it is not a paginated subset.");
    }

    private static ProblemHttpResult TraceNotFound(Guid runId)
        => TypedResults.Problem(
            title: "Trace not found",
            detail: $"There are no recorded spans for run '{runId}'. Span writing is " +
                    "sampled: only a portion of successful runs is recorded " +
                    "(AgentPrism:Observability:SuccessSampleRatio).",
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>Kept identical to <c>RunEndpoints.NotFound</c>: the two answer the same question.</summary>
    private static ProblemHttpResult RunNotFound(Guid runId)
        => TypedResults.Problem(
            title: "Run not found",
            detail: $"There is no run with id '{runId}'.",
            statusCode: StatusCodes.Status404NotFound);
}
