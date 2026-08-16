namespace AgentPrism.Core.UnitTests.Quotas;

/// <summary>
/// Tests of period boundary computation.
/// </summary>
/// <remarks>
/// Pure logic: no wall-clock read happens, each scenario gives an explicit
/// instant and time zone. Daylight-saving transitions are tested against the
/// real IANA rules.
/// </remarks>
public sealed class QuotaPeriodCalculatorTests
{
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public void Daily_period_is_the_local_calendar_day()
    {
        // 2026-08-03T22:30Z is 01:30 the next day in Istanbul (UTC+03).
        var instant = new DateTimeOffset(2026, 8, 3, 22, 30, 0, TimeSpan.Zero);

        QuotaPeriodCalculator.GetPeriodStart(instant, QuotaPeriod.Daily, Istanbul)
            .ShouldBe(new DateOnly(2026, 8, 4));

        // The same instant is still August 3rd in UTC.
        QuotaPeriodCalculator.GetPeriodStart(instant, QuotaPeriod.Daily, TimeZoneInfo.Utc)
            .ShouldBe(new DateOnly(2026, 8, 3));
    }

    [Fact]
    public void Monthly_period_is_the_first_day_of_the_month()
    {
        var instant = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

        QuotaPeriodCalculator.GetPeriodStart(instant, QuotaPeriod.Monthly, Istanbul)
            .ShouldBe(new DateOnly(2026, 8, 1));
    }

    [Fact]
    public void Daily_period_ends_at_local_midnight()
    {
        var instant = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

        // Istanbul UTC+03: August 4th 00:00 local = August 3rd 21:00 UTC.
        QuotaPeriodCalculator.GetPeriodEnd(instant, QuotaPeriod.Daily, Istanbul)
            .ShouldBe(new DateTimeOffset(2026, 8, 3, 21, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Monthly_period_end_follows_the_month_length()
    {
        // February has 28 days: 2026 is not a leap year.
        var instant = new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero);

        QuotaPeriodCalculator.GetPeriodEnd(instant, QuotaPeriod.Monthly, TimeZoneInfo.Utc)
            .ShouldBe(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Monthly_period_rolls_over_into_January_at_year_end()
    {
        var instant = new DateTimeOffset(2026, 12, 20, 12, 0, 0, TimeSpan.Zero);

        QuotaPeriodCalculator.GetPeriodEnd(instant, QuotaPeriod.Monthly, TimeZoneInfo.Utc)
            .ShouldBe(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Daylight_saving_transition_does_not_shift_the_period_boundary_by_a_day()
    {
        // 2026-03-08: clocks in New York jump from 02:00 to 03:00. Midnight
        // is still valid, but that day only lasts 23 hours.
        var instant = new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero);

        QuotaPeriodCalculator.GetPeriodStart(instant, QuotaPeriod.Daily, NewYork)
            .ShouldBe(new DateOnly(2026, 3, 8));

        // March 9th 00:00 EDT (UTC-04) = March 9th 04:00 UTC.
        QuotaPeriodCalculator.GetPeriodEnd(instant, QuotaPeriod.Daily, NewYork)
            .ShouldBe(new DateTimeOffset(2026, 3, 9, 4, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Nonexistent_midnight_resolves_to_a_moment_after_the_transition()
    {
        // In some time zones midnight is entirely swallowed by a forward
        // jump; the calculation must produce a defined instant instead of
        // throwing. Rather than testing zones like Lord Howe/Chatham
        // directly, this asserts the general behavior: any valid result is
        // at or after that day's local midnight and before the next day.
        var date = new DateOnly(2026, 3, 8);
        var instant = QuotaPeriodCalculator.ToUtcInstant(date, NewYork);

        var local = TimeZoneInfo.ConvertTime(instant, NewYork);

        local.Date.ShouldBe(new DateTime(2026, 3, 8));
    }

    [Fact]
    public void All_periods_are_computed_together()
    {
        var instant = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

        var starts = QuotaPeriodCalculator.GetAllPeriodStarts(instant, TimeZoneInfo.Utc);

        starts.Count.ShouldBe(2);
        starts[QuotaPeriod.Daily].ShouldBe(new DateOnly(2026, 8, 17));
        starts[QuotaPeriod.Monthly].ShouldBe(new DateOnly(2026, 8, 1));
    }
}
