using System.Net;
using System.Runtime.CompilerServices;
using AGUI.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AGUI.FunctionalTests.Infrastructure;
using Tracon.Testing;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// What the AG-UI surface emits and what the provider receives when one model answer asks for a server tool and for a
/// front end tool at once, and when the front end sends a new message instead of the result of a call it was asked to run.
/// </summary>
/// <remarks>
/// The model is a script that records every request it receives. A history in which a call has no result is a history a
/// real provider refuses with a 400, so the tests read the requests for exactly that.
/// </remarks>
public sealed class ClientToolMixedCallTests
{
    private const string BackendTool = "lookup_order";
    private const string Agent = "mixed-agui-agent";

    [Fact]
    public async Task The_server_call_runs_in_the_turn_that_asked_for_it_and_the_front_ends_call_follows_it()
    {
        var executions = new StrongBox<int>();
        var model = new FakeModelProvider("mixed-model")
            .CallsTools((BackendTool, null), (TestAgents.OpenPage, new { url = "https://example.test" }))
            .CallsTool(TestAgents.OpenPage, new { url = "https://example.test" })
            .EchoesLastToolResult("Final: ");

        await using var host = await AGUITestHost.StartAsync(builder => builder
            .AddModelProvider(model)
            .AddTool(
                Microsoft.Extensions.AI.AIFunctionFactory.Create(
                    () =>
                    {
                        Interlocked.Increment(ref executions.Value);

                        return "order 7 shipped";
                    },
                    BackendTool,
                    "Looks an order up."))
            .AddClientTool(TestAgents.OpenPage, "Opens a page in the user's browser.", System.Text.Json.JsonDocument.Parse(TestAgents.OpenPageSchema).RootElement.Clone())
            .AddAgent(TestAgents.Definition(Agent, "mixed-model", BackendTool, TestAgents.OpenPage)));

        var tools = new[] { AGUIInputs.Tool(TestAgents.OpenPage) };

        var first = await host.RunAsync(Agent, AGUIInputs.User("t-mixed", "look up order 7 and open the page", "r1", tools));

        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);

        var starts = first.Events.OfType<ToolCallStartEvent>().ToList();

        starts.Select(static start => start.ToolCallName).ShouldBe([BackendTool, TestAgents.OpenPage], "the server call and its result, then the front end's call, once each");
        first.Events.OfType<ToolCallResultEvent>().ShouldHaveSingleItem();
        executions.Value.ShouldBe(1);

        var clientCallId = starts.Single(static start => string.Equals(start.ToolCallName, TestAgents.OpenPage, StringComparison.Ordinal)).ToolCallId;

