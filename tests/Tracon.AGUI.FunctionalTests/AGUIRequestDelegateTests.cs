using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AGUI.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// The AG-UI endpoint runs a request delegate the compiler generated, not one built by reflection when the first request
/// arrives.
/// </summary>
/// <remarks>
/// The management API has the same test next to its own host; this package maps its endpoint with its own call, so it
/// is checked here. A generated delegate is declared in the generator's namespace.
/// </remarks>
public sealed class AGUIRequestDelegateTests
{
    private const string GeneratedNamespace = "Microsoft.AspNetCore.Http.Generated";

    [Fact]
    public async Task The_ag_ui_endpoint_runs_a_generated_request_delegate()
    {
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho);

        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.RoutePattern.RawText?.StartsWith(AGUITestHost.Pattern, StringComparison.Ordinal) == true)
            .ToList();

        endpoints.ShouldNotBeEmpty("the AG-UI endpoint is not mapped; the test would check nothing");

        var reflected = endpoints
            .Where(static endpoint => endpoint.RequestDelegate?.Method.DeclaringType is not { } type
                                      || !(type.Namespace ?? string.Empty).StartsWith(GeneratedNamespace, StringComparison.Ordinal))
            .Select(static endpoint => endpoint.DisplayName ?? endpoint.RoutePattern.RawText ?? "(unnamed)")
            .ToList();

        reflected.ShouldBeEmpty("These endpoints are built by reflection at the first request: " + string.Join("; ", reflected));
    }
}
