using System.Globalization;

namespace Tracon;

/// <summary>
/// A hand-written parser for the standard five-field cron subset
/// (<c>minute hour day-of-month month day-of-week</c>).
/// </summary>
/// <remarks>
/// <para>
/// No package is taken on: <c>Cronos</c> or <c>NCrontab</c> are small
/// packages, but every new dependency passes through to the
/// consumer. The five-field parser is ~150 lines and easy to test.
/// </para>
/// <para>
/// Supported syntax: <c>*</c>, a fixed value, an <c>N-M</c> range, a
/// <c>value1,value2,...</c> list, and <c>/step</c> (may be appended to any
/// of the above). Vixie-cron extensions such as a seconds field, <c>L</c>,
/// <c>W</c>, and <c>#</c> are <strong>not supported</strong> and are rejected
/// during parsing with a <see cref="FormatException"/>.
/// </para>
/// <para>
/// If day-of-week <em>and</em> day-of-month are both restricted (neither is
/// <c>*</c>), the standard cron rule applies: a match occurs when
/// <strong>either one</strong> holds (OR), not both at once.
/// </para>
/// </remarks>
internal sealed class CronExpression
{
    private readonly bool[] _minute;
    private readonly bool[] _hour;
    private readonly bool[] _dayOfMonth;
    private readonly bool[] _month;
    private readonly bool[] _dayOfWeek;
    private readonly bool _dayOfMonthRestricted;
    private readonly bool _dayOfWeekRestricted;
    private readonly bool _repeatsThroughRepeatedHour;

    private CronExpression(
        bool[] minute,
        bool[] hour,
        bool[] dayOfMonth,
        bool[] month,
        bool[] dayOfWeek,
        bool dayOfMonthRestricted,
        bool dayOfWeekRestricted,
        bool repeatsThroughRepeatedHour)
    {
        _minute = minute;
        _hour = hour;
        _dayOfMonth = dayOfMonth;
        _month = month;
        _dayOfWeek = dayOfWeek;
        _dayOfMonthRestricted = dayOfMonthRestricted;
        _dayOfWeekRestricted = dayOfWeekRestricted;
        _repeatsThroughRepeatedHour = repeatsThroughRepeatedHour;
    }

    /// <summary>Parses a cron expression.</summary>
    /// <param name="expression">A five-field cron expression.</param>
    /// <returns>The parsed expression.</returns>
    /// <exception cref="ArgumentException"><paramref name="expression"/> is empty or whitespace only.</exception>
    /// <exception cref="FormatException">The expression does not have five fields, or contains unsupported syntax.</exception>
    public static CronExpression Parse(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (fields.Length != 5)
        {
            throw new FormatException(
                $"A cron expression must have exactly 5 fields (minute hour day-of-month month day-of-week). " +
                $"Field count found: {fields.Length}. A seconds field is not supported.");
        }

        var minute = ParseField(fields[0], 0, 59, "minute", out _);
        var hour = ParseField(fields[1], 0, 23, "hour", out _);
        var dayOfMonth = ParseField(fields[2], 1, 31, "day-of-month", out var dayOfMonthRestricted);
        var month = ParseField(fields[3], 1, 12, "month", out _);
        var dayOfWeek = ParseField(fields[4], 0, 6, "day-of-week", out var dayOfWeekRestricted);

        // 🚨 Each field is valid on its own, so "0 0 30 2 *" parses; no date has a 30th of February. Left
        // to the search, it ran minute by minute to the end of its window and then failed, in an endpoint
        // that answered 500 and in a worker that retried the broken row on every tick.
        if (!(dayOfMonthRestricted && dayOfWeekRestricted) && !AnyDateExists(month, dayOfMonth))
        {
            throw new FormatException(
                "No date matches the day-of-month and month fields together: " +
                $"day-of-month '{fields[2]}' never occurs in month '{fields[3]}'.");
        }

        // An expression that runs in every hour, or in steps of hours, keeps its rhythm; one that names its hours
        // ("30 1 * * *", "0 1,2 * * *") is a time of day.
        var repeatsThroughRepeatedHour = fields[1].Contains('/', StringComparison.Ordinal) || Array.TrueForAll(hour, static allowed => allowed);

        return new CronExpression(
            minute, hour, dayOfMonth, month, dayOfWeek, dayOfMonthRestricted, dayOfWeekRestricted, repeatsThroughRepeatedHour);
    }

