using System.Net;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.OutputTruncationTests;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-320 — a tool-call turn that hit the output limit is an INTERMEDIATE
/// response. The tool loop runs the call and asks the model again; only the
/// turn that ends the loop decides the run's outcome.
/// </summary>
/// <remarks>
/// Measured (MEAI 10.9.0 <c>FunctionInvokingChatClient</c>, decompiled): the
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

    private static void AddTool(ITraconBuilder builder)
        => builder.AddTool((Func<string>)(static () => "42"), name: ToolName, description: "Returns a fact.");
}
