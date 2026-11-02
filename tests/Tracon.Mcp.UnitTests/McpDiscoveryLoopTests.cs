using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// The refresh loop of <see cref="McpDiscoveryService"/> when the server list it reads fails or never answers:
/// the loop outlives the failure, waits the refresh interval between attempts instead of spinning, and ends in
/// bounded time when the host stops.
/// </summary>
/// <remarks>
/// The loop waits with <c>Task.Delay</c> on the system clock, so the wait is real: the interval is short and the
/// attempts are counted against it. A spinning loop makes thousands of attempts in the window; the bound below
/// is several times what an honest loop makes, so a slow machine cannot trip it.
/// </remarks>
public sealed class McpDiscoveryLoopTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task A_failing_server_list_does_not_end_the_loop_and_is_not_read_in_a_tight_loop()
    {
        var plan = new FaultPlan { Fail = static _ => new IOException("the server list is unreachable") };
        var service = Build(plan);

        await service.StartAsync(TestContext.Current.CancellationToken);

        // The loop survives: it keeps trying after the first failures.
        await WaitUntil.TrueAsync(() => plan.Total >= 3, "the loop to try again after failing");

        // 1 second at a 100 ms interval is about 10 attempts; a tight loop makes thousands.
        var before = plan.Total;
        await Task.Delay(1000, TestContext.Current.CancellationToken); // delay: negative
        (plan.Total - before).ShouldBeLessThan(40, "the loop read the server list far more often than its interval allows");

        await service.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_server_list_that_never_answers_does_not_keep_the_host_from_stopping()
    {
        var plan = new FaultPlan { Hang = static _ => true };
        var service = Build(plan);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => plan.Total >= 1, "the loop to reach the call that hangs");

        await service.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private static McpDiscoveryService Build(FaultPlan plan)
    {
        var mcpOptions = new StaticOptions<TraconMcpOptions>(new TraconMcpOptions { RefreshInterval = Interval });

        var catalog = new McpToolCatalog(
            FaultProxy<IMcpServerStore>.Create(new InMemoryMcpServerStore(), plan),
            new InMemoryTenantStore(),
            new EmptyConfiguration(),
            new StaticOptions<TraconOptions>(new TraconOptions()),
            mcpOptions,
            NullLoggerFactory.Instance,
            new McpOAuthTokenCacheRegistry(),
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
            NoOpToolArgumentsValidator.Instance,
            attribution: null);

        return new McpDiscoveryService(catalog, mcpOptions, new SchemaReadyGate([]), NullLogger<McpDiscoveryService>.Instance);
    }

    private sealed class StaticOptions<T>(T value) : IOptionsMonitor<T>, IOptions<T>
        where T : class
    {
        public T CurrentValue => value;

        public T Value => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

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
