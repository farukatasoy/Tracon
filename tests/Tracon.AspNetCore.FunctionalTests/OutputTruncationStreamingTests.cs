using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
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

        // Phase 207: the notice mirror sits on the 'done' path; a structured cut is
        // an error, so the stream carries no notice frame.
        CustomFrames(frames).ShouldBeEmpty();

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Class.ShouldBe(RunErrorClass.OutputTruncated);

        // The recorder drops the usage updates that already streamed when the
        // run ends in an exception. The detector sums them and folds them into
        // the run before it throws, so the tokens are counted once.
        run.Usage.ShouldNotBeNull();
        run.Usage.OutputTokens.ShouldBe(200);
    }

    [Fact]
    public async Task A_streamed_structured_response_cut_at_the_limit_writes_one_rejection_when_validation_is_on()
    {
        // Audit finding: the streaming catch in StructuredResponseValidatingAgent
        // had no end-to-end test. The stream already reached the caller, so
        // repair never runs on this path: the rejection is attempt 1 of 1 and
        // claims no skipped repair, whatever MaxRepairAttempts says.
        var provider = new FinishReasonModelProvider()
            .Text("{\"title\": \"An unfinish", ChatFinishReason.Length, inputTokens: 40, outputTokens: 200);

        await using var host = await StartAsync(
            provider,
            Structured(maxOutputTokens: 200),
            services => services.Configure<TraconStructuredResponseOptions>(static options =>
            {
                options.Enabled = true;
                options.MaxRepairAttempts = 2;
            }));

        var frames = await PostStreamingAsync(host);

        frames.ShouldContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
        CustomFrames(frames).ShouldBeEmpty();

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Class.ShouldBe(RunErrorClass.OutputTruncated);
        provider.Calls.ShouldBe(1);

        var events = await EventsAsync(host, run.Id);

        events.ShouldNotContain(static e => e.Type == RunEventType.StructuredResponseRepairAttempted);

        var rejected = events.Where(static e => e.Type == RunEventType.StructuredResponseRejected).ShouldHaveSingleItem();
        rejected.Text.ShouldBe("The model stopped at its output token limit before the response was complete.");

        using var payload = JsonDocument.Parse(rejected.Payload!);
        payload.RootElement.GetProperty("attempt").GetInt32().ShouldBe(1);
        payload.RootElement.GetProperty("maxAttempts").GetInt32().ShouldBe(1);
        payload.RootElement.GetProperty("repairSkippedTruncated").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_streamed_plain_text_response_cut_at_the_limit_completes_and_records_the_notice()
    {
        var provider = new FinishReasonModelProvider()
            .Text("The first half of a long ans", ChatFinishReason.Length, inputTokens: 30, outputTokens: 50);

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 50));

        var frames = await PostStreamingAsync(host);

        // The run does not fail: the partial text streamed and the stream closed
        // normally. The notice is a run event; the live stream mirrors it as a
        // 'custom' frame (phase 207, tests below).
        frames.ShouldNotContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
        frames.ShouldContain(static frame => string.Equals(frame.Event, "done", StringComparison.Ordinal));

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        (await EventsAsync(host, run.Id))
            .Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.OutputTruncated, StringComparison.Ordinal))
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_last_update_frame_carries_the_model_finish_reason()
    {
        // Phase 207.1 measurement (2026-10-14, MAF 1.20.0; the test passes on 1.25.0, re-run
        // 2026-10-16): MAF copies
        // ChatResponseUpdate.FinishReason into AgentResponseUpdate.FinishReason and
        // the endpoint serializes the update as is. This is MAF's raw value per
        // model call, not a Tracon contract; the endpoint's notice gate and the
        // Chat Completions stream depend on it, so a MAF upgrade that drops the
        // field turns this test red.
        var provider = new FinishReasonModelProvider()
            .Text("The first half of a long ans", ChatFinishReason.Length, inputTokens: 30, outputTokens: 50);

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 50));

        var frames = await PostStreamingAsync(host);

        LastUpdateFinishReason(frames).ShouldBe("length");
    }

    [Fact]
    public async Task A_streamed_plain_text_response_cut_at_the_limit_sends_the_notice_before_done()
    {
        var provider = new FinishReasonModelProvider()
            .Text("The first half of a long ans", ChatFinishReason.Length, inputTokens: 30, outputTokens: 50);

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 50));

        var frames = await PostStreamingAsync(host);

        var notice = CustomFrames(frames).ShouldHaveSingleItem();
        frames.IndexOf(notice).ShouldBe(frames.FindIndex(static frame => string.Equals(frame.Event, "done", StringComparison.Ordinal)) - 1);

        using (var data = JsonDocument.Parse(notice.Data))
        {
            data.RootElement.GetProperty("type").GetString().ShouldBe("Custom");
            data.RootElement.GetProperty("customType").GetString().ShouldBe(RunEventCustomTypes.OutputTruncated);

            using var payload = JsonDocument.Parse(data.RootElement.GetProperty("payload").GetString()!);
            payload.RootElement.GetProperty("cause").GetString().ShouldBe("output_limit");
            payload.RootElement.GetProperty("maxOutputTokens").GetInt32().ShouldBe(50);
        }

        // Byte for byte: the live frame is the recorded event read back, not a
        // second, independently built copy.
        var run = await SingleRunAsync(host);
        var replayed = CustomFrames(await EventFramesAsync(host, run.Id)).ShouldHaveSingleItem();

        notice.Data.ShouldBe(replayed.Data);
    }

    [Fact]
    public async Task A_complete_streamed_response_sends_no_notice()
    {
        var provider = new FinishReasonModelProvider()
            .Text("A full answer.", ChatFinishReason.Stop, inputTokens: 30, outputTokens: 5);

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 50));

        var frames = await PostStreamingAsync(host);

        CustomFrames(frames).ShouldBeEmpty();
        frames[^1].Event.ShouldBe("done");
    }

    [Fact]
    public async Task A_streamed_answer_cut_by_the_context_window_names_the_cause_in_the_notice()
    {
        var provider = new FinishReasonModelProvider()
            .Text("The first half of a long ans", TraconFinishReasons.ContextWindowExceeded, inputTokens: 30, outputTokens: 50);

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 50));

        var frames = await PostStreamingAsync(host);

        var notice = CustomFrames(frames).ShouldHaveSingleItem();

        using var data = JsonDocument.Parse(notice.Data);
        using var payload = JsonDocument.Parse(data.RootElement.GetProperty("payload").GetString()!);

        payload.RootElement.GetProperty("cause").GetString().ShouldBe("context_window");
        frames[^1].Event.ShouldBe("done");
    }

    [Theory]
    [InlineData("length", 1)]
    [InlineData("stop", 0)]
    public async Task The_stream_reads_the_run_events_only_after_an_update_reported_a_cut(string finishReason, int expectedReads)
    {
        // The gate: a normal run must not pay a store read for a notice it cannot
        // have. The quota option is off (the default), so only the finish reason
        // the updates carried can open the scan.
        var provider = new FinishReasonModelProvider()
            .Text("Some answer", new ChatFinishReason(finishReason), inputTokens: 30, outputTokens: 50);
        CallCounter? reads = null;

        await using var host = await StartAsync(
            provider,
            PlainText(maxOutputTokens: 50),
            configureTracon: builder => reads = builder.Services.CountCalls<IRunStore>(nameof(IRunStore.ReadEventsAsync)));

        var frames = await PostStreamingAsync(host);

        // Read before any other request: GET /events reads the store too.
        reads!.Count.ShouldBe(expectedReads);
        frames[^1].Event.ShouldBe("done");
    }

    [Fact]
    public async Task A_truncated_stream_without_run_recording_ends_with_done_and_no_notice()
    {
        // Phase 207.5: with recording off there is no writer, so the detector
        // writes no event and the mirror finds nothing to send (Open Question 1-A).
        var provider = new FinishReasonModelProvider()
            .Text("The first half of a long ans", ChatFinishReason.Length, inputTokens: 30, outputTokens: 50);

        await using var host = await StartAsync(
            provider,
            PlainText(maxOutputTokens: 50),
            configureServices: static services => services.Configure<TraconOptions>(
                static options => options.RunRecording.Enabled = false));

        var frames = await PostStreamingAsync(host);

        frames.ShouldNotContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
        CustomFrames(frames).ShouldBeEmpty();
        frames[^1].Event.ShouldBe("done");
    }

    [Fact]
    public async Task A_harness_loop_whose_iterations_are_cut_sends_one_notice_per_recorded_event()
    {
        // Phase 207.3: the harness loop sits above the model pipeline, so every
        // iteration is its own pipeline call and the detector judges each one.
        var provider = new FinishReasonModelProvider()
            .Text("still working", ChatFinishReason.Length, inputTokens: 10, outputTokens: 50)
            .Text("ALL DONE", ChatFinishReason.Length, inputTokens: 10, outputTokens: 50);

        await using var host = await StartAsync(
            provider,
            PlainText(maxOutputTokens: 50) with
            {
                Harness = new HarnessSettings
                {
                    Loop = new LoopSettings
                    {
                        Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "ALL DONE" }],
                        MaxIterations = 4,
                    },
                },
            });

        var frames = await PostStreamingAsync(host);

        provider.Calls.ShouldBe(2);

        // Measured: the intermediate iteration's text reaches the client too.
        frames.Where(static frame => string.Equals(frame.Event, "update", StringComparison.Ordinal))
            .ShouldContain(static frame => frame.Data.Contains("still working", StringComparison.Ordinal));

        var run = await SingleRunAsync(host);
        var recorded = (await EventsAsync(host, run.Id))
            .Count(static e => string.Equals(e.CustomType, RunEventCustomTypes.OutputTruncated, StringComparison.Ordinal));

        recorded.ShouldBe(2);
        CustomFrames(frames).Count.ShouldBe(recorded);
        frames[^1].Event.ShouldBe("done");
    }

    internal static List<SseFrame> CustomFrames(IEnumerable<SseFrame> frames)
        => [.. frames.Where(static frame => string.Equals(frame.Event, "custom", StringComparison.Ordinal))];

    /// <summary>The raw frames of <c>GET /api/runs/{id}/events</c>.</summary>
    internal static async Task<List<SseFrame>> EventFramesAsync(TraconTestHost host, Guid runId)
    {
        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{runId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);

        return await SseReader.ReadAllAsync(stream);
    }

    private static string? LastUpdateFinishReason(IEnumerable<SseFrame> frames)
    {
        string? last = null;

        foreach (var frame in frames.Where(static frame => string.Equals(frame.Event, "update", StringComparison.Ordinal)))
        {
            using var update = JsonDocument.Parse(frame.Data);

            if (update.RootElement.TryGetProperty("finishReason", out var reason) && reason.ValueKind == JsonValueKind.String)
            {
                last = reason.GetString();
            }
        }

        return last;
    }
}
