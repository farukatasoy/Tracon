using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>Self-diagnosing setup diagnostics endpoint.</summary>
/// <remarks>
/// <para>
/// The response never carries any <c>secret</c> value: it returns only the
/// configuration key name and whether it resolved, never the value under any
/// condition.
/// </para>
/// <para>
/// Defaults to <strong>disabled</strong> — this endpoint is never mapped unless
/// <see cref="AgentPrismEndpointOptions.EnableDiagnosticsEndpoint"/> is turned on. When
/// enabled it requires <see cref="AgentPrismPolicies.Admin"/>.
/// </para>
/// </remarks>
internal static class DiagnosticsEndpoints
{
    /// <summary>Maps the diagnostics endpoint.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="services">The built service provider, used to resolve whether the UI is embedded once.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, IServiceProvider services, AgentPrismRolePolicies roles)
    {
        // IAgentPrismUiProvider may not be registered (the AgentPrism.UI package may not
        // be added). Phase 28 lesson: instead of marking a nullable service as an
        // endpoint parameter, resolve it here ONCE with the pattern MapUi follows and
        // catch it at closure; an unregistered service must not be mistaken for a "body"
        // and break every endpoint.
        var uiProvider = services.GetService<IAgentPrismUiProvider>();

        builder.MapGet("/api/diagnostics", async Task<Ok<AgentPrismDiagnosticsReport>> (
                [FromServices] AgentPrismDiagnosticsCollector collector,
                CancellationToken cancellationToken) =>
            {
                var report = await collector.CollectAsync(cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(report with { UiEmbedded = uiProvider?.HasAssets ?? false });
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismDiagnostics")
            .WithTags("AgentPrism", "Diagnostics")
            .WithSummary("Returns the setup's self-diagnosing summary report.")
            .WithDescription(
                "Never carries any secret value. Model provider status is read " +
                "from the cache; it makes no model call and applies no migration.");
    }
}
