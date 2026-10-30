using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The rewrite rules behind <c>incomplete</c> on <c>POST /v1/responses</c>, on their own: which frame
/// changes, which never does, and what happens when Microsoft Agent Framework already did the work or
/// writes something this code does not recognise. The end-to-end behaviour is in
/// <see cref="ResponsesIncompleteTests"/>.
/// </summary>
public sealed class ResponsesIncompletePatchTests
{
    [Fact]
    public void A_completed_body_becomes_incomplete_and_only_its_last_message_item_is_marked()
    {
        var response = Parse("""
            {"id":"resp_1","status":"completed","error":null,"output":[
              {"type":"function_call","status":"completed","id":"fc_1"},
              {"type":"message","status":"completed","id":"msg_a"},
              {"type":"function_call_output","status":"completed","id":"fo_1"},
              {"type":"message","status":"completed","id":"msg_b"}],"store":true}
            """);

        ResponsesIncompleteness.Mark(response, "max_output_tokens").ShouldBeTrue();

        response["status"]!.GetValue<string>().ShouldBe("incomplete");
        response["incomplete_details"]!["reason"]!.GetValue<string>().ShouldBe("max_output_tokens");
        response["output"]!.AsArray().Select(static item => $"{item!["type"]}:{item["status"]}").ShouldBe(
        [
            "function_call:completed",
            "message:completed",
            "function_call_output:completed",
            "message:incomplete",
        ]);
        response["store"]!.GetValue<bool>().ShouldBeTrue("a field the rewrite does not own is left as it was");
    }

    [Fact]
    public void A_body_without_a_message_item_still_becomes_incomplete()
    {
        var response = Parse("""{"status":"completed","output":[{"type":"function_call","status":"completed","id":"fc_1"}]}""");

        ResponsesIncompleteness.Mark(response, "content_filter").ShouldBeTrue();

        response["status"]!.GetValue<string>().ShouldBe("incomplete");
        response["incomplete_details"]!["reason"]!.GetValue<string>().ShouldBe("content_filter");
        response["output"]![0]!["status"]!.GetValue<string>().ShouldBe("completed");
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("failed")]
    [InlineData("in_progress")]
    public void A_body_that_is_not_completed_is_left_alone(string status)
    {
        // Microsoft Agent Framework may one day mark the answer itself; then this code has nothing to add,
        // and writing the same fields twice would replace its reason with ours.
        var response = Parse($$"""
            {"status":"{{status}}","incomplete_details":{"reason":"max_output_tokens"},"output":[{"type":"message","status":"{{status}}"}]}
            """);
        var before = response.ToJsonString();

        ResponsesIncompleteness.Mark(response, "content_filter").ShouldBeFalse();

        response.ToJsonString().ShouldBe(before);
    }

    [Fact]
    public async Task Before_the_updates_end_no_frame_is_rewritten_even_when_the_reason_is_known()
    {
        // MAF closes an earlier item (the text before a tool call) while updates still come; only the
        // frames it writes after the last update belong to the answer that was cut.
        var rewriter = new ResponsesStreamRewriter();
        await using var updates = rewriter.Observe(Updates([ChatFinishReason.Length, ChatFinishReason.Length])).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        (await updates.MoveNextAsync()).ShouldBeTrue();

        var itemDone = ItemDoneFrame("message");
        var completed = CompletedFrame();

        rewriter.Patch(itemDone).ShouldBeSameAs(itemDone);
        rewriter.Patch(completed).ShouldBeSameAs(completed);
    }

    [Fact]
    public async Task After_the_updates_end_the_closing_message_item_and_the_terminal_frame_are_rewritten()
    {
        var rewriter = await EndedAsync(ChatFinishReason.Length);

        using var item = Data(rewriter.Patch(ItemDoneFrame("message")), expectedEvent: "response.output_item.done");
        item.RootElement.GetProperty("item").GetProperty("status").GetString().ShouldBe("incomplete");
        item.RootElement.GetProperty("sequence_number").GetInt32().ShouldBe(7, "the numbering is not touched");

        using var terminal = Data(rewriter.Patch(CompletedFrame()), expectedEvent: "response.incomplete");
        terminal.RootElement.GetProperty("type").GetString().ShouldBe("response.incomplete");
        terminal.RootElement.GetProperty("response").GetProperty("status").GetString().ShouldBe("incomplete");
        terminal.RootElement.GetProperty("response").GetProperty("incomplete_details").GetProperty("reason").GetString()
            .ShouldBe("max_output_tokens");
        terminal.RootElement.GetProperty("response").GetProperty("output")[0].GetProperty("status").GetString()
            .ShouldBe("incomplete");
        terminal.RootElement.GetProperty("sequence_number").GetInt32().ShouldBe(8);

        rewriter.MissedTerminalFrame.ShouldBeFalse();
    }

