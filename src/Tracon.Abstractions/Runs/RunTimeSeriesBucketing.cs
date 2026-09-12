namespace Tracon;

/// <summary>
/// The shared pure logic for time-series bucket computation. Both
/// <c>InMemoryRunStore</c> and <c>PostgresRunStore</c> use this type, so the
/// bucket limit and rounding rules never diverge between the two stores.
/// </summary>
public static class RunTimeSeriesBucketing
{
    /// <summary>The maximum number of buckets a query may produce.</summary>
    public const int MaxBuckets = 500;

    /// <summary>Returns a bucket's width.</summary>
    public static TimeSpan StepFor(TimeSeriesBucket bucket) => bucket switch
    {
        TimeSeriesBucket.Hour => TimeSpan.FromHours(1),
        TimeSeriesBucket.Day => TimeSpan.FromDays(1),
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, message: null),
    };

    /// <summary>Rounds a timestamp down to the bucket boundary (UTC).</summary>
    public static DateTimeOffset Truncate(DateTimeOffset value, TimeSeriesBucket bucket)
    {
        var utc = value.ToUniversalTime();
        return bucket == TimeSeriesBucket.Hour
            ? new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, minute: 0, second: 0, TimeSpan.Zero)
            : new DateTimeOffset(utc.Year, utc.Month, utc.Day, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    }

    /// <summary>
    /// Validates that the requested range and bucket width does not exceed <see cref="MaxBuckets"/>.
    /// </summary>
    /// <exception cref="TraconException">The range exceeds the maximum number of buckets.</exception>
    public static void Validate(DateTimeOffset from, DateTimeOffset to, TimeSeriesBucket bucket)
    {
        var span = to - from;
        var count = span <= TimeSpan.Zero ? 0 : (long)Math.Ceiling(span / StepFor(bucket));
        if (count <= MaxBuckets)
        {
            return;
        }

        var suggestion = bucket == TimeSeriesBucket.Hour
            ? " Suggested bucket: day."
            : " Narrow the range.";
        throw new TraconException(
            $"The requested range produces {count} buckets; at most {MaxBuckets} are allowed.{suggestion}");
    }
}
