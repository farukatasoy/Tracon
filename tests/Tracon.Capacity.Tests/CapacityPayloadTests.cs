using Tracon.Capacity;

namespace Tracon.Capacity.Tests;

/// <summary>The deterministic payload both sides of the measurement compute independently.</summary>
/// <remarks>
/// 🚨 If these two derivations ever drift, the reconciliation reports content
/// mismatches that are really a divergence in one linked file. These cases pin
/// the derivation itself.
/// </remarks>
public sealed class CapacityPayloadTests
{
    [Fact]
    public void A_request_message_is_exactly_the_requested_size_and_carries_the_correlation()
    {
        var message = CapacityPayload.RequestMessage("abc123", 1024);

        message.Length.ShouldBe(1024);
        CapacityPayload.ExtractCorrelation(message).ShouldBe("abc123");
    }

    [Fact]
    public void A_correlation_longer_than_the_message_is_truncated_rather_than_overflowing()
    {
        var message = CapacityPayload.RequestMessage(new string('x', 100), 20);

        message.Length.ShouldBe(20);
    }

    [Fact]
    public void A_message_without_a_marker_yields_no_correlation_rather_than_a_guess()
    {
        CapacityPayload.ExtractCorrelation("hello").ShouldBe("");
        CapacityPayload.ExtractCorrelation(null).ShouldBe("");
        CapacityPayload.ExtractCorrelation("").ShouldBe("");
    }

    [Fact]
    public void The_answer_is_deterministic_and_unique_per_correlation()
    {
        var first = CapacityPayload.Answer("aaa", 20, 256);
        var again = CapacityPayload.Answer("aaa", 20, 256);
        var other = CapacityPayload.Answer("bbb", 20, 256);

        first.Length.ShouldBe(20 * 256);
        string.Equals(first, again, StringComparison.Ordinal).ShouldBeTrue();
        string.Equals(first, other, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Every_chunk_carries_the_correlation_and_its_index()
    {
        var chunks = CapacityPayload.AnswerChunks("abc", 3, 32);

        chunks.Count.ShouldBe(3);
        chunks[0].ShouldStartWith("abc#0:");
        chunks[2].ShouldStartWith("abc#2:");
        chunks.ShouldAllBe(chunk => chunk.Length == 32);
    }

    [Fact]
    public void The_streamed_chunks_concatenate_to_the_buffered_answer()
    {
        // The two scenarios must be comparable on content: what streaming
        // assembles is byte-for-byte what buffering returns.
        var chunks = CapacityPayload.AnswerChunks("abc", 20, 256);

        string.Concat(chunks).ShouldBe(CapacityPayload.Answer("abc", 20, 256));
    }

    [Fact]
    public void The_tool_result_is_deterministic_and_carries_the_correlation()
    {
        var result = CapacityPayload.ToolResult("abc", 1024);

        result.Length.ShouldBe(1024);
        result.ShouldStartWith("probe:abc");
        result.ShouldBe(CapacityPayload.ToolResult("abc", 1024));
    }

    [Fact]
    public void A_zero_byte_tool_result_is_empty_rather_than_throwing()
        => CapacityPayload.ToolResult("abc", 0).ShouldBe("");

    [Fact]
    public void The_checksum_is_stable_and_distinguishes_answers()
    {
        var first = CapacityPayload.Checksum(CapacityPayload.Answer("aaa", 2, 16));
        var other = CapacityPayload.Checksum(CapacityPayload.Answer("bbb", 2, 16));

        first.Length.ShouldBe(64);
        string.Equals(first, CapacityPayload.Checksum(CapacityPayload.Answer("aaa", 2, 16)), StringComparison.Ordinal).ShouldBeTrue();
        string.Equals(first, other, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void An_empty_correlation_is_refused_rather_than_producing_a_payload_nothing_can_match()
    {
        Should.Throw<ArgumentException>(() => CapacityPayload.RequestMessage("", 100));
        Should.Throw<ArgumentException>(() => CapacityPayload.Answer("  ", 1, 1));
    }
}
