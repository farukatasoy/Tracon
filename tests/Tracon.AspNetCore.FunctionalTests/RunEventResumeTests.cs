using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An event stream that is cut and resumed with <c>Last-Event-ID</c> joins into exactly the stream an uninterrupted
/// reader sees: every event once, in order, none repeated, none lost.
/// </summary>
/// <remarks>
/// <para>
/// This is the stream a browser's <c>EventSource</c> reconnects to by itself (<c>GET /api/runs/{id}/events</c>). The live
/// frames of a run request are a different contract and are not resumable by design.
/// </para>
/// <para>
/// The oracle is the run's own recorded event list read through the same endpoint without a header, after the run is
/// over; the joined sequence is compared to it event by event, by id, name and data. Each case cuts at every position,
/// and the live case cuts and resumes after every single frame while the run is still going.
/// </para>
/// </remarks>
public sealed class RunEventResumeTests
{
    private const string Agent = "kod-agent";

    [Fact]
    public async Task A_finished_runs_stream_cut_at_every_position_and_resumed_joins_into_the_full_sequence_exactly_once()
    {
        await using var host = await StartToolHostAsync();
        var runId = await RunToCompletionAsync(host);

        var full = await ReadAsync(host, runId, lastEventId: null, maxFrames: int.MaxValue);

        full.Count.ShouldBeGreaterThanOrEqualTo(4, "the run records a start, a tool call, an answer and a completion");
        full.Select(static frame => frame.Id).ShouldBe(Enumerable.Range(0, full.Count).Select(static id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList(), "ids are the recorded sequence, 0-based and gap free");

        for (var cut = 0; cut <= full.Count; cut++)
        {
            var first = await ReadAsync(host, runId, lastEventId: null, maxFrames: cut);
            var lastSeen = first.Count == 0 ? null : first[^1].Id;
            var resumed = await ReadAsync(host, runId, lastEventId: lastSeen, maxFrames: int.MaxValue);

            Join(first, resumed).ShouldBe(full, $"cut after {cut} frame(s) and resumed from Last-Event-ID {lastSeen ?? "(none)"}");
        }
    }

    [Fact]
    public async Task Resuming_from_the_last_event_or_beyond_it_ends_the_stream_instead_of_waiting()
    {
        await using var host = await StartToolHostAsync();
        var runId = await RunToCompletionAsync(host);

        var full = await ReadAsync(host, runId, lastEventId: null, maxFrames: int.MaxValue);

        foreach (var lastEventId in new[] { full[^1].Id, "99999", "9223372036854775807" })
        {
            var tail = await ReadAsync(host, runId, lastEventId, int.MaxValue);

            tail.ShouldBeEmpty($"nothing is newer than {lastEventId}, and a finished run closes its stream");
        }
    }

    [Fact]
    public async Task A_header_that_is_not_a_sequence_number_starts_the_stream_from_the_beginning()
    {
        await using var host = await StartToolHostAsync();
        var runId = await RunToCompletionAsync(host);

        var full = await ReadAsync(host, runId, lastEventId: null, maxFrames: int.MaxValue);

        foreach (var lastEventId in new[] { "abc", "-5", "1.5", string.Empty })
        {
            (await ReadAsync(host, runId, lastEventId, int.MaxValue)).ShouldBe(full, $"Last-Event-ID '{lastEventId}' is not a position");
        }
    }

    [Fact]
    public async Task A_running_runs_stream_cut_after_every_frame_and_resumed_joins_into_the_full_sequence_exactly_once()
    {
        var gated = new GatedModelProvider("resume-gated", "resume-1");

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(gated)
                .AddAgent(TestData.Definition() with { Model = new ModelBinding { Provider = "resume-gated", Model = "resume-1" } }),
            configureEndpoints: static options => options.RunEventPollInterval = TimeSpan.FromMilliseconds(25));

        // The run answers only when the gate opens, so the stream below is read while the run is going.
        var runTask = Task.Run(
            async () =>
            {
                using var response = await host.Client.PostAsJsonAsync(
                    new Uri($"/tracon/api/agents/{Agent}/run", UriKind.Relative),
                    new { message = "resume while running" },
                    TestContext.Current.CancellationToken);

                response.EnsureSuccessStatusCode();
            },
            TestContext.Current.CancellationToken);

        await gated.Started;

        var runs = host.Services.GetRequiredService<IRunStore>();
        var recorded = await WaitUntil.ValueAsync(
            async () => await runs.QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken),
            static found => found.Count > 0,
            "the run to be recorded");
        var runId = recorded[0].Id;

