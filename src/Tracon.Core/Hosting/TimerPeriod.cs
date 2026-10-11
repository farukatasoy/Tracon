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
/// <para>
/// A timeout meets the same ceiling: <c>CancellationTokenSource.CancelAfter</c>
/// and its <see cref="TimeSpan"/> constructor throw above about 49.7 days where the
/// timeout is armed - in a run, a request or a shutdown. A timeout that ends up as an
/// HTTP request timeout has a lower ceiling, <see cref="int.MaxValue"/> milliseconds
/// (about 24.8 days): <c>HttpClient.Timeout</c> and the SDKs that take an
/// <see cref="int"/> refuse or wrap anything longer (<see cref="IsValidHttpTimeout"/>).
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

    /// <summary>The longest accepted HTTP request timeout in whole milliseconds (about 24.8 days).</summary>
    public const long MaxHttpTimeoutMilliseconds = int.MaxValue;

    /// <summary>Reports whether an HTTP request timeout is in the range every HTTP stack takes.</summary>
    /// <param name="timeout">The timeout.</param>
    /// <returns><see langword="true"/> for a timeout from one millisecond to about 24.8 days.</returns>
    public static bool IsValidHttpTimeout(TimeSpan timeout)
        => timeout >= TimeSpan.FromMilliseconds(1) && timeout.TotalMilliseconds <= MaxHttpTimeoutMilliseconds;

    /// <summary>Builds the startup failure for an HTTP request timeout out of range.</summary>
    /// <param name="option">The option, as <c>Type.Property</c>.</param>
    /// <param name="actual">The configured value.</param>
    /// <returns>The failure message.</returns>
    public static string HttpTimeoutFailure(string option, TimeSpan actual)
        => $"{option} must be between 1 and {MaxHttpTimeoutMilliseconds} milliseconds. Actual value: {actual}.";

    /// <summary>Builds the startup failure for an option a timer cannot take.</summary>
    /// <param name="option">The option, as <c>Type.Property</c>.</param>
    /// <param name="actual">The configured value.</param>
    /// <param name="condition">When the check applies, for example <c>" when AutoRollbackEnabled is on"</c>; empty when always.</param>
    /// <returns>The failure message.</returns>
    public static string Failure(string option, TimeSpan actual, string condition = "")
        => $"{option} must be between 1 and {MaxMilliseconds} milliseconds{condition}. Actual value: {actual}.";
}
