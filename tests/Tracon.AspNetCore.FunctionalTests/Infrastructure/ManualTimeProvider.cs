namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>A fake time source that lets a test advance time manually.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset? start = null)
    {
        _now = start ?? DateTimeOffset.UtcNow;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan amount) => _now += amount;
}
