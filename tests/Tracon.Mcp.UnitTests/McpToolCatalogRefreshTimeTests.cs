using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// One refresh pass lasts as long as its slowest wave of connections, not as long as the sum of
/// every dead server's timeout.
/// </summary>
/// <remarks>
/// The pass holds the catalog's refresh gate, and the manual refresh, the completion of an OAuth
/// authorization, and the background discovery all wait for it. Connected one after another, a
/// tenant with several servers that accept a connection and never answer made all of them wait
/// for the sum of the timeouts. The limit here is a bound between the two shapes: side by side the
/// pass takes about one timeout, one after another it takes three.
/// </remarks>
public sealed class McpToolCatalogRefreshTimeTests
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Servers_that_never_answer_are_connected_side_by_side()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var held = new List<TcpClient>();
        var accepting = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync();

                    lock (held)
                    {
                        held.Add(client);
                    }
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException or OperationCanceledException)
            {
                // The listener stopped.
            }
        });

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var servers = new InMemoryMcpServerStore();

        for (var index = 0; index < 3; index++)
        {
            await servers.SaveAsync(
                new McpServerDefinition
                {
                    Id = Guid.NewGuid(),
                    TenantId = "default",
                    Name = $"silent-{index}",
                    Endpoint = new Uri($"http://127.0.0.1:{port}/mcp{index}"),
                },
                TestContext.Current.CancellationToken);
        }

        var catalog = new McpToolCatalog(
            servers,
            new InMemoryTenantStore(),
            new EmptyConfiguration(),
            Options.Create(new TraconOptions()),
            Options.Create(new TraconMcpOptions { ConnectionTimeout = ConnectionTimeout }),
            NullLoggerFactory.Instance,
            new McpOAuthTokenCacheRegistry(),
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
            NoOpToolArgumentsValidator.Instance,
            attribution: null,
            egressGuard: new EgressSocketGuard(static () => new EgressAddressPolicy(AllowPrivateNetworkTargets: true, AllowLoopback: true)));

        var clock = Stopwatch.StartNew();
        var outcome = await catalog.RefreshAsync(TestContext.Current.CancellationToken);
        clock.Stop();

        listener.Stop();
        await accepting;

        lock (held)
        {
            held.Count.ShouldBe(3, "every server was tried; the limit below would hold for a pass that skipped them");
        }

        outcome.HadUnreachableServers.ShouldBeTrue();
        clock.Elapsed.ShouldBeLessThan(
            ConnectionTimeout * 2.25,
            $"three servers that never answer took {clock.Elapsed} to refresh, with a limit of {ConnectionTimeout} each");
    }

    private sealed class EmptyConfiguration : IConfiguration
    {
        public string? this[string key]
        {
            get => null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
