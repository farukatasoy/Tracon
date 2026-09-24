using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Whether an approval decision stays with the remote tool it was made for.
/// </summary>
/// <remarks>
/// <para>
/// A standing approval rule is written by a security administrator
/// (<c>SecurityAdmin</c>). It names an agent, a tool, and optionally the
/// arguments - never the MCP server behind the tool. The server record, its
/// endpoint included, is written by a different role (<c>AgentsAdmin</c>). An
/// MCP tool is named <c>{server}_{tool}</c>, so pointing the same server name at
/// another endpoint keeps the tool name the rule matches.
/// </para>
/// <para>
/// These tests MEASURE today's behaviour against two real MCP servers. They
/// record the result as it is: when the rule is bound to its source, or when
/// <c>RequiresApproval</c> moves out of the <c>AgentsAdmin</c> record, the
/// assertions below flip and must be rewritten with the fix (ADAYLAR.md F-283 and
/// F-284).
/// </para>
/// </remarks>
public sealed class McpApprovalRuleSourceTests
{
    private const string ServerName = "lookup-srv";
    private const string ToolName = ServerName + "_lookup";

    /// <summary>
    /// The control first: without a rule the call waits for a person, so what
    /// approves it afterwards is the rule. Then the endpoint moves to another
    /// server, and the rule approves the new server's tool as well.
    /// </summary>
    [Fact]
    public async Task A_standing_rule_approves_the_tool_of_a_new_endpoint_after_an_endpoint_change()
    {
        await using var first = await LookupServer.StartAsync("from-endpoint-a");
        await using var second = await LookupServer.StartAsync("from-endpoint-b");
        await using var host = await StartAsync(runs: 3);

        var agentsAdmin = await CreateKeyAsync(host, "AgentsAdmin", "AgentsRead");
        var securityAdmin = await CreateKeyAsync(host, "SecurityAdmin");

        await SaveServerAsync(host, agentsAdmin, first.Endpoint, requiresApproval: true);
        await SaveAgentsAsync(host, runs: 3);

        (await RunAsync(host, run: 1)).Status.ShouldBe("AwaitingApproval", "the tool requires approval and no rule exists yet");

        using (var rule = WithKey(HttpMethod.Post, "/tracon/api/approvals/rules", new { toolName = ToolName }, securityAdmin))
        using (var created = await host.Client.SendAsync(rule))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        }

        var approved = await RunAsync(host, run: 2);
        approved.Status.ShouldBe("Completed");
        approved.Stream.ShouldContain("from-endpoint-a");

        // AgentsAdmin alone moves the server. Nobody with SecurityAdmin acts again.
        await SaveServerAsync(host, agentsAdmin, second.Endpoint, requiresApproval: true);

        var redirected = await RunAsync(host, run: 3);
        redirected.Status.ShouldBe("Completed", "the rule approved a tool of an endpoint it was never given for");
        redirected.Stream.ShouldContain("from-endpoint-b");
    }

    /// <summary>
    /// The shorter path to the same place: <c>RequiresApproval</c> is part of the
    /// server record, so an <c>AgentsAdmin</c> key turns approval off for every
    /// tool of the server without any rule.
    /// </summary>
    [Fact]
    public async Task An_agents_admin_key_can_switch_off_approval_for_every_tool_of_a_server()
    {
        await using var server = await LookupServer.StartAsync("from-unapproved-endpoint");
        await using var host = await StartAsync(runs: 1);

        var agentsAdmin = await CreateKeyAsync(host, "AgentsAdmin", "AgentsRead");

        await SaveServerAsync(host, agentsAdmin, server.Endpoint, requiresApproval: false);
        await SaveAgentsAsync(host, runs: 1);

        var run = await RunAsync(host, run: 1);
        run.Status.ShouldBe("Completed", "no approval was asked for, and no rule exists");
        run.Stream.ShouldContain("from-unapproved-endpoint");
    }

    // ---- Harness ----------------------------------------------------------------

    private static Task<TraconTestHost> StartAsync(int runs)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseMcp();

                for (var run = 1; run <= runs; run++)
                {
                    builder.AddModelProvider(new Tracon.Testing.FakeModelProvider($"mcp-model-{run}")
                        .CallsTool(ToolName)
                        .EchoesLastToolResult());
                }
            },
            configureServices: static services =>
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true));

    private static async Task SaveServerAsync(TraconTestHost host, string key, Uri endpoint, bool requiresApproval)
    {
        using (var request = WithKey(HttpMethod.Put, $"/tracon/api/mcp-servers/{ServerName}", new { endpoint = endpoint.ToString(), requiresApproval }, key))
        using (var saved = await host.Client.SendAsync(request))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        using var refreshed = await host.Client.PostAsync(new Uri("/tracon/api/mcp-servers/refresh", UriKind.Relative), content: null);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());
    }

    /// <summary>One stored agent per run: each scripted model answers one run.</summary>
    private static async Task SaveAgentsAsync(TraconTestHost host, int runs)
    {
        for (var run = 1; run <= runs; run++)
        {
            using var saved = await host.Client.PostAsJsonAsync(
                new Uri("/tracon/api/agents", UriKind.Relative),
                new AgentDefinitionRequest
                {
                    Name = $"mcp-agent-{run}",
                    Instructions = "Look the value up.",
                    Model = new ModelBinding { Provider = $"mcp-model-{run}", Model = "lookup" },
                    ToolNames = [ToolName],
                });

            saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        }
    }

    private static async Task<(string Status, string Stream)> RunAsync(TraconTestHost host, int run)
    {
        var session = $"mcp-session-{run}";

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/mcp-agent-{run}/run", UriKind.Relative),
            new AgentRunRequest { Message = "look it up", SessionId = session });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var stream = await response.Content.ReadAsStringAsync();

        using var runs = await host.Client.GetAsync(new Uri($"/tracon/api/runs?sessionId={session}", UriKind.Relative));
        var status = (await TraconTestHost.ReadJsonAsync(runs)).EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("status").GetString().ShouldNotBeNull();

        return (status, stream);
    }

    private static async Task<string> CreateKeyAsync(TraconTestHost host, params string[] scopes)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/api-keys", UriKind.Relative),
            new { name = $"mcp-{string.Join('-', scopes)}", scopes });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ApiKeyCreationResult>()).ShouldNotBeNull().PlaintextKey;
    }

    private static HttpRequestMessage WithKey(HttpMethod method, string path, object body, string key)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        return request;
    }

    /// <summary>A real MCP server on loopback that exposes one tool, <c>lookup</c>.</summary>
    private sealed class LookupServer : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private LookupServer(WebApplication app, Uri endpoint)
        {
            _app = app;
            Endpoint = endpoint;
        }

        public Uri Endpoint { get; }

        public static async Task<LookupServer> StartAsync(string answer)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddMcpServer()
                .WithHttpTransport()
                .WithTools([McpServerTool.Create(
                    [Description("Looks the value up.")] () => answer,
                    new McpServerToolCreateOptions { Name = "lookup" })]);

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            app.MapMcp("/mcp");
            await app.StartAsync();

            var address = app.Urls.First().TrimEnd('/');

            return new LookupServer(app, new Uri($"{address}/mcp"));
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
