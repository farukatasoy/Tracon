namespace Tracon.Core.UnitTests.Knowledge;

public sealed class TextChunkerTests
{
    [Fact]
    public void Empty_text_returns_an_empty_list()
    {
        TextChunker.Split(string.Empty, 100, 10).ShouldBeEmpty();
    }

    [Fact]
    public void Short_text_returns_a_single_chunk()
    {
        var chunks = TextChunker.Split("hello world", 100, 10);

        chunks.ShouldHaveSingleItem().ShouldBe("hello world");
    }

    [Fact]
    public void Long_text_is_split_into_overlapping_chunks()
    {
        var text = new string('a', 25);

        var chunks = TextChunker.Split(text, chunkSize: 10, chunkOverlap: 3);

        chunks.Count.ShouldBeGreaterThan(1);

        // Every chunk (except the last) is exactly chunkSize long; chunks
        // advance by the step (chunkSize - chunkOverlap) and no character is lost.
        var reconstructed = string.Concat(chunks.Select(static (c, i) => i == 0 ? c : c[3..]));
        reconstructed.ShouldBe(text);
    }

    [Fact]
    public void No_character_is_lost_at_the_boundary()
    {
        var text = string.Concat(Enumerable.Range(0, 37).Select(static i => (char)('a' + (i % 26))));

        var chunks = TextChunker.Split(text, chunkSize: 12, chunkOverlap: 4);

        chunks[^1].ShouldEndWith(text[^1].ToString());

        // The total unique covered character count must span the entire text.
        var coveredEnd = 0;
        var step = 12 - 4;

        for (var i = 0; i < chunks.Count; i++)
        {
            var start = i * step;
            coveredEnd = Math.Max(coveredEnd, start + chunks[i].Length);
        }

        coveredEnd.ShouldBe(text.Length);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    public void Invalid_chunkSize_fails(int chunkSize, int overlap)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => TextChunker.Split("x", chunkSize, overlap));
    }

    [Fact]
    public void Overlap_equal_to_or_greater_than_chunkSize_fails()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => TextChunker.Split("x", chunkSize: 10, chunkOverlap: 10));
        Should.Throw<ArgumentOutOfRangeException>(() => TextChunker.Split("x", chunkSize: 10, chunkOverlap: 11));
    }
}
