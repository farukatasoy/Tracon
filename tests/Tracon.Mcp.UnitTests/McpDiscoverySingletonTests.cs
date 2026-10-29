using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// Two real <see cref="McpDiscoveryService"/> instances: BOTH discover.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 This test says the opposite of what it said until phase 264. Phase 42 put discovery behind a
/// single-executor lease and this class asserted that it ran on ONLY ONE instance. That choice was
/// wrong: the catalog is a cache of the process and holds the live connection a tool call goes
/// through, so an instance that did not discover had no MCP tool at all (measured on two hosts:
/// 1 tool on the leader, 0 on the other). No MCP server is registered here; the goal is to observe
/// that discovery runs on every instance, not discovery itself, so the completion log
/// (<c>"MCP discovery completed"</c>) is the signal. The two-host consequence, with single-executor
/// selection on, is <c>McpDiscoveryEveryInstanceTests</c> in the functional tests.
/// </para>
/// </remarks>
public sealed class McpDiscoverySingletonTests
{
    private const string DiscoveryCompleted = "MCP discovery completed";

    [Fact]
    public async Task Discovery_runs_on_every_instance()
    {
        var mcpOptions = Options(new TraconMcpOptions { RefreshInterval = TimeSpan.FromMilliseconds(60) });

        var loggerA = new RecordingLogger<McpDiscoveryService>();
        var loggerB = new RecordingLogger<McpDiscoveryService>();

        var serviceA = BuildService(mcpOptions, loggerA);
        var serviceB = BuildService(mcpOptions, loggerB);

        await serviceA.StartAsync(CancellationToken.None);
        await serviceB.StartAsync(CancellationToken.None);

        // Neither instance waits for the other: each one completes its own passes. Two passes
        // on each, not a window of time (phase 184 removed the fixed delay this test once had).
        await WaitUntil.TrueAsync(
            () => loggerA.CountContaining(DiscoveryCompleted) >= 2 && loggerB.CountContaining(DiscoveryCompleted) >= 2,
            "two MCP discovery passes on each instance");

        await serviceA.StopAsync(CancellationToken.None);
        await serviceB.StopAsync(CancellationToken.None);
    }

    private static McpDiscoveryService BuildService(
        IOptions<TraconMcpOptions> mcpOptions,
        ILogger<McpDiscoveryService> logger)
    {
        var catalog = new McpToolCatalog(
            new InMemoryMcpServerStore(),
            new InMemoryTenantStore(),
            new EmptyConfiguration(),
            Options(new TraconOptions()),
            mcpOptions,
            NullLoggerFactory.Instance,
            new McpOAuthTokenCacheRegistry(),
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
            NoOpToolArgumentsValidator.Instance,
            attribution: null);

        // No SQL persistence provider is registered: the gate is open by
        // itself, and discovery starts without waiting (K-354).
        var schemaReadyGate = new SchemaReadyGate([]);

        return new McpDiscoveryService(catalog, mcpOptions, schemaReadyGate, logger);
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

        /// <summary>Counts the messages containing <paramref name="fragment"/>, safely while the service still logs.</summary>
        public int CountContaining(string fragment)
        {
            lock (_messages)
            {
                return _messages.Count(message => message.Contains(fragment, StringComparison.Ordinal));
            }
        }

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
