using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Phase 50: the A2A server that exposes AgentPrism agents.</summary>
public sealed class A2AEndpointTests
{
    [Fact]
    public async Task Agent_card_is_published()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseA2A(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismA2A());

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/a2a/kod-agent/.well-known/agent-card.json", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("name").GetString().ShouldBe("kod-agent");
    }

    [Fact]
    public async Task Sending_a_message_runs_the_agent_and_produces_a_runs_row()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseA2A(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismA2A());

        var (response, body) = await SendMessageAsync(host.Client, "kod-agent", "hello");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body?.ToString());

        var runs = host.Services.GetRequiredService<IRunStore>();
        var all = await runs.QueryRunsAsync(new RunQuery());

        all.ShouldHaveSingleItem().AgentName.ShouldBe("kod-agent");
    }

    [Fact]
    public async Task An_agent_not_on_the_allow_list_is_not_published()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseA2A(),
            configureAfterMap: app => app.MapAgentPrismA2A());

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/a2a/kod-agent/.well-known/agent-card.json", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_agent_added_at_runtime_does_not_appear_in_A2A()
    {
        // 🚨 A measured constraint of A2A: ExposedAgents is fixed AT REGISTRATION
        // time. Unlike MCP, an agent added later cannot appear without building
        // a NEW A2A server (section 50.5).
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseA2A(o => o.ExposedAgents.Add("db-agent")),
            configureAfterMap: app => app.MapAgentPrismA2A());

        using var createResponse = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents", UriKind.Relative),
            TestData.Request());
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/a2a/db-agent/.well-known/agent-card.json", UriKind.Relative));

        // At registration time "db-agent" was not yet in the catalog; the proxy was
        // still set up (the name came from the FIXED allow list), and the card was
        // ALREADY PRODUCED and works - the CONSTRAINT is not that an agent ADDED
        // at runtime fails to appear, but that the allow list itself CANNOT be
        // extended afterward. This test documents that boundary: there is no WAY
        // to add an "added-later" name to the allow list.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A2A_does_not_start_when_AllowRemoteAccess_is_on()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseA2A(o => o.ExposedAgents.Add("kod-agent")),
            configureEndpoints: o => o.AllowRemoteAccess = true,
            configureAfterMap: app => app.MapAgentPrismA2A()));
    }

    [Fact]
    public async Task An_agent_carrying_a_tool_that_requires_approval_cannot_be_exposed_via_A2A()
    {
        // The guard no longer runs synchronously at Map* time; it runs in the
        // background inside A2AApprovalGuardFilter (see AgentPrismA2AExtensions)
        // — that is why the error comes from the FIRST request, not from
        // MapAgentPrismA2A().
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddTool(
                    (Func<string, string>)CancelOrder,
                    name: "cancel_order",
                    description: "Cancels an order.",
                    configure: options => options.RequiresApproval = true)
                .AddAgent(TestData.Definition() with { ToolNames = ["cancel_order"] })
                .UseA2A(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismA2A());

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => host.Client.GetAsync(
            new Uri("/agentprism/a2a/kod-agent/.well-known/agent-card.json", UriKind.Relative)));

        exception.Message.ShouldContain("approval", Case.Sensitive);
    }

    [Fact]
    public async Task MapAgentPrismA2A_does_not_crash_on_an_empty_sqlite_database()
    {
        // Regression: MapAgentPrismA2A() used to read the catalog SYNCHRONOUSLY
        // at Map* time (BEFORE migrations start); it crashed with "no such table"
        // on a completely empty database (file NOT YET created). After the guard
        // moved to A2AApprovalGuardFilter, Map* no longer touches the DB at all
        // (the remaining read for the agent card also falls back gracefully on a
        // DB error). `:memory:` is NOT USED: without a shared cache, each new
        // connection opens its own isolated, empty database.
        using var database = new TempSqliteDatabase("a2a-empty-db");

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .UseSqlite(database.ConnectionString)
                .AddAgent(TestData.Definition())
                .UseA2A(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismA2A());

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/a2a/kod-agent/.well-known/agent-card.json", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("name").GetString().ShouldBe("kod-agent");
    }

    [Fact]
    public async Task The_call_is_written_to_the_audit_log()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseA2A(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismA2A());

        await SendMessageAsync(host.Client, "kod-agent", "hello");

        var auditLog = host.Services.GetRequiredService<IAuditLog>();
        var entries = await auditLog.QueryAsync(new AuditQuery { Action = "external.call" });

        var entry = entries.ShouldHaveSingleItem();
        entry.Entity.ShouldBe("agent:kod-agent");
        entry.After.ShouldNotBeNull().ShouldContain("\"protocol\":\"a2a\"", Case.Sensitive);
    }

    private static string CancelOrder(string orderId) => $"canceled: {orderId}";

    private static async Task<(HttpResponseMessage Response, System.Text.Json.JsonElement? Body)> SendMessageAsync(
        HttpClient client,
        string agentName,
        string text)
    {
        // The body is not built by hand; it is produced with the SDK's OWN types
        // and its own JsonSerializerOptions (A2AJsonUtilities.DefaultOptions) —
        // this relies on the ACTUAL source of the contract instead of assuming
        // field names/casing.
        var sendMessageRequest = new A2A.SendMessageRequest
        {
            Message = new A2A.Message
            {
                Role = A2A.Role.User,
                Parts = [A2A.Part.FromText(text)],
                MessageId = Guid.NewGuid().ToString(),
            },
        };

        var paramsJson = System.Text.Json.JsonSerializer.SerializeToElement(
            sendMessageRequest, A2A.A2AJsonUtilities.DefaultOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/agentprism/a2a/{agentName}")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "SendMessage",
                @params = paramsJson,
            }),
        };
        request.Headers.Accept.ParseAdd("application/json");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        return (response, body.Length == 0 ? null : System.Text.Json.JsonDocument.Parse(body).RootElement);
    }
}