        var second = await host.RunAsync(
            Agent,
            AGUIInputs.With(
                "t-mixed",
                "r2",
                [
                    AGUIInputs.UserMessage("u1", "look up order 7 and open the page"),
                    AGUIInputs.AssistantMessage("a1", string.Empty, (clientCallId, TestAgents.OpenPage)),
                    AGUIInputs.ToolMessage("tm1", clientCallId, "Shopping cart"),
                ],
                tools));

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        second.Text.ShouldBe("Final: Shopping cart");
        executions.Value.ShouldBe(1, "the next request does not run the server tool again");
        model.Requests.ShouldAllBe(static request => Orphaned(request.Messages).Count == 0, "a call without a result reached the provider");
    }

    [Fact]
    public async Task The_call_the_front_end_lost_is_announced_before_the_run_finishes_and_is_recorded_in_the_run()
    {
        var model = new FakeModelProvider("mixed-model")
            .CallsTools((BackendTool, null), (TestAgents.OpenPage, new { url = "https://example.test" }))
            .CallsTool(TestAgents.OpenPage, new { url = "https://example.test" })
            .EchoesLastToolResult("Final: ");

        await using var host = await AGUITestHost.StartAsync(builder => builder
            .AddModelProvider(model)
            .AddTool(Microsoft.Extensions.AI.AIFunctionFactory.Create(() => "order 7 shipped", BackendTool, "Looks an order up."))
            .AddClientTool(TestAgents.OpenPage, "Opens a page in the user's browser.", System.Text.Json.JsonDocument.Parse(TestAgents.OpenPageSchema).RootElement.Clone())
            .AddAgent(TestAgents.Definition(Agent, "mixed-model", BackendTool, TestAgents.OpenPage)));

        var response = await host.RunAsync(
            Agent,
            AGUIInputs.User("t-notice", "look up order 7 and open the page", "r1", [AGUIInputs.Tool(TestAgents.OpenPage)]));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);

        var notice = response.Events.OfType<CustomEvent>().Single(static custom => string.Equals(custom.Name, RunEventCustomTypes.ClientCallWithheld, StringComparison.Ordinal));
        var withheldCallId = notice.Value!.Value.GetProperty("toolCallId").GetString().ShouldNotBeNull();

        notice.Value!.Value.GetProperty("toolName").GetString().ShouldBe(TestAgents.OpenPage);
        notice.Value!.Value.EnumerateObject().Count().ShouldBe(2, "the call and the tool, and nothing the model said");

        var order = response.Events.ToList();

        order.IndexOf(notice).ShouldBeGreaterThan(order.FindIndex(static e => e is RunStartedEvent));
        order.IndexOf(notice).ShouldBeLessThan(order.FindIndex(static e => e is RunFinishedEvent), "the notice comes before the run finishes");

        // The run's own record carries the same event: a front end that was not connected reads it there.
        var store = host.Services.GetRequiredService<IRunStore>();
        var recorded = new List<RunEvent>();

        await foreach (var runEvent in store.ReadEventsAsync(response.TraconRunId, 0, TestContext.Current.CancellationToken))
        {
            if (string.Equals(runEvent.CustomType, RunEventCustomTypes.ClientCallWithheld, StringComparison.Ordinal))
            {
                recorded.Add(runEvent);
            }
        }

        recorded.ShouldHaveSingleItem().ToolCallId.ShouldBe(withheldCallId);
        recorded[0].ToolName.ShouldBe(TestAgents.OpenPage);
    }

    [Fact]
    public async Task A_client_call_that_shares_an_answer_with_an_approval_is_announced_and_does_not_run_in_that_turn()
    {
        var executions = new StrongBox<int>();
        var model = new FakeModelProvider("approval-model")
            .CallsTools((TestAgents.CancelOrder, new { orderId = "ORD-7" }), (TestAgents.OpenPage, new { url = "https://example.test" }))
            .EchoesLastToolResult();

        await using var host = await AGUITestHost.StartAsync(builder => builder
            .AddModelProvider(model)
            .AddTool(
                (Func<string, string>)(orderId =>
                {
                    Interlocked.Increment(ref executions.Value);

                    return $"{orderId} canceled.";
                }),
                name: TestAgents.CancelOrder,
                description: "Cancels an order.",
                configure: options => options.RequiresApproval = true)
            .AddClientTool(TestAgents.OpenPage, "Opens a page in the user's browser.", System.Text.Json.JsonDocument.Parse(TestAgents.OpenPageSchema).RootElement.Clone())
            .AddAgent(TestAgents.Definition(Agent, "approval-model", TestAgents.CancelOrder, TestAgents.OpenPage)));

        var response = await host.RunAsync(
            Agent,
            AGUIInputs.User("t-approval-notice", "cancel order 7 and open the page", "r1", [AGUIInputs.Tool(TestAgents.OpenPage)]));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);

        // The run ends at the approval. The client call is not among the calls the front end is asked to run, and the notice
        // says why: it came before the interrupt of RUN_FINISHED, not after it.
        response.Interrupts.ShouldHaveSingleItem();
        executions.Value.ShouldBe(0, "the approval has not been given");

        var notice = response.Events.OfType<CustomEvent>().Single(static custom => string.Equals(custom.Name, RunEventCustomTypes.ClientCallWithheld, StringComparison.Ordinal));
        var order = response.Events.ToList();

        notice.Value!.Value.GetProperty("toolName").GetString().ShouldBe(TestAgents.OpenPage);
        order.IndexOf(notice).ShouldBeLessThan(order.FindIndex(static e => e is RunFinishedEvent));
        response.Events.OfType<ToolCallStartEvent>().ShouldNotContain(static start => string.Equals(start.ToolCallName, TestAgents.OpenPage, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_new_message_closes_the_call_the_front_end_did_not_answer()
    {
        var model = new FakeModelProvider("client-model")
            .CallsTool(TestAgents.OpenPage, new { url = "https://example.test" })
            .EchoesLastToolResult("Last result: ");

        await using var host = await AGUITestHost.StartAsync(builder => builder
            .AddModelProvider(model)
            .AddClientTool(TestAgents.OpenPage, "Opens a page in the user's browser.", System.Text.Json.JsonDocument.Parse(TestAgents.OpenPageSchema).RootElement.Clone())
            .AddAgent(TestAgents.Definition(TestAgents.ClientTool, "client-model", TestAgents.OpenPage)));

        var tools = new[] { AGUIInputs.Tool(TestAgents.OpenPage) };

        var first = await host.RunAsync(TestAgents.ClientTool, AGUIInputs.User("t-new", "open the page", "r1", tools));
        var callId = first.Events.OfType<ToolCallStartEvent>().Single().ToolCallId;

        var second = await host.RunAsync(
            TestAgents.ClientTool,
            AGUIInputs.With(
                "t-new",
                "r2",
                [
                    AGUIInputs.UserMessage("u1", "open the page"),
                    AGUIInputs.AssistantMessage("a1", string.Empty, (callId, TestAgents.OpenPage)),
                    AGUIInputs.UserMessage("u2", "never mind, what time is it?"),
                ],
                tools));

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        second.Finished.Outcome.ShouldBeOfType<RunFinishedSuccessOutcome>();
        second.Text.ShouldBe("Last result: The call was not answered.");
        model.Requests.ShouldAllBe(static request => Orphaned(request.Messages).Count == 0, "a call without a result reached the provider");

        var late = await host.RunAsync(
            TestAgents.ClientTool,
            AGUIInputs.With("t-new", "r3", [AGUIInputs.ToolMessage("tm1", callId, "Shopping cart")], tools));

        late.Status.ShouldBe(HttpStatusCode.Conflict, "the call was closed by the new message and cannot be answered any more");
    }

    [Fact]
    public async Task The_front_ends_result_is_not_closed_by_the_run_that_brings_it()
    {
        var model = new FakeModelProvider("client-model")
            .CallsTool(TestAgents.OpenPage, new { url = "https://example.test" })
            .EchoesLastToolResult("Last result: ");

        await using var host = await AGUITestHost.StartAsync(builder => builder
            .AddModelProvider(model)
            .AddClientTool(TestAgents.OpenPage, "Opens a page in the user's browser.", System.Text.Json.JsonDocument.Parse(TestAgents.OpenPageSchema).RootElement.Clone())
            .AddAgent(TestAgents.Definition(TestAgents.ClientTool, "client-model", TestAgents.OpenPage)));

        var tools = new[] { AGUIInputs.Tool(TestAgents.OpenPage) };

        var first = await host.RunAsync(TestAgents.ClientTool, AGUIInputs.User("t-result", "open the page", "r1", tools));
        var callId = first.Events.OfType<ToolCallStartEvent>().Single().ToolCallId;

        var second = await host.RunAsync(
            TestAgents.ClientTool,
            AGUIInputs.With(
                "t-result",
                "r2",
                [
                    AGUIInputs.UserMessage("u1", "open the page"),
                    AGUIInputs.AssistantMessage("a1", string.Empty, (callId, TestAgents.OpenPage)),
                    AGUIInputs.ToolMessage("tm1", callId, "Shopping cart"),
                ],
                tools));

        second.Text.ShouldBe("Last result: Shopping cart");
        model.Requests.ShouldAllBe(static request => Orphaned(request.Messages).Count == 0);
    }

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
