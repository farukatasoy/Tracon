using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Read-only catalog endpoints: tools, model providers, and run summaries.
/// </summary>
internal static class CatalogEndpoints
{
    /// <summary>Maps the catalog endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/tools", Ok<IReadOnlyList<ToolDescriptor>> (IToolRegistry tools)
                => TypedResults.Ok(tools.List()))
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("TraconListTools")
            .WithTags("Tracon", "Agents")
            .WithSummary("Lists registered tools and their JSON schemas.")
            .WithDescription(
                "Tools are defined only in code. This endpoint does not offer a write path; " +
                "the UI lets users pick from this list when defining an agent.");

        builder.MapGet("/api/models", Ok<IReadOnlyList<ModelProviderDescriptor>> (
                IModelProviderRegistry models,
                ModelProviderHealthCache healthCache) =>
            {
                var descriptors = models.List();
                var withStatus = new List<ModelProviderDescriptor>(descriptors.Count);

                foreach (var descriptor in descriptors)
                {
                    withStatus.Add(healthCache.TryPeek(descriptor.Name, out var health)
                        ? descriptor with { Status = health.Status }
                        : descriptor);
                }

                return TypedResults.Ok<IReadOnlyList<ModelProviderDescriptor>>(withStatus);
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("TraconListModels")
            .WithTags("Tracon", "Agents")
            .WithSummary("Lists registered model providers and their models.")
            .WithDescription(
                "The model catalog comes from configuration; Tracon does not ship a built-in model list. " +
                "An empty list is not an error. The catalog is also not a validation list: " +
                "a model name that is not listed here can still be used. The `status` field comes " +
                "FROM THE CACHE, and this endpoint makes no network call to the provider; use " +
                "/api/models/health for an up-to-date check.");

        builder.MapGet("/api/stats", async Task<Ok<RunStatistics>> (
                IRunStore runs,
                string? agentName,
                string? userId,
                string? label,
                DateTimeOffset? startedAfter,
                int? maxAgents,
                CancellationToken cancellationToken) =>
            {
                var (labelKey, labelValue) = RunAttributionGate.ParseLabelFilter(label);

                var statistics = await runs.GetStatisticsAsync(
                    new RunStatisticsQuery
                    {
                        AgentName = agentName,
                        UserId = userId,
                        LabelKey = labelKey,
                        LabelValue = labelValue,
                        StartedAfter = startedAfter,
                        MaxAgents = maxAgents is { } max ? Math.Clamp(max, 0, 200) : 20,
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(statistics);
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconStats")
            .WithTags("Tracon", "Agents")
            .WithSummary("Returns run counts, token totals, and the error rate.")
            .WithDescription(
                "The summary is computed in the store itself. Cost is populated " +
                "only when pricing is configured (model catalog or Tracon:Pricing); " +
                "the count of models with undefined pricing is counted separately in the " +
                "RunsWithUnknownPricing field — it is not written as zero. Every breakdown " +
                "(byAgent, byModel, byVersion, byUser, byLabel) is ALWAYS returned; there is no " +
                "groupBy switch. 'userId' and 'label' ('key:value') narrow the whole summary " +
                "rather than choosing a breakdown. byLabel rows do NOT sum to totalRuns: a run " +
                "carrying three labels appears in three of them. cachedInputTokens and " +
                "reasoningTokens are counted INSIDE inputTokens/outputTokens, so adding them " +
                "double counts.");

        builder.MapGet("/api/stats/timeseries", async Task<Results<Ok<IReadOnlyList<TimeSeriesPoint>>, ProblemHttpResult>> (
                IRunStore runs,
                DateTimeOffset? from,
                DateTimeOffset? to,
                TimeSeriesBucket? bucket,
                string? agentName,
                string? modelId,
                RunKind? kind,
                [FromServices] TimeProvider? timeProvider,
                CancellationToken cancellationToken) =>
            {
                var effectiveTo = to ?? (timeProvider ?? TimeProvider.System).GetUtcNow();
                var effectiveFrom = from ?? effectiveTo.AddHours(-24);

                if (effectiveFrom >= effectiveTo)
                {
                    return TypedResults.Problem(
                        title: "Range invalid",
                        detail: "'from' must be before 'to'.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                try
                {
                    var points = await runs.GetTimeSeriesAsync(
                        new RunTimeSeriesQuery
                        {
                            From = effectiveFrom,
                            To = effectiveTo,
                            Bucket = bucket ?? TimeSeriesBucket.Hour,
                            AgentName = agentName,
                            ModelId = modelId,
                            Kind = kind,
                        },
                        cancellationToken).ConfigureAwait(false);

                    return TypedResults.Ok(points);
                }
                catch (TraconException ex)
                {
                    return TypedResults.Problem(
                        title: "Bucket count exceeded",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status400BadRequest);
                }
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconStatsTimeSeries")
            .WithTags("Tracon", "Agents")
            .WithSummary("Per-bucket time series of runs, errors, tokens, and cost.")
            .WithDescription(
                "Empty buckets are returned too. The default range is the last 24 hours, " +
                "the default bucket is an hour. At most 500 buckets; exceeding that returns 400. " +
                "Unlike /api/stats, this endpoint does NOT exclude Eval/Workflow runs " +
                "by default; it can be filtered with ?kind=.");

        builder.MapGet("/api/stats/errors", async Task<Ok<IReadOnlyList<RunErrorStatistics>>> (
                IRunStore runs,
                string? agentName,
                double? hours,
                [FromServices] TimeProvider? timeProvider,
                CancellationToken cancellationToken) =>
            {
                // At most 30 days: a wider range would have to scan the entire
                // bucket table and group all rows by class+fingerprint; the same
                // rationale as the bucket limit in /api/stats/timeseries.
                var effectiveHours = Math.Clamp(hours ?? 24, 0.01, 24 * 30);
                var startedAfter = (timeProvider ?? TimeProvider.System).GetUtcNow().AddHours(-effectiveHours);

                var statistics = await runs.GetStatisticsAsync(
                    new RunStatisticsQuery
                    {
                        AgentName = agentName,
                        StartedAfter = startedAfter,
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(statistics.ByErrorClass);
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconStatsErrors")
            .WithTags("Tracon", "Agents")
            .WithSummary("Returns the breakdown by error class and each class's top three clusters.")
            .WithDescription(
                "This is a narrow slice of /api/stats: it returns only the ByErrorClass field " +
                "(that field is also present in the /api/stats response). The default range " +
                "is the last 24 hours, changed with ?hours=. Rows written before error " +
                "classification existed appear in the Unknown bucket; a high Unknown share " +
                "means the taxonomy is incomplete.");

        builder.MapPost("/api/stats/recalculate-costs", async Task<Ok<RunCostRecalculationResult>> (
                RunCostRecalculationService recalculation,
                IAuditLog auditLog,
                IAuditActorResolver actorResolver,
                ITenantContext tenants,
                ILoggerFactory loggerFactory,
                [FromServices] TraconMetrics metrics,
                CancellationToken cancellationToken) =>
            {
                var result = await recalculation.RecalculateAsync(tenants.TenantId, cancellationToken)
                    .ConfigureAwait(false);

                await AuditRecorder.WriteAsync(
                    auditLog,
                    actorResolver,
                    loggerFactory.CreateLogger("Tracon.CatalogEndpoints"),
                    metrics,
                    tenants.TenantId,
                    action: "stats.recalculate-costs",
                    entity: "runs:*",
                    before: null,
                    after: AuditPayload.Write(writer =>
                    {
                        writer.WriteNumber("considered", result.RunsConsidered);
                        writer.WriteNumber("updated", result.RunsUpdated);
                        writer.WriteNumber("stillUnknown", result.RunsStillUnknown);
                        writer.WriteNumber("skipped", result.RunsSkipped);
                    }),
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(result);
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconRecalculateCosts")
            .WithTags("Tracon", "Agents")
            .WithSummary("Fills in the cost of runs whose price is still unknown.")
            .WithDescription(
                "This is a maintenance endpoint. It fills in the cost of runs whose price " +
                "is still unknown (unpriced when they completed, typically because the " +
                "model was not configured yet); a run's cost is a price snapshot and this " +
                "endpoint never rewrites one that already has a known price, even if the " +
                "price list changed since. A run written before the provider column existed " +
                "resolves by model name alone; if the same model name is defined for more " +
                "than one provider, the first alphabetical match wins. Requires Admin; " +
                "the call is written to the audit trail.");
    }
}
