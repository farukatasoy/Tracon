using System.Net;
using AGUI.Abstractions;
using Tracon.AGUI.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// A tool that runs on the front end: the model calls it, the front end runs it and sends the result back (Phase 236).
/// </summary>
public sealed class AGUIClientToolTests
{
    [Fact]
    public async Task The_model_calls_a_front_end_tool_and_the_run_ends_waiting_for_its_result()
    {
        await using var host = await AGUITestHost.StartAsync(builder => TestAgents.AddClientTool(builder));

        var first = await host.RunAsync(
            TestAgents.ClientTool,
            AGUIInputs.User("t-tool", "open the page", "r1", AGUIInputs.Tool(TestAgents.OpenPage)));

        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);

        var start = first.Events.OfType<ToolCallStartEvent>().Single();
        start.ToolCallName.ShouldBe(TestAgents.OpenPage);
        first.Types.ShouldContain("TOOL_CALL_ARGS", StringComparer.Ordinal);
        first.Types.ShouldContain("TOOL_CALL_END", StringComparer.Ordinal);
        first.Types.ShouldNotContain("TOOL_CALL_RESULT", StringComparer.Ordinal, "the server never ran the tool");

        // Not an interrupt: the front end runs the tool and starts the next request, the usual AG-UI shape.
        first.Finished.Outcome.ShouldBeOfType<RunFinishedSuccessOutcome>();
    }

    [Fact]
    public async Task The_front_ends_result_answers_the_pending_call_and_the_run_goes_on()
    {
        await using var host = await AGUITestHost.StartAsync(builder => TestAgents.AddClientTool(builder));

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

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        second.Text.ShouldBe("Opened: Shopping cart", "the front end's result reached the model, bound to the call it answered");
    }

    [Fact]
    public async Task A_result_for_a_call_nobody_made_is_a_400_and_a_second_answer_is_a_409()
    {
        await using var host = await AGUITestHost.StartAsync(builder => TestAgents.AddClientTool(builder));

        var tools = new[] { AGUIInputs.Tool(TestAgents.OpenPage) };
        var first = await host.RunAsync(TestAgents.ClientTool, AGUIInputs.User("t-wrong", "open the page", "r1", tools));
        var callId = first.Events.OfType<ToolCallStartEvent>().Single().ToolCallId;

        var wrong = await host.RunAsync(
            TestAgents.ClientTool,
            AGUIInputs.With("t-wrong", "r2", [AGUIInputs.ToolMessage("tm1", "call-that-never-was", "x")], tools));

        wrong.Status.ShouldBe(HttpStatusCode.BadRequest, wrong.Body);
        wrong.Problem.GetProperty("title").GetString().ShouldBe("Unknown tool call");

        (await host.RunAsync(
                TestAgents.ClientTool,
                AGUIInputs.With("t-wrong", "r3", [AGUIInputs.ToolMessage("tm2", callId, "Cart")], tools)))
            .Status.ShouldBe(HttpStatusCode.OK);

        var again = await host.RunAsync(
            TestAgents.ClientTool,
            AGUIInputs.With("t-wrong", "r4", [AGUIInputs.ToolMessage("tm2", callId, "Cart")], tools));
        again.Status.ShouldBe(HttpStatusCode.Conflict, again.Body);
    }

    [Fact]
    public async Task A_front_end_error_reaches_the_model_instead_of_the_result()
    {
        await using var host = await AGUITestHost.StartAsync(builder => TestAgents.AddClientTool(builder));

        var tools = new[] { AGUIInputs.Tool(TestAgents.OpenPage) };
        var first = await host.RunAsync(TestAgents.ClientTool, AGUIInputs.User("t-error", "open the page", "r1", tools));
        var callId = first.Events.OfType<ToolCallStartEvent>().Single().ToolCallId;

        var second = await host.RunAsync(
            TestAgents.ClientTool,
            AGUIInputs.With("t-error", "r2", [AGUIInputs.ToolMessage("tm1", callId, "ignored", error: "popup blocked")], tools));

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        second.Text.ShouldContain("popup blocked", Case.Sensitive);
        second.Text.ShouldNotContain("ignored", Case.Sensitive);
    }
}
