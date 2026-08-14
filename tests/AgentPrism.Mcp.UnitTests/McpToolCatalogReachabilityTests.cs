using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace AgentPrism.Mcp.UnitTests;

/// <summary>
/// HATA-006 / MT-CORE-006: aktif baglanti reddi ("connection refused") ile
/// gercek zaman asimi ayni sekilde ele alinmalidir — ikisi de
/// <see cref="McpRefreshOutcome.HadUnreachableServers"/>'i <see langword="true"/>
/// yapmalidir. Once bu kusur, yalniz zaman asiminda calisiyordu; aktif red
/// sessizce "basarili" bir tazeleme sayiliyordu (o sunucunun tool'lari
/// sadece listeden dusuyordu, bir sinyal uretmiyordu).
/// </summary>
public sealed class McpToolCatalogReachabilityTests
{
    [Fact]
    public async Task Baglanti_reddedilen_sunucu_HadUnreachableServers_true_yapar()
    {
        var servers = new InMemoryMcpServerStore();

        await servers.SaveAsync(
            new McpServerDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = "default",
                Name = "olu-mcp",
                // 🚨 Hicbir sey dinlemiyor: baglanti isletim sistemi tarafindan
                // ANINDA reddedilir (ECONNREFUSED) — zaman asimini beklemez.
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
            new McpOAuthTokenCacheRegistry());

        var outcome = await catalog.RefreshAsync();

        outcome.ToolCount.ShouldBe(0);
        outcome.HadUnreachableServers.ShouldBeTrue();
    }

    [Fact]
    public async Task Kayitli_sunucu_yokken_HadUnreachableServers_false_kalir()
    {
        var catalog = new McpToolCatalog(
            new InMemoryMcpServerStore(),
            new InMemoryTenantStore(),
            new EmptyConfiguration(),
            Options.Create(new AgentPrismOptions()),
            Options.Create(new AgentPrismMcpOptions()),
            NullLoggerFactory.Instance,
            new McpOAuthTokenCacheRegistry());

        var outcome = await catalog.RefreshAsync();

        outcome.ToolCount.ShouldBe(0);
        outcome.HadUnreachableServers.ShouldBeFalse();
    }

    /// <summary>
    /// Hicbir anahtar tasimayan sahte yapilandirma. MCP kesfi kayitli anahtar
    /// gerektirmedigi surece bu tip yalniz bir kurucu bagimliligini doldurmak
    /// icindir (bkz. <c>McpDiscoverySingletonTests</c>).
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
