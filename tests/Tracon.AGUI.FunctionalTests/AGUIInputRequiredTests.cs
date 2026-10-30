using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AGUI.Abstractions;
using Tracon.AGUI.FunctionalTests.Infrastructure;
using Tracon.Testing;
using Tracon.Tests.Shared;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// An MCP server asks the user for input in the middle of a tool call: an AG-UI <c>input_required</c> interrupt, answered by
/// the next request's <c>resume</c> (an earlier change on an earlier change).
/// </summary>
/// <remarks>
/// 🚨 Everything here runs against a REAL MCP server speaking the 2026-07-28 protocol to the SDK's own client. The suspension,
/// the interrupt and the answer meet only at run time, across the MCP connection, the tool chain, the session history, the
/// AG-UI converter and the HTTP surface.
/// </remarks>
public sealed class AGUIInputRequiredTests
{

    private const string ServerName = "elicit";
    private const string Agent = "elicit-agent";

    [Fact]
    public async Task A_tool_that_asks_ends_the_run_with_an_input_required_interrupt()
    {
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name");

        var first = await host.RunAsync(Agent, AGUIInputs.User("t-ask", "greet me", "r1"));

        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);

        var interrupt = first.Interrupts.ShouldHaveSingleItem();
        interrupt.Reason.ShouldBe(InterruptReasons.InputRequired);
        interrupt.Message.ShouldBe(ElicitingMcpServer.Question);
        interrupt.ResponseSchema!.Value.GetProperty("properties").GetProperty("name").GetProperty("type").GetString().ShouldBe("string");

        // The request Tracon recorded for the session is its own bookkeeping (replay identity, fingerprint). It is the
        // interrupt that travels, not the raw result of the suspended call.
        first.Body.ShouldNotContain("replayCallId", Case.Sensitive);
        first.Body.ShouldNotContain("fingerprint", Case.Sensitive);

