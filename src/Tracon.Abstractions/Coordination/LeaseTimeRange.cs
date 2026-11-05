using System.Globalization;

namespace Tracon;

/// <summary>
/// The span of time a lease store accepts for a lease, a renewal, a retry delay or an orphan threshold, and the one
/// place that says so.
/// </summary>
/// <remarks>
/// <para>
/// A lease lasts from one millisecond to a little over 68 years. The floor is one whole millisecond because SQL stores
/// keep milliseconds: half a millisecond would be written as zero, a lease that is over before it begins. The ceiling
/// is as far as SQL Server's <c>DATEADD</c> reaches in whole seconds (<see cref="int.MaxValue"/> seconds), the one
/// hard limit among the stores; the others take the same ceiling so that a lease the in-memory store accepts is a
/// lease every store accepts.
/// </para>
/// <para>
/// The stores call <see cref="ThrowIfInvalid"/> first, before they check the cancellation token and before they build a
/// statement, so a refused call writes nothing. Before this check the four stores answered the same input in two ways:
/// the in-memory store and SQLite wrote a lease of zero or less (over at once) and failed with an arithmetic overflow on
/// the largest values; PostgreSQL and SQL Server cut the largest values to the ceiling without a word.
/// </para>
/// </remarks>
internal static class LeaseTimeRange
{
    /// <summary>The longest duration, in milliseconds: <see cref="int.MaxValue"/> seconds, a little over 68 years.</summary>
    internal const long MaximumMilliseconds = int.MaxValue * 1000L;

    /// <summary>The parameter name a refused <see cref="JobLeaseRequest.LeaseDuration"/> is reported under.</summary>
    public const string LeaseRequestDurationName = "request.LeaseDuration";

    /// <summary>The shortest lease: one whole millisecond.</summary>
    public static readonly TimeSpan Minimum = TimeSpan.FromMilliseconds(1);

    /// <summary>The longest lease or delay: <see cref="int.MaxValue"/> seconds, a little over 68 years.</summary>
    public static readonly TimeSpan Maximum = TimeSpan.FromMilliseconds(MaximumMilliseconds);

    /// <summary>Refuses a lease, renewal or threshold duration outside <see cref="Minimum"/> to <see cref="Maximum"/>.</summary>
    /// <param name="value">The duration.</param>
    /// <param name="paramName">The name of the parameter the caller passed it in.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is outside the range.</exception>
    public static void ThrowIfInvalid(TimeSpan value, string paramName)
    {
        if (value < Minimum || value > Maximum)
        {
            throw new ArgumentOutOfRangeException(paramName, value, DurationMessage());
        }
    }

    /// <summary>
    /// Refuses a retry delay that is neither <see langword="null"/> nor between zero and <see cref="Maximum"/>.
    /// </summary>
    /// <param name="value">The delay; <see langword="null"/> and zero both mean "at once".</param>
    /// <param name="paramName">The name of the parameter the caller passed it in.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative or longer than <see cref="Maximum"/>.</exception>
    public static void ThrowIfInvalidRetryDelay(TimeSpan? value, string paramName)
    {
        if (value is { } delay && (delay < TimeSpan.Zero || delay > Maximum))
        {
            throw new ArgumentOutOfRangeException(paramName, delay, DelayMessage());
        }
    }

    /// <summary>
    /// Brings a retry delay a handler asked for into the range a store accepts: a negative delay becomes
    /// <see langword="null"/> (at once), a delay past <see cref="Maximum"/> becomes <see cref="Maximum"/>.
    /// </summary>
    /// <param name="value">The delay the handler gave.</param>
    /// <param name="wasChanged">Whether <paramref name="value"/> was outside the range and has been changed.</param>
    /// <returns>A delay <see cref="ThrowIfInvalidRetryDelay"/> accepts.</returns>
    /// <remarks>
    /// What a handler asks for is a request, not a call to the store, so it is not an error: the worker brings it into
    /// range and says so in the log.
    /// </remarks>
    public static TimeSpan? ClampRetryDelay(TimeSpan? value, out bool wasChanged)
    {
        wasChanged = false;

        if (value is not { } delay)
        {
            return null;
        }

        if (delay < TimeSpan.Zero)
        {
            wasChanged = true;

            return null;
        }

        if (delay > Maximum)
        {
            wasChanged = true;

            return Maximum;
        }

        return delay;
    }

    /// <summary>Gets the sentence that says what a lease duration may be, for a message that names the setting that broke it.</summary>
    /// <returns>The range, spelled in milliseconds and years.</returns>
    public static string Describe()
        => $"from {Minimum.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)} ms to " +
           $"{MaximumMilliseconds.ToString(CultureInfo.InvariantCulture)} ms (about 68 years)";

    private static string DurationMessage()
        => $"A lease lasts {Describe()}. Timeout.InfiniteTimeSpan is minus one millisecond, not a lease that never ends.";

    private static string DelayMessage()
        => $"A retry delay is null, zero or up to {MaximumMilliseconds.ToString(CultureInfo.InvariantCulture)} ms " +
           "(about 68 years); a negative delay is not \"at once\", pass null or zero for that.";
}
