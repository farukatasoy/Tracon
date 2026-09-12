using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Regression fence (Family F, manual acceptance test closure): EVERY API
/// endpoint in the protected group must carry an <c>ApiKeyScopeRequirement</c>
/// — except for a small, annotated, fixed-size exemption list.
/// </summary>
/// <remarks>
/// 🚨 This test blocks a return to the 21-file "no scope enforcement" gap
/// (HATA-S2-009, HATA-S2-011, HATA-S3-009). If a new endpoint is added and
/// <c>RequireApiKeyScope</c> is forgotten, this test breaks not at compile
/// time but on the first CI run — the silent gap does not reopen.
/// </remarks>
public sealed class ApiKeyScopeCoverageTests
{
    /// <summary>
    /// Endpoints that PASS bearer token validation but do NOT carry an
    /// <c>ApiKeyScopeRequirement</c> (docs/arsiv/manuel-test-kosum-2026-08/KAPANIS-PLANI.md §7.2 +
    /// Family F note). Each one is justified; adding to the list is a
    /// deliberate decision. These ALWAYS map in the default test host (the UI
    /// shell and the voice endpoint never connect at all when the UI provider
    /// and the voice driver are not registered — those two are in
    /// <see cref="ConditionallyMappedExemptRoutePatterns"/>).
    /// </summary>
    private static readonly HashSet<string> ExemptRoutePatterns = new(StringComparer.Ordinal)
    {
        // Reachable without authentication; carries no sensitive data.
        "/tracon/api/meta",
        // A browser redirected by the provider cannot carry a bearer token.
        "/tracon/api/mcp-servers/{name}/oauth/callback",
        // Describes the caller's own identity; adding a scope would needlessly
        // break every narrowly scoped key's startup probe.
        "/tracon/api/tenants/current",
        // An external system (e.g. Slack) cannot carry our bearer token or an
        // API key; its identity is an HMAC signature verified by
        // InboundTriggerDispatcher itself (phase 66, K-395's pattern).
        "/tracon/api/triggers/{tenantId}/{name}",
    };

    /// <summary>
    /// Exempt endpoints that APPEAR only when the UI provider or the voice
    /// driver is registered. Because the default test host registers neither,
    /// these rows are not tried by
    /// <see cref="Every_row_in_the_exemption_list_actually_exists_in_the_map"/>
    /// — they exist only to keep the first test (exempt-if-present) out of scope.
    /// </summary>
    private static readonly HashSet<string> ConditionallyMappedExemptRoutePatterns = new(StringComparer.Ordinal)
    {
        // SPA shell: <script src> cannot send an Authorization header; the shell carries no data.
        "/tracon/",
        "/tracon/{**path}",
        // Validates the token ITSELF against the static AuthToken; API keys
        // cannot open this endpoint at all today (adding metadata would be inert).
        "/tracon/api/voice/sessions/{sessionId}/stream",
    };

    [Fact]
    public async Task Every_endpoint_in_the_protected_group_carries_a_scope_or_is_explicitly_exempt()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true);

        var dataSource = host.Services.GetRequiredService<EndpointDataSource>();

        var uncovered = new List<string>();

        foreach (var endpoint in dataSource.Endpoints)
        {
            if (endpoint is not RouteEndpoint routeEndpoint)
            {
                continue;
            }

            var rawText = routeEndpoint.RoutePattern.RawText ?? string.Empty;

            // Only check candidate Tracon API endpoints: those carrying an
            // "/api/" or "/v1/" segment. The SPA shell and asset routes are
            // handled explicitly by separate exemption rows.
            if (!rawText.Contains("/api/", StringComparison.Ordinal)
                && !rawText.Contains("/v1/", StringComparison.Ordinal))
            {
                continue;
            }

            if (ExemptRoutePatterns.Contains(rawText) || ConditionallyMappedExemptRoutePatterns.Contains(rawText))
            {
                continue;
            }

            if (routeEndpoint.Metadata.GetMetadata<ApiKeyScopeRequirement>() is null)
            {
                uncovered.Add($"{rawText} ({string.Join(", ", GetHttpMethods(routeEndpoint))})");
            }
        }

        uncovered.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_row_in_the_exemption_list_actually_exists_in_the_map()
    {
        // The reverse direction: if a row in the list no longer matches anything
        // (the route moved/was deleted), the exemption silently becomes
        // meaningless — catch that too. Only the 3 rows that ALWAYS map are
        // tried; the UI shell and the voice endpoint are out of scope because
        // they never connect on this host.
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true);

        var dataSource = host.Services.GetRequiredService<EndpointDataSource>();

        var actualPatterns = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(static e => e.RoutePattern.RawText ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ExemptRoutePatterns.Where(pattern => !actualPatterns.Contains(pattern)).ToList();

        missing.ShouldBeEmpty();
    }

    private static IEnumerable<string> GetHttpMethods(RouteEndpoint endpoint)
        => endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods
           ?? ["?"];
}
