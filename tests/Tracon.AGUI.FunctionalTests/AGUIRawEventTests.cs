using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AGUI.Abstractions;
using Tracon.AGUI.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// The raw update of the model client is not sent to the browser: no frame of the stream carries <c>rawEvent</c>.
/// </summary>
/// <remarks>
/// <para>
/// The protocol's converter writes the provider's own update into every event (the model id, the response id, the author
/// name, the additional properties and the whole contents), and a front end has no use for it that the AG-UI event itself
/// does not give. Tracon drops it from each event before the frame is written. The update is the same object the session's
/// history is built from, so the test also reads the session back.
/// </para>
/// <para>
/// A search of the frames finds nothing unless the run got that far, so every case also asks for what the run was for:
/// the answer, the tool call, the interrupt.
/// </para>
/// </remarks>
public sealed class AGUIRawEventTests
{
    private const string RawEventProperty = "\"rawEvent\"";

    [Fact]
    public async Task No_frame_of_a_text_turn_carries_a_raw_event()
    {
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho);

        var response = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-raw-text", "hello"));

        response.Text.ShouldBe("Echo: hello");
        response.Frames.Count.ShouldBeGreaterThan(4, "a text turn is more than its start and its end");
        AssertNoRawEvent(response);
    }

    [Fact]
    public async Task No_frame_of_a_client_tool_turn_carries_a_raw_event()
    {
        await using var host = await AGUITestHost.StartAsync(builder => TestAgents.AddClientTool(builder));

        var tools = new[] { AGUIInputs.Tool(TestAgents.OpenPage) };
        var first = await host.RunAsync(TestAgents.ClientTool, AGUIInputs.User("t-raw-tool", "open the page", "r1", tools));
        var call = first.Events.OfType<ToolCallStartEvent>().Single();

        call.ToolCallName.ShouldBe(TestAgents.OpenPage);
        AssertNoRawEvent(first);

        var second = await host.RunAsync(
            TestAgents.ClientTool,
            AGUIInputs.With(
                "t-raw-tool",
                "r2",
                [
                    AGUIInputs.UserMessage("u1", "open the page"),
                    AGUIInputs.AssistantMessage("a1", string.Empty, (call.ToolCallId, TestAgents.OpenPage)),
                    AGUIInputs.ToolMessage("tm1", call.ToolCallId, "Shopping cart"),
                ],
                tools));

        second.Text.ShouldBe("Opened: Shopping cart");
        AssertNoRawEvent(second);
    }

    [Fact]
    public async Task No_frame_of_an_approval_turn_carries_a_raw_event()
    {
        var executions = new StrongBox<int>();
        await using var host = await AGUITestHost.StartAsync(builder => TestAgents.AddApprovalTool(builder, executions));

        var first = await host.RunAsync(TestAgents.Approval, AGUIInputs.User("t-raw-approval", "cancel the order", "r1"));

        var interrupt = first.Interrupts.ShouldHaveSingleItem();
        first.Events.OfType<ToolCallStartEvent>().Single().ToolCallName.ShouldBe(TestAgents.CancelOrder);
        AssertNoRawEvent(first);

        var second = await host.RunAsync(
            TestAgents.Approval,
            AGUIInputs.With("t-raw-approval", "r2", [], [], [AGUIInputs.Resolved(interrupt.Id!, new { approved = true })]));

        second.Status.ShouldBe(System.Net.HttpStatusCode.OK, second.Body);
        executions.Value.ShouldBe(1, "the approved tool ran");
        AssertNoRawEvent(second);
    }

    [Fact]
    public async Task The_sessions_history_is_what_the_agent_produced_with_or_without_the_front_end_reading_it()
    {
        // 🚨 The agent builds the session's history from the very same update objects it hands to the stream, and the
        // protocol's converter keeps a reference to one of them. Clearing the raw event on the update instead of on the AG-UI
        // event would have rewritten the history with it. The session must hold the same conversation whichever door the
        // run came through.
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho);

        await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-raw-history", "hello"));

        using var viaAGUI = await host.Client.GetAsync(new Uri("/tracon/api/sessions/t-raw-history", UriKind.Relative), TestContext.Current.CancellationToken);
        var agui = Messages(await viaAGUI.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken));

        using var run = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{TestAgents.Echo}/run", UriKind.Relative),
            new { message = "hello", sessionId = "t-raw-history-management" },
            TestContext.Current.CancellationToken);
        run.EnsureSuccessStatusCode();
        await run.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var viaManagement = await host.Client.GetAsync(new Uri("/tracon/api/sessions/t-raw-history-management", UriKind.Relative), TestContext.Current.CancellationToken);
        var management = Messages(await viaManagement.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken));

        agui.ShouldBe(["user: text: hello", "assistant: text: Echo: hello"]);
        agui.ShouldBe(management, "the AG-UI door leaves the same history as the management API's");
    }

    // Role, the kind of every content and the text: what the history holds, without the ids and times that differ per run.
    private static List<string> Messages(JsonElement session)
        => [.. session.GetProperty("messages").EnumerateArray().Select(static message =>
        {
            var contents = message.GetProperty("contents").EnumerateArray().ToList();

            return $"{message.GetProperty("role").GetString()}: " +
                   $"{string.Join('+', contents.Select(static content => content.GetProperty("$type").GetString()))}: " +
                   $"{string.Concat(contents.Where(static content => content.TryGetProperty("text", out _)).Select(static content => content.GetProperty("text").GetString()))}";
        })];

    private static void AssertNoRawEvent(AGUIResponse response)
    {
        response.Frames.ShouldNotBeEmpty();

        foreach (var frame in response.Frames)
        {
            frame.ShouldNotContain(RawEventProperty, Case.Sensitive, "a frame carries the provider's raw update");
        }

        response.Events.OfType<RawEvent>().ShouldBeEmpty("the protocol's own RAW event is not what is meant either");
    }
}
