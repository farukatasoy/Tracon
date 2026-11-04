using System.Text;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The shortened form of a label that is longer than the column that holds it: bounded, well-formed, stable, and different for
/// two labels that share their start.
/// </summary>
public sealed class SqlTextFitTests
{
    private const int Limit = 200;

    [Fact]
    public void A_label_that_fits_is_returned_as_it_is()
    {
        var exact = new string('a', Limit);

        SqlText.Fit(exact, Limit).ShouldBeSameAs(exact);
        SqlText.Fit("session:abc", Limit).ShouldBe("session:abc");
        SqlText.Fit(null, Limit).ShouldBeNull();
        SqlText.Fit(string.Empty, Limit).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(201)]
    [InlineData(208)]
    [InlineData(1000)]
    [InlineData(100_000)]
    public void A_longer_label_is_exactly_as_long_as_the_column_and_keeps_its_start(int length)
    {
        var label = "session:" + new string('s', length - "session:".Length);

        var fitted = SqlText.Fit(label, Limit)!;

        fitted.Length.ShouldBe(Limit);
        fitted.ShouldStartWith("session:ssssssssssssssssssssssssssssssssssssssssssssssssssssssss");
        fitted.ShouldContain("~");
    }

    [Fact]
    public void The_same_label_is_shortened_the_same_way_and_two_labels_that_share_their_start_are_told_apart()
    {
        var start = "tool:" + new string('t', 300);

        string.Equals(SqlText.Fit(start + "-a", Limit), SqlText.Fit(start + "-a", Limit), StringComparison.Ordinal).ShouldBeTrue();
        string.Equals(SqlText.Fit(start + "-a", Limit), SqlText.Fit(start + "-b", Limit), StringComparison.Ordinal).ShouldBeFalse();
    }

    [Theory]
    [InlineData(166)]
    [InlineData(167)]
    [InlineData(168)]
    public void A_cut_never_splits_a_surrogate_pair(int lettersBeforeThePair)
    {
        var label = new string('p', lettersBeforeThePair) + "\U0001F600" + new string('q', 100);

        var fitted = SqlText.Fit(label, Limit)!;

        fitted.Length.ShouldBeLessThanOrEqualTo(Limit);
        Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(fitted)).ShouldBe(fitted, "a lone half of a pair would not survive the database");
    }

    [Fact]
    public void A_limit_too_small_to_keep_anything_is_refused()
        => Should.Throw<ArgumentOutOfRangeException>(() => SqlText.Fit("anything", 10));
}
