using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace AgentPrism.Mcp.UnitTests;

/// <summary>
/// End-to-end scenario where two real <see cref="McpDiscoveryService"/>
/// instances share the SAME lease store (Phase 42, DoD: "Two instances are
/// set up; MCP discovery runs on only one"). No MCP server is registered;
/// the goal is to observe that discovery runs on ONLY ONE instance, not
/// discovery itself — so the completion log
/// (<c>"MCP discovery completed"</c>) is used as the signal.
/// </summary>
public sealed class McpDiscoverySingletonTests
{
    [Fact]
    public async Task Discovery_runs_on_only_one_instance()
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

        // No SQL persistence provider is registered: the gate is open by
        // itself, and discovery starts without waiting (K-354).
        var schemaReadyGate = new SchemaReadyGate([]);

        return new McpDiscoveryService(
            catalog, mcpOptions, leaseStore, singletonOptions, schemaReadyGate, logger);
    }

    private static StaticOptionsMonitor<T> Options<T>(T value) where T : class => new(value);

    /// <summary>
    /// Fake configuration that carries no keys. When no MCP server is
    /// registered, <c>McpToolCatalog</c> never reads configuration; this
    /// type exists only to fill a constructor dependency.
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

    /// <summary>A fake <see cref="IOptionsMonitor{T}"/> that returns a fixed value and never tracks changes.</summary>
    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>, IOptions<T>
        where T : class
    {
        public T CurrentValue => value;

        public T Value => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>Fake logger that accumulates formatted log messages.</summary>
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
