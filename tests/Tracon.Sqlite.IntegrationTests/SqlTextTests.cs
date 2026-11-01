namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The text every SQL store binds holds no U+0000: PostgreSQL refuses it, so each store writes
/// U+FFFD in its place.
/// </summary>
public sealed class SqlTextTests
{
    [Fact]
    public void A_text_without_a_NUL_is_returned_as_it_is()
    {
        var text = "plain";

        ReferenceEquals(SqlText.WithoutNul(text), text).ShouldBeTrue();
        SqlText.WithoutNul(null).ShouldBeNull();
    }

    [Fact]
    public void Every_NUL_of_a_text_becomes_U_FFFD()
        => SqlText.WithoutNul("a\0b\0").ShouldBe("a\uFFFDb\uFFFD");

    [Fact]
    public void A_json_without_the_characters_of_the_escape_is_returned_as_it_is()
    {
        var json = "{\"a\":\"b\"}";

        ReferenceEquals(SqlText.InJson(json), json).ShouldBeTrue();
        SqlText.InJson(null).ShouldBeNull();
    }

    [Fact]
    public void The_characters_of_the_escape_without_a_backslash_are_text()
        => SqlText.InJson("{\"a\":\"u0000\"}").ShouldBe("{\"a\":\"u0000\"}");

    [Theory]
    [InlineData("[\"a\\u0000b\"]", "[\"a\\uFFFDb\"]")]
    [InlineData("[\"\\u0000\",\"\\u0000\"]", "[\"\\uFFFD\",\"\\uFFFD\"]")]
    [InlineData("{\"k\\u0000\":\"v\"}", "{\"k\\uFFFD\":\"v\"}")]
    public void The_json_escape_of_a_NUL_becomes_the_escape_of_U_FFFD(string json, string expected)
        => SqlText.InJson(json).ShouldBe(expected);

    [Theory]
    [InlineData("[\"a\\\\u0000b\"]")]
    [InlineData("[\"\\\\\\\\u0000\"]")]
    [InlineData("[\"a\\u000b\"]")]
    [InlineData("[\"a\\u00\"]")]
    [InlineData("\\u000")]
    [InlineData("tail\\")]
    public void An_escaped_backslash_or_a_cut_escape_is_not_a_NUL(string json)
        => SqlText.InJson(json).ShouldBe(json);

    [Fact]
    public void An_escaped_backslash_before_the_escape_of_a_NUL_does_not_hide_it()
        => SqlText.InJson("[\"\\\\\\u0000\"]").ShouldBe("[\"\\\\\\uFFFD\"]");

    [Fact]
    public void A_raw_NUL_in_a_json_text_becomes_U_FFFD()
        => SqlText.InJson("[\"a\0b\"]").ShouldBe("[\"a\uFFFDb\"]");
}
