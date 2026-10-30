using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using TraconTestHost = Tracon.AspNetCore.FunctionalTests.Infrastructure.TraconTestHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-773 (phase 267) — the citations of a model response reach the run record, through a real DI-built
/// host and both the buffered and the streaming path. The payload rules are covered at unit level in
/// <c>CitationEventPayloadTests</c> and <c>CitationRecordingTests</c>; the options binding, the store, the
/// HTTP read model and the live stream are only real here.
/// </summary>
public sealed class CitationRecordingTests
{
    private const string SignedAddress = "https://store.example.com/reports/q3.pdf?sig=SECRETSIGNATURE";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Each_citation_is_one_event_that_names_the_host_and_never_the_path_or_query(bool streaming)
    {
        await using var host = await StartAsync(Scripted());

        await RunAsync(host, streaming);

        var run = await OutputTruncationTests.SingleRunAsync(host);
        var events = await OutputTruncationTests.EventsAsync(host, run.Id);
        var citations = CitationEvents(events);

        run.Status.ShouldBe(RunStatus.Completed);
        citations.Select(static e => e.Text).ShouldBe(["citation: web_search", "citation: file_search"]);

        using var web = JsonDocument.Parse(citations[0].Payload!);
        web.RootElement.GetProperty("url").GetString().ShouldBe("https://store.example.com");
        web.RootElement.GetProperty("title").GetString().ShouldBe("Q3 report");
        web.RootElement.GetProperty("start").GetInt32().ShouldBe(0);
        web.RootElement.GetProperty("end").GetInt32().ShouldBe(10);

        using var file = JsonDocument.Parse(citations[1].Payload!);
        file.RootElement.GetProperty("fileId").GetString().ShouldBe("file-42");
        file.RootElement.TryGetProperty("url", out _).ShouldBeFalse();

        // 🚨 Nowhere in the record: not in a payload, not in a summary.
        events.ShouldAllBe(static e => !(e.Payload ?? string.Empty).Contains("SECRETSIGNATURE", StringComparison.Ordinal)
            && !(e.Text ?? string.Empty).Contains("SECRETSIGNATURE", StringComparison.Ordinal));
        events.ShouldNotContain(static e => string.Equals(e.CustomType, RunEventCustomTypes.Content, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task With_the_option_on_the_whole_address_is_recorded(bool streaming)
    {
        await using var host = await StartAsync(
            Scripted(),
            static services => services.Configure<TraconOptions>(static options => options.RunRecording.RecordCitationUrls = true));

        await RunAsync(host, streaming);

        var run = await OutputTruncationTests.SingleRunAsync(host);
        var citation = CitationEvents(await OutputTruncationTests.EventsAsync(host, run.Id))[0];

        using var payload = JsonDocument.Parse(citation.Payload!);
        payload.RootElement.GetProperty("url").GetString().ShouldBe(SignedAddress);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task With_message_deltas_off_the_citations_stay(bool streaming)
    {
        await using var host = await StartAsync(
            Scripted(),
            static services => services.Configure<TraconOptions>(static options => options.RunRecording.RecordMessageDeltas = false));

        await RunAsync(host, streaming);

        var run = await OutputTruncationTests.SingleRunAsync(host);
        var events = await OutputTruncationTests.EventsAsync(host, run.Id);

        events.ShouldNotContain(static e => e.Type == RunEventType.MessageDelta);
        CitationEvents(events).Count.ShouldBe(2);
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
        var citations = CitationEvents(await OutputTruncationTests.EventsAsync(host, run.Id));

        citations.Select(static e => e.Text).ShouldBe(["citation: web_search", "citation: file_search"]);
        citations.ShouldAllBe(static e => e.Payload == null);
    }

    [Fact]
    public async Task The_live_stream_does_not_carry_the_citation_events()
    {
        // K-991: the live run stream mirrors a closed list of notices. A citation is read from the
        // record, not pushed to every caller of the stream.
        await using var host = await StartAsync(Scripted());

        var frames = await OutputTruncationTests.PostStreamingAsync(host);

        frames.ShouldNotContain(static frame => frame.Data.Contains(RunEventCustomTypes.Citation, StringComparison.Ordinal));
        frames.ShouldContain(static frame => string.Equals(frame.Event, "done", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_store_that_refuses_the_citation_event_does_not_fail_the_response()
    {
        await using var host = await StartAsync(
            Scripted(),
            configureTracon: static builder => builder.Services.ThrowFrom<IRunStore>(
                nameof(IRunStore.AppendEventAsync),
                static () => new InvalidOperationException("the event table is down"),
                when: static arguments => arguments[0] is RunEvent { CustomType: RunEventCustomTypes.Citation }));

        using var response = await OutputTruncationTests.PostBufferedAsync(host);

        // Observability does not break functionality: the caller still gets the answer.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("the answer", Case.Sensitive);

        // Read from the store, not over HTTP: the writer gave up on this run, so the run row is never
        // closed and the events endpoint would wait for it to end.
        var store = host.Services.GetRequiredService<IRunStore>();
        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var events = new List<RunEvent>();

        await foreach (var runEvent in store.ReadEventsAsync(run.Id))
        {
            events.Add(runEvent);
        }

        CitationEvents(events).ShouldBeEmpty();
    }

    private static ContentModelProvider Scripted() => new(
    [
        new TextContent("The report says revenue grew.")
        {
            Annotations =
            [
                new CitationAnnotation
                {
                    Title = "Q3 report",
                    Url = new Uri(SignedAddress),
                    ToolName = "web_search",
                    AnnotatedRegions = [new TextSpanAnnotatedRegion { StartIndex = 0, EndIndex = 10 }],
                },
            ],
        },
        new TextContent("the answer")
        {
            Annotations = [new CitationAnnotation { FileId = "file-42", ToolName = "file_search" }],
        },
    ]);

    private static List<RunEvent> CitationEvents(IEnumerable<RunEvent> events)
        => [.. events.Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.Citation, StringComparison.Ordinal))];

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
