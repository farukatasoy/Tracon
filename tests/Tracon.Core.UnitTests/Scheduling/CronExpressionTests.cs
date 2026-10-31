using System.Globalization;

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

    [Theory]
    [InlineData("0 0 30 2 *")] // no 30th of February
    [InlineData("0 0 31 4 *")] // April has 30 days
    [InlineData("0 0 31 4,6,9,11 *")] // none of these months has a 31st
    [InlineData("0 0 30-31 2 *")]
    public void An_expression_whose_day_and_month_never_meet_is_rejected_when_parsed(string expression)
    {
        // Each field is valid alone. Without this rule the expression parsed, and the search for its next run
        // ended in an exception that nothing around it handled.
        Should.Throw<FormatException>(() => CronExpression.Parse(expression))
            .Message.ShouldContain("day-of-month");
        CronExpression.TryParse(expression, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("0 0 29 2 *")] // 29th of February exists in a leap year
    [InlineData("0 0 31 1,3 *")]
    [InlineData("0 0 30 2 1")] // both restricted: either field may match, so Mondays count
    [InlineData("0 0 * 2 *")]
    public void An_expression_whose_day_and_month_do_meet_is_accepted(string expression)
    {
        CronExpression.TryParse(expression, out _).ShouldBeTrue();
    }

    [Fact]
    public void Every_expression_that_parses_has_a_next_run()
    {
        var after = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

        foreach (var expression in new[] { "0 0 29 2 *", "59 23 31 12 *", "0 0 31 1,3,5,7,8,10,12 *", "0 0 29,30,31 2 *", "30 4 1 * 0" })
        {
            var next = CronExpression.Parse(expression).GetNextOccurrence(after, TimeZoneInfo.Utc);

            next.ShouldBeGreaterThan(after, expression);
        }
    }

    [Fact]
    public void The_29th_of_February_is_found_across_a_century_that_is_not_a_leap_year()
    {
        // 2100 is not a leap year, so the gap after 2096 is eight years.
        var next = CronExpression.Parse("0 0 29 2 *")
            .GetNextOccurrence(new DateTimeOffset(2096, 3, 1, 0, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);

        next.ShouldBe(new DateTimeOffset(2104, 2, 29, 0, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("5/15 * * * *", 5, 20, 35, 50)]
    [InlineData("0/20 * * * *", 0, 20, 40)]
    [InlineData("5 * * * *", 5)]
    [InlineData("10-30/10 * * * *", 10, 20, 30)]
    public void A_value_with_a_step_runs_every_step_from_that_value_to_the_end_of_the_range(string expression, params int[] minutes)
    {
        // The oracle is written out here rather than computed by the parser: the parser agreeing with itself
        // would pass for "5/15" meaning just 5.
        var cron = CronExpression.Parse(expression);
        var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(-1);
        var seen = new List<int>();

        for (var index = 0; index < minutes.Length; index++)
        {
            at = cron.GetNextOccurrence(at, TimeZoneInfo.Utc);
            seen.Add(at.Minute);
            at.Hour.ShouldBe(0);
        }

        seen.ShouldBe(minutes);
        cron.GetNextOccurrence(at, TimeZoneInfo.Utc).Hour.ShouldBe(1);
    }

    [Fact]
    public void A_hour_with_a_step_runs_from_its_value_to_the_end_of_the_day()
    {
        var cron = CronExpression.Parse("0 6/6 * * *");
        var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var hours = new List<int>();

        for (var index = 0; index < 3; index++)
        {
            at = cron.GetNextOccurrence(at, TimeZoneInfo.Utc);
            hours.Add(at.Hour);
        }

        hours.ShouldBe([6, 12, 18]);
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

    // --- The clock set back (daylight saving time ends) ---
    // America/New_York 2026-11-01: at 06:00Z the clock goes from 02:00 EDT back to 01:00 EST, so the local
    // minutes 01:00-01:59 occur twice (05:00Z-05:59Z, then 06:00Z-06:59Z).

    private static readonly DateTimeOffset NewYorkFallBack = new(2026, 11, 1, 4, 0, 0, TimeSpan.Zero);

    private static List<DateTimeOffset> Runs(string expression, string zoneId, DateTimeOffset from, int count)
    {
        var cron = CronExpression.Parse(expression);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var runs = new List<DateTimeOffset>();
        var at = from;

        for (var index = 0; index < count; index++)
        {
            at = cron.GetNextOccurrence(at, zone);
            runs.Add(at);
        }

        return runs;
    }

    [Fact]
    public void A_job_every_five_minutes_keeps_its_rhythm_through_the_repeated_hour()
    {
        // A gap of an hour here is the repeated hour skipped: the clock-time walk reached 02:00 and never went back.
        var from = NewYorkFallBack.AddMinutes(55);
        var runs = Runs("*/5 * * * *", "America/New_York", from, 50);

        // The first gap counts too: it is the one that crosses into the repeated hour.
        var sequence = new List<DateTimeOffset> { from };
        sequence.AddRange(runs);

        for (var index = 1; index < sequence.Count; index++)
        {
            (sequence[index] - sequence[index - 1]).ShouldBe(TimeSpan.FromMinutes(5), $"between {sequence[index - 1]:u} and {sequence[index]:u}");
        }

        runs.ShouldContain(new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero));
        runs.ShouldContain(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void An_hourly_job_runs_in_both_passes_of_the_repeated_hour()
    {
        Runs("0 * * * *", "America/New_York", NewYorkFallBack, 3).ShouldBe(
        [
            new DateTimeOffset(2026, 11, 1, 5, 0, 0, TimeSpan.Zero), // 01:00 EDT
            new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero), // 01:00 EST
            new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero), // 02:00 EST
        ]);
    }

    [Fact]
    public void A_time_of_day_in_the_repeated_hour_runs_once_in_the_first_pass()
    {
        // 01:30 happens twice that night. A daily job at 01:30 runs once: at the earlier 01:30, and the next
        // run is the following day, not the second 01:30 an hour later.
        Runs("30 1 * * *", "America/New_York", NewYorkFallBack, 2).ShouldBe(
        [
            new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), // 01:30 EDT
            new DateTimeOffset(2026, 11, 2, 6, 30, 0, TimeSpan.Zero), // 01:30 EST, the next day
        ]);
    }

    [Fact]
    public void A_list_of_named_hours_runs_each_once()
    {
        Runs("0 1,2 * * *", "America/New_York", NewYorkFallBack, 2).ShouldBe(
        [
            new DateTimeOffset(2026, 11, 1, 5, 0, 0, TimeSpan.Zero), // 01:00 EDT
            new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero), // 02:00 EST; the second 01:00 is skipped
        ]);
    }

    [Fact]
    public void A_search_that_starts_in_the_second_pass_continues_from_there()
    {
        Runs("*/5 * * * *", "America/New_York", new DateTimeOffset(2026, 11, 1, 6, 7, 0, TimeSpan.Zero), 2).ShouldBe(
        [
            new DateTimeOffset(2026, 11, 1, 6, 10, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 1, 6, 15, 0, TimeSpan.Zero),
        ]);
    }

    [Fact]
    public void A_half_hour_shift_keeps_the_rhythm_too()
    {
        // Lord Howe sets its clock back by 30 minutes: 2026-04-04 15:00Z, 02:00 +11:00 -> 01:30 +10:30.
        var from = new DateTimeOffset(2026, 4, 4, 14, 20, 0, TimeSpan.Zero);
        var runs = Runs("*/10 * * * *", "Australia/Lord_Howe", from, 12);

        var sequence = new List<DateTimeOffset> { from };
        sequence.AddRange(runs);

        for (var index = 1; index < sequence.Count; index++)
        {
            (sequence[index] - sequence[index - 1]).ShouldBe(TimeSpan.FromMinutes(10), $"between {sequence[index - 1]:u} and {sequence[index]:u}");
        }

        runs.ShouldContain(new DateTimeOffset(2026, 4, 4, 15, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("*/5 * * * *", "America/New_York", 2026, 11, 1, 6)]
    [InlineData("0 * * * *", "America/New_York", 2026, 11, 1, 6)]
    [InlineData("30 1 * * *", "America/New_York", 2026, 11, 1, 6)]
    [InlineData("0 1,2 * * *", "America/New_York", 2026, 11, 1, 6)]
    [InlineData("15 0-3 * * *", "America/New_York", 2026, 11, 1, 6)]
    [InlineData("*/20 */1 * * *", "America/New_York", 2026, 11, 1, 6)]
    [InlineData("*/5 * * * *", "Europe/London", 2026, 10, 25, 1)]
    [InlineData("30 1 * * *", "Europe/London", 2026, 10, 25, 1)]
    [InlineData("*/10 * * * *", "Australia/Lord_Howe", 2026, 4, 4, 15)]
    public void The_runs_around_a_clock_set_back_match_a_walk_of_every_minute_in_elapsed_time(
        string expression, string zoneId, int year, int month, int day, int transitionHourUtc)
    {
        // The oracle walks UTC minutes and asks the zone what the wall clock says, so it needs no search logic
        // of its own: a minute runs when the wall clock matches and, for a named hour that occurs twice, only
        // in the first pass.
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var repeats = expression.Split(' ')[1] is var hourField && (hourField.Contains('/', StringComparison.Ordinal) || string.Equals(hourField, "*", StringComparison.Ordinal));
        var start = new DateTimeOffset(year, month, day, transitionHourUtc, 0, 0, TimeSpan.Zero).AddHours(-6);
        var end = start.AddHours(36);

        var fields = expression.Split(' ');
        var minutes = Allowed(fields[0], 0, 59);
        var hours = Allowed(fields[1], 0, 23);

        var expected = new List<DateTimeOffset>();

        for (var instant = start.AddMinutes(1); instant <= end; instant = instant.AddMinutes(1))
        {
            var local = TimeZoneInfo.ConvertTime(instant, zone);

            if (!minutes.Contains(local.Minute) || !hours.Contains(local.Hour))
            {
                continue;
            }

            var wall = local.DateTime;

            if (!repeats && zone.IsAmbiguousTime(wall) && zone.GetUtcOffset(instant) != zone.GetAmbiguousTimeOffsets(wall).Max())
            {
                continue;
            }

            expected.Add(instant);
        }

        var cron = CronExpression.Parse(expression);
        var actual = new List<DateTimeOffset>();
        var at = start;

        while (true)
        {
            at = cron.GetNextOccurrence(at, zone);

            if (at > end)
            {
                break;
            }

            actual.Add(at);
        }

        actual.ShouldBe(expected);
    }

    private static HashSet<int> Allowed(string field, int min, int max)
    {
        // Only the shapes the cases above use: "*", "*/n", "a", "a,b" and "a-b".
        var allowed = new HashSet<int>();

        foreach (var part in field.Split(','))
        {
            if (string.Equals(part, "*", StringComparison.Ordinal))
            {
                allowed.UnionWith(Enumerable.Range(min, max - min + 1));
            }
            else if (part.StartsWith("*/", StringComparison.Ordinal))
            {
                var step = int.Parse(part[2..], CultureInfo.InvariantCulture);
                allowed.UnionWith(Enumerable.Range(min, max - min + 1).Where(value => (value - min) % step == 0));
            }
            else if (part.Contains('-', StringComparison.Ordinal))
            {
                var bounds = part.Split('-');
                var low = int.Parse(bounds[0], CultureInfo.InvariantCulture);
                var high = int.Parse(bounds[1], CultureInfo.InvariantCulture);
                allowed.UnionWith(Enumerable.Range(low, high - low + 1));
            }
            else
            {
                allowed.Add(int.Parse(part, CultureInfo.InvariantCulture));
            }
        }

        return allowed;
    }
}
