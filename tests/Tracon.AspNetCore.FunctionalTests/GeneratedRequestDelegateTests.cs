using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Every endpoint <c>MapTracon</c> maps runs a request delegate the compiler generated, not one built by reflection
/// when the first request arrives.
/// </summary>
/// <remarks>
/// <para>
/// Built by reflection, the first request after a start built every endpoint of the group: about 430 ms and 16 MB on
/// one request, even one that answers 404 (measured on a bare host). The generator does that work at build time. It
/// covers a handler shape it understands; a handler it cannot read is built by reflection again, silently, and the
/// first request pays for it once more. This test is what makes that visible.
/// </para>
/// <para>
/// A generated delegate is declared in the generator's namespace. A handler that already is a <c>RequestDelegate</c>
/// has nothing to generate or to bind; when a filter wraps it, the framework's filter pipeline builder declares the
/// delegate, and that is accepted. The surfaces that <c>MapTracon</c> does not map by default (the MCP server, A2A) have
/// a test of their own, because the default host does not contain them.
/// </para>
/// </remarks>
public sealed class GeneratedRequestDelegateTests
{
    private const string GeneratedNamespace = "Microsoft.AspNetCore.Http.Generated";

    [Fact]
    public async Task Every_endpoint_of_the_management_api_runs_a_generated_request_delegate()
    {
        await using var host = await TraconTestHost.StartAsync();

        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        endpoints.Count.ShouldBeGreaterThan(150, "MapTracon maps the management API; the test would check nothing");

        Reflected(endpoints).ShouldBeEmpty(ReflectedAdvice);
    }

    [Fact]
    public async Task Every_endpoint_of_the_MCP_server_and_the_A2A_surface_runs_a_generated_request_delegate()
    {
        // Both surfaces are mapped by their own call after MapTracon and are not in the default host.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition())
                .UseA2A(static options => options.ExposedAgents.Add("kod-agent"))
                .UseMcpServer(),
            configureAfterMap: static app =>
            {
                app.MapTraconA2A();
                app.MapTraconMcpServer();
            });

        var surface = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.RoutePattern.RawText is { } text
                                      && (text.Contains("/a2a/", StringComparison.Ordinal) || text.Contains("/mcp", StringComparison.Ordinal)))
            .ToList();

        surface.ShouldNotBeEmpty("neither surface was mapped; the test would check nothing");

        Reflected(surface).ShouldBeEmpty(ReflectedAdvice);
    }

    private const string ReflectedAdvice =
        "These endpoints are built by reflection at the first request. Give the handler a shape the request delegate " +
        "generator reads (a lambda or a method group passed straight to Map*).";

    // A handler that already is a RequestDelegate (the MCP SDK's own endpoint) has no parameter to bind, so nothing is
    // built by reflection for it; a filter added to it wraps it in this pipeline builder, whose delegate is declared here.
    private const string FilteredRequestDelegateWrapper = "Microsoft.AspNetCore.Routing.RequestDelegateFilterPipelineBuilder";

    private static bool IsGenerated(Type? type)
        => type is not null
           && ((type.Namespace ?? string.Empty).StartsWith(GeneratedNamespace, StringComparison.Ordinal)
               || (type.FullName ?? string.Empty).StartsWith(FilteredRequestDelegateWrapper, StringComparison.Ordinal));

    private static List<string> Reflected(IEnumerable<RouteEndpoint> endpoints)
        => [.. endpoints
            .Where(static endpoint => !IsGenerated(endpoint.RequestDelegate?.Method.DeclaringType))
            .Select(static endpoint => $"{endpoint.DisplayName} -> {endpoint.RequestDelegate?.Method.DeclaringType?.FullName ?? "(none)"}")
            .Order(StringComparer.Ordinal)];
}
