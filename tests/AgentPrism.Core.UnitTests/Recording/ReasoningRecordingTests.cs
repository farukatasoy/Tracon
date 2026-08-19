using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Recording;

/// <summary>
/// <see cref="RunEventType.ReasoningDelta"/> (phase 70): off by default, and when
/// on it never merges into <see cref="RunEventType.MessageDelta"/>.
/// </summary>
public sealed class ReasoningRecordingTests
{
    [Fact]
    public void RecordReasoningDeltas_defaults_to_false()
    {
        new AgentPrismRunRecordingOptions().RecordReasoningDeltas.ShouldBeFalse();
    }

    [Fact]
    public async Task Reasoning_content_is_not_recorded_when_the_option_is_off()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            [new TextReasoningContent("thinking it through"), new TextContent("the answer")])));
        var agent = CreateAgent(store, client, new AgentPrismRunRecordingOptions());

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var types = await ReadEventTypesAsync(store, run.Id);

        types.ShouldNotContain(RunEventType.ReasoningDelta);
    }

    [Fact]
    public async Task Reasoning_content_is_recorded_as_its_own_event_when_the_option_is_on()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            [new TextReasoningContent("thinking it through"), new TextContent("the answer")])));
        var options = new AgentPrismRunRecordingOptions { RecordReasoningDeltas = true };
        var agent = CreateAgent(store, client, options);

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var events = await ReadEventsAsync(store, run.Id);

        var reasoning = events.Where(static e => e.Type == RunEventType.ReasoningDelta).ShouldHaveSingleItem();
        reasoning.Text.ShouldBe("thinking it through");

        // Separate from the answer text — never merged into the same event.
        var messageDelta = events.Where(static e => e.Type == RunEventType.MessageDelta).ShouldHaveSingleItem();
        messageDelta.Text.ShouldBe("the answer");
    }

    [Fact]
    public async Task Streaming_reasoning_deltas_stay_separate_from_text_deltas_when_the_option_is_on()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var client = new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("step one")]),
            new ChatResponseUpdate(ChatRole.Assistant, "He"),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("step two")]),
            new ChatResponseUpdate(ChatRole.Assistant, "llo"),
        ]);
        var options = new AgentPrismRunRecordingOptions { RecordReasoningDeltas = true };
        var agent = CreateAgent(store, client, options);

        await foreach (var _ in agent.RunStreamingAsync("hi"))
        {
            // drain the stream
        }

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var events = await ReadEventsAsync(store, run.Id);

        var reasoningTexts = events.Where(static e => e.Type == RunEventType.ReasoningDelta)
            .Select(static e => e.Text)
            .ToList();
        var messageTexts = events.Where(static e => e.Type == RunEventType.MessageDelta)
            .Select(static e => e.Text)
            .ToList();

        reasoningTexts.ShouldBe(["step one", "step two"]);
        messageTexts.ShouldBe(["He", "llo"]);
    }

    [Fact]
    public async Task Streaming_reasoning_deltas_are_dropped_when_the_option_is_off()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var client = new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("step one")]),
            new ChatResponseUpdate(ChatRole.Assistant, "He"),
        ]);
        var agent = CreateAgent(store, client, new AgentPrismRunRecordingOptions());

        await foreach (var _ in agent.RunStreamingAsync("hi"))
        {
            // drain the stream
        }

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var types = await ReadEventTypesAsync(store, run.Id);

        types.ShouldNotContain(RunEventType.ReasoningDelta);
        types.ShouldContain(RunEventType.MessageDelta);
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

    private static async Task<List<RunEventType>> ReadEventTypesAsync(InMemoryRunStore store, Guid runId)
        => (await ReadEventsAsync(store, runId)).Select(static e => e.Type).ToList();

    private static RunRecordingAgent CreateAgent(
        IRunStore store,
        FakeChatClient client,
        AgentPrismRunRecordingOptions options)
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
