using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Whether an approval decision stays with the remote tool it was made for.
/// </summary>
/// <remarks>
/// <para>
/// A standing approval rule is written by a security administrator
/// (<c>SecurityAdmin</c>). The MCP server record, its endpoint included, is
/// written by a different role (<c>AgentsAdmin</c>). An MCP tool is named
/// <c>{server}_{tool}</c>, so pointing the same server name at another endpoint
/// keeps the tool name a rule matches.
/// </para>
/// <para>
/// A rule therefore records the fingerprint of the endpoint its tool came from
/// and approves that tool only while the server keeps it; and switching approval
/// off for a server (<c>requiresApproval: false</c>) is a <c>SecurityAdmin</c>
/// decision about one endpoint. Both are measured here against real MCP servers,
/// because the tool name, the server record and the rule meet only at run time.
/// </para>
/// </remarks>
public sealed class McpApprovalRuleSourceTests
{
    private const string ServerName = "lookup-srv";
    private const string ToolName = ServerName + "_lookup";

    /// <summary>
    /// The control first: without a rule the call waits for a person, so what
    /// approves it afterwards is the rule. Then <c>AgentsAdmin</c> moves the
    /// server to another endpoint and the rule no longer approves its tool; a new
    /// rule, written for the new endpoint, does.
    /// </summary>
    [Fact]
    public async Task A_standing_rule_asks_again_after_the_endpoint_changes()
    {
        await using var first = await LookupMcpServer.StartAsync("from-endpoint-a");
        await using var second = await LookupMcpServer.StartAsync("from-endpoint-b");
        await using var host = await StartAsync(runs: 4);

        var agentsAdmin = await CreateKeyAsync(host, "AgentsAdmin", "AgentsRead");
        var securityAdmin = await CreateKeyAsync(host, "SecurityAdmin");

        await SaveServerAsync(host, agentsAdmin, first.Endpoint, requiresApproval: true);
        await SaveAgentsAsync(host, runs: 4);

        (await RunAsync(host, run: 1)).Status.ShouldBe("AwaitingApproval", "the tool requires approval and no rule exists yet");

        (await CreateRuleAsync(host, securityAdmin)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var approved = await RunAsync(host, run: 2);
        approved.Status.ShouldBe("Completed");
        approved.Stream.ShouldContain("from-endpoint-a");

        // AgentsAdmin alone moves the server. Nobody with SecurityAdmin acts.
        await SaveServerAsync(host, agentsAdmin, second.Endpoint, requiresApproval: true);

        var redirected = await RunAsync(host, run: 3);
        redirected.Status.ShouldBe("AwaitingApproval", "the rule was given for endpoint A, not for B");
        redirected.Stream.ShouldNotContain("from-endpoint-b");

        // The same tool, agent and conditions, but another source: a NEW rule,
        // not the old one handed back as a conflict.
        (await CreateRuleAsync(host, securityAdmin)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var reapproved = await RunAsync(host, run: 4);
        reapproved.Status.ShouldBe("Completed");
        reapproved.Stream.ShouldContain("from-endpoint-b");
    }

    /// <summary>
    /// Deleting the server and adding it again under the same name with another
    /// endpoint is the same move in two steps, and it asks again too.
    /// </summary>
    [Fact]
    public async Task Re_adding_a_server_under_the_same_name_with_another_endpoint_asks_again()
    {
        await using var first = await LookupMcpServer.StartAsync("from-endpoint-a");
        await using var second = await LookupMcpServer.StartAsync("from-endpoint-b");
        await using var host = await StartAsync(runs: 2);

        var agentsAdmin = await CreateKeyAsync(host, "AgentsAdmin", "AgentsRead");
        var securityAdmin = await CreateKeyAsync(host, "SecurityAdmin");

        await SaveServerAsync(host, agentsAdmin, first.Endpoint, requiresApproval: true);
        await SaveAgentsAsync(host, runs: 2);
        (await CreateRuleAsync(host, securityAdmin)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await RunAsync(host, run: 1)).Status.ShouldBe("Completed");

        using (var delete = WithKey(HttpMethod.Delete, $"/tracon/api/mcp-servers/{ServerName}", body: null, agentsAdmin))
        using (var deleted = await host.Client.SendAsync(delete))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        }

        await SaveServerAsync(host, agentsAdmin, second.Endpoint, requiresApproval: true);

        var readded = await RunAsync(host, run: 2);
        readded.Status.ShouldBe("AwaitingApproval");
        readded.Stream.ShouldNotContain("from-endpoint-b");
    }

    /// <summary>
    /// "Do not ask again", answered in the run itself, writes the same kind of
    /// rule: it holds for this tool of this endpoint only.
    /// </summary>
    /// <remarks>
    /// A remembered rule is scoped to the agent that asked, so every run here uses
    /// the SAME agent; only its scripted model is swapped between runs (one script
    /// per run). The control run proves the rule approves while the endpoint stays.
    /// </remarks>
    [Fact]
    public async Task A_remembered_approval_asks_again_after_the_endpoint_changes()
    {
        const string Agent = "mcp-agent-1";
        await using var first = await LookupMcpServer.StartAsync("from-endpoint-a");
        await using var second = await LookupMcpServer.StartAsync("from-endpoint-b");
        await using var host = await StartAsync(runs: 3);

        var agentsAdmin = await CreateKeyAsync(host, "AgentsAdmin", "AgentsRead");

        await SaveServerAsync(host, agentsAdmin, first.Endpoint, requiresApproval: true);
        await SaveAgentsAsync(host, runs: 1);

        var runUri = new Uri($"/tracon/api/agents/{Agent}/run", UriKind.Relative);

        using (var pending = await host.Client.PostAsJsonAsync(
                   runUri,
                   new AgentRunRequest { Message = "look it up", SessionId = "remembered" }))
        {
            pending.StatusCode.ShouldBe(HttpStatusCode.OK);
            var requestId = await ReadApprovalRequestIdAsync(pending);

            using var answered = await host.Client.PostAsJsonAsync(
                runUri,
                new AgentRunRequest
                {
                    SessionId = "remembered",
                    Approvals = [new ToolApprovalDecision { RequestId = requestId, Approved = true, Remember = true }],
                });

            answered.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await answered.Content.ReadAsStringAsync()).ShouldContain("from-endpoint-a");
        }

        using (var listed = await host.Client.GetAsync(new Uri("/tracon/api/approvals/rules", UriKind.Relative)))
        {
            var rule = (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray().ShouldHaveSingleItem();
            rule.GetProperty("toolName").GetString().ShouldBe(ToolName);
            rule.GetProperty("sourceFingerprint").GetString().ShouldBe(ToolSourceFingerprint.ForEndpoint(first.Endpoint));
        }

        // Control: same agent, same endpoint, a fresh session - the remembered
        // rule approves without asking.
        await UseModelAsync(host, Agent, "mcp-model-2");
        var control = await RunInSessionAsync(host, Agent, "remembered-control");
        control.Status.ShouldBe("Completed", "the remembered rule holds while the endpoint stays");
        control.Stream.ShouldContain("from-endpoint-a");

        await SaveServerAsync(host, agentsAdmin, second.Endpoint, requiresApproval: true);
        await UseModelAsync(host, Agent, "mcp-model-3");

        var redirected = await RunInSessionAsync(host, Agent, "remembered-moved");
        redirected.Status.ShouldBe("AwaitingApproval", "the remembered approval was given for endpoint A");
        redirected.Stream.ShouldNotContain("from-endpoint-b");
    }

    /// <summary>
    /// A rule for a tool of a server whose tools are not discovered right now
    /// would bind to no source and never approve the tool once the server is back;
    /// the request is refused instead of storing a rule that silently does nothing.
    /// </summary>
    [Fact]
    public async Task A_rule_for_a_tool_of_an_undiscovered_server_is_refused()
    {
        await using var host = await StartAsync(runs: 1);

        var agentsAdmin = await CreateKeyAsync(host, "AgentsAdmin", "AgentsRead");
        var securityAdmin = await CreateKeyAsync(host, "SecurityAdmin");

        // Nothing listens on port 1: the server is registered, its tools are not discovered.
        await SaveServerAsync(host, agentsAdmin, new Uri("http://127.0.0.1:1/mcp"), requiresApproval: true);

        using var refused = await CreateRuleAsync(host, securityAdmin);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());

        using var listed = await host.Client.GetAsync(new Uri("/tracon/api/approvals/rules", UriKind.Relative));
        (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray().ShouldBeEmpty();
    }

    /// <summary>
    /// <c>requiresApproval: false</c> waives approval for every tool of a server,
    /// which is the decision a rule makes for one tool. An <c>AgentsAdmin</c> key
    /// alone cannot make it, neither for a new server nor by moving a server that
    /// already waives approval to another endpoint.
    /// </summary>
    [Fact]
    public async Task Switching_approval_off_for_a_server_requires_the_security_admin_scope()
    {
        await using var first = await LookupMcpServer.StartAsync("from-waived-endpoint");
        await using var second = await LookupMcpServer.StartAsync("from-moved-endpoint");
        await using var host = await StartAsync(runs: 1);

        var agentsAdmin = await CreateKeyAsync(host, "AgentsAdmin", "AgentsRead");
        var both = await CreateKeyAsync(host, "AgentsAdmin", "AgentsRead", "SecurityAdmin");

        (await TrySaveServerAsync(host, agentsAdmin, first.Endpoint, requiresApproval: false))
            .ShouldBe(HttpStatusCode.Forbidden, "a new server that waives approval needs SecurityAdmin");

        await SaveServerAsync(host, both, first.Endpoint, requiresApproval: false);
        await SaveAgentsAsync(host, runs: 1);

        var waived = await RunAsync(host, run: 1);
        waived.Status.ShouldBe("Completed", "SecurityAdmin waived approval for this endpoint");
        waived.Stream.ShouldContain("from-waived-endpoint");

        (await TrySaveServerAsync(host, agentsAdmin, second.Endpoint, requiresApproval: false))
            .ShouldBe(HttpStatusCode.Forbidden, "the waiver was given for the first endpoint, not the second");

        (await TrySaveServerAsync(host, agentsAdmin, first.Endpoint, requiresApproval: false, description: "renamed"))
            .ShouldBe(HttpStatusCode.OK, "editing another field keeps the waiver SecurityAdmin gave");

        (await TrySaveServerAsync(host, agentsAdmin, second.Endpoint, requiresApproval: true))
            .ShouldBe(HttpStatusCode.OK, "requiring approval again is never a loosening");
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
        => (await TrySaveServerAsync(host, key, endpoint, requiresApproval)).ShouldBe(HttpStatusCode.OK);

    /// <summary>Saves the server and, when the save succeeds, refreshes the discovered tools.</summary>
    private static async Task<HttpStatusCode> TrySaveServerAsync(
        TraconTestHost host,
        string key,
        Uri endpoint,
        bool requiresApproval,
        string? description = null)
    {
        using (var request = WithKey(
                   HttpMethod.Put,
                   $"/tracon/api/mcp-servers/{ServerName}",
                   new { endpoint = endpoint.ToString(), requiresApproval, description },
                   key))
        using (var saved = await host.Client.SendAsync(request))
        {
            if (saved.StatusCode != HttpStatusCode.OK)
            {
                return saved.StatusCode;
            }
        }

        using var refreshed = await host.Client.PostAsync(new Uri("/tracon/api/mcp-servers/refresh", UriKind.Relative), content: null);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());

        return HttpStatusCode.OK;
    }

    private static async Task<HttpResponseMessage> CreateRuleAsync(TraconTestHost host, string securityAdmin)
    {
        using var rule = WithKey(HttpMethod.Post, "/tracon/api/approvals/rules", new { toolName = ToolName }, securityAdmin);

        return await host.Client.SendAsync(rule);
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

    /// <summary>Points an existing agent at another scripted model; the agent's name, and so its rules, stay.</summary>
    private static async Task UseModelAsync(TraconTestHost host, string agent, string modelProvider)
    {
        using var saved = await host.Client.PutAsJsonAsync(
            new Uri($"/tracon/api/agents/{agent}", UriKind.Relative),
            new AgentDefinitionRequest
            {
                Name = agent,
                Instructions = "Look the value up.",
                Model = new ModelBinding { Provider = modelProvider, Model = "lookup" },
                ToolNames = [ToolName],
            });

        saved.IsSuccessStatusCode.ShouldBeTrue(await saved.Content.ReadAsStringAsync());
    }

    private static async Task<(string Status, string Stream)> RunInSessionAsync(TraconTestHost host, string agent, string session)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{agent}/run", UriKind.Relative),
            new AgentRunRequest { Message = "look it up", SessionId = session });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var stream = await response.Content.ReadAsStringAsync();

        using var runs = await host.Client.GetAsync(new Uri($"/tracon/api/runs?sessionId={session}", UriKind.Relative));
        var status = (await TraconTestHost.ReadJsonAsync(runs)).EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("status").GetString().ShouldNotBeNull();

        return (status, stream);
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

    private static HttpRequestMessage WithKey(HttpMethod method, string path, object? body, string key)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        return request;
    }

    private static async Task<string> ReadApprovalRequestIdAsync(HttpResponseMessage response)
    {
        await foreach (var frame in SseReader.ReadAsync(await response.Content.ReadAsStreamAsync()))
        {
            if (string.Equals(frame.Event, "approvals", StringComparison.Ordinal))
            {
                return System.Text.Json.JsonDocument.Parse(frame.Data)
                    .RootElement.EnumerateArray()
                    .Select(request => request.GetProperty("requestId").GetString())
                    .First()
                    .ShouldNotBeNull();
            }
        }

        throw new ShouldAssertException("The run did not return a pending approval request.");
    }
}
