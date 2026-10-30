using System.Net;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.OutputTruncationTests;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tool-call turn that hit the output limit is an INTERMEDIATE
/// response. The tool loop runs the call and asks the model again; only the
/// turn that ends the loop decides the run's outcome.
/// </summary>
/// <remarks>
/// Measured (MEAI 10.11.0 <c>FunctionInvokingChatClient</c>, decompiled 2026-10-16): the
/// non-streaming loop returns the LAST turn's response object, so its finish
/// reason is the final one; the streaming loop yields the tool-call turn's
/// updates (with that turn's reason), then the tool results, then the next
/// turn. The detector sits outside the loop and reads the last reported reason.
/// </remarks>
public sealed class OutputTruncationToolTurnTests
{
    private const string ToolName = "lookup_fact";

    [Fact]
    public async Task An_intermediate_tool_turn_at_the_limit_does_not_fail_a_run_whose_final_turn_completes()
    {
        var provider = new FinishReasonModelProvider()
            .ToolCall(ToolName, ChatFinishReason.Length, inputTokens: 20, outputTokens: 100)
            .Text("{\"answer\": 42}", ChatFinishReason.Stop, inputTokens: 30, outputTokens: 10);

        await using var host = await StartAsync(
            provider, Structured(maxOutputTokens: 100, toolNames: [ToolName]), configureTracon: AddTool);

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        run.Error.ShouldBeNull();
        provider.Calls.ShouldBe(2);
        (await EventsAsync(host, run.Id)).ShouldNotContain(static e => string.Equals(e.CustomType, RunEventCustomTypes.OutputTruncated, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Streamed_intermediate_tool_turn_at_the_limit_does_not_fail_the_run()
    {
        var provider = new FinishReasonModelProvider()
            .ToolCall(ToolName, ChatFinishReason.Length, inputTokens: 20, outputTokens: 100)
            .Text("{\"answer\": 42}", ChatFinishReason.Stop, inputTokens: 30, outputTokens: 10);

        await using var host = await StartAsync(
            provider, Structured(maxOutputTokens: 100, toolNames: [ToolName]), configureTracon: AddTool);

        var frames = await PostStreamingAsync(host);

        frames.ShouldNotContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
        frames[^1].Event.ShouldBe("done");

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        provider.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Streamed_plain_text_run_whose_intermediate_tool_turn_hit_the_limit_sends_no_notice()
    {
        // The plain-text twin of the case above. A structured agent can
        // never get a notice frame (a cut structured answer throws instead), so only
        // a plain-text agent proves the rule: the tool turn's 'length' opens the
        // endpoint's notice scan, but the detector judges only the final turn and
        // writes no event, so the scan finds nothing.
        var provider = new FinishReasonModelProvider()
            .ToolCall(ToolName, ChatFinishReason.Length, inputTokens: 20, outputTokens: 100)
            .Text("The answer is 42.", ChatFinishReason.Stop, inputTokens: 30, outputTokens: 10);

        await using var host = await StartAsync(
            provider, PlainText(maxOutputTokens: 100, toolNames: [ToolName]), configureTracon: AddTool);

        var frames = await PostStreamingAsync(host);

        frames.ShouldNotContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
        OutputTruncationStreamingTests.CustomFrames(frames).ShouldBeEmpty();
        frames[^1].Event.ShouldBe("done");

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        provider.Calls.ShouldBe(2);
        (await EventsAsync(host, run.Id)).ShouldNotContain(static e => string.Equals(e.CustomType, RunEventCustomTypes.OutputTruncated, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_intermediate_tool_turn_stopped_by_a_content_filter_records_no_notice_for_a_run_whose_final_turn_completes()
    {
        // The filter twin of the cases above: the tool turn's reason is an intermediate one. The detector judges
        // only the turn that ends the loop, so the run records neither the filter notice nor a truncation notice.
        var provider = new FinishReasonModelProvider()
            .ToolCall(ToolName, ChatFinishReason.ContentFilter, inputTokens: 20, outputTokens: 100)
            .Text("The answer is 42.", ChatFinishReason.Stop, inputTokens: 30, outputTokens: 10);

        await using var host = await StartAsync(
            provider, PlainText(maxOutputTokens: 100, toolNames: [ToolName]), configureTracon: AddTool);

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        run.Error.ShouldBeNull();
        provider.Calls.ShouldBe(2);
        await AssertNoNoticeAsync(host, run.Id);
    }

    [Fact]
    public async Task Streamed_plain_text_run_whose_intermediate_tool_turn_was_stopped_by_a_content_filter_sends_no_notice()
    {
        // The streaming loop forwards the tool turn's update with that turn's own reason: the scan the endpoint
        // opens on it finds no event, because the detector reads the LAST reason, which is the final turn's.
        var provider = new FinishReasonModelProvider()
            .ToolCall(ToolName, ChatFinishReason.ContentFilter, inputTokens: 20, outputTokens: 100)
            .Text("The answer is 42.", ChatFinishReason.Stop, inputTokens: 30, outputTokens: 10);

        await using var host = await StartAsync(
            provider, PlainText(maxOutputTokens: 100, toolNames: [ToolName]), configureTracon: AddTool);

        var frames = await PostStreamingAsync(host);

        frames.ShouldNotContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
        OutputTruncationStreamingTests.CustomFrames(frames).ShouldBeEmpty();
        frames[^1].Event.ShouldBe("done");

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        provider.Calls.ShouldBe(2);
        await AssertNoNoticeAsync(host, run.Id);
    }

    [Fact]
    public async Task A_streamed_plain_text_final_turn_at_the_limit_after_a_tool_turn_sends_one_notice()
    {
        var provider = new FinishReasonModelProvider()
            .ToolCall(ToolName, ChatFinishReason.ToolCalls, inputTokens: 20, outputTokens: 10)
            .Text("The answer is 42, beca", ChatFinishReason.Length, inputTokens: 30, outputTokens: 100);

        await using var host = await StartAsync(
            provider, PlainText(maxOutputTokens: 100, toolNames: [ToolName]), configureTracon: AddTool);

        var frames = await PostStreamingAsync(host);

        var notice = OutputTruncationStreamingTests.CustomFrames(frames).ShouldHaveSingleItem();
        notice.Data.ShouldContain(RunEventCustomTypes.OutputTruncated, Case.Sensitive);
        frames[^1].Event.ShouldBe("done");

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        provider.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task A_final_turn_at_the_limit_after_a_tool_turn_fails_the_run()
    {
        var provider = new FinishReasonModelProvider()
            .ToolCall(ToolName, ChatFinishReason.ToolCalls, inputTokens: 20, outputTokens: 10)
            .Text("{\"answer\": \"cut", ChatFinishReason.Length, inputTokens: 30, outputTokens: 100);

        await using var host = await StartAsync(
            provider, Structured(maxOutputTokens: 100, toolNames: [ToolName]), configureTracon: AddTool);

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Class.ShouldBe(RunErrorClass.OutputTruncated);

        // Both turns' tokens: the loop sums them into the response the
        // detector folds into the run.
        run.Usage!.InputTokens.ShouldBe(50);
        run.Usage.OutputTokens.ShouldBe(110);
    }

    [Fact]
    public async Task A_streamed_final_turn_at_the_limit_after_a_tool_turn_fails_the_run()
    {
        var provider = new FinishReasonModelProvider()
            .ToolCall(ToolName, ChatFinishReason.ToolCalls, inputTokens: 20, outputTokens: 10)
            .Text("{\"answer\": \"cut", ChatFinishReason.Length, inputTokens: 30, outputTokens: 100);

        await using var host = await StartAsync(
            provider, Structured(maxOutputTokens: 100, toolNames: [ToolName]), configureTracon: AddTool);

        await PostStreamingAsync(host);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Class.ShouldBe(RunErrorClass.OutputTruncated);
        run.Usage!.OutputTokens.ShouldBe(110);
    }

    private static async Task AssertNoNoticeAsync(TraconTestHost host, Guid runId)
    {
        var events = await EventsAsync(host, runId);

        events.ShouldNotContain(static e => string.Equals(e.CustomType, RunEventCustomTypes.OutputFiltered, StringComparison.Ordinal));
        events.ShouldNotContain(static e => string.Equals(e.CustomType, RunEventCustomTypes.OutputTruncated, StringComparison.Ordinal));
    }

    private static void AddTool(ITraconBuilder builder)
        => builder.AddTool((Func<string>)(static () => "42"), name: ToolName, description: "Returns a fact.");
}
