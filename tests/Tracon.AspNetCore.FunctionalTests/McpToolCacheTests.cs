using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An agent that sits in the compiled-agent cache follows a change to the MCP
/// server its tool comes from on its very next run.
/// </summary>
/// <remarks>
/// <para>
/// The compiler binds the tool OBJECT it finds in the tenant's MCP snapshot,
/// and that object calls the server through the connection it was discovered
/// on. When a server definition changes, the server is deleted, or its tools
/// cannot be listed, the catalog replaces or drops that connection. The cache
/// key carries the connection each tool is bound to, so the next run compiles
/// again: it binds the live connection, or fails the way an uncached compile
/// does.
/// </para>
/// <para>
/// 🚨 Regression (2026-10-11): the key had no MCP component. In every case below
/// the second run returned HTTP 200 with status <c>Completed</c>: the cached
/// agent sent <c>tools/call</c> through the closed session, the call failed with
/// <c>TaskCanceledException</c>, and the model got "Error: Function failed.".
/// The request reached neither server. After approval was switched back on, the
/// cached agent did not ask; only the closed connection kept the tool from
/// running.
/// </para>
/// <para>
/// Every run uses one agent and one unchanged definition, so the second run is a
/// cache hit. <c>McpApprovalRuleSourceTests</c> did not see the defect: it gives
/// each run its own agent or changes the definition between runs. The scripted
/// model is re-armed with one tool call before each run instead: its queue lives
/// on the provider, and the cached agent's client reads the same queue.
/// </para>
/// </remarks>
public sealed class McpToolCacheTests
{
    private const string ServerName = "cache-srv";
    private const string ToolName = ServerName + "_lookup";
    private const string ReadResourceToolName = ServerName + "_read_resource";
    private const string AgentName = "mcp-cached-agent";
    private const string ProviderName = "mcp-cache-model";

    /// <summary>
    /// The server moves to another endpoint. The next run of the cached agent
    /// calls the new endpoint.
    /// </summary>
    [Fact]
    public async Task A_cached_agent_calls_the_new_endpoint_after_the_server_moves()
    {
        await using var first = await LookupMcpServer.StartAsync("from-endpoint-a");
        await using var second = await LookupMcpServer.StartAsync("from-endpoint-b");
        var model = NewModel();
        await using var host = await StartAsync(model);

        await SaveServerAsync(host, first.Endpoint, requiresApproval: false);
        await SaveAgentAsync(host, ToolName);

        var before = await RunAsync(host, model, "before-move", ToolName);
        before.Status.ShouldBe("Completed", before.Stream);
        before.Stream.ShouldContain("from-endpoint-a");

        await SaveServerAsync(host, second.Endpoint, requiresApproval: false);

        var after = await RunAsync(host, model, "after-move", ToolName);
        after.Stream.ShouldContain("from-endpoint-b", customMessage: await DescribeAsync(host, after));
        after.Status.ShouldBe("Completed", after.Stream);
        first.Calls.ShouldBe(1, "only the run before the move reaches endpoint A");
        second.Calls.ShouldBe(1);
    }

    /// <summary>
    /// The server is deleted. An agent whose tool is no longer registered fails to
    /// compile with the explicit "not registered" error, exactly as it would without
    /// the cache; it does not run with a tool bound to a closed connection.
    /// </summary>
    [Fact]
    public async Task A_cached_agent_does_not_run_a_tool_of_a_deleted_server()
    {
        await using var server = await LookupMcpServer.StartAsync("from-deleted-server");
        var model = NewModel();
        await using var host = await StartAsync(model);

        await SaveServerAsync(host, server.Endpoint, requiresApproval: false);
        await SaveAgentAsync(host, ToolName);

        var before = await RunAsync(host, model, "before-delete", ToolName);
        before.Status.ShouldBe("Completed", before.Stream);
        before.Stream.ShouldContain("from-deleted-server");

        using (var deleted = await host.Client.DeleteAsync(new Uri($"/tracon/api/mcp-servers/{ServerName}", UriKind.Relative)))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        }