    /// <summary>Attempts to parse a cron expression.</summary>
    /// <param name="expression">A five-field cron expression.</param>
    /// <param name="result">The parsed expression, if successful.</param>
    /// <returns><see langword="true"/> if parsing succeeded.</returns>
    public static bool TryParse(string expression, out CronExpression? result)
    {
        try
        {
            result = Parse(expression);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            result = null;
            return false;
        }
    }

    /// <summary>
    /// Computes the first time after the given instant that matches the
    /// expression.
    /// </summary>
    /// <param name="afterUtc">The instant the search starts from (UTC, exclusive).</param>
    /// <param name="timeZone">The time zone the expression is interpreted in.</param>
    /// <returns>The next run time (UTC).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No matching time was found within 8 years.</exception>
    /// <remarks>
    /// <para>
    /// When daylight saving time skips forward, some local minutes never
    /// occur; these minutes are silently skipped.
    /// </para>
    /// <para>
    /// When the clock is set back, the local minutes of one stretch occur twice. An expression that runs in every
    /// hour, or in steps of hours (<c>*/5 * * * *</c>, <c>0 * * * *</c>, <c>0 */2 * * *</c>), runs in both passes, so
    /// its rhythm in elapsed time does not break. An expression that names its hours (<c>30 1 * * *</c>,
    /// <c>0 1,2 * * *</c>) is a time of day and runs once, in the first pass.
    /// </para>
    /// </remarks>
    public DateTimeOffset GetNextOccurrence(DateTimeOffset afterUtc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var forward = SearchForward(afterUtc, timeZone);

        // 🚨 The search walks local minutes forward and never returns to one it has passed. After the clock is set
        // back, the stretch that repeats has to be visited again: from the last minute of its first pass the next
        // run is the first minute of its second pass, an hour earlier on the wall clock than where the search is.
        return _repeatsThroughRepeatedHour && FindInSecondPass(afterUtc, timeZone) is { } again && again < forward
            ? again
            : forward;
    }

