using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace AgentPrism.Mcp.UnitTests;

/// <summary>
/// HATA-006 / MT-CORE-006: an active connection refusal ("connection refused") and
/// a real timeout must be handled the same way — both must make
/// <see cref="McpRefreshOutcome.HadUnreachableServers"/> <see langword="true"/>.
/// Previously, this defect only fired on timeout; an active refusal was
/// silently counted as a "successful" refresh (that server's tools just
/// dropped off the list, producing no signal).
/// </summary>
public sealed class McpToolCatalogReachabilityTests
{
    [Fact]
    public async Task Server_with_refused_connection_makes_HadUnreachableServers_true()
    {
        var servers = new InMemoryMcpServerStore();

        await servers.SaveAsync(
            new McpServerDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = "default",
                Name = "dead-mcp",
                // 🚨 Nothing is listening: the connection is refused
                // IMMEDIATELY by the operating system (ECONNREFUSED) — it does not wait for a timeout.
                Endpoint = new Uri("http://127.0.0.1:59999/mcp"),
            },
            CancellationToken.None);

        var catalog = new McpToolCatalog(
            servers,
            new InMemoryTenantStore(),
            new EmptyConfiguration(),
            Options.Create(new AgentPrismOptions()),
            Options.Create(new AgentPrismMcpOptions { ConnectionTimeout = TimeSpan.FromSeconds(5) }),
            NullLoggerFactory.Instance,
            new McpOAuthTokenCacheRegistry(),
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            attribution: null);

        var outcome = await catalog.RefreshAsync();

        outcome.ToolCount.ShouldBe(0);
        outcome.HadUnreachableServers.ShouldBeTrue();
    }

    [Fact]
    public async Task HadUnreachableServers_stays_false_when_no_server_is_registered()
    {
        var catalog = new McpToolCatalog(
            new InMemoryMcpServerStore(),
            new InMemoryTenantStore(),
            new EmptyConfiguration(),
            Options.Create(new AgentPrismOptions()),
            Options.Create(new AgentPrismMcpOptions()),
            NullLoggerFactory.Instance,
            new McpOAuthTokenCacheRegistry(),
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            attribution: null);

        var outcome = await catalog.RefreshAsync();

        outcome.ToolCount.ShouldBe(0);
        outcome.HadUnreachableServers.ShouldBeFalse();
    }

    /// <summary>
    /// A fake configuration that carries no keys. As long as MCP discovery does not
    /// require a registered key, this type only exists to fill a constructor dependency
    /// (see <c>McpDiscoverySingletonTests</c>).
    /// </summary>
    private sealed class EmptyConfiguration : IConfiguration
    {
        public string? this[string key]
        {
            get => null;
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => NullChangeToken.Instance;

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();

        private sealed class NullChangeToken : IChangeToken
        {
            public static readonly NullChangeToken Instance = new();

            public bool HasChanged => false;

            public bool ActiveChangeCallbacks => false;

            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => NullDisposable.Instance;

            private sealed class NullDisposable : IDisposable
            {
                public static readonly NullDisposable Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
