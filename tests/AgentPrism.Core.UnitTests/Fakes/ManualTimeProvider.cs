namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>Testlerin zamani elle ilerletebildigi sahte zaman kaynagi.</summary>
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
