using System.Net;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.MixedCallRig;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// One model answer asks for a server tool and for a client-side tool at the same time. The server tool runs, the model
/// asks for the client tool again, and the history the provider receives never holds a call without a result.
/// </summary>
/// <remarks>
/// <para>
/// The server behind the agent is stricter than the ones the other tests use: it answers 400, as the real API does, when
/// an assistant message has a tool call that no <c>tool</c> message answers. Before the fix the first mixed answer left the
/// server call unexecuted and the caller's next request carried only the client's result; Tracon answered 502.
/// </para>
/// <para>
/// The ring that withholds the client call sits in the model pipeline, so these tests go through the whole stack: a plain
/// agent, a harness agent and a factory agent, and the buffered answer and the stream. The embedded path (an agent
/// resolved from the catalog and run in process) goes through the same pipeline and has its own test.
/// </para>
/// </remarks>
public sealed class ClientToolMixedCallTests
{
    private static AgentRunRequest NewMessage(string text) => new() { SessionId = SessionId, Message = text };

    private static AgentRunRequest ClientResult(string result = "page-title", string callId = ClientCallId)
        => new() { SessionId = SessionId, ToolResults = [new ClientToolResult { CallId = callId, Result = result }] };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_mixed_answer_runs_the_server_call_and_hands_the_client_call_to_the_caller_once(bool streaming)
    {
        await using var rig = await StartAsync();
        rig.Server.ThenCalls(BackendAndClientCalls).ThenCalls(ClientCallOnly).ThenAnswers("final answer");

        var first = await rig.RunAsync(NewMessage("check order 7 and open the page"), streaming);

        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);
        first.Contents.ShouldBe(
        [
            $"functionCall({BackendCallId}:{BackendTool})",
            $"functionResult({BackendCallId})",
            $"functionCall({ClientCallId}:{ClientTool})",
        ]);
        rig.Probe.BackendRuns.ShouldBe(1, "the server tool runs in the turn that asked for it");

