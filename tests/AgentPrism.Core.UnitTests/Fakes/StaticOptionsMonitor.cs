using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>Testlerde degismeyen tek bir ayar degeri dondüren sahte izleyici.</summary>
internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
