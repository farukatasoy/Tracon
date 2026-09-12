using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon.Cli.FunctionalTests.Infrastructure;

/// <summary>
/// A REAL, Kestrel-backed Tracon host on a random loopback port.
/// </summary>
/// <remarks>
/// <c>Tracon.Testing</c>'s <c>TraconTestHost</c> deliberately uses an
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

    /// <summary>The application root PLUS the <c>MapTracon</c> prefix, with a trailing slash.</summary>
    public Uri BaseAddress { get; }

    /// <summary>The application's service provider — for seeding stores directly (bypassing HTTP).</summary>
    public IServiceProvider Services => _app.Services;

    /// <param name="prefix">The path prefix.</param>
    /// <param name="configureServices">
    /// Registers additional services BEFORE <c>AddTracon()</c> runs, for
    /// example <c>services.UseScheduling(...)</c> to speed up or pause the
    /// background job worker.
    /// </param>
    /// <param name="configureTracon">
    /// Changes the Tracon chain, for example to register a model
    /// provider or a code-defined agent (an eval suite needs a real agent to
    /// measure).
    /// </param>
    public static async Task<RealHttpHost> StartAsync(
        string prefix = "tracon",
        Action<IServiceCollection>? configureServices = null,
        Action<ITraconBuilder>? configureTracon = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        configureServices?.Invoke(builder.Services);

        var tracon = builder.Services.AddTracon();
        configureTracon?.Invoke(tracon);

        var app = builder.Build();
        app.MapTracon('/' + prefix.Trim('/'));

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
