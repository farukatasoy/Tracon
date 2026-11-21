using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.OutputTruncationTests;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A content filter that stops the turn which ends the run decides the outcome by what THAT turn wrote.
/// A tool-call turn earlier in the loop is content of an earlier turn: when the final turn is empty the
/// run fails as <c>content_filtered</c>, on every surface that answers it, instead of completing with a
/// notice for an answer that holds nothing.
/// </summary>
/// <remarks>
/// The provider is scripted through the real tool-call loop (<c>FunctionInvokingChatClient</c>) and the real
/// endpoints, because the loop is what puts the earlier turn's function call into the same response the
/// finish-reason detector reads.
/// </remarks>
public sealed class ContentFilteredFinalTurnTests
{
    private const string ToolName = "lookup_fact";

    private static readonly Uri ChatCompletions = new("/tracon/v1/chat/completions", UriKind.Relative);
    private static readonly Uri Responses = new("/tracon/v1/responses", UriKind.Relative);

    [Fact]
    public async Task An_empty_content_filtered_final_turn_after_a_tool_turn_fails_the_run_as_content_filtered()
    {
        var provider = ToolTurnThenFilteredTurn(text: string.Empty);

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 100, toolNames: [ToolName]), configureTracon: AddTool);

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull();
        run.Error.Type.ShouldBe(TraconContentFilteredException.ContentFilteredErrorType);
        run.Error.Class.ShouldBe(RunErrorClass.ContentFiltered);
        provider.Calls.ShouldBe(2);

        // Both turns were paid for, and the failed run says so: the model pipeline counted each response as it
        // arrived, the filtered one included, once.
        run.Usage.ShouldNotBeNull();
        run.Usage.InputTokens.ShouldBe(50);
        run.Usage.OutputTokens.ShouldBe(10);

        // Nothing to keep, so no notice: the failure is the signal.
        await AssertNoFilterNoticeAsync(host, run.Id);
    }

    [Fact]
    public async Task A_streamed_empty_content_filtered_final_turn_after_a_tool_turn_fails_the_run_as_content_filtered()
    {
        var provider = ToolTurnThenFilteredTurn(text: string.Empty);

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 100, toolNames: [ToolName]), configureTracon: AddTool);

        var frames = await PostStreamingAsync(host);

        frames.ShouldContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
        frames.ShouldNotContain(static frame => string.Equals(frame.Event, "done", StringComparison.Ordinal));
        OutputTruncationStreamingTests.CustomFrames(frames).ShouldBeEmpty();

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Class.ShouldBe(RunErrorClass.ContentFiltered);
        provider.Calls.ShouldBe(2);
        run.Usage.ShouldNotBeNull();
        run.Usage.InputTokens.ShouldBe(50);
        await AssertNoFilterNoticeAsync(host, run.Id);
    }

    [Fact]
    public async Task A_whitespace_only_content_filtered_final_turn_after_a_tool_turn_is_empty_too()
    {
        var provider = ToolTurnThenFilteredTurn(text: " \n ");

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 100, toolNames: [ToolName]), configureTracon: AddTool);

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await SingleRunAsync(host)).Error!.Class.ShouldBe(RunErrorClass.ContentFiltered);
    }

    [Fact]
    public async Task A_content_filtered_final_turn_that_wrote_text_after_a_tool_turn_still_completes_with_the_notice()
    {
        // The partial answer is the final turn's own text: the rule is unchanged for it.
        var provider = ToolTurnThenFilteredTurn(text: "The answer is 42, beca");

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 100, toolNames: [ToolName]), configureTracon: AddTool);

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        run.Error.ShouldBeNull();
        (await EventsAsync(host, run.Id))
            .Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.OutputFiltered, StringComparison.Ordinal))
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_streamed_content_filtered_final_turn_that_wrote_text_after_a_tool_turn_still_completes_with_the_notice()
    {
        var provider = ToolTurnThenFilteredTurn(text: "The answer is 42, beca");

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 100, toolNames: [ToolName]), configureTracon: AddTool);

        var frames = await PostStreamingAsync(host);

        frames.ShouldNotContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
        OutputTruncationStreamingTests.CustomFrames(frames).ShouldHaveSingleItem();
        frames[^1].Event.ShouldBe("done");

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task The_responses_endpoint_answers_an_empty_content_filtered_final_turn_with_an_error(bool stream, bool afterToolTurn)
    {
        // afterToolTurn: false is the long-standing case (the control); true is the same final turn
        // behind a tool-call turn, which must read the same way.
        var provider = Script(afterToolTurn, text: string.Empty);

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 100, toolNames: [ToolName]), configureTracon: AddTool);

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = AgentName, input = "hello", stream },
            TestContext.Current.CancellationToken);

        if (!stream)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await TraconTestHost.ReadJsonAsync(response)).TryGetProperty("error", out _).ShouldBeTrue();
        }
        else
        {
            await using var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            var frames = await SseReader.ReadAllAsync(body);

            frames.ShouldContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
            frames.ShouldNotContain(static frame => string.Equals(frame.Event, "response.incomplete", StringComparison.Ordinal));
            frames.ShouldNotContain(static frame => string.Equals(frame.Event, "response.completed", StringComparison.Ordinal));
        }

        (await SingleRunAsync(host)).Error!.Class.ShouldBe(RunErrorClass.ContentFiltered);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task The_chat_completions_endpoint_answers_an_empty_content_filtered_final_turn_with_an_error(bool stream, bool afterToolTurn)
    {
        var provider = Script(afterToolTurn, text: string.Empty);

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 100, toolNames: [ToolName]), configureTracon: AddTool);

        using var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new
            {
                model = AgentName,
                stream,
                messages = new[] { new { role = "user", content = "hello" } },
            },
            TestContext.Current.CancellationToken);

        if (!stream)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await TraconTestHost.ReadJsonAsync(response)).TryGetProperty("error", out _).ShouldBeTrue();
        }
        else
        {
            await using var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            var frames = await SseReader.ReadAllAsync(body);

            frames.ShouldNotContain(static frame => string.Equals(frame.Data, "[DONE]", StringComparison.Ordinal));
            frames.ShouldContain(static frame => frame.Data.Contains("\"error\"", StringComparison.Ordinal));
        }

        (await SingleRunAsync(host)).Error!.Class.ShouldBe(RunErrorClass.ContentFiltered);
    }

    private static FinishReasonModelProvider ToolTurnThenFilteredTurn(string text)
        => Script(afterToolTurn: true, text);

    private static FinishReasonModelProvider Script(bool afterToolTurn, string text)
    {
        var provider = new FinishReasonModelProvider();

        if (afterToolTurn)
        {
            provider.ToolCall(ToolName, ChatFinishReason.ToolCalls, inputTokens: 20, outputTokens: 10);
        }

        return provider.Text(text, ChatFinishReason.ContentFilter, inputTokens: 30, outputTokens: 0);
    }

    private static async Task AssertNoFilterNoticeAsync(TraconTestHost host, Guid runId)
        => (await EventsAsync(host, runId)).ShouldNotContain(
            static e => string.Equals(e.CustomType, RunEventCustomTypes.OutputFiltered, StringComparison.Ordinal));

    private static void AddTool(ITraconBuilder builder)
        => builder.AddTool((Func<string>)(static () => "42"), name: ToolName, description: "Returns a fact.");
}
