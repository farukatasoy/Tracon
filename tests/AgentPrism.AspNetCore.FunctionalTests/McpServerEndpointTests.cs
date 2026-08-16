using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 50: the MCP server that exposes AgentPrism agents.
/// </summary>
public sealed class McpServerEndpointTests
{
    [Fact]
    public async Task Empty_allowlist_returns_no_tools()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var (response, body) = await McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Tools(body!.Value).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Allowlisted_agent_appears_as_a_tool()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var (_, body) = await McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list");

        var tools = Tools(body!.Value);
        tools.GetArrayLength().ShouldBe(1);
        tools[0].GetProperty("name").GetString().ShouldBe("agentprism_kod-agent");
    }

    [Fact]
    public async Task ToolsCall_runs_the_agent_and_produces_a_runs_row()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var (response, body) = await McpTestClient.SendAsync(
            host.Client,
            "/agentprism/mcp",
            "tools/call",
            new { name = "agentprism_kod-agent", arguments = new { message = "hello" } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = body!.Value.GetProperty("result");

        // On a successful response, IsError is null and is never written to JSON.
        (!result.TryGetProperty("isError", out var isError) || !isError.GetBoolean()).ShouldBeTrue();

        var text = result.GetProperty("content")[0].GetProperty("text").GetString().ShouldNotBeNull();
        text.ShouldContain("hello", Case.Sensitive);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var all = await runs.QueryRunsAsync(new RunQuery());

        all.ShouldHaveSingleItem().AgentName.ShouldBe("kod-agent");
    }

    [Fact]
    public async Task Unknown_tool_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var (_, body) = await McpTestClient.SendAsync(
            host.Client,
            "/agentprism/mcp",
            "tools/call",
            new { name = "agentprism_kod-agent", arguments = new { message = "hello" } });

        var result = body!.Value.GetProperty("result");
        result.GetProperty("isError").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Agent_carrying_a_tool_that_requires_approval_cannot_be_exposed()
    {
        // The guard is no longer synchronous at Map* time; it now runs in the
        // background inside McpApprovalGuardFilter (see AgentPrismMcpServerExtensions)
        // — that is why the error is thrown from the FIRST request, not from
        // MapAgentPrismMcpServer().
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddTool(
                    (Func<string, string>)CancelOrder,
                    name: "cancel_order",
                    description: "Cancels an order.",
                    requiresApproval: true)
                .AddAgent(TestData.Definition() with { ToolNames = ["cancel_order"] })
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list"));

        exception.Message.ShouldContain("approval", Case.Sensitive);
    }

    [Fact]
    public async Task MapAgentPrismMcpServer_does_not_crash_on_an_empty_sqlite_database()
    {
        // Regression: MapAgentPrismMcpServer() used to read the catalog
        // SYNCHRONOUSLY at Map* time (BEFORE migrations started); on a
        // completely empty database (file NOT YET created) it crashed with
        // "no such table". Now that the guard moved into McpApprovalGuardFilter,
        // Map* never touches the DB at all. `:memory:` is NOT used: without a
        // shared cache, each new connection opens its own isolated empty
        // database, which does not mimic the real "empty file" scenario.
        var databasePath = Path.Combine(Path.GetTempPath(), $"agentprism-mcp-empty-db-{Guid.NewGuid():N}.db");

        try
        {
            await using var host = await AgentPrismTestHost.StartAsync(
                configureAgentPrism: builder => builder
                    .UseSqlite($"Data Source={databasePath}")
                    .AddAgent(TestData.Definition())
                    .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
                configureAfterMap: app => app.MapAgentPrismMcpServer());

            var (response, body) = await McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list");

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            Tools(body!.Value).GetArrayLength().ShouldBe(1);
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm", ".agentprism-migration-lock" })
            {
                File.Delete(databasePath + suffix);
            }
        }
    }

    [Fact]
    public async Task Depth_limit_blocks_a_sub_call()
    {
        const string RouterModel = "router-model";
        const string ResearcherModel = "researcher-model";
        const string StartTask = "background_agents_start_task";
        const string WaitForCompletion = "background_agents_wait_for_first_completion";
        const string GetResults = "background_agents_get_task_results";

        await using var host = await AgentPrismTestHost.StartAsync(configureAgentPrism: builder =>
        {
            var provider = new AgentPrism.Testing.FakeModelProvider("routing")
                .ForModel(RouterModel, cfg => cfg
                    .CallsTool(StartTask, new { agentName = "researcher", input = "sub task", description = "sub task" })
                    .CallsTool(WaitForCompletion, new { taskIds = new[] { 1 } })
                    .CallsTool(GetResults, new { taskId = 1 })
                    .EchoesLastToolResult("Delegated: ", inputTokens: 4, outputTokens: 6))
                .ForModel(ResearcherModel, cfg => cfg.RespondsWith("Sub task done", inputTokens: 4, outputTokens: 6));

            builder.AddModelProvider(provider);

            builder.AddAgent(new AgentDefinition
            {
                Name = "researcher",
                Description = "Does research.",
                Instructions = "Research.",
                Model = new ModelBinding { Provider = "routing", Model = ResearcherModel },
                Origin = AgentDefinitionOrigin.Code,
            });

            builder.AddAgent(new AgentDefinition
            {
                Name = "router",
                Description = "Delegates the work.",
                Instructions = "Delegate.",
                Model = new ModelBinding { Provider = "routing", Model = RouterModel },
                CallableAgentNames = ["researcher"],
                Origin = AgentDefinitionOrigin.Code,
            });

            // MaxDepth=0: the outer call ITSELF is the root (Depth 0); even a
            // single sub-call produces Depth 1 and exceeds the limit of 0. This
            // clearly verifies "an externally called agent cannot call a sub-agent".
            builder.UseMcpServer(o =>
            {
                o.ExposedAgents.Add("router");
                o.Budget = new AgentRunBudget { MaxDepth = 0 };
            });
        },
        configureAfterMap: app => app.MapAgentPrismMcpServer());

        var (_, body) = await McpTestClient.SendAsync(
            host.Client,
            "/agentprism/mcp",
            "tools/call",
            new { name = "agentprism_router", arguments = new { message = "start" } });

        var text = body!.Value.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString().ShouldNotBeNull();
        text.ShouldContain("call depth limit was exceeded", Case.Sensitive);

        var runs = host.Services.GetRequiredService<IRunStore>();
        (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task MCP_cannot_be_opened_while_AllowRemoteAccess_is_on()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureEndpoints: o => o.AllowRemoteAccess = true,
            configureAfterMap: app => app.MapAgentPrismMcpServer()));
    }

    [Fact]
    public async Task Unauthenticated_request_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureEndpoints: o => o.AuthToken = "s3cr3t",
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var (response, _) = await McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_with_the_correct_token_is_accepted()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureEndpoints: o => o.AuthToken = "s3cr3t",
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var (response, _) = await McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list", token: "s3cr3t");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Call_is_written_to_the_audit_log()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        await McpTestClient.SendAsync(
            host.Client,
            "/agentprism/mcp",
            "tools/call",
            new { name = "agentprism_kod-agent", arguments = new { message = "hello" } });

        var auditLog = host.Services.GetRequiredService<IAuditLog>();
        var entries = await auditLog.QueryAsync(new AuditQuery { Action = "external.call" });

        var entry = entries.ShouldHaveSingleItem();
        entry.Entity.ShouldBe("agent:kod-agent");
        entry.After.ShouldNotBeNull().ShouldContain("\"protocol\":\"mcp\"", Case.Sensitive);
    }

    [Fact]
    public async Task Dynamic_catalog_shows_a_new_agent_without_restarting_the_server()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseMcpServer(o => o.ExposedAgents.Add("db-agent")),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var before = Tools((await McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list")).Body!.Value);
        before.GetArrayLength().ShouldBe(0);

        using var createResponse = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents", UriKind.Relative),
            TestData.Request());

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var after = Tools((await McpTestClient.SendAsync(host.Client, "/agentprism/mcp", "tools/list")).Body!.Value);
        after.GetArrayLength().ShouldBe(1);
        after[0].GetProperty("name").GetString().ShouldBe("agentprism_db-agent");
    }

    [Fact]
    public async Task Tenant_isolation_is_preserved()
    {
        const string TenantHeader = "X-AgentPrism-Tenant";

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .UseTenancy(o =>
                {
                    o.Enabled = true;
                    o.AllowHeaderResolution = true;
                })
                .UseMcpServer(o => o.ExposeAllAgents = true),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        using var createRequest = new HttpRequestMessage(
            HttpMethod.Post, new Uri("/agentprism/api/agents", UriKind.Relative))
        {
            Content = JsonContent.Create(TestData.Request()),
        };
        createRequest.Headers.Add(TenantHeader, "tenant-a");

        using var createResponse = await host.Client.SendAsync(createRequest);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Tenant a sees its own agent.
        var mine = Tools((await McpTestClient.SendAsync(
            host.Client, "/agentprism/mcp", "tools/list", tenantHeader: "tenant-a")).Body!.Value);
        mine.GetArrayLength().ShouldBe(1);

        // Tenant b sees nothing — the tenant header is not proof of identity,
        // but HttpTenantContext itself never shows one tenant's data to
        // another tenant anyway (the same boundary as TenancyTests).
        var theirs = Tools((await McpTestClient.SendAsync(
            host.Client, "/agentprism/mcp", "tools/list", tenantHeader: "tenant-b")).Body!.Value);
        theirs.GetArrayLength().ShouldBe(0);
    }

    private static string CancelOrder(string orderId) => $"canceled: {orderId}";

    private static System.Text.Json.JsonElement Tools(System.Text.Json.JsonElement body)
        => body.GetProperty("result").GetProperty("tools");
}
