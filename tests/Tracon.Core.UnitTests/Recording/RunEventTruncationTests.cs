using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// An event text or payload cut at <c>MaxPayloadLength</c> is still text.
/// </summary>
/// <remarks>
/// The writer cut by character count. A cut between the two halves of a surrogate pair left a lone
/// high surrogate in the stored event: Npgsql refuses to encode it, so the append failed, the writer
/// gave up on the run's recording, and every later event of the run was lost.
/// </remarks>
public sealed class RunEventTruncationTests
{
    private const string Pair = "\U0001F600";

    [Fact]
    public async Task A_text_cut_between_the_halves_of_a_surrogate_pair_is_stored_without_the_pair()
    {
        var store = new InMemoryRunStore();
        var writer = await StartAsync(store, maxPayloadLength: 10);

        // Nine characters, then a pair: the tenth character kept would be the high half.
        var text = "123456789" + Pair + "tail";

        await writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = "test.cut", Text = text, Payload = text });

        var stored = await ReadAsync(store, writer.RunId);
        var cut = stored.Single(static e => string.Equals(e.CustomType, "test.cut", StringComparison.Ordinal));

        cut.Text.ShouldBe("123456789" + RunEventWriter.TruncationMarker);
        cut.Payload.ShouldBe("123456789" + RunEventWriter.TruncationMarker);
        ContentEventPayload.Sanitize(cut.Text).ShouldBeSameAs(cut.Text);
        ContentEventPayload.Sanitize(cut.Payload).ShouldBeSameAs(cut.Payload);
    }

    [Fact]
    public async Task A_text_whose_cut_follows_a_whole_pair_keeps_the_pair()
    {
        var store = new InMemoryRunStore();
        var writer = await StartAsync(store, maxPayloadLength: 10);

        // Eight characters and a pair end exactly at the limit.
        var text = "12345678" + Pair + "tail";

        await writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = "test.cut", Text = text });

        var cut = (await ReadAsync(store, writer.RunId)).Single(static e => string.Equals(e.CustomType, "test.cut", StringComparison.Ordinal));

        cut.Text.ShouldBe("12345678" + Pair + RunEventWriter.TruncationMarker);
    }

    private static async Task<RunEventWriter> StartAsync(InMemoryRunStore store, int maxPayloadLength)
    {
        var writer = new RunEventWriter(
            store,
            new TraconRunRecordingOptions { MaxPayloadLength = maxPayloadLength },
            NullLogger.Instance,
            TraconId.NewId(),
            metrics: null);

        await writer.StartAsync(
            new RunStartInfo { RunId = writer.RunId, AgentName = "test-agent", StartedAt = DateTimeOffset.UtcNow },
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