        var seen = new List<SseFrame>();
        string? lastEventId = null;
        var opened = false;

        // One frame per connection: the connection is cut after every event, and the run is still going for the first one.
        // The bound keeps a resume that never moves forward from reading the same event for ever.
        for (var connection = 0; ; connection++)
        {
            connection.ShouldBeLessThan(50, "the stream must run out of events; it kept answering");

            var next = await ReadAsync(host, runId, lastEventId, maxFrames: 1);

            if (next.Count == 0)
            {
                break;
            }

            seen.AddRange(next);
            lastEventId = next[^1].Id;

            if (!opened)
            {
                opened = true;
                gated.Open();
            }
        }

        await runTask;
        await host.WaitForTerminalRunAsync(runId);

        var full = await ReadAsync(host, runId, lastEventId: null, maxFrames: int.MaxValue);

        seen.ShouldBe(full, "one frame per connection, resumed each time, is the whole stream");
        full.Any(static frame => string.Equals(frame.Event, "run.completed", StringComparison.Ordinal)).ShouldBeTrue("the stream ends with the run's completion");
    }

    /// <summary>
    /// The first block of the stream tells the client how long to wait before it reconnects, whether or not it asks to
    /// resume. A browser's <c>EventSource</c> reconnects on its own after a restart, and without this value every client
    /// of an instance that went down comes back at the same moment, at whatever delay the browser picked.
    /// </summary>
    [Fact]
    public async Task A_finished_runs_stream_opens_with_the_reconnect_delay_with_or_without_Last_Event_ID()
    {
        await using var host = await StartToolHostAsync();
        var runId = await RunToCompletionAsync(host);

        // 99999 is past the last event: the body is the reconnect delay and nothing else.
        foreach (var lastEventId in new string?[] { null, "1", "99999" })
        {
            var body = await ReadBodyAsync(host, runId, lastEventId);

            body.ShouldStartWith(ReconnectDelayBlock, Case.Sensitive, $"Last-Event-ID {lastEventId ?? "(none)"}\n{body}");
            CountOf(body, "retry:").ShouldBe(1, $"Last-Event-ID {lastEventId ?? "(none)"}\n{body}");
        }
    }

    /// <summary>
    /// The streamed answer to a run request is not resumable by design, so it does not tell the client when to
    /// reconnect either.
    /// </summary>
    [Fact]
    public async Task The_streamed_answer_to_a_run_request_sets_no_reconnect_delay()
    {
        await using var host = await StartToolHostAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{Agent}/run", UriKind.Relative),
            new { message = "where is ORD-1?" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldContain("event: ", Case.Sensitive, body);
        body.ShouldNotContain("retry:", Case.Sensitive, body);
    }

    /// <summary>
    /// An idle live stream writes a keep-alive comment on every poll, after the reconnect delay and before any event.
    /// </summary>
    /// <remarks>
    /// This pins today's cadence as the contract: the console and the client packages derive their idle limits from it,
    /// and a reverse proxy that ignores the no-buffering header holds an event back until enough comment bytes have
    /// filled its buffer. The stream's own silence-based keep-alive (every 15 seconds by default) never fires while the
    /// poll writes, because every write resets its timer. The wait is counted in comments, not in seconds.
    /// </remarks>
    [Fact]
    public async Task An_idle_live_stream_writes_a_waiting_comment_on_every_poll_after_a_single_reconnect_delay()
    {
        var gated = new GatedModelProvider("cadence-gated", "cadence-1");

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(gated)
                .AddAgent(TestData.Definition() with { Model = new ModelBinding { Provider = "cadence-gated", Model = "cadence-1" } }),
            configureEndpoints: static options => options.RunEventPollInterval = TimeSpan.FromMilliseconds(25));

        var runTask = Task.CompletedTask;

        try
        {
            runTask = Task.Run(
                async () =>
                {
                    using var response = await host.Client.PostAsJsonAsync(
                        new Uri($"/tracon/api/agents/{Agent}/run", UriKind.Relative),
                        new { message = "idle while gated" },
                        TestContext.Current.CancellationToken);

                    response.EnsureSuccessStatusCode();
                },
                TestContext.Current.CancellationToken);

            await gated.Started;

            var runs = host.Services.GetRequiredService<IRunStore>();
            var recorded = await WaitUntil.ValueAsync(
                async () => await runs.QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken),
                static found => found.Count > 0,
                "the run to be recorded");

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/tracon/api/runs/{recorded[0].Id}/events", UriKind.Relative));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30)); // cancel: bound

            using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var reader = new StreamReader(body, Encoding.UTF8);

            var seen = new StringBuilder();
            var waiting = 0;

            while (waiting < 3 && await reader.ReadLineAsync(deadline.Token) is { } line)
            {
                seen.Append(line).Append('\n');

                if (string.Equals(line, ": waiting", StringComparison.Ordinal))
                {
                    waiting++;
                }
            }

            var text = seen.ToString();

            waiting.ShouldBe(3, $"three idle polls write three comments\n{text}");
            text.ShouldStartWith(ReconnectDelayBlock, Case.Sensitive, text);
            CountOf(text, "retry:").ShouldBe(1, text);
        }
        finally
        {
            // The gated provider ignores cancellation; the host cannot stop while the run is still held.
            gated.Open();
        }

        await runTask;
    }

    /// <summary>The block that tells a client how long to wait before it reconnects.</summary>
    private const string ReconnectDelayBlock = "retry: 3000\n\n";

    private static int CountOf(string text, string value)
    {
        var count = 0;

        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>Reads the whole event stream of a run that has ended, exactly as it went over the wire.</summary>
    private static async Task<string> ReadBodyAsync(TraconTestHost host, Guid runId, string? lastEventId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/tracon/api/runs/{runId}/events", UriKind.Relative));

        if (lastEventId is not null)
        {
            request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); // cancel: bound

        using var response = await host.Client.SendAsync(request, deadline.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadAsStringAsync(deadline.Token);
    }

    private static List<SseFrame> Join(List<SseFrame> first, List<SseFrame> resumed) => [.. first, .. resumed];

    private static Task<TraconTestHost> StartToolHostAsync()
        => TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(new FakeModelProvider("resume-tool-model")
                .CallsTool("lookup_order", new { orderId = "ORD-1" })
                .EchoesLastToolResult("Result: "))
            .AddTool(
                (Func<string, string>)(orderId => $"{orderId} shipped"),
                name: "lookup_order",
                description: "Looks an order up.")
            .AddAgent(new AgentDefinition
            {
                Name = Agent,
                Instructions = "Look the order up.",
                Model = new ModelBinding { Provider = "resume-tool-model", Model = "tool-1" },
                ToolNames = ["lookup_order"],
            }));

    private static async Task<Guid> RunToCompletionAsync(TraconTestHost host)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{Agent}/run", UriKind.Relative),
            new { message = "where is ORD-1?" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var runs = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken);

        await host.WaitForTerminalRunAsync(runs[0].Id);

        return runs[0].Id;
    }

    /// <summary>Opens the event stream, reads up to <paramref name="maxFrames"/> frames and drops the connection.</summary>
    private static async Task<List<SseFrame>> ReadAsync(TraconTestHost host, Guid runId, string? lastEventId, int maxFrames)
    {
        var frames = new List<SseFrame>();

        if (maxFrames == 0)
        {
            return frames;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/tracon/api/runs/{runId}/events", UriKind.Relative));

        if (lastEventId is not null)
        {
            request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); // cancel: bound

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");

        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);

        await foreach (var frame in SseReader.ReadAsync(stream, deadline.Token))
        {
            frames.Add(frame);

            if (frames.Count >= maxFrames)
            {
                break;
            }
        }

        return frames;
    }
}