        var second = await rig.RunAsync(ClientResult(), streaming);

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        second.Text.ShouldBe("final answer");
        rig.Probe.BackendRuns.ShouldBe(1, "the next request does not run it again");
        rig.Server.Rejections.ShouldBeEmpty("a call without a result reached the provider");
        rig.Server.Transcripts.Count.ShouldBe(3, "the one extra model call a mixed answer costs");
    }

    [Fact]
    public async Task The_model_is_asked_again_with_the_result_of_the_server_tool_in_front_of_it()
    {
        await using var rig = await StartAsync();
        rig.Server.ThenCalls(BackendAndClientCalls).ThenCalls(ClientCallOnly).ThenAnswers("final answer");

        await rig.RunAsync(NewMessage("check order 7 and open the page"), streaming: false);

        // The second request holds the server call with its result and not the client call it has not been asked again for.
        var again = rig.Server.Transcripts[1];

        again.ShouldContain($"assistant[{BackendCallId}:{BackendTool}]", Case.Sensitive);
        again.ShouldContain($"tool({BackendCallId})", Case.Sensitive);
        again.ShouldNotContain(ClientCallId, Case.Sensitive);
    }

    [Fact]
    public async Task The_run_records_the_server_call_and_the_client_call_the_caller_received_and_not_the_one_set_aside()
    {
        await using var rig = await StartAsync();
        rig.Server
            .ThenCalls((BackendCallId, BackendTool, "{}"), ("call_client_first", ClientTool, "{}"))
            .ThenCalls(ClientCallOnly)
            .ThenAnswers("final answer");

        await rig.RunAsync(NewMessage("check order 7 and open the page"), streaming: false);

        var run = (await rig.RunsAsync()).ShouldHaveSingleItem();
        var rows = await rig.ToolRowsAsync(run.Id);

        rows.Select(static row => row.ToolCallId).Order(StringComparer.Ordinal).ToList().ShouldBe([BackendCallId, ClientCallId], "one row per call the caller saw");
        rows.Single(static row => string.Equals(row.ToolCallId, BackendCallId, StringComparison.Ordinal)).Succeeded.ShouldBeTrue();
        run.Status.ShouldBe(RunStatus.Completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_client_call_that_was_set_aside_is_recorded_as_an_event_with_its_call_id_and_tool_name(bool streaming)
    {
        await using var rig = await StartAsync();
        rig.Server
            .ThenCalls((BackendCallId, BackendTool, "{}"), ("call_client_first", ClientTool, "{}"))
            .ThenCalls(ClientCallOnly)
            .ThenAnswers("final answer");

        await rig.RunAsync(NewMessage("check order 7 and open the page"), streaming);

        var run = (await rig.RunsAsync()).ShouldHaveSingleItem();
        var events = await rig.EventsTextAsync(run.Id);

        // One notice for the one call the answer lost, naming the call the model gave it and the tool, and no argument.
        var notices = events.Split('\n').Where(static line => line.Contains("tracon.client_call_withheld", StringComparison.Ordinal)).ToList();

        // The payload is JSON inside the event's JSON, so its quotes are escaped as " in the line.
        notices.ShouldHaveSingleItem().ShouldContain("\\u0022toolCallId\\u0022:\\u0022call_client_first\\u0022", Case.Sensitive);
        notices[0].ShouldContain($"\\u0022toolName\\u0022:\\u0022{ClientTool}\\u0022", Case.Sensitive);
        notices[0].ShouldNotContain("arguments", Case.Sensitive);
    }

    [Fact]
    public async Task The_model_may_ask_for_the_client_call_with_other_arguments_and_the_caller_gets_the_second_ones()
    {
        // The price of the second request: the first answer's client call is gone, and what the model asks for the next time
        // is what the caller runs.
        await using var rig = await StartAsync();
        rig.Server
            .ThenCalls((BackendCallId, BackendTool, """{"orderId":"7"}"""), (ClientCallId, ClientTool, """{"url":"https://first.example"}"""))
            .ThenCalls((ClientCallId, ClientTool, """{"url":"https://second.example"}"""))
            .ThenAnswers("final answer");

        var first = await rig.RunAsync(NewMessage("check order 7 and open the page"), streaming: false);

        first.ArgumentsOf(ClientCallId).ShouldContain("second.example", Case.Sensitive);
        first.ArgumentsOf(ClientCallId).ShouldNotContain("first.example", Case.Sensitive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_model_that_does_not_ask_for_the_client_call_again_ends_the_turn_with_its_answer(bool streaming)
    {
        // The client call was only set aside: if the model has read the server's result and wants nothing more, there is no
        // call left to answer and nothing is waiting.
        await using var rig = await StartAsync();
        rig.Server.ThenCalls(BackendAndClientCalls).ThenAnswers("Order 7 shipped, nothing to open.").ThenAnswers("Anything else?");

        var first = await rig.RunAsync(NewMessage("check order 7 and open the page"), streaming);

        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);
        first.Text.ShouldBe("Order 7 shipped, nothing to open.");
        first.Contents.ShouldNotContain(static content => content.StartsWith("functionCall(call_client", StringComparison.Ordinal));

        var next = await rig.RunAsync(NewMessage("thanks"), streaming);

        next.Status.ShouldBe(HttpStatusCode.OK, next.Body);
        rig.Server.Rejections.ShouldBeEmpty();
        rig.Server.Transcripts[^1].ShouldNotContain("The call was not answered.", Case.Sensitive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_server_calls_in_one_answer_with_no_client_call_all_run_in_the_first_turn(bool streaming)
    {
        // A control: an answer that is not mixed goes through the ring as it came.
        await using var rig = await StartAsync();
        rig.Server.ThenCalls((BackendCallId, BackendTool, "{}"), (SecondBackendCallId, SecondBackendTool, "{}")).ThenAnswers("both done");

        var first = await rig.RunAsync(NewMessage("check order 7 and the customer"), streaming);

        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);
        first.Text.ShouldBe("both done");
        rig.Probe.BackendRuns.ShouldBe(1);
        rig.Probe.SecondBackendRuns.ShouldBe(1);
        rig.Server.Rejections.ShouldBeEmpty();
        rig.Server.Transcripts.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_client_call_alone_ends_the_turn_at_once_with_no_extra_model_call(bool streaming)
    {
        await using var rig = await StartAsync();
        rig.Server.ThenCalls(ClientCallOnly).ThenAnswers("final answer");

        var first = await rig.RunAsync(NewMessage("open the page"), streaming);

        first.Contents.ShouldBe([$"functionCall({ClientCallId}:{ClientTool})"]);
        rig.Server.Transcripts.Count.ShouldBe(1);
        rig.Probe.BackendRuns.ShouldBe(0);

        var second = await rig.RunAsync(ClientResult(), streaming);

        second.Text.ShouldBe("final answer");
        rig.Server.Rejections.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_harness_agent_gets_the_same_behavior(bool streaming)
    {
        // The framework's own switch for this does not reach a harness agent; the ring is in the pipeline under every agent.
#pragma warning disable MAAI001 // the harness API is marked "evaluation purposes only"
        await using var rig = await StartAsync(new MixedCallRigOptions { Harness = new HarnessSettings() });
#pragma warning restore MAAI001
        rig.Server.ThenCalls(BackendAndClientCalls).ThenCalls(ClientCallOnly).ThenAnswers("final answer");

        var first = await rig.RunAsync(NewMessage("check order 7 and open the page"), streaming);

        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);
        first.Contents.ShouldContain($"functionCall({ClientCallId}:{ClientTool})", StringComparer.Ordinal);
        rig.Probe.BackendRuns.ShouldBe(1);

        var second = await rig.RunAsync(ClientResult(), streaming);

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        rig.Server.Rejections.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_factory_agent_built_over_the_client_tracon_hands_it_gets_the_same_behavior(bool streaming)
    {
        await using var rig = await StartAsync(new MixedCallRigOptions { FactoryAgent = true });
        rig.Server.ThenCalls(BackendAndClientCalls).ThenCalls(ClientCallOnly).ThenAnswers("final answer");

        var first = await rig.RunAsync(NewMessage("check order 7 and open the page"), streaming);

        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);
        first.Contents.ShouldContain($"functionCall({ClientCallId}:{ClientTool})", StringComparer.Ordinal);
        rig.Probe.BackendRuns.ShouldBe(1);

        var second = await rig.RunAsync(ClientResult(), streaming);

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        rig.Server.Rejections.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_call_of_a_server_tool_reaches_a_streaming_caller_while_the_tool_is_still_running_and_the_tool_row_keeps_its_duration()
    {
        // The ring holds updates only until the model's answer has ended, and the loop runs its tools after that: a front
        // end sees the call while the tool works, and the row of the tool records how long it took. The tool waits for the
        // test to say the call arrived, so a regression that holds the call back until the tool is done fails here.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new ManualClock();

        await using var rig = await StartAsync(new MixedCallRigOptions
        {
            Clock = clock,
            BackendBody = async () =>
            {
                await gate.Task;
                clock.Advance(TimeSpan.FromMilliseconds(250));
            },
        });

        rig.Server.ThenCalls(BackendAndClientCalls).ThenCalls(ClientCallOnly).ThenAnswers("final answer");

        var seen = new List<string>();
        var announced = false;

        var turn = Task.Run(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
            {
                Content = System.Net.Http.Json.JsonContent.Create(NewMessage("check order 7 and open the page")),
            };
            request.Headers.Add("X-Tracon-Tenant", Tenant);

            using var response = await rig.Host.Client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken)
                .ConfigureAwait(false);

            await foreach (var frame in SseReader.ReadAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken).ConfigureAwait(false)))
            {
                if (!string.Equals(frame.Event, "update", StringComparison.Ordinal))
                {
                    continue;
                }

                using var update = System.Text.Json.JsonDocument.Parse(frame.Data);

                if (!update.RootElement.TryGetProperty("contents", out var contents))
                {
                    continue;
                }

                foreach (var content in contents.EnumerateArray())
                {
                    var type = content.GetProperty("$type").GetString();

                    if (type is not ("functionCall" or "functionResult"))
                    {
                        continue;
                    }

                    var callId = content.GetProperty("callId").GetString();

                    lock (seen)
                    {
                        seen.Add($"{type}({callId})");
                    }

                    if (string.Equals(type, "functionCall", StringComparison.Ordinal) && string.Equals(callId, BackendCallId, StringComparison.Ordinal))
                    {
                        Volatile.Write(ref announced, true);
                    }
                }
            }
        });

        // A turn that ends before the tool started is a turn that never ran it: the loop stopped at the client call.
        (await Task.WhenAny(rig.Probe.BackendHasStarted, turn)).ShouldBeSameAs(rig.Probe.BackendHasStarted, "the turn ended without running the server tool");
        await WaitUntil.TrueAsync(() => Volatile.Read(ref announced), "the call of the server tool reached the caller");

        rig.Probe.BackendRuns.ShouldBe(0, "the tool is still waiting for the test");
        gate.SetResult();
        await turn;

        seen.ShouldBe([$"functionCall({BackendCallId})", $"functionResult({BackendCallId})", $"functionCall({ClientCallId})"]);

        var run = (await rig.RunsAsync()).ShouldHaveSingleItem();
        var row = (await rig.ToolRowsAsync(run.Id)).Single(static row => string.Equals(row.ToolName, BackendTool, StringComparison.Ordinal));

        row.Duration.ShouldBe(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task The_embedded_path_gets_the_same_behavior()
    {
        var probe = new Probe();
        var provider = new Tracon.Testing.FakeModelProvider("embedded-model")
            .CallsTools((BackendTool, null), (ClientTool, null))
            .CallsTool(ClientTool)
            .EchoesLastToolResult("Final: ");

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddTool(
                AIFunctionFactory.Create(
                    () =>
                    {
                        probe.BackendRan();

                        return "order 7 shipped";
                    },
                    BackendTool,
                    "Looks an order up."))
            .AddClientTool(ClientTool, "Opens a page in the user's browser.", System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"type":"object","properties":{}}"""))
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Answer the user.",
                Model = new ModelBinding { Provider = "embedded-model", Model = "embedded-1" },
                ToolNames = [BackendTool, ClientTool],
                Origin = AgentDefinitionOrigin.Code,
            }));

        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        var first = await agent.RunAsync([new ChatMessage(ChatRole.User, "go")], session, options: null, TestContext.Current.CancellationToken);
        var calls = first.Messages.SelectMany(static message => message.Contents).OfType<FunctionCallContent>().ToList();

        calls.Select(static call => call.Name).ShouldBe([BackendTool, ClientTool]);
        probe.BackendRuns.ShouldBe(1);

        var second = await agent.RunAsync(
            [new ChatMessage(ChatRole.Tool, [new FunctionResultContent(calls[1].CallId, "page-title")])],
            session,
            options: null,
            TestContext.Current.CancellationToken);

        second.Text.ShouldBe("Final: page-title");
        provider.Requests.ShouldAllBe(static request => Orphaned(request.Messages).Count == 0, "a call without a result reached the provider");
        probe.BackendRuns.ShouldBe(1);
    }

    private static Task<MixedCallRig> StartAsync(MixedCallRigOptions? options = null) => MixedCallRig.StartAsync(options);

    /// <summary>The ids of calls that no later result answers.</summary>
    private static List<string> Orphaned(IEnumerable<ChatMessage> messages)
    {
        var open = new List<string>();

        foreach (var message in messages)
        {
            foreach (var result in message.Contents.OfType<FunctionResultContent>())
            {
                open.Remove(result.CallId);
            }

            foreach (var call in message.Contents.OfType<FunctionCallContent>())
            {
                open.Add(call.CallId);
            }
        }

        return open;
    }
}
