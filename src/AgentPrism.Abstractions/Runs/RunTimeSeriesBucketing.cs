namespace AgentPrism;

/// <summary>
/// Zaman serisi kova hesabinin paylasilan saf mantigi. Hem <c>InMemoryRunStore</c>
/// hem <c>PostgresRunStore</c> bu turu kullanir; boylece kova sinirlamasi ve
/// yuvarlama kurallari iki depoda asla birbirinden sapmaz.
/// </summary>
public static class RunTimeSeriesBucketing
{
    /// <summary>Bir sorgunun uretebilecegi en fazla kova sayisi.</summary>
    public const int MaxBuckets = 500;

    /// <summary>Bir kovanin genisligini dondurur.</summary>
    public static TimeSpan StepFor(TimeSeriesBucket bucket) => bucket switch
    {
        TimeSeriesBucket.Hour => TimeSpan.FromHours(1),
        TimeSeriesBucket.Day => TimeSpan.FromDays(1),
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, message: null),
    };

    /// <summary>Bir zaman damgasini kova sinirina yuvarlar (UTC).</summary>
    public static DateTimeOffset Truncate(DateTimeOffset value, TimeSeriesBucket bucket)
    {
        var utc = value.ToUniversalTime();
        return bucket == TimeSeriesBucket.Hour
            ? new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, minute: 0, second: 0, TimeSpan.Zero)
            : new DateTimeOffset(utc.Year, utc.Month, utc.Day, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    }

    /// <summary>
    /// Istenen aralik ve kova genisliginin <see cref="MaxBuckets"/>'i asmadigini
    /// dogrular.
    /// </summary>
    /// <exception cref="AgentPrismException">Aralik en fazla kova sayisini asiyor.</exception>
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
        throw new AgentPrismException(
            $"The requested range produces {count} buckets; at most {MaxBuckets} are allowed.{suggestion}");
    }
}
