using CsCheck;

namespace Tracon.Core.UnitTests.Quotas;

/// <summary>
/// Properties of <see cref="QuotaPeriodCalculator"/> over every time zone the system knows: periods tile the time line,
/// each instant belongs to exactly the period that contains it, and no period boundary is reached twice.
/// </summary>
/// <remarks>
/// A quota counter is keyed by the local date a period starts on, and the period end is the instant the next one begins.
/// The rules that break a daily or monthly boundary are the zone rules: a midnight that a spring-forward transition swallows,
/// a midnight that occurs twice, a transition that is not a whole hour (Lord Howe, 30 minutes), and a period that is
/// 23 or 25 hours long. Sampling the zones instead of naming a few keeps the odd ones in the run.
/// </remarks>
public sealed class QuotaPeriodPropertyTests
{
    private static readonly IReadOnlyList<TimeZoneInfo> Zones = TimeZoneInfo.GetSystemTimeZones();

    private static readonly Gen<TimeZoneInfo> Zone = Gen.OneOf<TimeZoneInfo>([.. Zones]);

    // 1990-01-01 to 2040-01-01, in seconds.
    private static readonly Gen<DateTimeOffset> Instant =
        Gen.Long[631_152_000L, 2_208_988_800L].Select(static seconds => DateTimeOffset.FromUnixTimeSeconds(seconds));

    private static readonly Gen<QuotaPeriod> Period = Gen.OneOf(QuotaPeriod.Daily, QuotaPeriod.Monthly);

    [Fact]
    public void The_period_end_is_after_the_instant_and_not_more_than_a_period_and_a_shift_away()
    {
        Gen.Select(Instant, Period, Zone).Sample(
            static (instant, period, zone) =>
            {
                var end = QuotaPeriodCalculator.GetPeriodEnd(instant, period, zone);
                var longest = period == QuotaPeriod.Daily ? TimeSpan.FromHours(26) : TimeSpan.FromDays(32);

                (end > instant).ShouldBeTrue($"{zone.Id} {period}: the end {end:O} is not after {instant:O}");
                (end - instant <= longest).ShouldBeTrue($"{zone.Id} {period}: {end - instant} from {instant:O}");
            },
            iter: 5_000);
    }

    [Fact]
    public void The_instant_a_period_ends_belongs_to_the_next_period()
    {
        Gen.Select(Instant, Period, Zone).Sample(
            static (instant, period, zone) =>
            {
                var end = QuotaPeriodCalculator.GetPeriodEnd(instant, period, zone);
                var startNow = QuotaPeriodCalculator.GetPeriodStart(instant, period, zone);
                var startAtEnd = QuotaPeriodCalculator.GetPeriodStart(end, period, zone);

                (startAtEnd > startNow).ShouldBeTrue(
                    $"{zone.Id} {period}: at {instant:O} the period starts {startNow}, but at its end {end:O} it still starts {startAtEnd}");
            },
            iter: 5_000);
    }

    [Fact]
    public void An_instant_one_tick_before_the_end_is_still_in_the_period()
    {
        Gen.Select(Instant, Period, Zone).Sample(
            static (instant, period, zone) =>
            {
                var end = QuotaPeriodCalculator.GetPeriodEnd(instant, period, zone);
                var startNow = QuotaPeriodCalculator.GetPeriodStart(instant, period, zone);
                var justBefore = QuotaPeriodCalculator.GetPeriodStart(end - TimeSpan.FromTicks(1), period, zone);

                // In the one hour a midnight that occurs twice leaves open, the end is the EARLIER occurrence and the
                // later one still counts to the old period only if the boundary were the later one: the contract is that
                // the boundary is the earlier one, so just before it the period is still the current one.
                justBefore.ShouldBe(startNow, $"{zone.Id} {period}: one tick before the end {end:O} the period already changed");
            },
            iter: 5_000);
    }

    [Fact]
    public void The_start_instant_of_a_period_is_inside_that_period()
    {
        Gen.Select(Instant, Period, Zone).Sample(
            static (instant, period, zone) =>
            {
                var start = QuotaPeriodCalculator.GetPeriodStart(instant, period, zone);
                var startInstant = QuotaPeriodCalculator.ToUtcInstant(start, zone);

                QuotaPeriodCalculator.GetPeriodStart(startInstant, period, zone)
                    .ShouldBe(start, $"{zone.Id} {period}: the first instant of {start} belongs to another date");
            },
            iter: 5_000);
    }

    /// <summary>
    /// Every boundary holds in the hours around every offset change of every zone, 2023 to 2026.
    /// </summary>
    /// <remarks>
    /// A random instant almost never lands on a day whose midnight a zone rule has touched: it happens once a year in a
    /// handful of zones. So the sweep finds the offset changes itself (an hour-by-hour walk of the zone's offset) and looks
    /// at every ten minutes of the day before and the day after each one, with both period kinds.
    /// </remarks>
    [Fact]
    public void The_boundaries_hold_around_every_offset_change_of_every_zone()
    {
        var failures = new List<string>();
        var from = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var until = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var checks = 0;

        foreach (var zone in Zones)
        {
            var previous = zone.GetUtcOffset(from);

            for (var hour = from.AddHours(1); hour < until; hour = hour.AddHours(1))
            {
                var offset = zone.GetUtcOffset(hour);

                if (offset == previous)
                {
                    continue;
                }

                previous = offset;

                for (var probe = hour.AddHours(-30); probe <= hour.AddHours(30); probe = probe.AddMinutes(10))
                {
                    foreach (var period in new[] { QuotaPeriod.Daily, QuotaPeriod.Monthly })
                    {
                        checks++;
                        var problem = Check(probe, period, zone);

                        if (problem is not null && !failures.Any(f => f.StartsWith(zone.Id + " " + period, StringComparison.Ordinal)))
                        {
                            failures.Add(problem);
                        }
                    }
                }
            }
        }

        checks.ShouldBeGreaterThan(100_000, "the sweep found too few offset changes to mean anything");
        failures.ShouldBeEmpty();
    }

    private static string? Check(DateTimeOffset instant, QuotaPeriod period, TimeZoneInfo zone)
    {
        var end = QuotaPeriodCalculator.GetPeriodEnd(instant, period, zone);
        var startNow = QuotaPeriodCalculator.GetPeriodStart(instant, period, zone);

        if (end <= instant)
        {
            return $"{zone.Id} {period}: the end {end:O} is not after {instant:O}";
        }

        if (QuotaPeriodCalculator.GetPeriodStart(end, period, zone) <= startNow)
        {
            return $"{zone.Id} {period}: the instant {end:O} that ends the period of {instant:O} is still in it";
        }

        var justBefore = QuotaPeriodCalculator.GetPeriodStart(end - TimeSpan.FromTicks(1), period, zone);

        if (justBefore != startNow)
        {
            return $"{zone.Id} {period}: one tick before the end {end:O} the period of {instant:O} already changed to {justBefore}";
        }

        var startInstant = QuotaPeriodCalculator.ToUtcInstant(startNow, zone);

        return QuotaPeriodCalculator.GetPeriodStart(startInstant, period, zone) != startNow
            ? $"{zone.Id} {period}: the first instant of {startNow} belongs to another date"
            : null;
    }
}
