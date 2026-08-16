namespace AgentPrism.Core.UnitTests.Voice;

/// <summary>
/// Verifies that streaming text is split into speakable chunks.
/// </summary>
/// <remarks>
/// This split is the source of latency: the earlier the first chunk comes out,
/// the earlier the user hears audio.
/// </remarks>
public sealed class VoiceSpeechSegmenterTests
{
    [Fact]
    public void Chunk_is_produced_at_end_of_sentence()
    {
        var segmenter = new VoiceSpeechSegmenter();

        segmenter.Append("Hello, how").ShouldBeEmpty();

        var ready = segmenter.Append(" can I help you? Next sentence");

        ready.Count.ShouldBe(1);
        ready[0].ShouldBe("Hello, how can I help you?");
    }

    [Fact]
    public void Sentence_end_character_AT_END_OF_TEXT_is_awaited()
    {
        // "3." or an abbreviation also ends with a period; without the next
        // character arriving, it cannot be known whether it is a sentence end.
        var segmenter = new VoiceSpeechSegmenter();

        segmenter.Append("Your order number is 12345.").ShouldBeEmpty();
        segmenter.Append(" It has shipped.").Count.ShouldBe(1);
    }

    [Fact]
    public void Very_short_chunk_is_merged_with_the_NEXT_one()
    {
        // "Yes." spoken alone sounds choppy.
        var segmenter = new VoiceSpeechSegmenter();

        segmenter.Append("Yes. ").ShouldBeEmpty();

        var ready = segmenter.Append("Your order is ready. ");

        ready.Count.ShouldBe(1);
        ready[0].ShouldBe("Yes. Your order is ready.");
    }

    [Fact]
    public void Splits_at_length_limit_when_punctuation_never_arrives()
    {
        // A model that does not use punctuation would otherwise never split
        // and would wait for the end of the response for the first audio.
        var segmenter = new VoiceSpeechSegmenter();
        var ready = segmenter.Append(string.Join(' ', Enumerable.Repeat("word", 80)));

        ready.ShouldNotBeEmpty();
        ready[0].Length.ShouldBeLessThanOrEqualTo(240);
        ready[0].ShouldEndWith("word");
    }

    [Fact]
    public void Flush_returns_the_remainder_and_empties_the_buffer()
    {
        var segmenter = new VoiceSpeechSegmenter();

        segmenter.Append("An unfinished sentence");

        segmenter.Flush().ShouldBe("An unfinished sentence");
        segmenter.Flush().ShouldBeNull();
    }

    [Fact]
    public void Line_break_is_also_a_chunk_boundary()
    {
        var segmenter = new VoiceSpeechSegmenter();
        var ready = segmenter.Append("This is the first item\nSecond item");

        ready.Count.ShouldBe(1);
        ready[0].ShouldBe("This is the first item");
    }

    [Fact]
    public void Empty_chunk_is_ignored()
    {
        var segmenter = new VoiceSpeechSegmenter();

        segmenter.Append(null).ShouldBeEmpty();
        segmenter.Append(string.Empty).ShouldBeEmpty();
        segmenter.Flush().ShouldBeNull();
    }
}
