using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

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
/// </remarks>
public sealed class McpServerMisbehaviorTests
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(2);

    private const string GoodTool = """{"name":"healthy_tool","description":"works","inputSchema":{"type":"object","properties":{}}}""";

    [Fact]
    public async Task A_server_that_never_answers_tools_list_costs_one_timeout_and_the_healthy_server_keeps_its_tool()
    {
        await using var broken = ScriptedMcpServer.Start((method, _, _, _) => Task.FromResult<string?>(
            string.Equals(method, "tools/list", StringComparison.Ordinal) ? null : "{}"));

        var (outcome, elapsed, tools) = await RefreshAsync(broken);

        outcome.HadUnreachableServers.ShouldBeTrue("a server that never answers is unreachable");
        elapsed.ShouldBeLessThan(ConnectionTimeout * 4, $"the refresh waited {elapsed} for a server that never answers tools/list");
        HasHealthyTool(tools).ShouldBeTrue("the healthy server's tool is gone");
    }

    [Fact]
    public async Task A_server_with_three_thousand_tools_is_listed_in_bounded_time()
    {
        var many = string.Join(',', Enumerable.Range(0, 3000).Select(static index =>
            "{\"name\":\"tool_" + index + "\",\"description\":\"d\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"));

        await using var broken = ScriptedMcpServer.Start((method, _, _, _) => Task.FromResult<string?>(
            string.Equals(method, "tools/list", StringComparison.Ordinal) ? Tools(many) : "{}"));

        var (outcome, elapsed, tools) = await RefreshAsync(broken);

        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20), $"listing 3000 tools took {elapsed}");
        HasHealthyTool(tools).ShouldBeTrue("the healthy server's tool is gone");
        outcome.ToolCount.ShouldBeGreaterThan(0);
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

        var (_, elapsed, tools) = await RefreshAsync(broken);

        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        HasHealthyTool(tools).ShouldBeTrue($"the healthy server's tool is gone after {brokenTool}");
    }

    [Fact]
    public async Task Two_tools_under_one_name_do_not_stop_the_refresh()
    {
        await using var broken = ScriptedMcpServer.Start((method, _, _, _) => Task.FromResult<string?>(
            string.Equals(method, "tools/list", StringComparison.Ordinal) ? Tools(GoodTool, GoodTool) : "{}"));

        var (_, _, tools) = await RefreshAsync(broken);

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

        var (_, elapsed, tools) = await RefreshAsync(broken);

        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20));
        HasHealthyTool(tools).ShouldBeTrue("the healthy server's tool is gone");
    }

    private static bool HasHealthyTool(List<string> tools)
        => tools.Any(static name => string.Equals(name, "healthy_healthy_tool", StringComparison.Ordinal));

    private static string Tools(params string[] tools) => "{\"tools\":[" + string.Join(',', tools) + "]}";

    /// <summary>Registers the scripted server and a healthy one for one tenant, refreshes, and lists the tool names.</summary>
    private static async Task<(McpRefreshOutcome Outcome, TimeSpan Elapsed, List<string> Tools)> RefreshAsync(ScriptedMcpServer broken)
    {
        await using var healthy = ScriptedMcpServer.Start((method, _, _, _) => Task.FromResult<string?>(
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

        await using var catalog = new McpToolCatalog(
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

        var tools = catalog.ForTenant("default").Descriptors.Select(static tool => tool.Name).ToList();

        return (outcome, clock.Elapsed, tools);
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
