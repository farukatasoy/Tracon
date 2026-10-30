using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using TraconTestHost = Tracon.AspNetCore.FunctionalTests.Infrastructure.TraconTestHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Model content Tracon has no event for (a hosted web search, a provider
/// error, generated data) reaches the run record, through a real DI-built host and both the
/// buffered and the streaming path. The payload rules are covered at unit level in
/// <c>ContentEventPayloadTests</c> and <c>ReservedPayloadPolicyTests</c>; the options binding,
/// the store, the HTTP read model and the live stream are only real here.
/// </summary>
public sealed class UnrecognizedContentRecordingTests
{
    private static readonly byte[] Pixels = [.. Enumerable.Range(0, 64).Select(static i => (byte)(i + 1))];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_web_search_an_error_and_data_each_become_one_content_event_in_order(bool streaming)
    {
        await using var host = await StartAsync(Scripted());

        await RunAsync(host, streaming);

        var run = await OutputTruncationTests.SingleRunAsync(host);
        var notices = ContentEvents(await OutputTruncationTests.EventsAsync(host, run.Id));

        run.Status.ShouldBe(RunStatus.Completed);
        notices.Select(static e => e.Text).ShouldBe(["web_search call", "error: rate_limited", "data: image/png"]);

        using var search = JsonDocument.Parse(notices[0].Payload!);
        search.RootElement.GetProperty("type").GetString().ShouldBe("hosted_tool_call");
        search.RootElement.GetProperty("tool").GetString().ShouldBe("web_search");
        search.RootElement.GetProperty("callId").GetString().ShouldBe("call-1");
        notices[0].Payload!.ShouldNotContain("private query", Case.Sensitive);

        using var error = JsonDocument.Parse(notices[1].Payload!);
        error.RootElement.GetProperty("errorCode").GetString().ShouldBe("rate_limited");
        error.RootElement.GetProperty("message").GetString().ShouldBe("The provider is rate limiting this key.");

        using var data = JsonDocument.Parse(notices[2].Payload!);
        data.RootElement.GetProperty("mediaType").GetString().ShouldBe("image/png");
        data.RootElement.GetProperty("length").GetInt32().ShouldBe(Pixels.Length);
        notices[2].Payload!.ShouldNotContain(Convert.ToBase64String(Pixels), Case.Sensitive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task With_payloads_off_the_events_stay_and_their_payloads_are_null(bool streaming)
    {
        await using var host = await StartAsync(
            Scripted(),
            static services => services.Configure<TraconOptions>(static options => options.RunRecording.RecordToolPayloads = false));

        await RunAsync(host, streaming);

        var run = await OutputTruncationTests.SingleRunAsync(host);
        var notices = ContentEvents(await OutputTruncationTests.EventsAsync(host, run.Id));

        notices.Select(static e => e.Text).ShouldBe(["web_search call", "error: rate_limited", "data: image/png"]);
        notices.ShouldAllBe(static e => e.Payload == null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task With_message_deltas_off_plain_text_leaves_no_content_event(bool streaming)
    {
        await using var host = await StartAsync(
            new ContentModelProvider([new TextContent("the answer")]),
            static services => services.Configure<TraconOptions>(static options => options.RunRecording.RecordMessageDeltas = false));

        await RunAsync(host, streaming);

        var run = await OutputTruncationTests.SingleRunAsync(host);
        var events = await OutputTruncationTests.EventsAsync(host, run.Id);

        run.Status.ShouldBe(RunStatus.Completed);

        // Every run records its context; nothing else is a Custom event here.
        events.Where(static e => e.Type == RunEventType.Custom)
            .ShouldAllBe(static e => string.Equals(e.CustomType, RunEventCustomTypes.Context, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_live_stream_does_not_carry_the_content_events()
    {
        // The live run stream mirrors a closed list of notices. A content event is read
        // from the record, not pushed to every caller of the stream.
        await using var host = await StartAsync(Scripted());

        var frames = await OutputTruncationTests.PostStreamingAsync(host);

        frames.ShouldNotContain(static frame => frame.Data.Contains(RunEventCustomTypes.Content, StringComparison.Ordinal));
        frames.ShouldContain(static frame => string.Equals(frame.Event, "done", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_store_that_refuses_the_content_event_does_not_fail_the_response()
    {
        await using var host = await StartAsync(
            Scripted(),
            configureTracon: static builder => builder.Services.ThrowFrom<IRunStore>(
                nameof(IRunStore.AppendEventAsync),
                static () => new InvalidOperationException("the event table is down"),
                when: static arguments => arguments[0] is RunEvent { CustomType: RunEventCustomTypes.Content }));

        using var response = await OutputTruncationTests.PostBufferedAsync(host);

        // Observability does not break functionality: the caller still gets the answer.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("the answer", Case.Sensitive);

        // The fault did fire: the event the store refused is not in the record. The record is
        // read from the store, not over HTTP: the writer gave up on this run, so the run row is
        // never closed and the events endpoint would wait for it to end.
        var store = host.Services.GetRequiredService<IRunStore>();
        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var events = new List<RunEvent>();

        await foreach (var runEvent in store.ReadEventsAsync(run.Id))
        {
            events.Add(runEvent);
        }

        ContentEvents(events).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_output_truncated_notice_keeps_its_payload_with_payloads_off()
    {
        var provider = new FinishReasonModelProvider()
            .Text("The first half of a long ans", ChatFinishReason.Length, inputTokens: 30, outputTokens: 50);

        await using var host = await OutputTruncationTests.StartAsync(
            provider,
            OutputTruncationTests.PlainText(maxOutputTokens: 50),
            static services => services.Configure<TraconOptions>(static options => options.RunRecording.RecordToolPayloads = false));

        using var response = await OutputTruncationTests.PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var run = await OutputTruncationTests.SingleRunAsync(host);
        var notice = (await OutputTruncationTests.EventsAsync(host, run.Id))
            .Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.OutputTruncated, StringComparison.Ordinal))
            .ShouldHaveSingleItem();

        notice.Payload.ShouldNotBeNull();
        notice.Payload.ShouldContain("\"provider\":\"scripted\"");
    }

    private static ContentModelProvider Scripted() => new(
    [
        new WebSearchToolCallContent("call-1") { Queries = ["private query text"] },
        new ErrorContent("The provider is rate limiting this key.") { ErrorCode = "rate_limited" },
        new DataContent(Pixels, "image/png"),
        new TextContent("the answer"),
    ]);

    private static List<RunEvent> ContentEvents(IEnumerable<RunEvent> events)
        => [.. events.Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.Content, StringComparison.Ordinal))];

    private static async Task RunAsync(TraconTestHost host, bool streaming)
    {
        if (streaming)
        {
            await OutputTruncationTests.PostStreamingAsync(host);

            return;
        }

        using var response = await OutputTruncationTests.PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Task<TraconTestHost> StartAsync(
        ContentModelProvider provider,
        Action<IServiceCollection>? configureServices = null,
        Action<ITraconBuilder>? configureTracon = null)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider).AddAgent(OutputTruncationTests.PlainText(maxOutputTokens: null));
                configureTracon?.Invoke(builder);
            },
            configureServices: configureServices);
}
