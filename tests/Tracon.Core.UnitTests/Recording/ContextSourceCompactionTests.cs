using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// F-773 (phase 267): the context event of a run whose history the summarizer compacts. The summarization
/// call runs BEFORE the agent's first model call, through the real <c>SideChannelUsageTrackingChatClient</c>;
/// the event must count the agent's request and not the summarizer's.
/// </summary>
public sealed class ContextSourceCompactionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_summarizer_runs_first_and_the_event_still_counts_the_agents_own_request(bool streaming)
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var mainClient = new FakeChatClient();
        var summarizerClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary")));
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(
                new FakeModelProvider(mainClient, name: "fake"),
                new FakeModelProvider(summarizerClient, name: "summarizer")),
            TestData.Registry());

        var agent = new RunRecordingAgent(
            compiler.Compile(TestData.Definition() with
            {
                Compaction = new CompactionSettings
                {
                    Strategy = CompactionStrategyKind.Summarization,
                    TriggerMessages = 2,
                    MinimumPreservedGroups = 1,
                    SummarizationModel = new ModelBinding { Provider = "summarizer", Model = "summarizer-model" },
                },
            }),
            store,
            new FixedTenantContext(),
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance);

        var history = Enumerable.Range(0, 12)
            .Select(static i => new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, $"message {i}"))
            .ToList();

        if (streaming)
        {
            await foreach (var _ in agent.RunStreamingAsync(history))
            {
                // drain the stream
            }
        }
        else
        {
            await agent.RunAsync(history);
        }

        // The side channel really ran, before the agent's own call.
        summarizerClient.CallCount.ShouldBeGreaterThan(0);
        mainClient.CallCount.ShouldBe(1);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var events = new List<RunEvent>();

        await foreach (var runEvent in store.ReadEventsAsync(run.Id))
        {
            events.Add(runEvent);
        }

        var notice = events
            .Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.Context, StringComparison.Ordinal))
            .ShouldHaveSingleItem();

        // It counts what the agent's model was sent (the compacted history), not what the summarizer was.
        notice.Text.ShouldBe($"context: {mainClient.LastRequest.Count} messages");
        mainClient.LastRequest.Count.ShouldNotBe(summarizerClient.LastRequest.Count, "the two requests must differ for this to prove anything");

        // Measured: the compaction runs inside the agent before its client, so the ring sees the compacted
        // request. The summary message the compaction wrote counts as ChatHistory (the framework attributes
        // it to the stored conversation) and the messages it kept stay the caller's own.
        using var payload = JsonDocument.Parse(notice.Payload!);
        var sources = payload.RootElement.GetProperty("sources").EnumerateArray()
            .Select(static s => $"{s.GetProperty("type").GetString()}={s.GetProperty("count").GetInt32()}")
            .ToList();

        sources.ShouldBe(["ChatHistory=1", $"External={mainClient.LastRequest.Count - 1}"]);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }
}
