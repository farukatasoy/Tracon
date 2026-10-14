namespace Tracon.Workflows.UnitTests.Fakes;

/// <summary>A time source whose reading a test sets; its timers are the real ones.</summary>
/// <remarks>
/// The expiry service compares the rows' times with <see cref="GetUtcNow"/> once per scan, and a
/// test drives one scan by hand, so only the reading needs to be controlled.
/// </remarks>
internal sealed class AdjustableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the reading forward.</summary>
    /// <param name="amount">How far.</param>
    public void Advance(TimeSpan amount) => _now += amount;
}
