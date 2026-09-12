namespace Tracon.Core.UnitTests.Tools;

public sealed class TextTrimmingTests
{
    [Fact]
    public void Text_below_the_limit_is_not_trimmed()
    {
        var (text, truncated) = TextTrimming.Trim("hello world", maxBytes: 1024);

        text.ShouldBe("hello world");
        truncated.ShouldBeFalse();
    }

    [Fact]
    public void Text_above_the_limit_is_trimmed()
    {
        var (text, truncated) = TextTrimming.Trim("0123456789", maxBytes: 5);

        text.ShouldBe("01234");
        truncated.ShouldBeTrue();
    }

    [Fact]
    public void Does_not_cut_through_the_middle_of_a_multi_byte_character()
    {
        // 'é' is 2 bytes in UTF-8. If the limit lands exactly in the middle
        // (byte 3), it must back off until a valid boundary is found.
        var text = "abé"; // a(1) b(1) é(2) = 4 bytes

        var (trimmed, truncated) = TextTrimming.Trim(text, maxBytes: 3);

        trimmed.ShouldBe("ab");
        truncated.ShouldBeTrue();

        // The result must ALWAYS be valid UTF-8.
        System.Text.Encoding.UTF8.GetByteCount(trimmed).ShouldBeLessThanOrEqualTo(3);
    }

    [Fact]
    public void Zero_limit_returns_empty_text()
    {
        var (text, truncated) = TextTrimming.Trim("some text", maxBytes: 0);

        text.ShouldBe(string.Empty);
        truncated.ShouldBeTrue();
    }

    [Fact]
    public void Empty_text_does_not_count_as_trimmed()
    {
        var (text, truncated) = TextTrimming.Trim(string.Empty, maxBytes: 100);

        text.ShouldBe(string.Empty);
        truncated.ShouldBeFalse();
    }
}
