using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// F-589 (phase 223): which model content becomes a <see cref="RunEventCustomTypes.Content"/>
/// event and which does not. Content Tracon recognizes but records elsewhere (or by option,
/// not at all) must never fall through to the "unrecognized" record.
/// </summary>
public sealed class ContentEventClassificationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Text_with_message_deltas_off_is_recognized_and_records_nothing(bool streaming)
    {
        // 🚨 A `when` clause that fails sends the content to `default`; the first draft
        // of this change recorded every text as unrecognized content with the option off.
        var events = await RunAsync(
            streaming,
            [new TextContent("the answer")],
            new TraconRunRecordingOptions { RecordMessageDeltas = false });

        events.ShouldNotContain(static e => e.Type == RunEventType.MessageDelta);
        ContentEvents(events).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reasoning_with_the_option_off_is_recognized_and_records_nothing(bool streaming)
    {
        var events = await RunAsync(
            streaming,
            [new TextReasoningContent("thinking it through"), new TextContent("the answer")],
            new TraconRunRecordingOptions());

        events.ShouldNotContain(static e => e.Type == RunEventType.ReasoningDelta);
        ContentEvents(events).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_text_and_empty_reasoning_are_recognized_and_record_nothing(bool streaming)
    {
        var events = await RunAsync(
            streaming,
            [new TextContent(string.Empty), new TextReasoningContent(string.Empty), new TextContent("the answer")],
            new TraconRunRecordingOptions { RecordReasoningDeltas = true });

        events.Count(static e => e.Type == RunEventType.MessageDelta).ShouldBe(1);
        events.ShouldNotContain(static e => e.Type == RunEventType.ReasoningDelta);
        ContentEvents(events).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Usage_content_is_accounted_for_by_the_run_and_records_no_content_event(bool streaming)
    {
        var events = await RunAsync(
            streaming,
            [new TextContent("the answer"), new UsageContent(new UsageDetails { InputTokenCount = 3, OutputTokenCount = 4 })],
            new TraconRunRecordingOptions());

        ContentEvents(events).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_approval_request_is_a_pending_request_not_a_content_event(bool streaming)
    {
        var request = new ToolApprovalRequestContent(
            "req-1",
            new FunctionCallContent("call-1", "delete_order", new Dictionary<string, object?>(StringComparer.Ordinal)));

        var events = await RunAsync(streaming, [request], new TraconRunRecordingOptions());

        events.ShouldContain(static e => e.Type == RunEventType.RunAwaitingInput);
        ContentEvents(events).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Approval_responses_including_the_always_approve_form_record_no_content_event(bool streaming)
    {
        var call = new FunctionCallContent("call-1", "delete_order", new Dictionary<string, object?>(StringComparer.Ordinal));
        var request = new ToolApprovalRequestContent("req-1", call);

        var events = await RunAsync(
            streaming,
            [
                new TextContent("the answer"),
                request.CreateResponse(approved: true, reason: "ok"),
                request.CreateAlwaysApproveToolResponse(reason: "ok"),
            ],
            new TraconRunRecordingOptions());

        ContentEvents(events).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unrecognized_content_is_recorded_once_each_in_the_order_it_arrived(bool streaming)
    {
        var events = await RunAsync(
            streaming,
            [
                new TextContent("before"),
                new WebSearchToolCallContent("call-1"),
                new ErrorContent("boom") { ErrorCode = "rate_limited" },
                new TextContent("after"),
            ],
            new TraconRunRecordingOptions());

        var interesting = events
            .Where(static e => e.Type is RunEventType.MessageDelta or RunEventType.Custom
                && !string.Equals(e.CustomType, RunEventCustomTypes.Context, StringComparison.Ordinal))
            .Select(static e => e.Text)
            .ToList();

        interesting.ShouldBe(["before", "web_search call", "error: rate_limited", "after"]);

        var recorded = ContentEvents(events);
        recorded.Count.ShouldBe(2);
        recorded.ShouldAllBe(static e => e.Type == RunEventType.Custom);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_content_event_payload_is_written_while_payloads_are_recorded(bool streaming)
    {
        var events = await RunAsync(
            streaming,
            [new ErrorContent("boom") { ErrorCode = "rate_limited" }],
            new TraconRunRecordingOptions());

        var notice = ContentEvents(events).ShouldHaveSingleItem();

        notice.Payload.ShouldNotBeNull();
        notice.Payload.ShouldContain("\"type\":\"error\"");
        notice.Payload.ShouldContain("\"message\":\"boom\"");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_content_event_keeps_its_text_and_loses_its_payload_with_payloads_off(bool streaming)
    {
        var events = await RunAsync(
            streaming,
            [new ErrorContent("boom") { ErrorCode = "rate_limited" }],
            new TraconRunRecordingOptions { RecordToolPayloads = false });

        var notice = ContentEvents(events).ShouldHaveSingleItem();

        notice.Text.ShouldBe("error: rate_limited");
        notice.Payload.ShouldBeNull();
    }

    private static List<RunEvent> ContentEvents(IEnumerable<RunEvent> events)
        => [.. events.Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.Content, StringComparison.Ordinal))];

    private static async Task<List<RunEvent>> RunAsync(
        bool streaming,
        IReadOnlyList<AIContent> contents,
        TraconRunRecordingOptions options)
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var client = streaming
            ? new FakeChatClient(streamingUpdates: [.. contents.Select(static c => new ChatResponseUpdate(ChatRole.Assistant, [c]))])
            : new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, [.. contents])));
        var agent = CreateAgent(store, client, options);

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

    private static RunRecordingAgent CreateAgent(
        IRunStore store,
        FakeChatClient client,
        TraconRunRecordingOptions options)
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry());

        return new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            new FixedTenantContext(),
            options,
            NullLogger<RunRecordingAgent>.Instance);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }
}
