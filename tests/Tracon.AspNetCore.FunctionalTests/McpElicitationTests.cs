using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Shared;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An MCP server asks the user for input in the middle of a tool call (elicitation, Phase 235): the run
/// ends waiting, the request reaches the caller, and the answer — the next turn's input — runs the tool again.
/// </summary>
/// <remarks>
/// 🚨 Everything here runs against a REAL MCP server speaking the 2026-07-28 protocol to the SDK's own
/// client: the suspension, the replayed call and the answer meet only at run time, across the MCP
/// connection, the tool chain, the function-invocation loop, the session history and the HTTP surface.
/// A unit test with a fake client would prove none of that. The class also pins the one piece of
/// Microsoft Agent Framework's state Tracon writes (<c>ToolInputSessionBridge</c>): a framework upgrade
/// that moves it fails the very first test, not a production run.
/// </remarks>
public sealed class McpElicitationTests
{
    private const string ServerName = "elicit";
    private const string Agent = "elicit-agent";

    [Fact]
    public async Task A_tool_that_asks_suspends_the_run_and_the_answer_runs_the_tool_again()
    {
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name");

        var first = await RunAsync(host, new AgentRunRequest { Message = "greet me", SessionId = "elicit-1" });

        first.EventNames.ShouldContain("inputs", StringComparer.Ordinal);
        IndexOf(first.EventNames, "inputs").ShouldBeLessThan(IndexOf(first.EventNames, "done"), "'inputs' comes right before 'done'");

        var request = first.Inputs.ShouldHaveSingleItem();
        request.GetProperty("serverName").GetString().ShouldBe(ServerName);
        request.GetProperty("toolName").GetString().ShouldBe($"{ServerName}_ask_name");
        request.GetProperty("message").GetString().ShouldBe(ElicitingMcpServer.Question);
        request.GetProperty("requestedSchema").GetProperty("properties").GetProperty("name").GetProperty("type").GetString().ShouldBe("string");

        (await RunStatusesAsync(host, "elicit-1")).ShouldBe(["AwaitingInput"]);
        server.Calls.ShouldBe(1, "the tool ran once and was suspended");

        var answered = await RunAsync(host, Answer("elicit-1", request, ToolInputAction.Accept, ("name", "Ada")));

        answered.Text.ShouldContain("hello Ada", Case.Sensitive);
        answered.Inputs.ShouldBeEmpty();
        server.Seen.ShouldBe(["asked", "asked", "accept:Ada"], "the call ran again from its start and the answer reached the server");

        (await RunStatusesAsync(host, "elicit-1")).ShouldBe(["AwaitingInput", "Completed"]);
    }

