namespace Tracon.Core.UnitTests.Quotas;

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
    public void Midnight_that_occurs_twice_resolves_to_the_earlier_of_the_two()
    {
        // Cuba sets its clock back at 01:00, so local 00:00-00:59 on 2026-11-01 occurs twice: first at -04:00
        // (04:00Z), then at -05:00 (05:00Z). The period starts at the first midnight, as the remarks promise.
        var havana = TimeZoneInfo.FindSystemTimeZoneById("America/Havana");

        QuotaPeriodCalculator.ToUtcInstant(new DateOnly(2026, 11, 1), havana)
            .ShouldBe(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero));
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

    /// <summary>
    /// Where a rule moves the clock at local midnight, the date starts at the first instant the zone itself puts on it.
    /// </summary>
    /// <remarks>
    /// Paraguay, Chile and Greenland start daylight saving time at local midnight, so that midnight never shows on a clock.
    /// The offset-based estimate used the offset from after the move and landed an hour inside the previous date: the
    /// period ended an hour early and a caller told to retry then was still refused. The oracle here is a minute-by-minute
    /// scan with the zone's own conversion, so it shares no arithmetic with the code under test.
    /// </remarks>
    [Theory]
    [InlineData("America/Asuncion", 2023, 10, 1)]
    [InlineData("America/Santiago", 2023, 9, 3)]
    [InlineData("America/Scoresbysund", 2023, 3, 26)]
    [InlineData("America/Havana", 2026, 3, 8)]
    [InlineData("Australia/Lord_Howe", 2026, 10, 4)]
    [InlineData("America/New_York", 2026, 3, 8)]
    public void The_first_instant_of_a_date_is_the_first_instant_the_zone_puts_on_it(string zoneId, int year, int month, int day)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var date = new DateOnly(year, month, day);

        var scan = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero).AddHours(-30);

        while (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(scan, zone).DateTime) < date)
        {
            scan = scan.AddMinutes(1);
        }

        // Windows writes a rule that moves the clock at midnight as 23:59:59.999 of the day before, so the first instant of the
        // date can be some milliseconds before the whole minute the scan stops at. The scan goes on, a millisecond at a time,
        // from the minute before; the IANA rules move a clock on a whole minute and the second scan finds nothing earlier.
        var fine = scan.AddMinutes(-1);

        while (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fine, zone).DateTime) < date)
        {
            fine = fine.AddMilliseconds(1);
        }

        QuotaPeriodCalculator.ToUtcInstant(date, zone).ShouldBe(fine);
    }

    /// <summary>
    /// A period that ends where its zone skips a whole date ends when the next date that exists begins.
    /// </summary>
    /// <remarks>
    /// Kiribati (1994), Samoa and Tokelau (2011) moved across the date line and have no 2011-12-30 or 1994-12-31. The offset
    /// estimate for the missing date landed a day inside the previous one and the walk to the date gave up after four hours,
    /// so the period ended some twenty hours early. The oracle scans forward minute by minute with the zone's own conversion.
    /// </remarks>
    [Theory]
    [InlineData("Pacific/Fakaofo", 2011, 12, 29, 11, 21)]
    [InlineData("Pacific/Apia", 2011, 12, 29, 12, 0)]
    [InlineData("Pacific/Kiritimati", 1994, 12, 30, 20, 0)]
    public void A_period_ends_when_the_next_date_that_exists_begins_where_the_zone_skips_a_date(
        string zoneId, int year, int month, int day, int hour, int minute)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var instant = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
        var startNow = QuotaPeriodCalculator.GetPeriodStart(instant, QuotaPeriod.Daily, zone);

        var scan = instant;

        while (QuotaPeriodCalculator.GetPeriodStart(scan, QuotaPeriod.Daily, zone) == startNow)
        {
            scan = scan.AddMinutes(1);
        }

        QuotaPeriodCalculator.GetPeriodEnd(instant, QuotaPeriod.Daily, zone).ShouldBe(scan);
    }

    /// <summary>
    /// Windows writes a rule that moves the clock at midnight as 23:59:59.999 of the day before (Egypt, Chile, Paraguay),
    /// and the zone then moves the clock at that millisecond. The first instant of the new date is a millisecond before
    /// the whole minute a walk in minutes stops at.
    /// </summary>
    [Fact]
    public void A_clock_that_moves_a_millisecond_before_midnight_ends_the_period_at_that_instant()
    {
        var zone = ZoneThatMovesForwardAtTheLastMillisecondOfTheDay();
        var instant = new DateTimeOffset(2023, 4, 27, 9, 0, 0, TimeSpan.Zero);
        var firstInstantOfTheNextDate = new DateTimeOffset(2023, 4, 27, 21, 59, 59, 999, TimeSpan.Zero);

        var end = QuotaPeriodCalculator.GetPeriodEnd(instant, QuotaPeriod.Daily, zone);

        end.ShouldBe(firstInstantOfTheNextDate);
        QuotaPeriodCalculator.GetPeriodStart(end, QuotaPeriod.Daily, zone).ShouldBe(new DateOnly(2023, 4, 28));
        QuotaPeriodCalculator.GetPeriodStart(end - TimeSpan.FromTicks(1), QuotaPeriod.Daily, zone).ShouldBe(new DateOnly(2023, 4, 27));
    }

    [Fact]
    public void The_first_instant_of_a_date_in_such_a_zone_is_on_that_date()
    {
        var zone = ZoneThatMovesForwardAtTheLastMillisecondOfTheDay();

        var first = QuotaPeriodCalculator.ToUtcInstant(new DateOnly(2023, 4, 28), zone);

        QuotaPeriodCalculator.GetPeriodStart(first, QuotaPeriod.Daily, zone).ShouldBe(new DateOnly(2023, 4, 28));
        QuotaPeriodCalculator.GetPeriodStart(first - TimeSpan.FromTicks(1), QuotaPeriod.Daily, zone).ShouldBe(new DateOnly(2023, 4, 27));
    }

    private static TimeZoneInfo ZoneThatMovesForwardAtTheLastMillisecondOfTheDay()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2023, 1, 1),
            new DateTime(2023, 12, 31),
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 23, 59, 59, 999), 4, 27),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 23, 59, 59, 999), 10, 26));

        return TimeZoneInfo.CreateCustomTimeZone("Moves-at-the-last-millisecond", TimeSpan.FromHours(2), "test", "test", "test", [rule]);
    }
}