        await RefreshAsync(host);

        await AssertRefusedAsNotRegisteredAsync(host, model, "after-delete");
        server.Calls.ShouldBe(1, "only the run before the delete reaches the server");
    }

    /// <summary>
    /// Approval is switched back on for the same endpoint. The next run of the
    /// cached agent waits for a person instead of calling the tool.
    /// </summary>
    [Fact]
    public async Task A_cached_agent_asks_for_approval_after_approval_is_switched_back_on()
    {
        await using var server = await LookupMcpServer.StartAsync("from-waived-server");
        var model = NewModel();
        await using var host = await StartAsync(model);

        await SaveServerAsync(host, server.Endpoint, requiresApproval: false);
        await SaveAgentAsync(host, ToolName);

        var before = await RunAsync(host, model, "before-approval", ToolName);
        before.Status.ShouldBe("Completed", before.Stream);
        before.Stream.ShouldContain("from-waived-server");

        await SaveServerAsync(host, server.Endpoint, requiresApproval: true);

        var after = await RunAsync(host, model, "after-approval", ToolName);
        after.Status.ShouldBe("AwaitingApproval", await DescribeAsync(host, after));
        after.Stream.ShouldNotContain("from-waived-server");
        server.Calls.ShouldBe(1, "the tool does not run before a person approves it");
    }

    /// <summary>
    /// One failed <c>tools/list</c> keeps the last good list while it is younger
    /// than <see cref="TraconMcpOptions.StaleToolRetention"/>. The connection is the same,
    /// so the binding is the same: the cached agent keeps running and its call reaches
    /// the server. Before, a single blip refused every agent of the server until the
    /// next good refresh.
    /// </summary>
    [Fact]
    public async Task A_cached_agent_keeps_working_through_a_transient_listing_failure()
    {
        await using var server = await LookupMcpServer.StartAsync("from-flaky-listing");
        var model = NewModel();
        await using var host = await StartAsync(model);

        await SaveServerAsync(host, server.Endpoint, requiresApproval: false);
        await SaveAgentAsync(host, ToolName);

        var before = await RunAsync(host, model, "before-blip", ToolName);
        before.Status.ShouldBe("Completed", before.Stream);

        server.FailListing = true;
        await RefreshAsync(host);

        var during = await RunAsync(host, model, "during-blip", ToolName);
        during.Status.ShouldBe("Completed", await DescribeAsync(host, during));
        during.Stream.ShouldContain("from-flaky-listing");
        server.Calls.ShouldBe(2, "the kept tool calls the live connection");
    }

    /// <summary>
    /// The server stops answering and, once the retention has passed, the refresh
    /// drops its tools; the connection itself stays registered. The cached path then
    /// behaves like an uncached compile: the agent is refused with "not registered".
    /// A retention of zero drops them at the first
    /// failed listing; a millisecond has always passed by the next refresh.
    /// </summary>
    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:00:00.001")]
    public async Task A_cached_agent_is_refused_like_an_uncached_compile_once_the_retention_has_passed(string retention)
    {
        await using var server = await LookupMcpServer.StartAsync("from-stopped-server");
        var model = NewModel();
        await using var host = await StartAsync(
            model,
            mcp => mcp.StaleToolRetention = TimeSpan.Parse(retention, System.Globalization.CultureInfo.InvariantCulture));

        await SaveServerAsync(host, server.Endpoint, requiresApproval: false);
        await SaveAgentAsync(host, ToolName);

        var before = await RunAsync(host, model, "before-failure", ToolName);
        before.Status.ShouldBe("Completed", before.Stream);
        before.Stream.ShouldContain("from-stopped-server");

        // Stopped early; the second dispose at the end of the test does nothing.
        await server.DisposeAsync();
        await RefreshAsync(host);

        await AssertRefusedAsNotRegisteredAsync(host, model, "after-failure");
    }

    /// <summary>
    /// The server changes the tool's description while the connection stays
    /// the same. The cached agent embedded the tool object with the old description;
    /// the binding carries a hash of the definition, so the next run compiles again and
    /// the model sees the new one. Before, the model kept seeing the old description and
    /// schema until the definition changed or the process restarted.
    /// </summary>
    [Fact]
    public async Task A_cached_agent_sees_a_tool_description_the_server_changed()
    {
        await using var server = await LookupMcpServer.StartAsync("from-edited-server");
        var model = NewModel();
        await using var host = await StartAsync(model);

        server.ToolDescription = "Looks the value up in the old index.";
        await SaveServerAsync(host, server.Endpoint, requiresApproval: false);
        await SaveAgentAsync(host, ToolName);

        var before = await RunAsync(host, model, "before-edit", ToolName);
        before.Status.ShouldBe("Completed", before.Stream);
        DescriptionSeenByModel(model).ShouldBe("Looks the value up in the old index.");

        server.ToolDescription = "Looks the value up in the new index.";
        await RefreshAsync(host);

        var after = await RunAsync(host, model, "after-edit", ToolName);
        after.Status.ShouldBe("Completed", after.Stream);
        DescriptionSeenByModel(model).ShouldBe("Looks the value up in the new index.");
        server.Calls.ShouldBe(2);
    }

    /// <summary>
    /// The generated <c>{server}_read_resource</c> tool is bound to its connection
    /// the same way. After the server moves, the cached agent reads the resource
    /// from the new endpoint.
    /// </summary>
    [Fact]
    public async Task A_cached_agent_reads_a_resource_from_the_new_endpoint_after_the_server_moves()
    {
        await using var first = await LookupMcpServer.StartAsync("resource-from-a", withResource: true);
        await using var second = await LookupMcpServer.StartAsync("resource-from-b", withResource: true);
        var model = NewModel();
        await using var host = await StartAsync(model);

        await SaveServerAsync(host, first.Endpoint, requiresApproval: false);
        await SaveAgentAsync(host, ReadResourceToolName);

        var before = await RunAsync(host, model, "before-resource-move", ReadResourceToolName, new { uri = LookupMcpServer.ResourceUri });
        before.Status.ShouldBe("Completed", before.Stream);
        before.Stream.ShouldContain("resource-from-a");

        await SaveServerAsync(host, second.Endpoint, requiresApproval: false);

        var after = await RunAsync(host, model, "after-resource-move", ReadResourceToolName, new { uri = LookupMcpServer.ResourceUri });
        after.Stream.ShouldContain("resource-from-b", customMessage: await DescribeAsync(host, after));
        after.Status.ShouldBe("Completed", after.Stream);
        first.ResourceReads.ShouldBe(1, "only the run before the move reads from endpoint A");
        second.ResourceReads.ShouldBe(1);
    }

    // ---- Harness ----------------------------------------------------------------

    /// <summary>A model that echoes the last tool result; each run arms one tool call first.</summary>
    private static Tracon.Testing.FakeModelProvider NewModel()
        => new Tracon.Testing.FakeModelProvider(ProviderName).EchoesLastToolResult();

    private static Task<TraconTestHost> StartAsync(Tracon.Testing.FakeModelProvider model, Action<TraconMcpOptions>? mcp = null)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseMcp();
                builder.AddModelProvider(model);
            },
            configureServices: services =>
            {
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);

                if (mcp is not null)
                {
                    services.Configure(mcp);
                }
            });

    /// <summary>The description of the tool in the newest model request.</summary>
    private static string? DescriptionSeenByModel(Tracon.Testing.FakeModelProvider model)
        => model.Requests[^1].Options?.Tools?.Single(tool => string.Equals(tool.Name, ToolName, StringComparison.Ordinal)).Description;

    /// <summary>Saves the server and refreshes the discovered tools.</summary>
    private static async Task SaveServerAsync(TraconTestHost host, Uri endpoint, bool requiresApproval)
    {
        using (var saved = await host.Client.PutAsJsonAsync(
                   new Uri($"/tracon/api/mcp-servers/{ServerName}", UriKind.Relative),
                   new { endpoint = endpoint.ToString(), requiresApproval }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        await RefreshAsync(host);
    }

    private static async Task RefreshAsync(TraconTestHost host)
    {
        using var refreshed = await host.Client.PostAsync(new Uri("/tracon/api/mcp-servers/refresh", UriKind.Relative), content: null);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());
    }

    private static async Task SaveAgentAsync(TraconTestHost host, string toolName)
    {
        using var saved = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            new AgentDefinitionRequest
            {
                Name = AgentName,
                Instructions = "Look the value up.",
                Model = new ModelBinding { Provider = ProviderName, Model = "lookup" },
                ToolNames = [toolName],
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> PostRunAsync(TraconTestHost host, string session)
        => host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative),
            new AgentRunRequest { Message = "look it up", SessionId = session });

    /// <summary>Arms one tool call, runs the agent in a fresh session, and reads the run's status.</summary>
    private static async Task<RunOutcome> RunAsync(
        TraconTestHost host,
        Tracon.Testing.FakeModelProvider model,
        string session,
        string toolName,
        object? arguments = null)
    {
        model.CallsTool(toolName, arguments);

        using var response = await PostRunAsync(host, session);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var stream = await response.Content.ReadAsStringAsync();

        using var runs = await host.Client.GetAsync(new Uri($"/tracon/api/runs?sessionId={session}", UriKind.Relative));
        var run = (await TraconTestHost.ReadJsonAsync(runs)).EnumerateArray().ShouldHaveSingleItem();

        return new RunOutcome(
            run.GetProperty("status").GetString().ShouldNotBeNull(),
            stream,
            run.GetProperty("id").GetString().ShouldNotBeNull());
    }

    /// <summary>
    /// The model is armed to call the tool, so a cached agent that still held it
    /// would call it; the run must be refused before any model call instead.
    /// </summary>
    private static async Task AssertRefusedAsNotRegisteredAsync(TraconTestHost host, Tracon.Testing.FakeModelProvider model, string session)
    {
        model.CallsTool(ToolName);

        using var after = await PostRunAsync(host, session);
        var body = await after.Content.ReadAsStringAsync();

        after.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"{body}\n{await DescribeSessionAsync(host, session)}");
        body.ShouldContain(ToolName);
        body.ShouldContain("not registered");
    }

    /// <summary>What the run recorded and what the MCP client did: the evidence when an assertion fails.</summary>
    private static async Task<string> DescribeAsync(TraconTestHost host, RunOutcome run)
    {
        using var events = await host.Client.GetAsync(new Uri($"/tracon/api/runs/{run.RunId}/events", UriKind.Relative));

        return $"status: {run.Status}\nstream:\n{run.Stream}\nevents:\n{await events.Content.ReadAsStringAsync()}\n{RelevantLogs(host)}";
    }

    private static async Task<string> DescribeSessionAsync(TraconTestHost host, string session)
    {
        using var runs = await host.Client.GetAsync(new Uri($"/tracon/api/runs?sessionId={session}", UriKind.Relative));

        return $"runs: {await runs.Content.ReadAsStringAsync()}\n{RelevantLogs(host)}";
    }

    private static string RelevantLogs(TraconTestHost host)
        => "logs:\n" + string.Join(
            '\n',
            host.Logs.Entries.Where(static entry =>
                entry.Contains("tools/call", StringComparison.Ordinal)
                || entry.Contains("resources/read", StringComparison.Ordinal)
                || entry.Contains("FunctionInvokingChatClient", StringComparison.Ordinal)
                || entry.Contains("disposed", StringComparison.OrdinalIgnoreCase)
                || entry.StartsWith("Warning", StringComparison.Ordinal)
                || entry.StartsWith("Error", StringComparison.Ordinal)));

    private sealed record RunOutcome(string Status, string Stream, string RunId);
}
