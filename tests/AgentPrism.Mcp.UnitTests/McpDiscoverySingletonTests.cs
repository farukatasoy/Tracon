using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace AgentPrism.Mcp.UnitTests;

/// <summary>
/// Iki gercek <see cref="McpDiscoveryService"/> orneginin AYNI kira deposunu
/// paylastigi uctan uca senaryo (Faz 42, DoD: "Iki ornek kurulur; MCP kesfi
/// yalniz birinde kosar"). MCP sunucusu kayitli degildir; amac kesfin
/// KENDISINI degil, YALNIZ BIR orneginin calistigini gozlemektir — bu yuzden
/// tamamlanma logu (<c>"MCP discovery completed"</c>) sinyal olarak kullanilir.
/// </summary>
public sealed class McpDiscoverySingletonTests
{
    [Fact]
    public async Task Kesif_yalniz_bir_ornekte_kosar()
    {
        var leaseStore = new InMemorySingletonLeaseStore();
        var singletonOptions = Options(new SingletonExecutionOptions
        {
            Enabled = true,
            LeaseDuration = TimeSpan.FromSeconds(1),
        });

        var mcpOptions = Options(new AgentPrismMcpOptions { RefreshInterval = TimeSpan.FromMilliseconds(60) });

        var loggerA = new RecordingLogger<McpDiscoveryService>();
        var loggerB = new RecordingLogger<McpDiscoveryService>();

        var serviceA = BuildService(mcpOptions, leaseStore, singletonOptions, loggerA);
        var serviceB = BuildService(mcpOptions, leaseStore, singletonOptions, loggerB);

        await serviceA.StartAsync(CancellationToken.None);
        await serviceB.StartAsync(CancellationToken.None);

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        await serviceA.StopAsync(CancellationToken.None);
        await serviceB.StopAsync(CancellationToken.None);

        var completedA = loggerA.Messages.Count(m => m.Contains("MCP discovery completed", StringComparison.Ordinal));
        var completedB = loggerB.Messages.Count(m => m.Contains("MCP discovery completed", StringComparison.Ordinal));

        (completedA > 0 ^ completedB > 0).ShouldBeTrue($"completedA={completedA}, completedB={completedB}");
    }

    private static McpDiscoveryService BuildService(
        IOptions<AgentPrismMcpOptions> mcpOptions,
        ISingletonLeaseStore leaseStore,
        IOptionsMonitor<SingletonExecutionOptions> singletonOptions,
        ILogger<McpDiscoveryService> logger)
    {
        var catalog = new McpToolCatalog(
            new InMemoryMcpServerStore(),
            new InMemoryTenantStore(),
            new EmptyConfiguration(),
            Options(new AgentPrismOptions()),
            mcpOptions,
            NullLoggerFactory.Instance,
            new McpOAuthTokenCacheRegistry());

        // Hicbir SQL kalicilik saglayicisi kayitli degil: kapi kendiliginden aciktir
        // ve kesif beklemeden baslar (K-354).
        var schemaReadyGate = new SchemaReadyGate([]);

        return new McpDiscoveryService(
            catalog, mcpOptions, leaseStore, singletonOptions, schemaReadyGate, logger);
    }

    private static StaticOptionsMonitor<T> Options<T>(T value) where T : class => new(value);

    /// <summary>
    /// Hicbir anahtar tasimayan sahte yapilandirma. MCP kayitli sunucusu
    /// yokken <c>McpToolCatalog</c> yapilandirmayi hic okumaz; bu tip yalniz
    /// bir kurucu bagimliligini doldurmak icindir.
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

    /// <summary>Sabit bir deger dondüren, degisikligi izlemeyen sahte <see cref="IOptionsMonitor{T}"/>.</summary>
    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>, IOptions<T>
        where T : class
    {
        public T CurrentValue => value;

        public T Value => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>Bicimlendirilmis log mesajlarini biriktiren sahte gunlukleyici.</summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages => _messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_messages)
            {
                _messages.Add(formatter(state, exception));
            }
        }
    }
}
