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
public sealed class CronExpression
{
    private readonly bool[] _minute;
    private readonly bool[] _hour;
    private readonly bool[] _dayOfMonth;
    private readonly bool[] _month;
    private readonly bool[] _dayOfWeek;
    private readonly bool _dayOfMonthRestricted;
    private readonly bool _dayOfWeekRestricted;

    private CronExpression(
        bool[] minute,
        bool[] hour,
        bool[] dayOfMonth,
        bool[] month,
        bool[] dayOfWeek,
        bool dayOfMonthRestricted,
        bool dayOfWeekRestricted)
    {
        _minute = minute;
        _hour = hour;
        _dayOfMonth = dayOfMonth;
        _month = month;
        _dayOfWeek = dayOfWeek;
        _dayOfMonthRestricted = dayOfMonthRestricted;
        _dayOfWeekRestricted = dayOfWeekRestricted;
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

        return new CronExpression(minute, hour, dayOfMonth, month, dayOfWeek, dayOfMonthRestricted, dayOfWeekRestricted);
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
    /// <exception cref="InvalidOperationException">No matching time was found within 4 years.</exception>
    /// <remarks>
    /// When daylight saving time skips forward, some local minutes never
    /// occur; these minutes are silently skipped. When the clock is set back,
    /// one minute occurs twice; <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/>
    /// picks the first (earlier) occurrence by default.
    /// </remarks>
    public DateTimeOffset GetNextOccurrence(DateTimeOffset afterUtc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var localAfter = TimeZoneInfo.ConvertTime(afterUtc, timeZone);
        var candidate = new DateTime(
            localAfter.Year,
            localAfter.Month,
            localAfter.Day,
            localAfter.Hour,
            localAfter.Minute,
            0,
            DateTimeKind.Unspecified).AddMinutes(1);

        var limit = candidate.AddYears(4);

        while (candidate <= limit)
        {
            if (Matches(candidate))
            {
                DateTime utc;

                try
                {
                    utc = TimeZoneInfo.ConvertTimeToUtc(candidate, timeZone);
                }
                catch (ArgumentException)
                {
                    // Daylight saving time skipped forward: this local minute never occurred.
                    candidate = candidate.AddMinutes(1);
                    continue;
                }

                return new DateTimeOffset(utc, TimeSpan.Zero);
            }

            candidate = candidate.AddMinutes(1);
        }

        throw new InvalidOperationException(
            "No next run time matching the cron expression was found within 4 years.");
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

        if (!_month[local.Month - 1])
        {
            return false;
        }

        var domMatch = _dayOfMonth[local.Day - 1];
        var dowMatch = _dayOfWeek[(int)local.DayOfWeek];

        // Standard cron rule: OR if both are restricted, AND otherwise (an
        // unrestricted field always returns "true" anyway, so it produces the
        // same result as AND; the rule only differs when both are restricted).
        return _dayOfMonthRestricted && _dayOfWeekRestricted
            ? domMatch || dowMatch
            : domMatch && dowMatch;
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

                    rangeEnd = rangeStart;
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
