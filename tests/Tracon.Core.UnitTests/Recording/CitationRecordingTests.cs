using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// Which annotations of a model response become a
/// <see cref="RunEventCustomTypes.Citation"/> event, and how many, through the run recorder on both of
/// its paths.
/// </summary>
public sealed class CitationRecordingTests
{
    private static CitationAnnotation Notes() => new()
    {
        Title = "Release notes",
        Url = new Uri("https://example.com/notes?sig=SECRET"),
        ToolName = "web_search",
        Snippet = "Ships on Friday.",
        AnnotatedRegions = [new TextSpanAnnotatedRegion { StartIndex = 0, EndIndex = 10 }],
    };

    private static TextContent Cited(string text, params AIAnnotation[] annotations)
        => new(text) { Annotations = [.. annotations] };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_citation_on_the_answer_is_one_citation_event(bool streaming)
    {
        var events = await RunAsync(streaming, [[Cited("The release ships on Friday.", Notes())]], new TraconRunRecordingOptions());

        var citation = CitationEvents(events).ShouldHaveSingleItem();

        citation.Type.ShouldBe(RunEventType.Custom);
        citation.Text.ShouldBe("citation: web_search");
        citation.Payload.ShouldNotBeNull();
        citation.Payload.ShouldContain("\"title\":\"Release notes\"");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_citation_is_recorded_while_message_text_is_not(bool streaming)
    {
        // A citation is not message text: the option that turns the text off must not take it along,
        // and it must not fall to the "unrecognized content" record either.
        var events = await RunAsync(
            streaming,
            [[Cited("The release ships on Friday.", Notes())]],
            new TraconRunRecordingOptions { RecordMessageDeltas = false });

        events.ShouldNotContain(static e => e.Type == RunEventType.MessageDelta);
        ContentEvents(events).ShouldBeEmpty();
        CitationEvents(events).ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_citation_on_content_other_than_text_is_recorded(bool streaming)
    {
        // The annotation sits on AIContent, the base type: looking at TextContent alone misses this.
        var reasoning = new TextReasoningContent("thinking") { Annotations = [Notes()] };

        var events = await RunAsync(streaming, [[reasoning, new TextContent("the answer")]], new TraconRunRecordingOptions());

        CitationEvents(events).ShouldHaveSingleItem();
        events.ShouldNotContain(static e => e.Type == RunEventType.ReasoningDelta);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Each_citation_of_a_response_is_an_event_in_the_order_it_arrived(bool streaming)
    {
        var first = new CitationAnnotation { Title = "A", Url = new Uri("https://a.example/") };
        var second = new CitationAnnotation { Title = "B", Url = new Uri("https://b.example/") };

        var events = await RunAsync(
            streaming,
            [[Cited("one", first), Cited("two", second)]],
            new TraconRunRecordingOptions());

        CitationEvents(events).Select(static e => Title(e)).ShouldBe(["A", "B"]);
    }

    [Fact]
    public async Task A_stream_that_shows_one_citation_in_two_updates_records_it_once()
    {
        // The content part is shown again when it closes: the same annotation, the same message.
        var events = await RunAsync(
            streaming: true,
            [[Cited("The release ships on Friday.", Notes()), Cited(string.Empty, Notes())]],
            new TraconRunRecordingOptions(),
            messageIds: ["m1", "m1"]);

        CitationEvents(events).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_stream_whose_updates_name_no_message_is_one_message()
    {
        var events = await RunAsync(
            streaming: true,
            [[Cited("The release ships on Friday.", Notes()), Cited(string.Empty, Notes())]],
            new TraconRunRecordingOptions());

        CitationEvents(events).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Two_messages_that_cite_the_same_source_are_two_citations_in_a_stream()
    {
        // Two turns of the tool loop can cite the same page at the same place; each is a citation.
        var events = await RunAsync(
            streaming: true,
            [[Cited("first answer", Notes()), Cited("second answer", Notes())]],
            new TraconRunRecordingOptions(),
            messageIds: ["m1", "m2"]);

        CitationEvents(events).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Two_messages_that_cite_the_same_source_are_two_citations_in_a_buffered_response()
    {
        var events = await RunBufferedMessagesAsync(
            [
                new ChatMessage(ChatRole.Assistant, [Cited("first answer", Notes())]) { MessageId = "m1" },
                new ChatMessage(ChatRole.Assistant, [Cited("second answer", Notes())]) { MessageId = "m2" },
            ],
            new TraconRunRecordingOptions());

        CitationEvents(events).Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_passages_of_one_page_are_two_citations(bool streaming)
    {
        var first = new CitationAnnotation { Url = new Uri("https://example.com/p"), Snippet = "first passage" };
        var second = new CitationAnnotation { Url = new Uri("https://example.com/p"), Snippet = "second passage" };

        var events = await RunAsync(streaming, [[Cited("answer", first, second)]], new TraconRunRecordingOptions());

        CitationEvents(events).Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_buffered_and_the_streamed_path_record_the_same_events_for_the_same_response()
    {
        AIContent[][] turns = [[Cited("a", Notes()), Cited("b", new CitationAnnotation { FileId = "file-9", ToolName = "file_search" })]];

        var buffered = await RunAsync(streaming: false, turns, new TraconRunRecordingOptions());
        var streamed = await RunAsync(streaming: true, turns, new TraconRunRecordingOptions());

        CitationEvents(streamed).Select(static e => (e.Text, e.Payload))
            .ShouldBe(CitationEvents(buffered).Select(static e => (e.Text, e.Payload)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task With_payloads_off_the_event_stays_with_its_text_and_loses_its_payload(bool streaming)
    {
        var events = await RunAsync(
            streaming,
            [[Cited("answer", Notes())]],
            new TraconRunRecordingOptions { RecordToolPayloads = false });

        var citation = CitationEvents(events).ShouldHaveSingleItem();

        citation.Text.ShouldBe("citation: web_search");
        citation.Payload.ShouldBeNull("a title and a snippet come from a source and can be personal data");
    }

    [Theory]
    [InlineData(false, "https://example.com")]
    [InlineData(true, "https://example.com/notes?sig=SECRET")]
    public async Task The_address_follows_the_url_option(bool recordUrls, string expected)
    {
        var events = await RunAsync(
            streaming: false,
            [[Cited("answer", Notes())]],
            new TraconRunRecordingOptions { RecordCitationUrls = recordUrls });

        using var payload = JsonDocument.Parse(CitationEvents(events).ShouldHaveSingleItem().Payload!);

        payload.RootElement.GetProperty("url").GetString().ShouldBe(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_annotation_that_is_not_a_citation_records_nothing(bool streaming)
    {
        var events = await RunAsync(streaming, [[Cited("answer", new UnknownAnnotation())]], new TraconRunRecordingOptions());

        CitationEvents(events).ShouldBeEmpty();
        ContentEvents(events).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_response_without_citations_records_no_citation_event(bool streaming)
    {
        var events = await RunAsync(streaming, [[new TextContent("plain answer")]], new TraconRunRecordingOptions());

        CitationEvents(events).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_canceled_after_a_citation_keeps_the_citation_and_ends_Canceled()
    {
        // An earlier fix sibling: the event is written with the caller's token, so a cancel that lands while the
        // model is silent ends the run as Canceled, with what was recorded before it still recorded.
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var registry = new RunCancellationRegistry();
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(new CitedThenSilentChatClient(Notes()))),
            TestData.Registry());
        var agent = new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            new FixedTenantContext(),
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            cancellationRegistry: registry);

        var runTask = Task.Run(async () =>
        {
            await foreach (var _ in agent.RunStreamingAsync("hello"))
            {
                // keeps consuming like a client; the second update never comes
            }
        });

        await WaitUntil.TrueAsync(async () =>
        {
            var runs = await store.QueryRunsAsync(new RunQuery());

            return runs.Count == 1 && CitationEvents(await ReadEventsAsync(store, runs[0].Id)).Count == 1;
        });

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        registry.TryCancel(run.Id, "test").ShouldBeTrue();

        await Should.ThrowAsync<OperationCanceledException>(async () => await runTask);

        (await store.GetRunAsync(run.Id))!.Status.ShouldBe(RunStatus.Canceled);
        CitationEvents(await ReadEventsAsync(store, run.Id)).ShouldHaveSingleItem();
    }

    private static async Task<List<RunEvent>> ReadEventsAsync(InMemoryRunStore store, Guid runId)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in store.ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        return events;
    }

    /// <summary>Streams one cited update, then stays silent until the caller's token is canceled.</summary>
    private sealed class CitedThenSilentChatClient(CitationAnnotation citation) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The test streams.");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("cited") { Annotations = [citation] }]);

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false); // delay: simulated
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing to release.
        }
    }

    private static string? Title(RunEvent e)
    {
        using var payload = JsonDocument.Parse(e.Payload!);

        return payload.RootElement.GetProperty("title").GetString();
    }

    private static List<RunEvent> CitationEvents(IEnumerable<RunEvent> events)
        => [.. events.Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.Citation, StringComparison.Ordinal))];

    private static List<RunEvent> ContentEvents(IEnumerable<RunEvent> events)
        => [.. events.Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.Content, StringComparison.Ordinal))];

    private static Task<List<RunEvent>> RunBufferedMessagesAsync(IReadOnlyList<ChatMessage> messages, TraconRunRecordingOptions options)
        => RunClientAsync(new FakeChatClient(_ => new ChatResponse([.. messages])), streaming: false, options);

    /// <summary>Runs one turn whose model answer is <paramref name="turns"/>[0], on the chosen path.</summary>
    private static Task<List<RunEvent>> RunAsync(
        bool streaming,
        AIContent[][] turns,
        TraconRunRecordingOptions options,
        string?[]? messageIds = null)
    {
        var contents = turns[0];
        var client = streaming
            ? new FakeChatClient(streamingUpdates:
                [.. contents.Select((c, i) => new ChatResponseUpdate(ChatRole.Assistant, [c]) { MessageId = messageIds?[i] })])
            : new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, [.. contents])));

        return RunClientAsync(client, streaming, options);
    }

    private static async Task<List<RunEvent>> RunClientAsync(FakeChatClient client, bool streaming, TraconRunRecordingOptions options)
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry());
        var agent = new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            new FixedTenantContext(),
            options,
            NullLogger<RunRecordingAgent>.Instance);

        if (streaming)
        {
            await foreach (var _ in agent.RunStreamingAsync("hello"))
            {
                // drain the stream
            }
        }
        else
        {
            await agent.RunAsync("hello");
        }

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var events = new List<RunEvent>();

        await foreach (var runEvent in store.ReadEventsAsync(run.Id))
        {
            events.Add(runEvent);
        }

        return events;
    }

    private sealed class UnknownAnnotation : AIAnnotation;

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }
}
