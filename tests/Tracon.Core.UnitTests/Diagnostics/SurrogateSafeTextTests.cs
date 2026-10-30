namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// The one cut-by-count helper (F-241): a cut never leaves half of a surrogate pair.
/// </summary>
public sealed class SurrogateSafeTextTests
{
    private const string Pair = "\U0001F600";

    [Fact]
    public void Text_that_fits_is_returned_as_the_same_instance()
    {
        var text = "abc" + Pair;

        SurrogateSafeText.Prefix(text, text.Length).ShouldBeSameAs(text);
        SurrogateSafeText.Prefix(text, text.Length + 10).ShouldBeSameAs(text);
    }

    [Fact]
    public void A_cut_between_the_two_halves_of_a_pair_drops_the_pair()
    {
        // "ab" + pair: indexes 2 and 3 are the pair. Keeping 3 characters ends on the high half.
        var cut = SurrogateSafeText.Prefix("ab" + Pair + "cd", 3);

        cut.ShouldBe("ab");
        SurrogateSafeText.PrefixLength("ab" + Pair + "cd", 3).ShouldBe(2);
    }

    [Fact]
    public void A_cut_after_a_whole_pair_keeps_it()
        => SurrogateSafeText.Prefix("ab" + Pair + "cd", 4).ShouldBe("ab" + Pair);

    [Fact]
    public void A_cut_in_text_without_pairs_is_the_plain_prefix()
        => SurrogateSafeText.Prefix("abcdef", 4).ShouldBe("abcd");

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_limit_that_keeps_nothing_returns_the_empty_text(int maxLength)
        => SurrogateSafeText.Prefix(Pair + "x", maxLength).ShouldBeEmpty();

    [Fact]
    public void A_limit_of_one_on_a_leading_pair_returns_the_empty_text()
        => SurrogateSafeText.Prefix(Pair + "x", 1).ShouldBeEmpty();

    [Fact]
    public void Every_cut_of_a_text_made_of_pairs_is_well_formed()
    {
        var text = string.Concat(Enumerable.Repeat(Pair, 20));

        for (var limit = 0; limit <= text.Length; limit++)
        {
            var cut = SurrogateSafeText.Prefix(text, limit);

            ContentEventPayload.Sanitize(cut).ShouldBeSameAs(cut, $"limit {limit} left a lone surrogate");
            cut.Length.ShouldBeLessThanOrEqualTo(limit);
        }
    }
}
