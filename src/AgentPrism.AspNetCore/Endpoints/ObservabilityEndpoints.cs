using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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
                CancellationToken cancellationToken)
                => await traces.GetTraceByRunAsync(runId, cancellationToken).ConfigureAwait(false) is { } trace
                    ? TypedResults.Ok(trace)
                    : TypedResults.Problem(
                        title: "Trace not found",
                        detail: $"There are no recorded spans for run '{runId}'. Span writing is " +
                                "sampled: only a portion of successful runs is recorded " +
                                "(AgentPrism:Observability:SuccessSampleRatio).",
                        statusCode: StatusCodes.Status404NotFound))
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismGetRunTrace")
            .WithTags("AgentPrism", "Runs")
            .WithSummary("Returns a run's span tree.")
            .WithDescription(
                "Spans are written with sampling. Spans for failed runs are always recorded " +
                "by default; successes are recorded at a configurable rate.");

        builder.MapGet("/api/runs/{runId:guid}/tools", async Task<Ok<IReadOnlyList<ToolInvocationRecord>>> (
                Guid runId,
                IRunStore runs,
                CancellationToken cancellationToken)
                => TypedResults.Ok(
                    await runs.ListToolInvocationsAsync(runId, cancellationToken).ConfigureAwait(false)))
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismListRunToolInvocations")
            .WithTags("AgentPrism", "Runs")
            .WithSummary("Lists a run's tool calls in chronological order.")
            .WithDescription(
                "Duration is measured only for streaming runs: in a non-streaming run all " +
                "messages arrive at once, so the true duration between call and result cannot be read.");

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
}