    [Fact]
    public async Task A_closing_item_that_is_not_a_message_keeps_its_status()
    {
        var rewriter = await EndedAsync(ChatFinishReason.Length);

        var functionCallDone = ItemDoneFrame("function_call");

        rewriter.Patch(functionCallDone).ShouldBeSameAs(functionCallDone);
    }

    [Fact]
    public async Task Frames_that_are_not_closing_frames_are_never_parsed_or_rewritten()
    {
        var rewriter = await EndedAsync(ChatFinishReason.Length);
        var delta = Frame("response.output_text.delta", new { type = "response.output_text.delta", delta = "x", sequence_number = 4 });

        rewriter.Patch(delta).ShouldBeSameAs(delta);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("tool_calls")]
    [InlineData("")]
    public async Task A_complete_answer_leaves_every_frame_as_it_is(string reason)
    {
        var rewriter = await EndedAsync(reason.Length == 0 ? null : new ChatFinishReason(reason));
        var itemDone = ItemDoneFrame("message");
        var completed = CompletedFrame();

        rewriter.Patch(itemDone).ShouldBeSameAs(itemDone);
        rewriter.Patch(completed).ShouldBeSameAs(completed);
        rewriter.MissedTerminalFrame.ShouldBeFalse();
    }

    [Fact]
    public async Task The_last_reported_reason_decides_not_an_earlier_one()
    {
        // The tool loop reports a tool turn's own reason before the next turn; null updates carry none.
        var cutThenComplete = await EndedAsync(ChatFinishReason.Length, null, ChatFinishReason.Stop);
        var completeThenCut = await EndedAsync(ChatFinishReason.Stop, null, ChatFinishReason.Length);

        var completed = CompletedFrame();

        cutThenComplete.Patch(completed).ShouldBeSameAs(completed);
        completeThenCut.Patch(completed).ShouldNotBeSameAs(completed);
    }

    [Theory]
    [InlineData("content_filter", "content_filter")]
    [InlineData("context_window_exceeded", "max_output_tokens")]
    public async Task The_reason_comes_from_the_one_mapping_the_chat_completions_endpoint_reads(string finishReason, string expected)
    {
        var rewriter = await EndedAsync(new ChatFinishReason(finishReason));

        using var terminal = Data(rewriter.Patch(CompletedFrame()), expectedEvent: "response.incomplete");

        terminal.RootElement.GetProperty("response").GetProperty("incomplete_details").GetProperty("reason").GetString()
            .ShouldBe(expected);
    }

    [Fact]
    public async Task A_terminal_frame_that_already_says_incomplete_is_left_alone_and_not_reported_missing()
    {
        var rewriter = await EndedAsync(ChatFinishReason.Length);
        var alreadyMarked = Frame(
            "response.incomplete",
            new
            {
                type = "response.incomplete",
                response = new { id = "resp_1", status = "incomplete", incomplete_details = new { reason = "content_filter" } },
                sequence_number = 8,
            });

        rewriter.Patch(alreadyMarked).ShouldBeSameAs(alreadyMarked);
        rewriter.MissedTerminalFrame.ShouldBeFalse();
    }

    [Fact]
    public async Task A_completed_frame_whose_body_is_not_completed_is_left_alone()
    {
        var rewriter = await EndedAsync(ChatFinishReason.Length);
        var odd = CompletedFrame(status: "incomplete");

        rewriter.Patch(odd).ShouldBeSameAs(odd);
    }

    [Fact]
    public async Task A_stream_that_never_wrote_the_terminal_frame_is_reported_missing()
    {
        // The warning's input: a cut answer was seen, no frame was rewritten and none was already marked.
        var rewriter = await EndedAsync(ChatFinishReason.Length);

        rewriter.Patch(ItemDoneFrame("message"));

        rewriter.MissedTerminalFrame.ShouldBeTrue();
    }

