using System.Text;
using Tracon.CapacityDriver;

namespace Tracon.Capacity.Tests;

/// <summary>The SSE reader, including the truncation it must report rather than swallow.</summary>
public sealed class CapacityStreamReaderTests
{
    private static MemoryStream Body(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Frames_are_read_in_order_with_their_ids_and_names()
    {
        var reader = new SseFrameReader(System.Diagnostics.Stopwatch.GetTimestamp());

        var frames = new List<SseFrame>();

        await foreach (var frame in reader.ReadAsync(Body(
            "id: 0\nevent: run\ndata: {\"runId\":\"abc\"}\n\n" +
            "id: 1\nevent: update\ndata: {\"text\":\"hi\"}\n\n")))
        {
            frames.Add(frame);
        }

        frames.Count.ShouldBe(2);
        frames[0].Id.ShouldBe("0");
        frames[0].EventName.ShouldBe("run");
        frames[0].Data.ShouldBe("{\"runId\":\"abc\"}");
        frames[1].EventName.ShouldBe("update");
        reader.TruncatedFrame.ShouldBeFalse();
    }

    [Fact]
    public async Task A_body_that_ends_mid_frame_is_reported_as_truncated_and_the_partial_frame_is_not_yielded()
    {
        // 🚨 A lost final frame is one of the failure modes this apparatus
        // exists to catch. Yielding the partial frame would make it look like
        // a clean end of stream.
        var reader = new SseFrameReader(System.Diagnostics.Stopwatch.GetTimestamp());
        var frames = new List<SseFrame>();

        await foreach (var frame in reader.ReadAsync(Body(
            "id: 0\nevent: update\ndata: one\n\nid: 1\nevent: done\ndata: two")))
        {
            frames.Add(frame);
        }

        frames.Count.ShouldBe(1);
        reader.TruncatedFrame.ShouldBeTrue();
    }

    [Fact]
    public async Task Keep_alive_comments_are_not_counted_as_content()
    {
        var reader = new SseFrameReader(System.Diagnostics.Stopwatch.GetTimestamp());
        var frames = new List<SseFrame>();

        await foreach (var frame in reader.ReadAsync(Body(": waiting\n\nid: 0\nevent: update\ndata: one\n\n")))
        {
            frames.Add(frame);
        }

        frames.Count.ShouldBe(1);
        frames[0].EventName.ShouldBe("update");
        reader.TruncatedFrame.ShouldBeFalse();
    }

    [Fact]
    public async Task Multi_line_data_is_joined_the_way_the_protocol_says()
    {
        var reader = new SseFrameReader(System.Diagnostics.Stopwatch.GetTimestamp());
        var frames = new List<SseFrame>();

        await foreach (var frame in reader.ReadAsync(Body("event: update\ndata: one\ndata: two\n\n")))
        {
            frames.Add(frame);
        }

        frames.Single().Data.ShouldBe("one\ntwo");
    }

    [Fact]
    public async Task An_empty_body_yields_nothing_and_is_not_truncation()
    {
        var reader = new SseFrameReader(System.Diagnostics.Stopwatch.GetTimestamp());
        var count = 0;

        await foreach (var _ in reader.ReadAsync(Body("")))
        {
            count++;
        }

        count.ShouldBe(0);
        reader.TruncatedFrame.ShouldBeFalse();
    }
}
