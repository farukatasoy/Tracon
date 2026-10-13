using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.OutputTruncationTests;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-320 on the streaming (SSE) path: the finish reason arrives in the LAST
/// update, after the content already reached the client, and the run scope is
/// re-established before every step of the stream — only a real host proves
/// the decision still lands on the run.
/// </summary>
public sealed class OutputTruncationStreamingTests
{
    [Fact]
    public async Task A_streamed_structured_response_cut_at_the_limit_fails_the_run_as_output_truncated()
    {
        var provider = new FinishReasonModelProvider()
            .Text("{\"title\": \"An unfinish", ChatFinishReason.Length, inputTokens: 40, outputTokens: 200);

        await using var host = await StartAsync(provider, Structured(maxOutputTokens: 200));

        var frames = await PostStreamingAsync(host);

        frames.ShouldContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Class.ShouldBe(RunErrorClass.OutputTruncated);

        // The streamed usage update passed through before the decision; it is
        // counted once (the detector does not fold on this path).
        run.Usage.ShouldNotBeNull();
        run.Usage.OutputTokens.ShouldBe(200);
    }

    [Fact]
    public async Task A_streamed_plain_text_response_cut_at_the_limit_completes_and_records_the_notice()
    {
        var provider = new FinishReasonModelProvider()
            .Text("The first half of a long ans", ChatFinishReason.Length, inputTokens: 30, outputTokens: 50);

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 50));

        var frames = await PostStreamingAsync(host);

        // The run does not fail: the partial text streamed and the stream closed
        // normally. The notice is a run event, read from the run's own event
        // stream below (measured: the live 'update' frames carry no finish reason).
        frames.ShouldNotContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
        frames.ShouldContain(static frame => string.Equals(frame.Event, "done", StringComparison.Ordinal));

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        (await EventsAsync(host, run.Id))
            .Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.OutputTruncated, StringComparison.Ordinal))
            .ShouldHaveSingleItem();
    }
}
