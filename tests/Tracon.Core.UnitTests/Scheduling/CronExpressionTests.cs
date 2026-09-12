namespace Tracon.Core.UnitTests.Scheduling;

public sealed class CronExpressionTests
{
    [Theory]
    [InlineData("* * * * *")]
    [InlineData("0 3 * * *")]
    [InlineData("*/15 * * * *")]
    [InlineData("0 0 1 1 *")]
    [InlineData("0 9 * * 1-5")]
    [InlineData("0,30 8-17 * * *")]
    public void Valid_expressions_are_parsed(string expression)
    {
        CronExpression.TryParse(expression, out var result).ShouldBeTrue();
        result.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("* * * *")] // four fields
    [InlineData("* * * * * *")] // six fields (seconds)
    [InlineData("60 * * * *")] // minute out of range
    [InlineData("* 24 * * *")] // hour out of range
    [InlineData("* * 32 * *")] // day-of-month out of range
    [InlineData("* * L * *")] // vixie-cron extension
    [InlineData("* * * * ?")] // unsupported syntax
    public void Invalid_expressions_are_rejected(string expression)
    {
        CronExpression.TryParse(expression, out var result).ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Fact]
    public void Parse_throws_FormatException_for_an_invalid_expression()
    {
        Should.Throw<FormatException>(() => CronExpression.Parse("* * * * * *"));
    }

    [Fact]
    public void A_daily_time_rolls_over_to_the_next_day()
    {
        var cron = CronExpression.Parse("30 3 * * *");
        var after = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        var next = cron.GetNextOccurrence(after, TimeZoneInfo.Utc);

        next.ShouldBe(new DateTimeOffset(2026, 1, 2, 3, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_step_value_matches_at_the_specified_intervals()
    {
        var cron = CronExpression.Parse("*/15 * * * *");
        var after = new DateTimeOffset(2026, 1, 1, 10, 1, 0, TimeSpan.Zero);

        var next = cron.GetNextOccurrence(after, TimeZoneInfo.Utc);

        next.ShouldBe(new DateTimeOffset(2026, 1, 1, 10, 15, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Day_of_week_rolls_over_to_the_correct_day()
    {
        // 2026-01-01 is a Thursday. The next Monday (1) is 2026-01-05.
        var cron = CronExpression.Parse("0 9 * * 1");
        var after = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var next = cron.GetNextOccurrence(after, TimeZoneInfo.Utc);

        next.ShouldBe(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void When_both_day_of_month_and_day_of_week_are_restricted_they_match_with_OR()
    {
        // Standard cron rule: when both are restricted, a match is "either one or
        // the other". The 15th of the month OR Monday (1) matches every month;
        // whichever comes first.
        var cron = CronExpression.Parse("0 0 15 * 1");

        // 2026-01-01 is a Thursday -> first match is Monday 2026-01-05 (before the 15th).
        var next = cron.GetNextOccurrence(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);

        next.ShouldBe(new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void An_invalid_local_time_during_the_spring_forward_DST_transition_is_skipped()
    {
        // In the US, clocks jump from 02:00 to 03:00 on 2024-03-10 (a fixed rule
        // by law since 2007); 02:30 never happens that day. The next match must
        // roll over to the following day.
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var cron = CronExpression.Parse("30 2 * * *");

        var after = new DateTimeOffset(2024, 3, 9, 12, 0, 0, TimeSpan.Zero);
        var next = cron.GetNextOccurrence(after, timeZone);
        var local = TimeZoneInfo.ConvertTime(next, timeZone);

        local.Date.ShouldBe(new DateTime(2024, 3, 11));
        local.TimeOfDay.ShouldBe(TimeSpan.FromMinutes(150));
    }
}
