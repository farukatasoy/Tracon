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
/// The loop waits on a manual clock, as the core package's loops do (<c>HostedTickLoopTests</c>): a pass happens at
/// start and again each time the test advances the clock by the interval, so the number of calls the store sees is
/// exactly the number of passes the test gave, and a loop that retried on its own would show as more calls than
/// passes. No count depends on how fast the machine is. The interval is read after every pass.
/// </remarks>
public sealed class McpDiscoveryLoopTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_failing_server_list_does_not_end_the_loop_and_is_read_again_only_on_the_next_pass()
    {
        var plan = new FaultPlan { Fail = static _ => new IOException("the server list is unreachable") };
        var clock = new ManualClock();
        var service = Build(plan, clock, new TraconMcpOptions { RefreshInterval = Interval });

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => clock.Pending.Contains(Interval), "the loop to fail its first pass and wait out the interval");

        var perPass = plan.Total;

        perPass.ShouldBeGreaterThan(0);

        for (var pass = 2; pass <= 4; pass++)
        {
            clock.Advance(Interval);
            await WaitUntil.TrueAsync(() => plan.Total >= perPass * pass, $"the loop to try again on pass {pass}");
            await WaitUntil.TrueAsync(() => clock.Pending.Contains(Interval), "the loop to wait out the interval again");
        }

        // A tight loop shows here: the loop waits on a timer of this clock only after the pass is over (the wait just above),
        // so every read it made has been counted by now, and nothing advanced the clock since the fourth pass.
        plan.Total.ShouldBe(
            perPass * 4,
            "the loop read the server list without a pass: "
            + string.Join(", ", plan.Calls.GroupBy(static name => name, StringComparer.Ordinal).Select(static group => $"{group.Key} x{group.Count()}")));

        await service.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_server_list_that_never_answers_does_not_keep_the_host_from_stopping()
    {
        var plan = new FaultPlan { Hang = static _ => true };
        var service = Build(plan, new ManualClock(), new TraconMcpOptions { RefreshInterval = Interval });

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => plan.Total >= 1, "the loop to reach the call that hangs");

        await service.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_change_of_the_refresh_interval_applies_to_the_wait_after_the_next_pass()
    {
        var shorter = TimeSpan.FromSeconds(10);
        var plan = new FaultPlan();
        var clock = new ManualClock();
        var options = new TraconMcpOptions { RefreshInterval = Interval };
        var service = Build(plan, clock, options);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => clock.Pending.Contains(Interval), "the loop to finish its first pass and wait out the interval");

        var perPass = plan.Total;

        // A value a timer cannot take is not applied: the loop keeps the interval it has.
        options.RefreshInterval = TimeSpan.FromTicks(1);
        clock.Advance(Interval);
        await WaitUntil.TrueAsync(() => plan.Total >= perPass * 2, "the second pass");
        await WaitUntil.TrueAsync(() => clock.Pending.Contains(Interval), "the loop to wait out the interval it had");

        options.RefreshInterval = shorter;
        clock.Advance(Interval);
        await WaitUntil.TrueAsync(() => plan.Total >= perPass * 3, "the third pass");

        // The third pass found the new interval, so the fourth comes after it and not after the old one.
        await WaitUntil.TrueAsync(() => clock.Pending.Contains(shorter), "the loop to follow the new interval");

        clock.Advance(shorter);
        await WaitUntil.TrueAsync(() => plan.Total >= perPass * 4, "the fourth pass, one new interval after the third");

        await service.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private static McpDiscoveryService Build(FaultPlan plan, TimeProvider clock, TraconMcpOptions options)
    {
        var mcpOptions = new StaticOptions<TraconMcpOptions>(options);

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

        return new McpDiscoveryService(catalog, mcpOptions, new SchemaReadyGate([]), NullLogger<McpDiscoveryService>.Instance, clock);
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
