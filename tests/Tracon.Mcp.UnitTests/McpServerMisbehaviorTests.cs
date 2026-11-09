using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// An MCP server that misbehaves cannot stop the refresh, cannot take the tools of the servers beside it with it, and
/// cannot make it wait longer than its own limit.
/// </summary>
/// <remarks>
/// The server is a local one that answers the handshake as a working server does and then says what a broken one says
/// to <c>tools/list</c>: nothing at all, three thousand tools, a schema that is not a schema, two tools under one name,
/// a description of several megabytes. A second, healthy server is registered beside it; its tool must survive each of
/// them.
/// <para>
/// The refresh runs on a clock the test moves. A refresh that is meant to end without using its connection limit ends with
/// the clock where it started, and one that is meant to end at the limit is carried to it by one step of exactly that limit:
/// no wall-clock bound says how long it took.
/// </para>
/// </remarks>
public sealed class McpServerMisbehaviorTests
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(2);

    private const string GoodTool = """{"name":"healthy_tool","description":"works","inputSchema":{"type":"object","properties":{}}}""";

    [Fact]
    public async Task A_server_that_never_answers_tools_list_costs_one_timeout_and_the_healthy_server_keeps_its_tool()
    {
        var asked = 0;

        await using var broken = ScriptedMcpServer.Start((method, _, _, _) =>
        {
            if (!string.Equals(method, "tools/list", StringComparison.Ordinal))
            {
                return Task.FromResult<string?>("{}");
            }

            Interlocked.Increment(ref asked);

            return Task.FromResult<string?>(null);
        });

        var (outcome, tools) = await RefreshToTheLimitAsync(broken, () => Volatile.Read(ref asked) > 0, "the silent server to be asked for its tools");

        outcome.HadUnreachableServers.ShouldBeTrue("a server that never answers is unreachable");
        HasHealthyTool(tools).ShouldBeTrue("the healthy server's tool is gone");
    }

    [Fact]
    public async Task A_server_with_three_thousand_tools_is_listed_in_bounded_time()
    {
        var many = string.Join(',', Enumerable.Range(0, 3000).Select(static index =>
            "{\"name\":\"tool_" + index + "\",\"description\":\"d\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"));

        await using var broken = ScriptedMcpServer.Start((method, _, _, _) => Task.FromResult<string?>(
            string.Equals(method, "tools/list", StringComparison.Ordinal) ? Tools(many) : "{}"));

        var (outcome, tools) = await RefreshAsync(broken);

        HasHealthyTool(tools).ShouldBeTrue("the healthy server's tool is gone");

        // The limit per server is the default of the option (100), and the healthy server's one tool is on top of it. These
        // 3000 tools are one page, and a page is read whole; the pages after it are what the limit stops (see the paged test).
        var limit = new TraconMcpOptions().MaxToolsPerServer;

        outcome.ToolCount.ShouldBe(limit + 1);
        tools.Count(static name => name.StartsWith("broken_", StringComparison.Ordinal)).ShouldBe(limit);
    }

    [Fact]
    public async Task A_server_whose_tool_list_never_ends_is_read_up_to_the_limit_and_no_further()
    {
        // One tool per page and a cursor after every page: the list has no last page. The SDK's collecting overload read
        // pages until the connection timeout (9 444 pages and 3 GB in ten seconds) and only then looked at the limit.
        var pages = 0;

        await using var broken = ScriptedMcpServer.Start((method, _, _, _) =>
        {
            if (!string.Equals(method, "tools/list", StringComparison.Ordinal))
            {
                return Task.FromResult<string?>("{}");
            }

            var page = Interlocked.Increment(ref pages);

            return Task.FromResult<string?>(
                "{\"tools\":[{\"name\":\"tool_" + page + "\",\"description\":\"d\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}],\"nextCursor\":\"page-" + page + "\"}");
        });

        // The pages are read one after the other, so reaching the limit costs one round trip for each tool kept. A small limit
        // keeps that inside the connection timeout on a machine whose round trips are slow (a CI runner, a container: the
        // default limit of 100 needs 101 of them in two seconds); what the test pins is that the reading STOPS there.
        const int limit = 10;

        var (outcome, tools) = await RefreshAsync(broken, limit);

        // The server is reachable and keeps its first tools: a list that does not end is cut like a list that is too long.
        // The refresh ended with the connection limit unreached: the clock it runs on was never moved.
        outcome.HadUnreachableServers.ShouldBeFalse("the limit was reached, so the list was not waited for");
        tools.Count(static name => name.StartsWith("broken_", StringComparison.Ordinal)).ShouldBe(limit);
        HasHealthyTool(tools).ShouldBeTrue("the healthy server's tool is gone");

        // One page for each kept tool, and the one that showed the list was longer. Not a page more.
        Volatile.Read(ref pages).ShouldBe(limit + 1);
    }

    [Fact]
    public async Task A_server_whose_tool_list_never_ends_in_tools_nobody_can_use_costs_one_connection_timeout()
    {
        // Nothing is kept, so the limit is never reached, and nothing is held either: each page is dropped as it is read.
        // The time a server can take this way is the connection timeout, as for a server that never answers.
        var pages = 0;

        await using var broken = ScriptedMcpServer.Start((method, _, _, _) =>
        {
            if (!string.Equals(method, "tools/list", StringComparison.Ordinal))
            {
                return Task.FromResult<string?>("{}");
            }

            var page = Interlocked.Increment(ref pages);

            return Task.FromResult<string?>(
                "{\"tools\":[{\"name\":\"not a name " + page + "\",\"description\":\"d\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}],\"nextCursor\":\"page-" + page + "\"}");
        });

        var (outcome, tools) = await RefreshToTheLimitAsync(
            broken,
            () => Volatile.Read(ref pages) > 1,
            "the server to be asked for a second page of its list");

        outcome.HadUnreachableServers.ShouldBeTrue("a list with no last page is a server that cannot be listed");
        Volatile.Read(ref pages).ShouldBeGreaterThan(1, "the server was never asked for a second page, so the list did not end");
        HasHealthyTool(tools).ShouldBeTrue("the healthy server's tool is gone");
        tools.ShouldNotContain(static name => name.StartsWith("broken_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_paged_tool_list_longer_than_the_limit_stops_at_the_page_that_shows_it()
    {
        // 50 tools a page, 10 pages. The limit is 100: pages 1 and 2 fill it and page 3 shows that there is more.
        var pages = 0;

        await using var broken = ScriptedMcpServer.Start((method, _, _, _) =>
        {
            if (!string.Equals(method, "tools/list", StringComparison.Ordinal))
            {
                return Task.FromResult<string?>("{}");
            }

            var page = Interlocked.Increment(ref pages);
            var listed = string.Join(',', Enumerable.Range((page - 1) * 50, 50).Select(static index =>
                "{\"name\":\"tool_" + index + "\",\"description\":\"d\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"));

            return Task.FromResult<string?>(
                "{\"tools\":[" + listed + "]" + (page < 10 ? ",\"nextCursor\":\"page-" + page + "\"" : string.Empty) + "}");
        });

        var (_, tools) = await RefreshAsync(broken);

        tools.Count(static name => name.StartsWith("broken_", StringComparison.Ordinal)).ShouldBe(new TraconMcpOptions().MaxToolsPerServer);
        tools.Contains("broken_tool_0", StringComparer.Ordinal).ShouldBeTrue();
        tools.Contains("broken_tool_99", StringComparer.Ordinal).ShouldBeTrue();
        tools.Contains("broken_tool_100", StringComparer.Ordinal).ShouldBeFalse();
        Volatile.Read(ref pages).ShouldBe(3);
    }

    [Fact]
    public async Task A_paged_tool_list_within_the_limit_is_read_to_its_last_page()
    {
        var pages = 0;

        await using var broken = ScriptedMcpServer.Start((method, _, _, _) =>
        {
            if (!string.Equals(method, "tools/list", StringComparison.Ordinal))
            {
                return Task.FromResult<string?>("{}");
            }

            var page = Interlocked.Increment(ref pages);

            return Task.FromResult<string?>(
                "{\"tools\":[{\"name\":\"tool_" + page + "\",\"description\":\"d\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}]"
                + (page < 4 ? ",\"nextCursor\":\"page-" + page + "\"" : string.Empty) + "}");
        });

        var (_, tools) = await RefreshAsync(broken);

        tools.Count(static name => name.StartsWith("broken_", StringComparison.Ordinal)).ShouldBe(4);
        Volatile.Read(ref pages).ShouldBe(4);
    }

    [Fact]
    public async Task A_tool_with_an_invalid_x_mcp_header_is_left_out_and_the_other_tools_are_kept()
    {
        const string EmptyHeader = """{"name":"bad_header","description":"x","inputSchema":{"type":"object","properties":{"region":{"type":"string","x-mcp-header":""}}}}""";

        await using var broken = ScriptedMcpServer.Start((method, _, _, _) => Task.FromResult<string?>(
            string.Equals(method, "tools/list", StringComparison.Ordinal) ? Tools(EmptyHeader, GoodTool) : "{}"));

        var (_, tools) = await RefreshAsync(broken);

        tools.Contains("broken_healthy_tool", StringComparer.Ordinal).ShouldBeTrue();
        tools.Contains("broken_bad_header", StringComparer.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task A_call_to_a_tool_whose_server_never_answers_ends_at_the_tool_timeout_on_the_clock_of_the_catalog()
    {
        // The list is fine and the call is not: only the tool's own limit, which the catalog's tool set arms on the clock the
        // catalog was given, can end it.
        var called = 0;

        await using var broken = ScriptedMcpServer.Start((method, _, _, _) =>
        {
            if (string.Equals(method, "tools/call", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref called);

                return Task.FromResult<string?>(null);
            }

            return Task.FromResult<string?>(string.Equals(method, "tools/list", StringComparison.Ordinal) ? Tools(GoodTool) : "{}");
        });

        var clock = new ManualClock();
        await using var rig = await Rig.StartAsync(broken, maxToolsPerServer: null, clock);

        (await rig.Catalog.RefreshAsync(TestContext.Current.CancellationToken)).HadUnreachableServers.ShouldBeFalse();
        rig.Catalog.ForTenant("default").TryGet("broken_healthy_tool", out var tool).ShouldBeTrue();

        var call = ((AIFunction)tool!).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), TestContext.Current.CancellationToken).AsTask();

        await WaitUntil.TrueAsync(() => Volatile.Read(ref called) > 0 && clock.Pending.Count > 0, "the call to reach the silent server with its limit armed");

        var limit = new TraconOptions().Tools.DefaultTimeout;

        clock.Pending.ShouldBe([limit]);

        await clock.AdvanceUntilAsync(call, within: limit);

        (await call)?.ToString().ShouldNotBeNull().ShouldContain("did not complete within");
        clock.Steps.ShouldBe([limit]);
    }

    [Fact]
    public async Task A_call_to_a_tool_with_an_x_mcp_header_still_sends_the_header_the_list_declared()
    {
        // The collecting overload of the SDK fills the client's tool cache, and the cache is how a call finds which of its
        // arguments to repeat in a header. Reading the pages by hand has to fill it too, or the header is dropped without a word.
        const string GeoTool = """{"name":"geo","description":"x","inputSchema":{"type":"object","properties":{"region":{"type":"string","x-mcp-header":"Region"}}}}""";

        await using var broken = ScriptedMcpServer.Start((method, _, _, _) => Task.FromResult<string?>(
            string.Equals(method, "tools/list", StringComparison.Ordinal)
                ? Tools(GeoTool)
                : string.Equals(method, "tools/call", StringComparison.Ordinal) ? """{"content":[{"type":"text","text":"ok"}]}""" : "{}"));

        await using var rig = await Rig.StartAsync(broken);
        await rig.Catalog.RefreshAsync(TestContext.Current.CancellationToken);

        rig.Catalog.ForTenant("default").TryGet("broken_geo", out var tool).ShouldBeTrue();

        _ = await ((AIFunction)tool!).InvokeAsync(
            new AIFunctionArguments(StringComparer.Ordinal) { ["region"] = "eu-west" },
            TestContext.Current.CancellationToken);

        (string Method, IReadOnlyDictionary<string, string> Headers) call;

        lock (broken.Requests)
        {
            call = broken.Requests.Single(static request => string.Equals(request.Method, "tools/call", StringComparison.Ordinal));
        }

        call.Headers.ShouldContainKeyAndValue("Mcp-Param-Region", "eu-west");
    }

    [Theory]
    [InlineData("""{"name":"bad_schema","description":"x","inputSchema":5}""")]
    [InlineData("""{"name":"bad_schema","description":"x","inputSchema":"object"}""")]
    [InlineData("""{"name":"bad_schema","description":"x","inputSchema":{"type":"object","properties":7}}""")]
    [InlineData("""{"name":"bad_schema","description":"x","inputSchema":{"$ref":"#/$defs/a","$defs":{"a":{"$ref":"#/$defs/a"}}}}""")]
    [InlineData("""{"name":"bad_schema","description":"x"}""")]
    [InlineData("""{"description":"no name","inputSchema":{"type":"object"}}""")]
    public async Task A_tool_whose_schema_is_not_a_schema_does_not_take_the_other_tools_with_it(string brokenTool)
    {
        await using var broken = ScriptedMcpServer.Start((method, _, _, _) => Task.FromResult<string?>(
            string.Equals(method, "tools/list", StringComparison.Ordinal) ? Tools(brokenTool, GoodTool) : "{}"));

        var (_, tools) = await RefreshAsync(broken);

        HasHealthyTool(tools).ShouldBeTrue($"the healthy server's tool is gone after {brokenTool}");
    }

    [Fact]
    public async Task Two_tools_under_one_name_do_not_stop_the_refresh()
    {
        await using var broken = ScriptedMcpServer.Start((method, _, _, _) => Task.FromResult<string?>(
            string.Equals(method, "tools/list", StringComparison.Ordinal) ? Tools(GoodTool, GoodTool) : "{}"));

        var (_, tools) = await RefreshAsync(broken);

        HasHealthyTool(tools).ShouldBeTrue("the healthy server's tool is gone");
    }

    [Fact]
    public async Task A_description_of_several_megabytes_does_not_stop_the_refresh()
    {
        var description = new string('d', 6 * 1024 * 1024);

        await using var broken = ScriptedMcpServer.Start((method, _, _, _) => Task.FromResult<string?>(
            string.Equals(method, "tools/list", StringComparison.Ordinal)
                ? Tools("{\"name\":\"huge\",\"description\":\"" + description + "\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}")
                : "{}"));

        var (_, tools) = await RefreshAsync(broken);

        HasHealthyTool(tools).ShouldBeTrue("the healthy server's tool is gone");
    }

    private static bool HasHealthyTool(List<string> tools)
        => tools.Any(static name => string.Equals(name, "healthy_healthy_tool", StringComparison.Ordinal));

    private static string Tools(params string[] tools) => "{\"tools\":[" + string.Join(',', tools) + "]}";

    /// <summary>
    /// Registers the scripted server and a healthy one for one tenant, refreshes on a clock nobody moves, and lists the tool
    /// names. A refresh that ends here ended without its connection limit: the limit is armed on the clock and never reached,
    /// and one that would need it never ends and fails the wait.
    /// </summary>
    private static async Task<(McpRefreshOutcome Outcome, List<string> Tools)> RefreshAsync(ScriptedMcpServer broken, int? maxToolsPerServer = null)
    {
        var clock = new ManualClock();
        await using var rig = await Rig.StartAsync(broken, maxToolsPerServer, clock);

        var outcome = await rig.Catalog.RefreshAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        clock.CreatedTimers.ShouldBeGreaterThan(0, "the connection limit runs on the clock the catalog was given");

        return (outcome, rig.Catalog.ForTenant("default").Descriptors.Select(static tool => tool.Name).ToList());
    }

    /// <summary>
    /// The same refresh for a server that makes it wait for the connection limit: once <paramref name="reached"/> says the
    /// server has the request in hand and the healthy server is done (only the silent server's two limits are armed, the
    /// connection's and the list's), the clock moves by exactly the limit and the refresh gives up on it.
    /// </summary>
    private static async Task<(McpRefreshOutcome Outcome, List<string> Tools)> RefreshToTheLimitAsync(ScriptedMcpServer broken, Func<bool> reached, string description)
    {
        var clock = new ManualClock();
        await using var rig = await Rig.StartAsync(broken, maxToolsPerServer: null, clock);

        var refreshing = rig.Catalog.RefreshAsync(TestContext.Current.CancellationToken).AsTask();

        await WaitUntil.TrueAsync(() => reached() && clock.Pending.Count == 2, description);

        clock.Pending.ShouldBe([ConnectionTimeout, ConnectionTimeout], "the silent server's connection limit and list limit, both one limit away");

        clock.Advance(ConnectionTimeout);

        var outcome = await refreshing.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        return (outcome, rig.Catalog.ForTenant("default").Descriptors.Select(static tool => tool.Name).ToList());
    }

    private static TraconMcpOptions MisbehaviorOptions(int? maxToolsPerServer)
    {
        var options = new TraconMcpOptions { ConnectionTimeout = ConnectionTimeout };

        if (maxToolsPerServer is { } limit)
        {
            options.MaxToolsPerServer = limit;
        }

        return options;
    }

    /// <summary>The scripted server and a healthy one, registered for one tenant, with the catalog that lists them.</summary>
    private sealed class Rig : IAsyncDisposable
    {
        private readonly ScriptedMcpServer _healthy;

        private Rig(McpToolCatalog catalog, ScriptedMcpServer healthy)
        {
            Catalog = catalog;
            _healthy = healthy;
        }

        public McpToolCatalog Catalog { get; }

        public static async Task<Rig> StartAsync(ScriptedMcpServer broken, int? maxToolsPerServer = null, ManualClock? clock = null)
        {
            var healthy = ScriptedMcpServer.Start((method, _, _, _) => Task.FromResult<string?>(
                string.Equals(method, "tools/list", StringComparison.Ordinal) ? Tools(GoodTool) : "{}"));

            var servers = new InMemoryMcpServerStore();

            foreach (var (name, server) in new[] { ("broken", broken), ("healthy", healthy) })
            {
                await servers.SaveAsync(
                    new McpServerDefinition
                    {
                        Id = Guid.NewGuid(),
                        TenantId = "default",
                        Name = name,
                        Endpoint = server.Endpoint,
                    },
                    TestContext.Current.CancellationToken);
            }

            var catalog = new McpToolCatalog(
                servers,
                new InMemoryTenantStore(),
                new EmptyMcpConfiguration(),
                Options.Create(new TraconOptions()),
                Options.Create(MisbehaviorOptions(maxToolsPerServer)),
                NullLoggerFactory.Instance,
                new McpOAuthTokenCacheRegistry(),
                new AllowAllToolAuthorizationHandler(),
                McpToolPolicyTestGates.Open(),
                NoOpToolArgumentsValidator.Instance,
                attribution: null,
                egressGuard: new EgressSocketGuard(static () => new EgressAddressPolicy(AllowPrivateNetworkTargets: true, AllowLoopback: true)),
                timeProvider: clock);

            return new Rig(catalog, healthy);
        }

        public async ValueTask DisposeAsync()
        {
            await Catalog.DisposeAsync();
            await _healthy.DisposeAsync();
        }
    }
}
