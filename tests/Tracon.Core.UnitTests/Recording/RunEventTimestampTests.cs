using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// An event keeps the time its draft names: a tool call written from the tool-call ledger when the run closes
/// is stamped with the time it ran, and every other event is stamped as it always was.
/// </summary>
public sealed class RunEventTimestampTests
{
    [Fact]
    public async Task A_draft_with_a_time_keeps_it_in_the_event_and_in_the_store()
    {
        var store = new InMemoryRunStore();
        var writer = await StartAsync(store, new ManualTimeProvider());
        var ran = new DateTimeOffset(2026, 10, 27, 9, 15, 30, TimeSpan.Zero) + TimeSpan.FromTicks(1234);

        var runEvent = await writer.AppendAsync(new RunEventDraft(RunEventType.ToolInvoking) { ToolName = "lookup", Timestamp = ran });

        // Whole microseconds, the finest precision every store keeps.
        var expected = new DateTimeOffset(2026, 10, 27, 9, 15, 30, TimeSpan.Zero) + TimeSpan.FromTicks(1230);

        runEvent.Timestamp.ShouldBe(expected);

        var stored = await ReadAsync(store, writer.RunId);

        stored.Single(static e => e.Type == RunEventType.ToolInvoking).Timestamp.ShouldBe(expected);
    }

    [Fact]
    public async Task A_draft_without_a_time_is_stamped_by_the_writers_clock()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero));
        var writer = await StartAsync(new InMemoryRunStore(), clock);

        clock.Advance(TimeSpan.FromMinutes(5));

        var runEvent = await writer.AppendAsync(new RunEventDraft(RunEventType.ToolInvoked) { ToolName = "lookup" });

        runEvent.Timestamp.ShouldBe(clock.GetUtcNow());
    }

    [Fact]
    public async Task The_time_of_a_draft_does_not_change_the_order_of_the_events()
    {
        // Order is the sequence number; the time is the event's own. A later event may carry an earlier time.
        var writer = await StartAsync(new InMemoryRunStore(), new ManualTimeProvider());

        var late = await writer.AppendAsync(new RunEventDraft(RunEventType.ToolInvoked) { ToolName = "a" });
        var early = await writer.AppendAsync(new RunEventDraft(RunEventType.ToolInvoking)
        {
            ToolName = "b",
            Timestamp = late.Timestamp - TimeSpan.FromHours(1),
        });

        early.Sequence.ShouldBe(late.Sequence + 1);
        early.Timestamp.ShouldBeLessThan(late.Timestamp);
    }

    private static async Task<RunEventWriter> StartAsync(InMemoryRunStore store, TimeProvider clock)
    {
        var writer = new RunEventWriter(
            store,
            new TraconRunRecordingOptions(),
            NullLogger.Instance,
            TraconId.NewId(),
            metrics: null,
            sinks: null,
            clock);

        await writer.StartAsync(
            new RunStartInfo { RunId = writer.RunId, AgentName = "test-agent", StartedAt = clock.GetUtcNow() },
            query: "hello");

        return writer;
    }

    private static async Task<List<RunEvent>> ReadAsync(InMemoryRunStore store, Guid runId)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in store.ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        return events;
    }
}
