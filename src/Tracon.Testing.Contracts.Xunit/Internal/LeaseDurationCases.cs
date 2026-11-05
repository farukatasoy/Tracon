namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// The durations the lease contracts hand a store at the edge of the range the store interfaces accept, written
/// out so a third-party store is held to the same numbers the built-in stores are.
/// </summary>
/// <remarks>
/// The range is one millisecond to a little over 68 years: SQL stores keep whole milliseconds, and SQL Server's
/// <c>DATEADD</c> reaches no further than that in whole seconds. The numbers are spelled here and not read from
/// the product: a limit that moves must break these tests, not move with them.
/// </remarks>
internal static class LeaseDurationCases
{
    /// <summary>The longest duration a store accepts, in milliseconds.</summary>
    public const long LongestMilliseconds = 2_147_483_647_000L;

    /// <summary>The longest duration a store accepts.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromMilliseconds(LongestMilliseconds);

    /// <summary>The shortest duration a store accepts: the database keeps whole milliseconds.</summary>
    public static readonly TimeSpan Shortest = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// Durations a lease cannot have. <see cref="Timeout.InfiniteTimeSpan"/> is minus one millisecond and not "never
    /// expires"; half a millisecond is not a whole one.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> InvalidDurations =
    [
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(-1),
        Timeout.InfiniteTimeSpan,
        TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond / 2),
        Longest + TimeSpan.FromMilliseconds(1),
        TimeSpan.MaxValue,
        TimeSpan.MinValue,
    ];

    /// <summary>The durations a lease cannot have, as theory data.</summary>
    /// <returns>The durations, as theory data.</returns>
    public static TheoryData<TimeSpan> Invalid()
    {
        var data = new TheoryData<TimeSpan>();

        foreach (var duration in InvalidDurations)
        {
            data.Add(duration);
        }

        return data;
    }

    /// <summary>
    /// Delays a retry cannot wait. Zero is not here: it means "at once". <see cref="TimeSpan.Zero"/> and
    /// <see langword="null"/> are accepted by <see cref="IJobStore.ReleaseForRetryAsync"/>.
    /// </summary>
    /// <returns>The delays, as theory data.</returns>
    public static TheoryData<TimeSpan> InvalidRetryDelays() => new()
    {
        TimeSpan.FromMilliseconds(-1),
        Timeout.InfiniteTimeSpan,
        Longest + TimeSpan.FromMilliseconds(1),
        TimeSpan.MaxValue,
        TimeSpan.MinValue,
    };
}