    [Fact]
    public async Task The_buffered_response_carries_the_request_in_its_body()
    {
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name");

        // 'Idempotency-Key' makes the run non-streaming (a replayed response must be reconstructable).
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{Agent}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "greet me", SessionId = "elicit-buffered" }),
        };
        message.Headers.Add("Idempotency-Key", "elicit-buffered-1");

        using var response = await host.Client.SendAsync(message);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var body = await TraconTestHost.ReadJsonAsync(response);
        var request = body.GetProperty("inputs").EnumerateArray().ShouldHaveSingleItem();
        request.GetProperty("message").GetString().ShouldBe(ElicitingMcpServer.Question);
        (await RunStatusesAsync(host, "elicit-buffered")).ShouldBe(["AwaitingInput"]);
    }

    [Fact]
    public async Task A_server_on_the_older_protocol_gets_a_decline_for_its_direct_request()
    {
        // The older protocol delivers the request from the connection's receive loop, outside any tool call:
        // there is no run to suspend and no call to replay. The loop must not answer on behalf of whichever
        // run happens to share the connection, and the server must not be left waiting.
        await using var server = await ElicitingMcpServer.StartAsync(olderProtocol: true);
        await using var host = await StartAsync(server, "ask_direct");

        var run = await RunAsync(host, new AgentRunRequest { Message = "greet me", SessionId = "elicit-older" });

        run.Inputs.ShouldBeEmpty("a direct request never suspends a run");
        run.Text.ShouldContain("declined", Case.Sensitive, $"FULL={run.Text} SEEN={string.Join("|", server.Seen)}");
        server.Seen.ShouldBe(["asked", "decline"]);
        (await RunStatusesAsync(host, "elicit-older")).ShouldBe(["Completed"]);
    }

    [Fact]
    public async Task An_approval_required_server_asks_for_approval_once_and_for_input_once()
    {
        // 🚨 The control that matters most: an approval-required server (the default) enters the turn that
        // runs the call WITH a pending approval recorded by the framework, and the framework erases that whole
        // set after the call. A suspended call written down from inside the tool would be erased with it.
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name", requiresApproval: true);

        var first = await RunAsync(host, new AgentRunRequest { Message = "greet me", SessionId = "elicit-approval" });

        first.EventNames.ShouldContain("approvals", StringComparer.Ordinal);
        first.Inputs.ShouldBeEmpty("the call has not run yet: it waits for approval first");
        (await RunStatusesAsync(host, "elicit-approval")).ShouldBe(["AwaitingApproval"]);
        server.Calls.ShouldBe(0);

        var approvalId = first.Approvals.ShouldHaveSingleItem().GetProperty("requestId").GetString()!;

        var approved = await RunAsync(
            host,
            new AgentRunRequest
            {
                SessionId = "elicit-approval",
                Approvals = [new ToolApprovalDecision { RequestId = approvalId, Approved = true }],
            });

        var request = approved.Inputs.ShouldHaveSingleItem();
        (await RunStatusesAsync(host, "elicit-approval")).ShouldBe(["AwaitingApproval", "AwaitingInput"]);

        var answered = await RunAsync(host, Answer("elicit-approval", request, ToolInputAction.Accept, ("name", "Grace")));

        answered.EventNames.ShouldNotContain("approvals", StringComparer.Ordinal, "answering a request is not a second approval");
        answered.Text.ShouldContain("hello Grace", Case.Sensitive);
        server.Seen.ShouldBe(["asked", "asked", "accept:Grace"]);
        (await RunStatusesAsync(host, "elicit-approval")).ShouldBe(["AwaitingApproval", "AwaitingInput", "Completed"]);
    }

    [Theory]
    [InlineData(ToolInputAction.Decline, "decline", "declined")]
    [InlineData(ToolInputAction.Cancel, "cancel", "cancelled")]
    public async Task Declining_or_cancelling_tells_the_server_and_the_tool_goes_on(ToolInputAction action, string told, string result)
    {
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name");

        var first = await RunAsync(host, new AgentRunRequest { Message = "greet me", SessionId = "elicit-decline" });
        var answered = await RunAsync(host, Answer("elicit-decline", first.Inputs.ShouldHaveSingleItem(), action));

        server.Seen.ShouldBe(["asked", "asked", told]);
        answered.Text.ShouldContain(result, Case.Sensitive);
        (await RunStatusesAsync(host, "elicit-decline")).ShouldBe(["AwaitingInput", "Completed"]);
    }

    [Fact]
    public async Task A_run_without_a_session_declines_instead_of_waiting_for_an_answer_nobody_can_give()
    {
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name");

        var run = await RunAsync(host, new AgentRunRequest { Message = "greet me" });

        run.Inputs.ShouldBeEmpty();
        run.Text.ShouldContain("declined", Case.Sensitive);
        server.Seen.ShouldBe(["asked", "decline"], "no_session: the server was told, not left waiting");

        var declined = await DeclineEventsAsync(host, run.RunId);
        declined.ShouldHaveSingleItem().reason.ShouldBe("no_session");
    }

    [Fact]
    public async Task An_openai_compatible_call_declines()
    {
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name");

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/chat/completions", UriKind.Relative),
            new { model = Agent, messages = new[] { new { role = "user", content = "greet me" } } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).ShouldContain("declined", Case.Sensitive);
        server.Seen.ShouldBe(["asked", "decline"]);

        // A Chat Completions call never has a session, so that is the reason - not a missing channel.
        using var runs = await host.Client.GetAsync(new Uri("/tracon/api/runs", UriKind.Relative));
        var latest = (await TraconTestHost.ReadJsonAsync(runs)).EnumerateArray()
            .OrderByDescending(static run => run.GetProperty("startedAt").GetDateTimeOffset())
            .First();

        (await DeclineEventsAsync(host, latest.GetProperty("id").GetGuid())).ShouldHaveSingleItem().reason.ShouldBe("no_session");
    }

    [Fact]
    public async Task A_child_agent_cannot_ask_the_user()
    {
        // K-103: an agent below the root of a tree cannot wait in the middle of it. The request is declined
        // and the server decides; the root run neither waits nor fails.
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name", withChildAgent: true);

        var run = await RunAsync(host, new AgentRunRequest { Message = "delegate", SessionId = "elicit-child" }, agent: "elicit-router");

        run.Inputs.ShouldBeEmpty();
        server.Seen.ShouldBe(["asked", "decline"]);
        (await RunStatusesAsync(host, "elicit-child")).ShouldBe(["Completed"]);

        using var runs = await host.Client.GetAsync(new Uri("/tracon/api/runs?includeChildren=true", UriKind.Relative));
        var child = (await TraconTestHost.ReadJsonAsync(runs)).EnumerateArray()
            .Single(static item => item.GetProperty("depth").GetInt32() == 1);

        (await DeclineEventsAsync(host, child.GetProperty("id").GetGuid())).ShouldHaveSingleItem().reason.ShouldBe("child_run");
    }

    [Fact]
    public async Task An_answer_to_a_request_nobody_is_waiting_for_is_refused_before_the_run_starts()
    {
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name");

        var first = await RunAsync(host, new AgentRunRequest { Message = "greet me", SessionId = "elicit-refuse" });
        var request = first.Inputs.ShouldHaveSingleItem();
        var requestId = request.GetProperty("requestId").GetString()!;

        // No session: the request lives in a session's history.
        (await PostRunAsync(host, new AgentRunRequest { InputResponses = [Response(requestId, ToolInputAction.Decline)] }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A request id that was never issued: re-running a tool under a made-up identity repeats whatever it does.
        (await PostRunAsync(host, Answer("elicit-refuse", "no-such-request", ToolInputAction.Decline)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The same request answered twice in one request.
        (await PostRunAsync(
                host,
                new AgentRunRequest
                {
                    SessionId = "elicit-refuse",
                    InputResponses = [Response(requestId, ToolInputAction.Decline), Response(requestId, ToolInputAction.Decline)],
                }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A value the server never asked for, a required field left out, a field of the wrong type, and a
        // value on a refusal: each is a 400 the user can correct, never a tool call the server then fails.
        (await PostRunAsync(host, Answer("elicit-refuse", request, ToolInputAction.Accept, ("nickname", "Ada"))))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostRunAsync(host, Answer("elicit-refuse", request, ToolInputAction.Accept)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostRunAsync(host, Answer("elicit-refuse", request, ToolInputAction.Accept, ("name", 7))))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostRunAsync(host, Answer("elicit-refuse", request, ToolInputAction.Decline, ("name", "Ada"))))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        server.Calls.ShouldBe(1, "no refused answer ran the tool again");
        (await RunStatusesAsync(host, "elicit-refuse")).ShouldBe(["AwaitingInput"], "no refused answer started a run");

        // A good answer still works after the refusals, and a second one for it is a 409.
        (await PostRunAsync(host, Answer("elicit-refuse", request, ToolInputAction.Accept, ("name", "Ada"))))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostRunAsync(host, Answer("elicit-refuse", request, ToolInputAction.Accept, ("name", "Ada"))))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_answer_from_another_user_is_refused()
    {
        var identity = new SwitchableAttribution { UserId = "a" };

        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(
            server,
            "ask_name",
            configureServices: services =>
            {
                services.Configure<TraconSessionOwnershipOptions>(static options => options.Enabled = true);
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(identity));
            });

        var first = await RunAsync(host, new AgentRunRequest { Message = "greet me", SessionId = "elicit-owned" });
        var request = first.Inputs.ShouldHaveSingleItem();

        identity.UserId = "b";

        (await PostRunAsync(host, Answer("elicit-owned", request, ToolInputAction.Accept, ("name", "Mallory"))))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        server.Calls.ShouldBe(1, "user b's answer did not reach the tool");

        identity.UserId = "a";

        (await PostRunAsync(host, Answer("elicit-owned", request, ToolInputAction.Accept, ("name", "Ada"))))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        server.Seen.ShouldBe(["asked", "asked", "accept:Ada"]);
    }

    [Fact]
    public async Task The_answer_is_audited_by_field_name_and_never_by_value()
    {
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name");

        var first = await RunAsync(host, new AgentRunRequest { Message = "greet me", SessionId = "elicit-audit" });
        await RunAsync(host, Answer("elicit-audit", first.Inputs.ShouldHaveSingleItem(), ToolInputAction.Accept, ("name", "Zaphod-Beeblebrox")));

        var audit = host.Services.GetRequiredService<IAuditLog>();
        var entries = await audit.QueryAsync(new AuditQuery { TenantId = "default", Action = ToolInputResolver.AuditAction });

        var entry = entries.ShouldHaveSingleItem();
        entry.Entity.ShouldBe($"tool:{ServerName}_ask_name");
        entry.After.ShouldNotBeNull().ShouldContain("\"fields\":[\"name\"]", Case.Sensitive);
        entry.After.ShouldNotContain("Zaphod-Beeblebrox", Case.Sensitive);
        (entry.Before ?? string.Empty).ShouldNotContain("Zaphod-Beeblebrox", Case.Sensitive);
    }

    [Fact]
    public async Task An_answer_that_cannot_be_audited_is_not_applied()
    {
        // K-089, K-370: an answer releases what a server asked for into a tool call. A decision that cannot be
        // recorded is not carried out — the tool does not run again and the run does not claim it did.
        var audit = new SwitchableAuditLog();

        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(
            server,
            "ask_name",
            configureServices: services => services.AddSingleton<IAuditLog>(audit));

        var first = await RunAsync(host, new AgentRunRequest { Message = "greet me", SessionId = "elicit-unaudited" });
        var request = first.Inputs.ShouldHaveSingleItem();

        audit.Fail = true;

        var refused = await RunAsync(host, Answer("elicit-unaudited", request, ToolInputAction.Accept, ("name", "Ada")), expectSuccess: false);

        refused.EventNames.ShouldBe(["error"], "the refusal is the stream's only frame");
        refused.Frames.ShouldHaveSingleItem().Data.ShouldContain("was not applied because it could not be written to the audit trail");
        server.Calls.ShouldBe(1, "the tool did not run again");

        // A refusal, not a poison pill: once the store is back the same answer is applied.
        audit.Fail = false;

        var answered = await RunAsync(host, Answer("elicit-unaudited", request, ToolInputAction.Accept, ("name", "Ada")));

        answered.Text.ShouldContain("hello Ada", Case.Sensitive);
        server.Seen.ShouldBe(["asked", "asked", "accept:Ada"]);
    }

    [Fact]
    public async Task A_replayed_call_that_asks_something_else_is_declined_not_given_the_old_answer()
    {
        // An answer belongs to the question it was given to. The server asks another question the second time:
        // a "yes" to the first must not reach it, and asking again would never end.
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_changing");

        var first = await RunAsync(host, new AgentRunRequest { Message = "greet me", SessionId = "elicit-changing" });
        var answered = await RunAsync(host, Answer("elicit-changing", first.Inputs.ShouldHaveSingleItem(), ToolInputAction.Accept, ("name", "Ada")));

        server.Seen.ShouldBe(["asked:1", "asked:2", "decline"]);
        answered.Text.ShouldContain("declined", Case.Sensitive);
        answered.Inputs.ShouldBeEmpty();
        (await RunStatusesAsync(host, "elicit-changing")).ShouldBe(["AwaitingInput", "Completed"]);
        (await DeclineEventsAsync(host, answered.RunId)).ShouldHaveSingleItem().reason.ShouldBe("mismatch");
    }

    [Fact]
    public async Task A_server_that_asks_on_every_call_ends_the_call_instead_of_looping()
    {
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_forever");

        var first = await RunAsync(host, new AgentRunRequest { Message = "greet me", SessionId = "elicit-forever" });
        var answered = await RunAsync(host, Answer("elicit-forever", first.Inputs.ShouldHaveSingleItem(), ToolInputAction.Accept, ("name", "Ada")));

        answered.Inputs.ShouldBeEmpty("the answer is used once; a second request is declined, and the SDK stops the server's retries");
        (await RunStatusesAsync(host, "elicit-forever")).ShouldBe(["AwaitingInput", "Completed"]);
        server.Calls.ShouldBeLessThan(40, "the retry loops are bounded");
    }

    [Fact]
    public async Task Two_runs_on_one_connection_keep_their_requests_and_answers_apart()
    {
        // The MCP connection is shared by every run of the tenant. The request a handler sees must be the one
        // of the call it was reached from, and the answer must go to the call that was asked.
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name", models: 2);

        var runs = await Task.WhenAll(
            RunAsync(host, new AgentRunRequest { Message = "greet me", SessionId = "elicit-par-1" }, agent: "elicit-agent-1"),
            RunAsync(host, new AgentRunRequest { Message = "greet me", SessionId = "elicit-par-2" }, agent: "elicit-agent-2"));

        var first = runs[0].Inputs.ShouldHaveSingleItem();
        var second = runs[1].Inputs.ShouldHaveSingleItem();
        string.Equals(first.GetProperty("requestId").GetString(), second.GetProperty("requestId").GetString(), StringComparison.Ordinal)
            .ShouldBeFalse("each run asked its own request");

        // Answered in the opposite order, with different names.
        var answers = await Task.WhenAll(
            RunAsync(host, Answer("elicit-par-2", second, ToolInputAction.Accept, ("name", "Beta")), agent: "elicit-agent-2"),
            RunAsync(host, Answer("elicit-par-1", first, ToolInputAction.Accept, ("name", "Alpha")), agent: "elicit-agent-1"));

        answers[0].Text.ShouldContain("hello Beta", Case.Sensitive);
        answers[0].Text.ShouldNotContain("Alpha", Case.Sensitive);
        answers[1].Text.ShouldContain("hello Alpha", Case.Sensitive);
        answers[1].Text.ShouldNotContain("Beta", Case.Sensitive);
    }

    [Fact]
    public async Task A_call_that_ran_after_the_suspended_one_in_the_same_turn_gets_a_result_so_the_history_stays_valid()
    {
        // The loop stops after the suspended call; the calls queued behind it in the same model turn never
        // run and have no result. A provider refuses a history with a call that has none.
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name", twoCallsInOneTurn: true);

        var first = await RunAsync(host, new AgentRunRequest { Message = "greet me twice", SessionId = "elicit-siblings" });
        var request = first.Inputs.ShouldHaveSingleItem();

        var answered = await RunAsync(host, Answer("elicit-siblings", request, ToolInputAction.Accept, ("name", "Ada")));

        server.Seen.ShouldBe(["asked", "asked", "accept:Ada"], "only the suspended call ran again; the skipped one did not run at all");

        using var session = await host.Client.GetAsync(new Uri("/tracon/api/sessions/elicit-siblings", UriKind.Relative));
        var history = (await TraconTestHost.ReadJsonAsync(session)).GetProperty("messages").GetRawText();
        history.ShouldContain(ToolInputSessionBridge.SkippedCallResult, Case.Sensitive);
        answered.Text.ShouldContain("hello Ada", Case.Sensitive);
    }

    [Fact]
    public async Task A_user_who_ignores_the_form_and_types_on_leaves_a_history_a_provider_accepts()
    {
        // The turn stopped at the first call; the call queued behind it never ran. If the user types a message
        // instead of answering, that call must still get a result, or the provider refuses the whole history.
        await using var server = await ElicitingMcpServer.StartAsync();
        var model = TwoCallsModel("ask_name");
        await using var host = await StartAsync(server, "ask_name", scripted: model);

        var first = await RunAsync(host, new AgentRunRequest { Message = "greet me twice", SessionId = "elicit-ignored" });
        first.Inputs.ShouldHaveSingleItem();

        await RunAsync(host, new AgentRunRequest { Message = "never mind", SessionId = "elicit-ignored" });

        ShouldPairEveryCallWithAResult(model.Requests[^1].Messages);
    }

    [Fact]
    public async Task Calls_queued_behind_an_approved_and_suspended_call_get_a_result_too()
    {
        // The default: the server asks for approval, so both calls wait for it together and are approved
        // together; the first then stops for input and the second never runs.
        await using var server = await ElicitingMcpServer.StartAsync();
        var model = TwoCallsModel("ask_name");
        await using var host = await StartAsync(server, "ask_name", requiresApproval: true, scripted: model);

        var turn = await RunAsync(host, new AgentRunRequest { Message = "greet me twice", SessionId = "elicit-queued" });

        // The framework asks for one approval at a time and runs the calls once the last one is given.
        var approvalsGiven = 0;

        while (turn.Approvals.Count > 0)
        {
            approvalsGiven++;

            turn = await RunAsync(
                host,
                new AgentRunRequest
                {
                    SessionId = "elicit-queued",
                    Approvals = [new ToolApprovalDecision { RequestId = turn.Approvals[0].GetProperty("requestId").GetString()!, Approved = true }],
                });
        }

        approvalsGiven.ShouldBe(2, "both calls waited for approval");

        var request = turn.Inputs.ShouldHaveSingleItem();

        var answered = await RunAsync(host, Answer("elicit-queued", request, ToolInputAction.Accept, ("name", "Ada")));

        answered.Text.ShouldContain("hello Ada", Case.Sensitive);
        ShouldPairEveryCallWithAResult(model.Requests[^1].Messages);
    }

    [Fact]
    public async Task A_caller_cannot_write_the_result_of_a_server_call_that_never_ran()
    {
        // The call queued behind the suspended one has no result in the history. It is the server's call: only
        // the server writes its result. A client-side tool result for it would put words in a tool's mouth.
        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(server, "ask_name", scripted: TwoCallsModel("ask_name"));

        var first = await RunAsync(host, new AgentRunRequest { Message = "greet me twice", SessionId = "elicit-forged" });
        first.Inputs.ShouldHaveSingleItem();

        var skipped = SkippedCallId(first);

        using var forged = await PostRunAsync(
            host,
            new AgentRunRequest
            {
                SessionId = "elicit-forged",
                ToolResults = [new ClientToolResult { CallId = skipped, Result = "verified" }],
            });

        forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await forged.Content.ReadAsStringAsync()).ShouldContain("Unknown tool call", Case.Sensitive);
    }

    [Fact]
    public async Task The_request_survives_a_session_store_that_serializes_the_session()
    {
        // The suspended call is recorded in the session's state bag; the in-memory store keeps the object, a SQL
        // store keeps its JSON. The record must come back from the JSON in the shape the framework reads.
        using var database = new TempSqliteDatabase("elicit");

        await using var server = await ElicitingMcpServer.StartAsync();
        await using var host = await StartAsync(
            server,
            "ask_name",
            configureTracon: builder => builder.UseSqlite(database.ConnectionString));

        var first = await RunAsync(host, new AgentRunRequest { Message = "greet me", SessionId = "elicit-sql" });
        var answered = await RunAsync(host, Answer("elicit-sql", first.Inputs.ShouldHaveSingleItem(), ToolInputAction.Accept, ("name", "Ada")));

        answered.Text.ShouldContain("hello Ada", Case.Sensitive);
        server.Seen.ShouldBe(["asked", "asked", "accept:Ada"]);
    }

    // ---- Harness ----------------------------------------------------------------

    private static async Task<TraconTestHost> StartAsync(
        ElicitingMcpServer server,
        string tool,
        bool requiresApproval = false,
        bool withChildAgent = false,
        bool twoCallsInOneTurn = false,
        int models = 1,
        Action<IServiceCollection>? configureServices = null,
        Action<ITraconBuilder>? configureTracon = null,
        FakeModelProvider? scripted = null)
    {
        var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseMcp();
                configureTracon?.Invoke(builder);

                if (scripted is not null)
                {
                    builder.AddModelProvider(scripted);
                }

                for (var model = 1; scripted is null && model <= models; model++)
                {
                    var name = models == 1 ? "elicit-model" : $"elicit-model-{model}";
                    var provider = new FakeModelProvider(name);

                    if (twoCallsInOneTurn)
                    {
                        provider.CallsTools(($"{ServerName}_{tool}", null), ($"{ServerName}_{tool}", null)).EchoesLastToolResult();
                    }
                    else
                    {
                        provider.CallsTool($"{ServerName}_{tool}").EchoesLastToolResult();
                    }

                    builder.AddModelProvider(provider);
                }

                if (withChildAgent)
                {
                    builder.AddModelProvider(new FakeModelProvider("elicit-router")
                        .CallsTool("background_agents_start_task", new { agentName = Agent, input = "ask", description = "ask" })
                        .CallsTool("background_agents_wait_for_first_completion", new { taskIds = new[] { 1 } })
                        .CallsTool("background_agents_get_task_results", new { taskId = 1 })
                        .EchoesLastToolResult("Delegated: "));
                }
            },
            configureServices: services =>
            {
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                configureServices?.Invoke(services);
            });

        try
        {
            var key = await CreateKeyAsync(host, "AgentsAdmin", "AgentsRead", "SecurityAdmin");

            using (var request = WithKey(
                       HttpMethod.Put,
                       $"/tracon/api/mcp-servers/{ServerName}",
                       new { endpoint = server.Endpoint.ToString(), requiresApproval },
                       key))
            using (var saved = await host.Client.SendAsync(request))
            {
                saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            }

            using (var refreshed = await host.Client.PostAsync(new Uri("/tracon/api/mcp-servers/refresh", UriKind.Relative), content: null))
            {
                refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());
            }

            for (var model = 1; model <= models; model++)
            {
                var suffix = models == 1 ? string.Empty : $"-{model}";

                await SaveAgentAsync(host, new AgentDefinitionRequest
                {
                    Name = Agent + suffix,
                    Instructions = "Greet the user.",
                    Model = new ModelBinding { Provider = "elicit-model" + suffix, Model = "elicit" },
                    ToolNames = [$"{ServerName}_{tool}"],
                });
            }

            if (withChildAgent)
            {
                await SaveAgentAsync(host, new AgentDefinitionRequest
                {
                    Name = "elicit-router",
                    Instructions = "Delegate.",
                    Model = new ModelBinding { Provider = "elicit-router", Model = "router" },
                    CallableAgentNames = [Agent],
                });
            }

            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private static async Task SaveAgentAsync(TraconTestHost host, AgentDefinitionRequest definition)
    {
        using var agent = await host.Client.PostAsJsonAsync(new Uri("/tracon/api/agents", UriKind.Relative), definition);

        agent.StatusCode.ShouldBe(HttpStatusCode.Created, await agent.Content.ReadAsStringAsync());
    }

    private static FakeModelProvider TwoCallsModel(string tool)
        => new FakeModelProvider("elicit-model")
            .CallsTools(($"{ServerName}_{tool}", null), ($"{ServerName}_{tool}", null))
            .EchoesLastToolResult();

    /// <summary>A provider refuses a history that holds a call without a result; so does this check.</summary>
    private static void ShouldPairEveryCallWithAResult(IEnumerable<Microsoft.Extensions.AI.ChatMessage> sent)
    {
        var contents = sent.SelectMany(static message => message.Contents).ToList();
        var results = contents.OfType<Microsoft.Extensions.AI.FunctionResultContent>().Select(static result => result.CallId).ToHashSet(StringComparer.Ordinal);

        contents.OfType<Microsoft.Extensions.AI.FunctionCallContent>()
            .Select(static call => call.CallId)
            .Where(id => !results.Contains(id))
            .ShouldBeEmpty("every call sent to the model carries its result");
    }

    /// <summary>The call a turn made and never answered.</summary>
    private static string SkippedCallId(StreamedRun run)
    {
        var answered = new HashSet<string>(StringComparer.Ordinal);
        var made = new List<string>();

        foreach (var frame in run.Frames.Where(static frame => string.Equals(frame.Event, "update", StringComparison.Ordinal)))
        {
            using var update = JsonDocument.Parse(frame.Data);

            if (!update.RootElement.TryGetProperty("contents", out var contents))
            {
                continue;
            }

            foreach (var content in contents.EnumerateArray())
            {
                var type = content.TryGetProperty("$type", out var kind) ? kind.GetString() : null;
                var id = content.TryGetProperty("callId", out var callId) ? callId.GetString() : null;

                if (id is null)
                {
                    continue;
                }

                if (string.Equals(type, "functionCall", StringComparison.Ordinal))
                {
                    made.Add(id);
                }
                else if (string.Equals(type, "functionResult", StringComparison.Ordinal))
                {
                    answered.Add(id);
                }
            }
        }

        return made.Single(id => !answered.Contains(id));
    }

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (var index = 0; index < names.Count; index++)
        {
            if (string.Equals(names[index], name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static AgentRunRequest Answer(string session, JsonElement request, ToolInputAction action, params (string Name, object Value)[] fields)
        => Answer(session, request.GetProperty("requestId").GetString()!, action, fields);

    private static AgentRunRequest Answer(string session, string requestId, ToolInputAction action, params (string Name, object Value)[] fields)
        => new() { SessionId = session, InputResponses = [Response(requestId, action, fields)] };

    private static ToolInputResponse Response(string requestId, ToolInputAction action, params (string Name, object Value)[] fields)
        => new()
        {
            RequestId = requestId,
            Action = action,
            Content = fields.Length == 0
                ? null
                : fields.ToDictionary(
                    static field => field.Name,
                    static field => JsonSerializer.SerializeToElement(field.Value),
                    StringComparer.Ordinal),
        };

    /// <summary>What a streaming run sent: its run id, its frames, the text it produced and what it announced.</summary>
    private sealed record StreamedRun(
        Guid RunId,
        IReadOnlyList<SseFrame> Frames,
        string Text,
        IReadOnlyList<JsonElement> Inputs,
        IReadOnlyList<JsonElement> Approvals)
    {
        public IReadOnlyList<string> EventNames { get; } = [.. Frames.Select(static frame => frame.Event ?? string.Empty)];
    }

    private static async Task<HttpResponseMessage> PostRunAsync(TraconTestHost host, AgentRunRequest request, string agent = Agent)
        => await host.Client.PostAsJsonAsync(new Uri($"/tracon/api/agents/{agent}/run", UriKind.Relative), request);

    private static async Task<StreamedRun> RunAsync(TraconTestHost host, AgentRunRequest request, string agent = Agent, bool expectSuccess = true)
    {
        using var response = await PostRunAsync(host, request, agent);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var frames = new List<SseFrame>();
        await foreach (var frame in SseReader.ReadAsync(await response.Content.ReadAsStreamAsync()))
        {
            frames.Add(frame);
        }

        if (expectSuccess)
        {
            frames.Select(static frame => frame.Event).ShouldNotContain("error", StringComparer.Ordinal, string.Join('\n', frames.Select(static frame => frame.Data)));
        }

        var text = string.Concat(frames
            .Where(static frame => string.Equals(frame.Event, "update", StringComparison.Ordinal))
            .Select(static frame => frame.Data));

        return new StreamedRun(
            RunIdOf(frames),
            frames,
            text,
            Announced(frames, "inputs"),
            Announced(frames, "approvals"));
    }

    private static Guid RunIdOf(IReadOnlyList<SseFrame> frames)
    {
        var accepted = frames.FirstOrDefault(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal));

        return accepted is null
            ? Guid.Empty
            : JsonDocument.Parse(accepted.Data).RootElement.GetProperty("runId").GetGuid();
    }

    private static List<JsonElement> Announced(IReadOnlyList<SseFrame> frames, string name)
        => [.. frames
            .Where(frame => string.Equals(frame.Event, name, StringComparison.Ordinal))
            .SelectMany(static frame => JsonDocument.Parse(frame.Data).RootElement.EnumerateArray().Select(static item => item.Clone()))];

    private static async Task<IReadOnlyList<string>> RunStatusesAsync(TraconTestHost host, string session)
    {
        using var runs = await host.Client.GetAsync(new Uri($"/tracon/api/runs?sessionId={session}", UriKind.Relative));

        return (await TraconTestHost.ReadJsonAsync(runs)).EnumerateArray()
            .OrderBy(static run => run.GetProperty("startedAt").GetDateTimeOffset())
            .Select(static run => run.GetProperty("status").GetString()!)
            .ToList();
    }

    /// <summary>The decline notices of a run: the reason code each one carries.</summary>
    private static async Task<IReadOnlyList<(string reason, string server)>> DeclineEventsAsync(TraconTestHost host, Guid runId)
    {
        using var events = await host.Client.GetAsync(new Uri($"/tracon/api/runs/{runId}/events", UriKind.Relative));

        var declines = new List<(string, string)>();

        foreach (var frame in await SseReader.ReadAllAsync(await events.Content.ReadAsStreamAsync()))
        {
            using var data = JsonDocument.Parse(frame.Data);

            if (!data.RootElement.TryGetProperty("customType", out var type) ||
                !string.Equals(type.GetString(), RunEventCustomTypes.ToolInputDeclined, StringComparison.Ordinal))
            {
                continue;
            }

            using var payload = JsonDocument.Parse(data.RootElement.GetProperty("payload").GetString()!);
            declines.Add((payload.RootElement.GetProperty("reason").GetString()!, payload.RootElement.GetProperty("server").GetString()!));
        }

        return declines;
    }

    private static async Task<string> CreateKeyAsync(TraconTestHost host, params string[] scopes)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/api-keys", UriKind.Relative),
            new { name = $"elicit-{string.Join('-', scopes)}", scopes });

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

    /// <summary>An audit log that refuses every write while <see cref="Fail"/> is on.</summary>
    private sealed class SwitchableAuditLog : IAuditLog
    {
        private readonly InMemoryAuditLog _inner = new();

        public bool Fail { get; set; }

        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
            => Fail
                ? throw new InvalidOperationException("audit store is unreachable")
                : _inner.WriteAsync(entry, cancellationToken);

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => _inner.QueryAsync(query, cancellationToken);

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => _inner.VerifyChainAsync(query, cancellationToken);
    }

    /// <summary>An <see cref="IRunAttributionContext"/> whose answer the test changes between requests.</summary>
    private sealed class SwitchableAttribution : IRunAttributionContext
    {
        public string? UserId { get; set; }

        public IReadOnlyDictionary<string, string>? Labels => null;
    }
}
