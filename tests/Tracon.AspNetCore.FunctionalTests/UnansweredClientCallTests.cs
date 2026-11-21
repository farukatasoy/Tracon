using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.MixedCallRig;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A caller that does not answer the call of a client-side tool and sends a new message instead gets a history the provider
/// accepts: the call is closed with a result that says it was not answered. A caller that does answer is left alone.
/// </summary>
/// <remarks>
/// <para>
/// The server behind the agent answers 400, as the real API does, when an assistant message has a tool call that no
/// <c>tool</c> message answers. Before the fix the call stayed in the history and the next request of the session failed
/// that way; Tracon answered 502.
/// </para>
/// <para>
/// What the run leaves with the session is kept in the session's state, so it survives a restart of the process when the
/// session lives in a database, and it belongs to that session of that tenant alone.
/// </para>
/// </remarks>
public sealed class UnansweredClientCallTests
{
    private const string NotAnswered = "The call was not answered.";

    private static AgentRunRequest NewMessage(string text, string session = SessionId) => new() { SessionId = session, Message = text };

    private static AgentRunRequest ClientResult(string result = "page-title")
        => new() { SessionId = SessionId, ToolResults = [new ClientToolResult { CallId = ClientCallId, Result = result }] };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_new_message_closes_the_client_call_the_caller_did_not_answer(bool streaming)
    {
        await using var rig = await MixedCallRig.StartAsync();
        rig.Server.ThenCalls(ClientCallOnly).ThenAnswers("Sure.");

        var first = await rig.RunAsync(NewMessage("open the page"), streaming);

        first.Contents.ShouldBe([$"functionCall({ClientCallId}:{ClientTool})"]);

        var second = await rig.RunAsync(NewMessage("never mind, what time is it?"), streaming);

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        second.Text.ShouldBe("Sure.");
        rig.Server.Rejections.ShouldBeEmpty("the call reached the provider without a result");
        rig.Server.Transcripts[1].ShouldContain($"tool({ClientCallId})=\"{NotAnswered}\"", Case.Sensitive);
        (await rig.RunsAsync()).ShouldAllBe(static run => run.Status == RunStatus.Completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_new_message_after_a_mixed_answer_closes_only_the_client_call(bool streaming)
    {
        await using var rig = await MixedCallRig.StartAsync();
        rig.Server.ThenCalls(BackendAndClientCalls).ThenCalls(ClientCallOnly).ThenAnswers("Sure.");

        await rig.RunAsync(NewMessage("check order 7 and open the page"), streaming);

        var second = await rig.RunAsync(NewMessage("never mind"), streaming);

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        rig.Server.Rejections.ShouldBeEmpty();

        var request = rig.Server.Transcripts[^1];

        request.ShouldContain($"tool({ClientCallId})=\"{NotAnswered}\"", Case.Sensitive);
        request.ShouldNotContain($"tool({BackendCallId})=\"{NotAnswered}\"", Case.Sensitive);
        rig.Probe.BackendRuns.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_run_that_brings_the_result_does_not_close_the_call_it_answers(bool streaming)
    {
        await using var rig = await MixedCallRig.StartAsync();
        rig.Server.ThenCalls(ClientCallOnly).ThenAnswers("Opened.");

        await rig.RunAsync(NewMessage("open the page"), streaming);

        var second = await rig.RunAsync(ClientResult("Shopping cart"), streaming);

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        rig.Server.Rejections.ShouldBeEmpty();

        var request = rig.Server.Transcripts[1];

        request.ShouldContain($"tool({ClientCallId})=\"Shopping cart\"", Case.Sensitive);
        request.ShouldNotContain(NotAnswered, Case.Sensitive);
    }

    [Fact]
    public async Task A_result_that_arrives_after_the_call_was_closed_is_refused_as_already_answered()
    {
        await using var rig = await MixedCallRig.StartAsync();
        rig.Server.ThenCalls(ClientCallOnly).ThenAnswers("Sure.");

        await rig.RunAsync(NewMessage("open the page"), streaming: false);
        await rig.RunAsync(NewMessage("never mind"), streaming: false);

        var late = await rig.RunAsync(ClientResult(), streaming: false);

        late.Status.ShouldBe(HttpStatusCode.Conflict, late.Body);
    }

    [Fact]
    public async Task A_session_that_asked_again_after_the_answer_closes_only_the_new_call()
    {
        // The result answers the first call; the model asks for a client call again; a new message closes that one.
        await using var rig = await MixedCallRig.StartAsync();
        rig.Server
            .ThenCalls((ClientCallId, ClientTool, "{}"))
            .ThenCalls(("call_client_again", ClientTool, "{}"))
            .ThenAnswers("Sure.");

        await rig.RunAsync(NewMessage("open the page"), streaming: false);
        var second = await rig.RunAsync(ClientResult(), streaming: false);

        second.Contents.ShouldBe([$"functionCall(call_client_again:{ClientTool})"]);

        var third = await rig.RunAsync(NewMessage("never mind"), streaming: false);

        third.Status.ShouldBe(HttpStatusCode.OK, third.Body);
        rig.Server.Rejections.ShouldBeEmpty();
        rig.Server.Transcripts[^1].ShouldContain($"tool(call_client_again)=\"{NotAnswered}\"", Case.Sensitive);
        rig.Server.Transcripts[^1].ShouldNotContain($"tool({ClientCallId})=\"{NotAnswered}\"", Case.Sensitive);
    }

    [Fact]
    public async Task The_closing_survives_a_restart_of_the_process_when_the_session_lives_in_a_database()
    {
        using var database = new TempSqliteDatabase("unanswered-call");
        await using var server = await StrictToolCallServer.StartAsync();
        server.ThenCalls(ClientCallOnly).ThenAnswers("Sure.");

        var options = new MixedCallRigOptions { SqliteConnectionString = database.ConnectionString };

        await using (var before = await MixedCallRig.StartAsync(options, server))
        {
            var first = await before.RunAsync(NewMessage("open the page"), streaming: false);

            first.Contents.ShouldBe([$"functionCall({ClientCallId}:{ClientTool})"]);
        }

        await using var after = await MixedCallRig.StartAsync(options, server);

        var second = await after.RunAsync(NewMessage("never mind"), streaming: false);

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        server.Rejections.ShouldBeEmpty();
        server.Transcripts[1].ShouldContain($"tool({ClientCallId})=\"{NotAnswered}\"", Case.Sensitive);
    }

    [Fact]
    public async Task The_result_of_a_client_tool_reaches_the_model_after_a_restart_as_it_did_before_it()
    {
        // The session is read back from the database before the result arrives, and the result goes on to the provider
        // as the caller wrote it.
        using var database = new TempSqliteDatabase("unanswered-result");
        await using var server = await StrictToolCallServer.StartAsync();
        server.ThenCalls(ClientCallOnly).ThenAnswers("Opened.");

        var options = new MixedCallRigOptions { SqliteConnectionString = database.ConnectionString };

        await using (var before = await MixedCallRig.StartAsync(options, server))
        {
            await before.RunAsync(NewMessage("open the page"), streaming: false);
        }

        await using var after = await MixedCallRig.StartAsync(options, server);

        var second = await after.RunAsync(ClientResult("Shopping cart"), streaming: false);

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        server.Rejections.ShouldBeEmpty();
        server.Transcripts[1].ShouldContain($"tool({ClientCallId})=\"Shopping cart\"", Case.Sensitive);
        server.Transcripts[1].ShouldNotContain(NotAnswered, Case.Sensitive);
    }

    [Fact]
    public async Task Another_session_and_another_tenant_are_not_touched_and_the_session_that_waits_still_closes_its_call()
    {
        await using var rig = await MixedCallRig.StartAsync();
        rig.Server
            .ThenCalls(ClientCallOnly)
            .ThenAnswers("hello other session")
            .ThenAnswers("hello other tenant")
            .ThenAnswers("closed");

        await rig.RunAsync(NewMessage("open the page"), streaming: false);

        var otherSession = await rig.RunAsync(NewMessage("hello", session: "other-session"), streaming: false);
        var otherTenant = await rig.RunAsync(NewMessage("hello"), streaming: false, tenant: "tenant-b");

        otherSession.Text.ShouldBe("hello other session");
        otherTenant.Text.ShouldBe("hello other tenant");
        rig.Server.Transcripts[1].ShouldNotContain("tool(", Case.Sensitive);
        rig.Server.Transcripts[2].ShouldNotContain("tool(", Case.Sensitive);

        var waiting = await rig.RunAsync(NewMessage("never mind"), streaming: false);

        waiting.Text.ShouldBe("closed");
        rig.Server.Transcripts[3].ShouldContain($"tool({ClientCallId})=\"{NotAnswered}\"", Case.Sensitive);
        rig.Server.Rejections.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_embedded_path_closes_the_call_before_the_new_message()
    {
        var provider = new Tracon.Testing.FakeModelProvider("embedded-model")
            .CallsTool(ClientTool)
            .RespondsWith("Sure.");

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddClientTool(ClientTool, "Opens a page in the user's browser.", System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"type":"object","properties":{}}"""))
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Answer the user.",
                Model = new ModelBinding { Provider = "embedded-model", Model = "embedded-1" },
                ToolNames = [ClientTool],
                Origin = AgentDefinitionOrigin.Code,
            }));

        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        await agent.RunAsync([new ChatMessage(ChatRole.User, "open the page")], session, options: null, TestContext.Current.CancellationToken);

        var second = await agent.RunAsync([new ChatMessage(ChatRole.User, "never mind")], session, options: null, TestContext.Current.CancellationToken);

        second.Text.ShouldBe("Sure.");

        var closing = provider.Requests[^1].Messages
            .SelectMany(static message => message.Contents)
            .OfType<FunctionResultContent>()
            .ShouldHaveSingleItem();

        closing.Result.ShouldBe(NotAnswered);
    }
}
