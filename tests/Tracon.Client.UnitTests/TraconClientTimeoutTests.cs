using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Client.Generated;

namespace Tracon.Client.UnitTests;

/// <summary>
/// The request budget the registered <see cref="HttpClient"/> actually
/// enforces (Phase 166).
/// </summary>
/// <remarks>
/// 🚨 A caller that wraps a client call in its own
/// <c>CancellationTokenSource.CancelAfter(budget)</c> only gets that budget if
/// the transport underneath does not cap it first. <see cref="HttpClient"/>
/// defaults to 100 seconds and reports its own cap as an
/// <see cref="OperationCanceledException"/> with nothing cancelled (K-737), so
/// a longer caller budget silently could not be reached and the caller then
/// named a limit that never elapsed.
/// </remarks>
public sealed class TraconClientTimeoutTests
{
    [Fact]
    public async Task The_configured_timeout_bounds_a_request_the_server_never_answers()
    {
        using var server = new HangingServer();

        var services = new ServiceCollection();

        services.AddTraconClient(options =>
        {
            options.BaseAddress = server.BaseAddress;
            options.Timeout = TimeSpan.FromMilliseconds(400);
        });

        var client = Resolve(services);

        var watch = Stopwatch.StartNew();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await client.TraconModelsHealthAsync(refresh: null, CancellationToken.None));

        watch.Stop();

        // Without the setting this takes HttpClient's own default of 100 s -
        // which is precisely the cap a longer caller budget could never cross.
        watch.Elapsed.ShouldBeLessThan(
            TimeSpan.FromSeconds(30),
            "the configured timeout, not HttpClient's 100 s default, must bound the request.");
    }

    [Fact]
    public void An_unset_timeout_leaves_the_client_on_its_own_default()
    {
        var services = new ServiceCollection();

        services.AddTraconClient(options => options.BaseAddress = new Uri("https://example.com/tracon/"));

        // Registration must not fail or throw when the setting is omitted:
        // the field is additive and every existing caller leaves it null.
        Resolve(services).ShouldNotBeNull();
    }

    /// <summary>Builds the client from the registered factory, without a container.</summary>
    private static TraconApiClient Resolve(IServiceCollection services)
    {
        var descriptor = services.Single(service => service.ServiceType == typeof(TraconApiClient));

        return (TraconApiClient)descriptor.ImplementationFactory!(null!);
    }

    /// <summary>Accepts a connection and never answers it.</summary>
    private sealed class HangingServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();

        public HangingServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/tracon/");

            _ = AcceptAsync();
        }

        public Uri BaseAddress { get; }

        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Stop();
            _stopping.Dispose();
        }

        private async Task AcceptAsync()
        {
            try
            {
                // The connection is held open and deliberately never written
                // to, so only a timeout can end the caller's wait.
                using var connection = await _listener.AcceptTcpClientAsync(_stopping.Token).ConfigureAwait(false);

                await Task.Delay(Timeout.InfiniteTimeSpan, _stopping.Token).ConfigureAwait(false); // delay: simulated
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // The test finished and stopped the listener.
            }
        }
    }
}
