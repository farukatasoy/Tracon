namespace Tracon;

/// <summary>
/// The range of periods a <see cref="PeriodicTimer"/> accepts, as the single
/// check behind every option that becomes a background timer's period.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="PeriodicTimer"/> takes a period from one millisecond to
/// <c>uint.MaxValue - 1</c> milliseconds (about 49.7 days); any other value
/// throws <see cref="ArgumentOutOfRangeException"/> when the timer is created.
/// <see cref="Task.Delay(TimeSpan)"/> has the same ceiling. Every Tracon
/// background service creates its timer inside <c>ExecuteAsync</c>, and
/// .NET's default <c>BackgroundServiceExceptionBehavior</c> is
/// <c>StopHost</c>: one wrong interval in configuration stopped the whole host
/// after it had reported "Application started". Measured with
/// <c>Tracon:Approvals:ScanInterval=00:00:00</c> on the sample app — approval
/// expiration is on by default.
/// </para>
/// <para>
/// A validator checks the option with <see cref="IsValid"/> so the host fails
/// at startup with the option's name instead. A feature that is off creates no
/// timer, so its interval is checked only while the feature is on.
/// </para>
/// </remarks>
internal static class TimerPeriod
{
    /// <summary>The longest accepted period in whole milliseconds (about 49.7 days).</summary>
    public const long MaxMilliseconds = uint.MaxValue - 1L;

    /// <summary>Reports whether a <see cref="PeriodicTimer"/> accepts the period.</summary>
    /// <param name="period">The period.</param>
    /// <returns><see langword="true"/> for a period from one millisecond to about 49.7 days.</returns>
    public static bool IsValid(TimeSpan period)
        => period >= TimeSpan.FromMilliseconds(1) && period.TotalMilliseconds <= MaxMilliseconds;

    /// <summary>Builds the startup failure for an option a timer cannot take.</summary>
    /// <param name="option">The option, as <c>Type.Property</c>.</param>
    /// <param name="actual">The configured value.</param>
    /// <param name="condition">When the check applies, for example <c>" when AutoRollbackEnabled is on"</c>; empty when always.</param>
    /// <returns>The failure message.</returns>
    public static string Failure(string option, TimeSpan actual, string condition = "")
        => $"{option} must be between 1 and {MaxMilliseconds} milliseconds{condition}. Actual value: {actual}.";
}