    [Fact]
    public async Task A_complete_stream_is_never_reported_missing()
    {
        var rewriter = await EndedAsync(ChatFinishReason.Stop);

        rewriter.MissedTerminalFrame.ShouldBeFalse();
    }

    [Theory]
    [InlineData("event: response.completed\ndata: {not json\n\n")]
    [InlineData("event: response.completed\n\n")]
    [InlineData("event: response.completed\ndata: [1,2]\n\n")]

    // F-735: the rewriter recognises a frame by its "\n" line ends, the way MAF writes them. A frame with
    // CRLF ends is not recognised, passes through unchanged and is reported, so the endpoint logs that the
    // status stayed "completed" instead of rewriting half of a frame it cannot read.
    [InlineData("event: response.completed\r\ndata: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[]}}\r\n\r\n")]
    public async Task A_terminal_frame_this_code_cannot_read_passes_through_and_is_reported_missing(string unreadable)
    {
        var rewriter = await EndedAsync(ChatFinishReason.Length);

        rewriter.Patch(unreadable).ShouldBeSameAs(unreadable);
        rewriter.MissedTerminalFrame.ShouldBeTrue();
    }

    [Fact]
    public async Task The_rewritten_frame_keeps_its_wire_shape()
    {
        var rewriter = await EndedAsync(ChatFinishReason.Length);

        var patched = rewriter.Patch(CompletedFrame());

        patched.ShouldStartWith("event: response.incomplete\ndata: {");
        patched.ShouldEndWith("}\n\n");
        patched.Count(static character => character == '\n').ShouldBe(3, "the end of the event line, the end of the data line and the blank line");
    }

    [Fact]
    public async Task Observing_forwards_every_update_untouched_and_passes_cancellation_on()
    {
        var rewriter = new ResponsesStreamRewriter();
        var source = new List<AgentResponseUpdate>();

        await foreach (var update in rewriter.Observe(Updates([ChatFinishReason.Stop, ChatFinishReason.Length])).WithCancellation(TestContext.Current.CancellationToken))
        {
            source.Add(update);
        }

        source.Select(static update => update.FinishReason?.Value).ShouldBe(["stop", "length"]);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in rewriter.Observe(Updates([ChatFinishReason.Stop])).WithCancellation(cancelled.Token))
            {
                // The first step throws.
            }
        });
    }

    private static async Task<ResponsesStreamRewriter> EndedAsync(params ChatFinishReason?[] reasons)
    {
        var rewriter = new ResponsesStreamRewriter();

        await foreach (var _ in rewriter.Observe(Updates(reasons)).WithCancellation(TestContext.Current.CancellationToken))
        {
            // Drained: the source ended, as it does before MAF writes the closing frames.
        }

        return rewriter;
    }

    private static async IAsyncEnumerable<AgentResponseUpdate> Updates(
        IReadOnlyList<ChatFinishReason?> reasons,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var reason in reasons)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            yield return new AgentResponseUpdate(ChatRole.Assistant, "x") { FinishReason = reason };
        }
    }

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    private static string CompletedFrame(string status = "completed")
        => Frame(
            "response.completed",
            new
            {
                type = "response.completed",
                response = new
                {
                    id = "resp_1",
                    status,
                    output = new[] { new { type = "message", status = "completed", id = "msg_1" } },
                },
                sequence_number = 8,
            });

    private static string ItemDoneFrame(string itemType)
        => Frame(
            "response.output_item.done",
            new
            {
                type = "response.output_item.done",
                output_index = 0,
                item = new { type = itemType, status = "completed", id = "item_1" },
                sequence_number = 7,
            });

    private static string Frame(string name, object body)
        => $"event: {name}\ndata: {JsonSerializer.Serialize(body)}\n\n";

    private static JsonDocument Data(string frame, string expectedEvent)
    {
        frame.ShouldStartWith($"event: {expectedEvent}\ndata: ");

        return JsonDocument.Parse(frame[(frame.IndexOf("data: ", StringComparison.Ordinal) + "data: ".Length)..].TrimEnd('\n'));
    }
}
