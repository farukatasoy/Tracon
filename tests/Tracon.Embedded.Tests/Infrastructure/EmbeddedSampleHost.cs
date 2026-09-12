using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Embedded.Tests.Infrastructure;

/// <summary>
/// Runs the ACTUAL <c>samples/Tracon.Embedded</c> entry point on
/// <c>WebApplicationFactory</c>'s in-memory <c>TestServer</c>, with the remote
/// address simulated as loopback.
/// </summary>
/// <remarks>
/// <c>MapTracon</c>'s three-layer guard checks
/// <c>HttpContext.Connection.RemoteIpAddress</c>, which the in-memory transport
/// never sets — the same gap
/// <c>tests/Tracon.AspNetCore.FunctionalTests/Infrastructure/TraconTestHost.cs</c>
/// closes with a test-only middleware. This class closes it the same way,
/// through an <see cref="IStartupFilter"/> registered in
/// <see cref="ConfigureWebHost"/> — the sample's own <c>Program.cs</c> is never
/// touched, so a defect in its actual wiring is what this suite catches.
/// </remarks>
internal sealed class EmbeddedSampleHost : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.ConfigureServices(static services =>
            services.AddTransient<IStartupFilter, SimulateLoopbackStartupFilter>());

    private sealed class SimulateLoopbackStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => builder =>
            {
                builder.Use(static (context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Loopback;

                    return nextMiddleware(context);
                });

                next(builder);
            };
    }
}
