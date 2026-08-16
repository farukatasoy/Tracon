using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Model provider health check endpoints.
/// </summary>
/// <remarks>
/// The check incurs no cost: it hits the <c>GET {endpoint}/models</c> endpoint, it
/// makes no model call. The result is cached (60 s by default); <c>?refresh=true</c>
/// bypasses the cache. See <c>docs/08-SAGLAYICI-GENISLEMESI.md</c>, section 8.3.
/// </remarks>
internal static class ModelHealthEndpoints
{
    /// <summary>Maps the health check endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/models/health", async Task<Ok<IReadOnlyList<ModelProviderHealth>>> (
                ModelProviderHealthCache cache,
                bool? refresh,
                CancellationToken cancellationToken)
                => TypedResults.Ok(await cache.GetAllAsync(refresh ?? false, cancellationToken).ConfigureAwait(false)))
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismModelsHealth")
            .WithTags("AgentPrism", "Models")
            .WithSummary("Returns the cached health status of all registered providers.")
            .WithDescription(
                "If a provider does not implement IModelProviderHealthCheck, its status is " +
                "Unknown; this is not an error. A provider whose circuit is open shows as " +
                "Unhealthy. The error detail does NOT include an API key or endpoint address.");

        builder.MapGet("/api/models/health/{provider}", async Task<Results<Ok<ModelProviderHealth>, ProblemHttpResult>> (
                string provider,
                ModelProviderHealthCache cache,
                bool? refresh,
                CancellationToken cancellationToken) =>
            {
                var health = await cache.GetAsync(provider, refresh ?? false, cancellationToken).ConfigureAwait(false);

                return health is null
                    ? TypedResults.Problem(
                        title: "Provider not found",
                        detail: $"There is no registered model provider named '{provider}'.",
                        statusCode: StatusCodes.Status404NotFound)
                    : TypedResults.Ok(health);
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismModelHealth")
            .WithTags("AgentPrism", "Models")
            .WithSummary("Returns the cached health status of a single provider.")
            .WithDescription(
                "The status is served from the cache; add '?refresh=true' to force a fresh check. " +
                "A check reads the provider's model list and never runs a completion, so it costs " +
                "nothing. An unregistered provider name returns 404 — which is different from a " +
                "registered provider that reports Unknown because it implements no health check. " +
                "Error details never carry an API key or an endpoint address.");
    }
}
