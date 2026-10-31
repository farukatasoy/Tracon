using System.Collections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// No endpoint accepts a service interface as its request body.
/// </summary>
/// <remarks>
/// <para>
/// Minimal APIs ask <c>IServiceProviderIsService</c> whether a parameter type is a
/// service. A type that is not registered is read from the body instead. On a
/// <c>GET</c> that crashes every endpoint at startup, so the mistake is loud. On a
/// <c>POST</c> it is silent: the endpoint still maps, the OpenAPI document gains a
/// request body, and <c>Tracon.Client</c> ships a class for it
/// (<c>POST /api/mcp-servers/refresh</c> published <c>IMcpToolRefresher</c> for four
/// preview releases).
/// </para>
/// <para>
/// The host below registers <c>AddTracon()</c> and nothing else, so every service of
/// an optional package (<c>Tracon.Mcp</c>, <c>Tracon.Voice</c>, ...) is missing. That is
/// the worst case: an optional service parameter without <c>[FromServices]</c> turns into
/// a body here.
/// </para>
/// </remarks>
public sealed class EndpointBodyInferenceTests
{
    [Fact]
    public async Task No_endpoint_reads_a_service_interface_from_the_request_body()
    {
        await using var host = await TraconTestHost.StartAsync();

        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        var offenders = new List<string>();
        var checkedBodies = 0;

        foreach (var endpoint in endpoints)
        {
            foreach (var accepts in endpoint.Metadata.GetOrderedMetadata<IAcceptsMetadata>())
            {
                if (accepts.RequestType is not { } requestType)
                {
                    continue;
                }

                checkedBodies++;

                // Two interfaces are legitimate bodies: IFormFile (a multipart/form-data part) and a
                // collection such as IReadOnlyList<T> (a JSON array). Anything else is a service.
                if (requestType.IsInterface &&
                    requestType != typeof(IFormFile) &&
                    !typeof(IEnumerable).IsAssignableFrom(requestType))
                {
                    offenders.Add($"{endpoint.DisplayName}: body is {requestType.FullName}");
                }
            }
        }

        checkedBodies.ShouldBeGreaterThan(
            0,
            "No endpoint declared a request body; the test would pass without checking anything.");

        offenders.ShouldBeEmpty(
            "Mark every service parameter that may be unregistered with [FromServices]: " +
            string.Join("; ", offenders));
    }
}