        var record = await host.Services.GetRequiredService<IRunStore>().GetRunAsync(first.TraconRunId, TestContext.Current.CancellationToken);
        record!.Status.ShouldBe(RunStatus.AwaitingInput);
        server.Calls.ShouldBe(1, "the tool ran once and was suspended");
    }

    [Fact]
    public async Task The_session_keeps_what_the_agent_produced_not_what_the_front_end_was_sent()
    {
        // 🚨 The agent builds the session's history from the very same update objects it hands to the caller. Rewriting an
        // update on its way to the front end (the suspended call's result is not for the front end to read) once rewrote the
        // history too, and the answer that followed found no request to answer.
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name");

        var first = await host.RunAsync(Agent, AGUIInputs.User("t-history", "greet me", "r1"));
        first.Interrupts.ShouldHaveSingleItem();

        using var session = await host.Client.GetAsync(new Uri("/tracon/api/sessions/t-history", UriKind.Relative), TestContext.Current.CancellationToken);
        var history = (await session.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("messages").GetRawText();

        history.ShouldNotContain("waiting for the user", Case.Insensitive);
        history.ShouldContain("requestId", Case.Sensitive, "the request Tracon recorded for the session is still there");
    }

    [Fact]
    public async Task The_answer_runs_the_tool_again_with_the_users_values()
    {
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name");

        var first = await host.RunAsync(Agent, AGUIInputs.User("t-answer", "greet me", "r1"));
        var id = first.Interrupts.ShouldHaveSingleItem().Id!;

        var answered = await host.RunAsync(
            Agent,
            AGUIInputs.With("t-answer", "r2", [], resume: [AGUIInputs.Resolved(id, new { name = "Ada" })]));

        answered.Status.ShouldBe(HttpStatusCode.OK, answered.Body);
        answered.Interrupts.ShouldBeEmpty();
        answered.Text.ShouldContain("hello Ada", Case.Sensitive);
        server.Seen.ShouldBe(["asked", "asked", "accept:Ada"], "the call ran again from its start and the answer reached the server");

        // The audit entry names the fields and never carries a value.
        var audit = await host.Services.GetRequiredService<IAuditLog>()
            .QueryAsync(new AuditQuery { Action = "tool_input.answer" }, TestContext.Current.CancellationToken);
        audit.ShouldHaveSingleItem().After!.ShouldNotContain("Ada", Case.Sensitive);
    }

    [Fact]
    public async Task A_dismissed_interrupt_tells_the_server_so()
    {
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name");

        var first = await host.RunAsync(Agent, AGUIInputs.User("t-dismiss", "greet me", "r1"));
        var id = first.Interrupts.ShouldHaveSingleItem().Id!;

        var dismissed = await host.RunAsync(Agent, AGUIInputs.With("t-dismiss", "r2", [], resume: [AGUIInputs.Cancelled(id)]));

        dismissed.Status.ShouldBe(HttpStatusCode.OK, dismissed.Body);
        dismissed.Text.ShouldContain("cancelled", Case.Sensitive);
        server.Seen.ShouldBe(["asked", "asked", "cancel"]);
    }

    [Fact]
    public async Task Values_that_do_not_fit_the_requested_fields_are_a_400_and_the_tool_does_not_run_again()
    {
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name");

        var first = await host.RunAsync(Agent, AGUIInputs.User("t-invalid", "greet me", "r1"));
        var id = first.Interrupts.ShouldHaveSingleItem().Id!;

        var invalid = await host.RunAsync(
            Agent,
            AGUIInputs.With("t-invalid", "r2", [], resume: [AGUIInputs.Resolved(id, new { name = 42 })]));

        invalid.Status.ShouldBe(HttpStatusCode.BadRequest, invalid.Body);
        server.Calls.ShouldBe(1, "a wrong answer is refused before anything is repeated");

        // The interrupt is still pending, so the right answer goes through.
        var fixedAnswer = await host.RunAsync(
            Agent,
            AGUIInputs.With("t-invalid", "r3", [], resume: [AGUIInputs.Resolved(id, new { name = "Ada" })]));

        fixedAnswer.Text.ShouldContain("hello Ada", Case.Sensitive);
    }

    private static async Task<AGUITestHost> StartAsync(ElicitingMcpServer server, string tool)
    {
        var host = await AGUITestHost.StartAsync(
            builder =>
            {
                builder.UseMcp();
                builder.AddModelProvider(new FakeModelProvider("elicit-model").CallsTool($"{ServerName}_{tool}").EchoesLastToolResult());
            },
            configureServices: static services => services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true));

        try
        {
            // The server and the agent are registered through the management API, the way an operator does it: an MCP
            // server is data, a tool is code.
            var key = await CreateKeyAsync(host, "AgentsAdmin", "AgentsRead", "SecurityAdmin");

            using (var saved = await host.Client.SendAsync(WithKey(
                       HttpMethod.Put,
                       $"/tracon/api/mcp-servers/{ServerName}",
                       new { endpoint = server.Endpoint.ToString(), requiresApproval = false },
                       key)))
            {
                saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            }

            using (var refreshed = await host.Client.PostAsync(new Uri("/tracon/api/mcp-servers/refresh", UriKind.Relative), content: null))
            {
                refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());
            }

            using var agent = await host.Client.PostAsJsonAsync(
                new Uri("/tracon/api/agents", UriKind.Relative),
                new AgentDefinitionRequest
                {
                    Name = Agent,
                    Instructions = "Greet the user.",
                    Model = new ModelBinding { Provider = "elicit-model", Model = "elicit" },
                    ToolNames = [$"{ServerName}_{tool}"],
                });

            agent.StatusCode.ShouldBe(HttpStatusCode.Created, await agent.Content.ReadAsStringAsync());

            return host;
        }
        catch
        {
            await host.DisposeAsync();

            throw;
        }
    }

    private static async Task<string> CreateKeyAsync(AGUITestHost host, params string[] scopes)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/api-keys", UriKind.Relative),
            new { name = $"agui-{string.Join('-', scopes)}", scopes });

        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();

        return created.GetProperty("plaintextKey").GetString()!;
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
}
