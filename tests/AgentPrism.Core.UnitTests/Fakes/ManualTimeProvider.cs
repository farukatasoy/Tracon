namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>A fake time source that lets tests advance time manually.</summary>
/// <remarks>
/// Fakes BOTH clocks a <see cref="TimeProvider"/> exposes. <see cref="GetUtcNow"/>
/// is the wall clock and may be moved in either direction; <see cref="GetTimestamp"/>
/// is the monotonic clock and only ever moves forward, exactly like the real
/// one. Code that measures a duration with
/// <c>GetTimestamp()</c>/<c>GetElapsedTime()</c> can therefore be tested against
/// a wall clock that jumps backwards without the measurement going negative.
/// </remarks>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;
    private long _timestamp;

    public ManualTimeProvider(DateTimeOffset? start = null)
    {
        _now = start ?? DateTimeOffset.UtcNow;
    }

    /// <summary>One tick per 100 ns, so an advance maps to the timestamp one-to-one.</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _now;

    public override long GetTimestamp() => _timestamp;

    /// <summary>Moves both clocks forward. A negative amount moves only the wall clock.</summary>
    /// <param name="amount">How far to move.</param>
    public void Advance(TimeSpan amount)
    {
        _now += amount;

        // The monotonic clock never runs backwards, so a negative advance
        // leaves it where it is.
        if (amount > TimeSpan.Zero)
        {
            _timestamp += amount.Ticks;
        }
    }
}