    private DateTimeOffset SearchForward(DateTimeOffset afterUtc, TimeZoneInfo timeZone)
    {
        var candidate = LocalMinute(afterUtc, timeZone).AddMinutes(1);

        // Eight years: the longest gap between two 29ths of February is eight years, across a century year.
        var limit = candidate.AddYears(8);

        while (candidate <= limit)
        {
            // Whole months and days that cannot match are stepped over; only the minutes of a day that can are
            // tried one by one.
            if (!_month[candidate.Month - 1])
            {
                candidate = new DateTime(candidate.Year, candidate.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(1);
                continue;
            }

            if (!DayMatches(candidate))
            {
                candidate = candidate.Date.AddDays(1);
                continue;
            }

            if (Matches(candidate) && Resolve(candidate, afterUtc, timeZone) is { } found)
            {
                return found;
            }

            candidate = candidate.AddMinutes(1);
        }

        throw new InvalidOperationException(
            "No next run time matching the cron expression was found within 8 years.");
    }

    /// <summary>Turns a matching local minute into the instant it runs, or <see langword="null"/> if it does not run after <paramref name="afterUtc"/>.</summary>
    private DateTimeOffset? Resolve(DateTime local, DateTimeOffset afterUtc, TimeZoneInfo timeZone)
    {
        if (timeZone.IsInvalidTime(local))
        {
            // Daylight saving time skipped forward: this local minute never occurred.
            return null;
        }

        if (!timeZone.IsAmbiguousTime(local))
        {
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
        }

        // The clock was set back: this minute occurs twice, first at the larger offset.
        var offsets = timeZone.GetAmbiguousTimeOffsets(local);
        var firstPass = new DateTimeOffset(local - offsets.Max(), TimeSpan.Zero);

        if (firstPass > afterUtc)
        {
            return firstPass;
        }

        var secondPass = new DateTimeOffset(local - offsets.Min(), TimeSpan.Zero);

        return _repeatsThroughRepeatedHour && secondPass > afterUtc ? secondPass : null;
    }

    /// <summary>
    /// When <paramref name="afterUtc"/> is in the first pass of a stretch of local time that occurs twice, finds
    /// the first minute of that stretch that matches, in its second pass.
    /// </summary>
    private DateTimeOffset? FindInSecondPass(DateTimeOffset afterUtc, TimeZoneInfo timeZone)
    {
        var local = LocalMinute(afterUtc, timeZone);

        if (!timeZone.IsAmbiguousTime(local))
        {
            return null;
        }

        var offsets = timeZone.GetAmbiguousTimeOffsets(local);

        if (timeZone.GetUtcOffset(afterUtc) != offsets.Max())
        {
            return null; // already in the second pass: the forward search covers what is left of it
        }

        // The repeated stretch is a few hours at most; the bound only keeps a broken time-zone table from looping.
        var minute = local;

        for (var step = 0; step < 24 * 60 && timeZone.IsAmbiguousTime(minute.AddMinutes(-1)); step++)
        {
            minute = minute.AddMinutes(-1);
        }

        for (var step = 0; step < 24 * 60 && timeZone.IsAmbiguousTime(minute); step++)
        {
            if (Matches(minute))
            {
                return new DateTimeOffset(minute - GetSecondOffset(timeZone, minute), TimeSpan.Zero);
            }

            minute = minute.AddMinutes(1);
        }

        return null;

        static TimeSpan GetSecondOffset(TimeZoneInfo zone, DateTime ambiguous) => zone.GetAmbiguousTimeOffsets(ambiguous).Min();
    }

    private static DateTime LocalMinute(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, timeZone);

        return new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified);
    }

    private bool Matches(DateTime local)
    {
        if (!_minute[local.Minute])
        {
            return false;
        }

        if (!_hour[local.Hour])
        {
            return false;
        }

        return _month[local.Month - 1] && DayMatches(local);
    }

    private bool DayMatches(DateTime local)
    {
        var domMatch = _dayOfMonth[local.Day - 1];
        var dowMatch = _dayOfWeek[(int)local.DayOfWeek];

        // Standard cron rule: OR if both are restricted, AND otherwise (an
        // unrestricted field always returns "true" anyway, so it produces the
        // same result as AND; the rule only differs when both are restricted).
        return _dayOfMonthRestricted && _dayOfWeekRestricted
            ? domMatch || dowMatch
            : domMatch && dowMatch;
    }

    /// <summary>Tells whether some allowed month has an allowed day, counting February as 29 days long.</summary>
    private static bool AnyDateExists(bool[] month, bool[] dayOfMonth)
    {
        for (var monthNumber = 1; monthNumber <= 12; monthNumber++)
        {
            if (!month[monthNumber - 1])
            {
                continue;
            }

            // 2024 is a leap year: the longest each month can be.
            var length = DateTime.DaysInMonth(2024, monthNumber);

            for (var day = 1; day <= length; day++)
            {
                if (dayOfMonth[day - 1])
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool[] ParseField(string field, int min, int max, string fieldName, out bool restricted)
    {
        restricted = !string.Equals(field, "*", StringComparison.Ordinal);

        var allowed = new bool[max - min + 1];

        foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = part;
            var step = 1;
            var slashIndex = segment.IndexOf('/');

            if (slashIndex >= 0)
            {
                var stepText = segment[(slashIndex + 1)..];

                if (!int.TryParse(stepText, NumberStyles.Integer, CultureInfo.InvariantCulture, out step) || step <= 0)
                {
                    throw new FormatException($"'{part}' carries an invalid step value (field: {fieldName}).");
                }

                segment = segment[..slashIndex];
            }

            int rangeStart, rangeEnd;

            if (string.Equals(segment, "*", StringComparison.Ordinal))
            {
                rangeStart = min;
                rangeEnd = max;
            }
            else
            {
                var dashIndex = segment.IndexOf('-');

                if (dashIndex >= 0)
                {
                    if (!int.TryParse(segment[..dashIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out rangeStart)
                        || !int.TryParse(segment[(dashIndex + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out rangeEnd))
                    {
                        throw new FormatException($"'{part}' is not a valid range (field: {fieldName}).");
                    }
                }
                else
                {
                    if (!int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out rangeStart))
                    {
                        throw new FormatException(
                            $"'{part}' is not a valid number (field: {fieldName}). " +
                            "A seconds field and the L/W/# extensions are not supported.");
                    }

                    // "5/15" is 5-59/15, as in every other cron implementation; a lone value without a step is just itself.
                    rangeEnd = slashIndex >= 0 ? max : rangeStart;
                }
            }

            if (rangeStart < min || rangeEnd > max || rangeStart > rangeEnd)
            {
                throw new FormatException(
                    $"'{part}' is outside the valid range ({min}-{max}) for field {fieldName}.");
            }

            for (var value = rangeStart; value <= rangeEnd; value += step)
            {
                allowed[value - min] = true;
            }
        }

        return allowed;
    }
}
