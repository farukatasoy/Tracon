namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// A time span in configuration is the clock, <c>[days.]hh:mm:ss</c>. A bare number is not days and hours do not pass 23.
/// </summary>
/// <remarks>
/// <c>TimeSpan.TryParse</c> read <c>30</c> as thirty days and <c>48:00:00</c> as forty-eight, and every validator that bounds a
/// time span let them through (they accept up to 49.7 days). <c>Tools:DefaultTimeout=30</c> was a tool call that may run for a
/// month. The reader refuses the shapes that are not the clock, so the host stops at start and names the key.
/// </remarks>
public sealed class TimeSpanReadingTests
{
    [Theory]
    [InlineData("00:00:30", 30)]
    [InlineData("0:00:30", 30)]
    [InlineData("0:0:30", 30)]
    [InlineData("  00:00:30  ", 30)]
    [InlineData("00:05:00", 300)]
    [InlineData("23:59:59", 86399)]
    [InlineData("1.00:00:00", 86400)]
    [InlineData("2.12:00:00", 216000)]
    [InlineData("00:00:00", 0)]
    [InlineData("00:00:30.5", 30.5)]
    [InlineData("00:00:00.0010000", 0.001)]
    [InlineData("-00:00:30", -30)]
    [InlineData("-00:00:00.001", -0.001)]
    public void The_clock_is_read(string text, double expectedSeconds)
    {
        ConfigurationValueKinds.TryTimeSpan(text, out var value).ShouldBeTrue();

        value.ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData("30")]
    [InlineData("1")]
    [InlineData("00:30")]
    [InlineData("48:00:00")]
    [InlineData("24:00:00")]
    [InlineData("100:00:00")]
    [InlineData("00:60:00")]
    [InlineData("00:00:60")]
    [InlineData("1.24:00:00")]
    [InlineData("00:00:30.")]
    [InlineData("00:00:30.12345678")]
    [InlineData("00:00:30,5")]
    [InlineData("+00:00:30")]
    [InlineData("--00:00:30")]
    [InlineData("1.2.00:00:00")]
    [InlineData("30s")]
    [InlineData("-")]
    [InlineData("99999999.00:00:00")]
    public void A_shape_that_is_not_the_clock_is_refused(string text)
    {
        ConfigurationValueKinds.TryTimeSpan(text, out var value).ShouldBeFalse();

        value.ShouldBe(TimeSpan.Zero);
    }
}
