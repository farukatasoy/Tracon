namespace Tracon;

/// <summary>
/// The fixed-window decision both rate limiters make from a counter store's answer.
/// </summary>
/// <remarks>
/// <para>
/// The store only counts; the window, the comparison and <c>Retry-After</c> live here, once, so
/// the endpoint limiter and the inbound trigger limiter cannot drift apart and a consumer's store
/// cannot change them.
/// </para>
/// <para>
/// A rejected request is counted too. In a fixed window that is right: the counter starts again
/// when the window ends.
/// </para>
/// </remarks>
internal readonly record struct FixedWindowDecision(bool Allowed, TimeSpan RetryAfter)
{
    private static readonly TimeSpan MinimumRetryAfter = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Aligns a moment to the start of its window. The windows are cut from the Unix epoch, so
    /// every instance computes the same boundary for the same moment.
    /// </summary>
    /// <param name="now">The moment (UTC).</param>
    /// <param name="window">The window length. Must be greater than zero.</param>
    /// <returns>The start of the window that holds <paramref name="now"/>.</returns>
    public static DateTimeOffset AlignWindowStart(DateTimeOffset now, TimeSpan window)
    {
        var sinceEpoch = now.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        var windowTicks = window.Ticks;

        // % keeps the sign of the dividend; a moment before the epoch needs the floor.
        var remainder = sinceEpoch % windowTicks;

        if (remainder < 0)
        {
            remainder += windowTicks;
        }

        return new DateTimeOffset(now.UtcTicks - remainder, TimeSpan.Zero);
    }

    /// <summary>Reads the permit limit the limiters apply. A value below one is one.</summary>
    /// <param name="options">The rate limit settings.</param>
    /// <returns>The effective permit limit.</returns>
    public static int EffectivePermitLimit(TraconRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Math.Max(1, options.PermitLimit);
    }

    /// <summary>Reads the window the limiters apply. A window that is not positive is one minute.</summary>
    /// <param name="options">The rate limit settings.</param>
    /// <returns>The effective window.</returns>
    public static TimeSpan EffectiveWindow(TraconRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Window > TimeSpan.Zero ? options.Window : TimeSpan.FromMinutes(1);
    }

    /// <summary>Decides whether a counted request is within the limit.</summary>
    /// <param name="counted">The store's answer.</param>
    /// <param name="permitLimit">The permit limit.</param>
    /// <param name="window">The window length.</param>
    /// <param name="now">The moment the request arrived (UTC).</param>
    /// <returns>The decision. <see cref="RetryAfter"/> is a whole number of seconds, at least one.</returns>
    public static FixedWindowDecision Decide(
        RateLimitWindowCount counted,
        int permitLimit,
        TimeSpan window,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(counted);

        if (counted.Count <= permitLimit)
        {
            return new FixedWindowDecision(true, TimeSpan.Zero);
        }

        var remaining = counted.WindowStart + window - now;
        var seconds = Math.Max(MinimumRetryAfter.TotalSeconds, Math.Ceiling(remaining.TotalSeconds));

        return new FixedWindowDecision(false, TimeSpan.FromSeconds(seconds));
    }
}
