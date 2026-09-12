namespace Tracon;

/// <summary>Shared pure logic for score trend bucket truncation (<see cref="RunScoreBucket"/>).</summary>
/// <remarks>
/// Used by <c>InMemoryRunScoreStore</c> directly; each SQL provider mirrors the
/// same rule in its own date-truncation SQL, so the bucket boundary never
/// diverges between implementations — a shared contract test checks this
/// across all four.
/// </remarks>
public static class RunScoreBucketing
{
    /// <summary>Rounds a timestamp down to its bucket boundary, in UTC.</summary>
    /// <param name="value">The timestamp to round down.</param>
    /// <param name="bucket">The bucket width.</param>
    /// <returns>The bucket's start (UTC). A <see cref="RunScoreBucket.Week"/> bucket starts on Monday (ISO 8601).</returns>
    public static DateTimeOffset Truncate(DateTimeOffset value, RunScoreBucket bucket)
    {
        var utc = value.ToUniversalTime();

        return bucket switch
        {
            RunScoreBucket.Hour => new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, minute: 0, second: 0, TimeSpan.Zero),
            RunScoreBucket.Day => new DateTimeOffset(utc.Year, utc.Month, utc.Day, hour: 0, minute: 0, second: 0, TimeSpan.Zero),
            RunScoreBucket.Week => TruncateToMonday(utc),
            _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, message: null),
        };
    }

    private static DateTimeOffset TruncateToMonday(DateTimeOffset utc)
    {
        var day = new DateTimeOffset(utc.Year, utc.Month, utc.Day, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

        // DayOfWeek: Sunday = 0 .. Saturday = 6. This maps it to "days since
        // Monday" (Monday = 0 .. Sunday = 6) without a locale-dependent
        // "first day of week" setting.
        var daysSinceMonday = ((int)day.DayOfWeek + 6) % 7;

        return day.AddDays(-daysSinceMonday);
    }
}
