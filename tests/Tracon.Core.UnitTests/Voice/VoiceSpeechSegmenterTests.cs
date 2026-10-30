namespace Tracon.Core.UnitTests.Voice;

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
    public void A_segment_never_exceeds_the_limit_it_was_given()
    {
        // A synthesizer accepts at most MaxCharactersPerRequest characters.
        // The driver hands that limit to the segmenter; a smaller limit than the
        // 240-character default must hold for every segment, including the tail.
        var segmenter = new VoiceSpeechSegmenter(maxSegmentLength: 30);
        var segments = new List<string>(segmenter.Append(string.Join(' ', Enumerable.Repeat("word", 40))));

        if (segmenter.Flush() is { } tail)
        {
            segments.Add(tail);
        }

        segments.Count.ShouldBeGreaterThan(1);
        segments.ShouldAllBe(static segment => segment.Length <= 30);
        string.Join(' ', segments).ShouldBe(string.Join(' ', Enumerable.Repeat("word", 40)));
    }

    [Fact]
    public void A_sentence_longer_than_the_limit_is_cut_inside_the_sentence()
    {
        // The sentence end arrives far past the limit in ONE chunk. The cut must
        // not wait for the sentence end: that segment would reach the synthesizer
        // longer than the limit.
        var segmenter = new VoiceSpeechSegmenter(maxSegmentLength: 40);
        var sentence = string.Join(' ', Enumerable.Repeat("word", 30)) + ". Next.";
        var segments = new List<string>(segmenter.Append(sentence + " "));

        segments.ShouldNotBeEmpty();
        segments.ShouldAllBe(static segment => segment.Length <= 40);
    }

    [Fact]
    public void A_limit_below_the_minimum_segment_length_still_cuts()
    {
        var segmenter = new VoiceSpeechSegmenter(maxSegmentLength: 5);
        var segments = new List<string>(segmenter.Append("abcdefghijklmnop"));

        segments.ShouldNotBeEmpty();
        segments.ShouldAllBe(static segment => segment.Length <= 5);
    }

    [Fact]
    public void A_limit_that_is_not_positive_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(static () => new VoiceSpeechSegmenter(maxSegmentLength: 0));
    }

    [Fact]
    public void A_sentence_longer_than_the_default_limit_is_cut_inside_the_sentence()
    {
        // The sentence end arrives far past 240 characters in ONE chunk. The cut
        // must not wait for it: the segment would reach the synthesizer uncut.
        var segmenter = new VoiceSpeechSegmenter();
        var sentence = string.Join(' ', Enumerable.Repeat("word", 150)) + ". Next.";
        var segments = new List<string>(segmenter.Append(sentence + " "));

        segments.ShouldNotBeEmpty();
        segments.ShouldAllBe(static segment => segment.Length <= 240);
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
