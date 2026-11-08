namespace Tracon;

/// <summary>
/// Calculates which quota period an instant falls into, and when that period resets.
/// </summary>
/// <remarks>
/// <para>
/// Pure logic: it holds no state and reads no clock. This lets a unit test
/// exercise day and month boundaries, daylight-saving transitions, and month
/// ends without depending on real time.
/// </para>
/// <para>
/// The period boundary is calculated in the <strong>local</strong> time
/// zone, not UTC. An admin who says "daily quota" means their own business day;
/// resetting by UTC would reset a <c>UTC+03</c> tenant's counter three hours
/// early, before noon.
/// </para>
/// </remarks>
internal static class QuotaPeriodCalculator
{
    /// <summary>Finds the first day of the period an instant falls into.</summary>
    /// <param name="instant">The instant (UTC or another offset).</param>
    /// <param name="period">The period interval.</param>
    /// <param name="timeZone">The time zone the period boundary is calculated in.</param>
    /// <returns>The first day of the period, in the local calendar.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is <see langword="null"/>.</exception>
    public static DateOnly GetPeriodStart(DateTimeOffset instant, QuotaPeriod period, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var local = TimeZoneInfo.ConvertTime(instant, timeZone);
        var date = DateOnly.FromDateTime(local.DateTime);

        return period switch
        {
            QuotaPeriod.Monthly => new DateOnly(date.Year, date.Month, 1),
            _ => date,
        };
    }

    /// <summary>Finds when the period an instant falls into will reset.</summary>
    /// <param name="instant">The instant.</param>
    /// <param name="period">The period interval.</param>
    /// <param name="timeZone">The time zone the period boundary is calculated in.</param>
    /// <returns>The instant the next period starts (UTC).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The returned value is reflected to the client as "when it resets" in
    /// <c>Retry-After</c> and in <c>ProblemDetails</c>.
    /// </remarks>
    public static DateTimeOffset GetPeriodEnd(DateTimeOffset instant, QuotaPeriod period, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var start = GetPeriodStart(instant, period, timeZone);

        var nextStart = period switch
        {
            QuotaPeriod.Monthly => start.AddMonths(1),
            _ => start.AddDays(1),
        };

        return ToUtcInstant(nextStart, timeZone);
    }

    /// <summary>Converts a period start into midnight in that time zone.</summary>
    /// <param name="date">The first day of the period (local calendar).</param>
    /// <param name="timeZone">The time zone.</param>
    /// <returns>The UTC equivalent of local midnight.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// During a daylight-saving transition, local midnight may <em>not
    /// exist</em> (spring forward) or may be <em>valid twice</em> (fall back).
    /// A time that does not exist is moved to right after the transition; for
    /// an ambiguous time, the <strong>earlier</strong> offset is chosen. Both
    /// are defined behavior instead of silently producing a wrong result.
    /// </remarks>
    public static DateTimeOffset ToUtcInstant(DateOnly date, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(midnight))
        {
            // Midnight was swallowed by a spring-forward transition: find the
            // first valid instant. A transition lasts at most a few hours;
            // stepping minute by minute is safe under every time zone rule.
            for (var minutes = 1; minutes <= 24 * 60; minutes++)
            {
                var candidate = midnight.AddMinutes(minutes);

                if (!timeZone.IsInvalidTime(candidate))
                {
                    midnight = candidate;
                    break;
                }
            }
        }

        // 🚨 At an ambiguous (twice-occurring) time, GetUtcOffset returns the STANDARD offset, which is the later of
        // the two instants; the earlier one has the larger offset. The period boundary starting early is
        // preferred over starting late — the quota may reset a minute early, but no consumption is ever
        // written to the wrong period — so the larger offset is asked for by name.
        var offset = timeZone.IsAmbiguousTime(midnight)
            ? timeZone.GetAmbiguousTimeOffsets(midnight).Max()
            : timeZone.GetUtcOffset(midnight);

        var instant = new DateTimeOffset(midnight, offset).ToUniversalTime();

        // The zone's own conversion is the authority on which date an instant is on, and GetPeriodStart asks it.
        // Where a rule moves the clock at local midnight (Paraguay, Chile, Greenland), the offset above can be the one
        // from after the move and the instant lands an hour inside the previous date. Walk to the first instant that
        // is on the date. The backward walk stops at a few hours, the longest a rule moves a clock; the forward walk
        // can have to cross a whole date, because a zone that moves across the date line skips one (Samoa and
        // Tokelau skipped 2011-12-30), and the first instant on the date is then the first instant of the next one.
        for (var step = 0; step < MaxForwardSteps && DateOf(instant, timeZone) < date; step++)
        {
            instant = instant.AddMinutes(1);
        }

        for (var step = 0; step < MaxAdjustSteps && DateOf(instant.AddMinutes(-1), timeZone) >= date; step++)
        {
            instant = instant.AddMinutes(-1);
        }

        return FirstTickOnTheDate(instant, date, timeZone);
    }

    /// <summary>
    /// Narrows an instant that is on <paramref name="date"/> and whose minute before is not, to the first tick that is.
    /// </summary>
    /// <remarks>
    /// A rule that moves the clock at midnight is written by Windows as 23:59:59.999 of the day before, and the zone then
    /// moves the clock at that millisecond: the first instant of the new date is a millisecond before the whole minute the
    /// walk above stops at, and a tick before the period end the old period would already be over. Inside one minute the
    /// date changes once, so a bisection at tick resolution finds the instant.
    /// </remarks>
    private static DateTimeOffset FirstTickOnTheDate(DateTimeOffset instant, DateOnly date, TimeZoneInfo timeZone)
    {
        var before = instant.AddMinutes(-1);

        if (DateOf(instant, timeZone) < date || DateOf(before, timeZone) >= date)
        {
            return instant;
        }

        var low = before.UtcTicks;
        var high = instant.UtcTicks;

        while (high - low > 1)
        {
            var middle = low + ((high - low) / 2);

            if (DateOf(new DateTimeOffset(middle, TimeSpan.Zero), timeZone) >= date)
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        return new DateTimeOffset(high, TimeSpan.Zero);
    }

    /// <summary>The longest, in minutes, the first instant of a date can be before the offset-based estimate.</summary>
    private const int MaxAdjustSteps = 4 * 60;

    /// <summary>
    /// The longest, in minutes, the first instant of a date can be after the offset-based estimate: a skipped date plus
    /// the hours a rule moves a clock. Only a zone that skipped a date walks far; every other zone stops within a few steps.
    /// </summary>
    private const int MaxForwardSteps = 36 * 60;

    private static DateOnly DateOf(DateTimeOffset instant, TimeZoneInfo timeZone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);

    /// <summary>Calculates the start of every period a rule set touches.</summary>
    /// <param name="instant">The instant.</param>
    /// <param name="timeZone">The time zone.</param>
    /// <returns>The first day of the period, for each period interval.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Consumption is written to both period counters: a tenant can define both
    /// a daily and a monthly quota at the same time, and the two are counted independently.
    /// </remarks>
    public static IReadOnlyDictionary<QuotaPeriod, DateOnly> GetAllPeriodStarts(
        DateTimeOffset instant,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return new Dictionary<QuotaPeriod, DateOnly>
        {
            [QuotaPeriod.Daily] = GetPeriodStart(instant, QuotaPeriod.Daily, timeZone),
            [QuotaPeriod.Monthly] = GetPeriodStart(instant, QuotaPeriod.Monthly, timeZone),
        };
    }
}
