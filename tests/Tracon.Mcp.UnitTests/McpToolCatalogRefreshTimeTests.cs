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
/// for the sum of the timeouts. The pass runs on a clock the test moves: side by side the three limits
/// are armed together and one step of the clock carries them all, one after another the clock would
/// have to move three times.
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

        var clock = new ManualClock();

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
            egressGuard: new EgressSocketGuard(static () => new EgressAddressPolicy(AllowPrivateNetworkTargets: true, AllowLoopback: true)),
            timeProvider: clock);

        var refreshing = catalog.RefreshAsync(TestContext.Current.CancellationToken).AsTask();

        // Every server's limit is armed before any of them is reached; a pass that connected them one after another would
        // never have more than one armed, and this wait would fail.
        await WaitUntil.TrueAsync(
            () => clock.Pending.Count == 3,
            "the limits of all three silent servers to be armed together",
            TimeSpan.FromSeconds(10));

        // Every server was tried: a pass that skipped one would end early and prove nothing about the limit.
        await WaitUntil.TrueAsync(
            () =>
            {
                lock (held)
                {
                    return held.Count == 3;
                }
            },
            "all three servers to have a connection accepted");

        await clock.AdvanceUntilAsync(refreshing, within: ConnectionTimeout);

        var outcome = await refreshing;

        listener.Stop();
        await accepting;

        outcome.HadUnreachableServers.ShouldBeTrue();
        clock.Steps.ShouldBe(
            [ConnectionTimeout],
            $"three servers that never answer took more than one {ConnectionTimeout} limit to give up on");
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
