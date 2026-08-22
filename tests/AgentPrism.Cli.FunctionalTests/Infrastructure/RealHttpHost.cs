using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPrism.Cli.FunctionalTests.Infrastructure;

/// <summary>
/// A REAL, Kestrel-backed AgentPrism host on a random loopback port.
/// </summary>
/// <remarks>
/// <c>AgentPrism.Testing</c>'s <c>AgentPrismTestHost</c> deliberately uses an
/// in-memory <c>TestServer</c> - there is no socket to connect to. The CLI
/// opens its own <see cref="HttpClient"/> against a URL string, so testing it
/// needs an address a real <see cref="HttpClient"/> can actually reach.
/// </remarks>
internal sealed class RealHttpHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private RealHttpHost(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    /// <summary>The application root PLUS the <c>MapAgentPrism</c> prefix, with a trailing slash.</summary>
    public Uri BaseAddress { get; }

    public static async Task<RealHttpHost> StartAsync(string prefix = "agentprism")
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder.Services.AddAgentPrism();

        var app = builder.Build();
        app.MapAgentPrism('/' + prefix.Trim('/'));

        await app.StartAsync().ConfigureAwait(false);

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        return new RealHttpHost(app, new Uri($"{address}/{prefix.Trim('/')}/"));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
