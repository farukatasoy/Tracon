using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>A fake monitor that returns a single, unchanging settings value in tests.</summary>
internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
