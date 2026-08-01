using System.Collections.Concurrent;
using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Derlenmis agent'lari <c>(ad, surum)</c> anahtariyla onbellege alir.
/// </summary>
/// <remarks>
/// Tanim guncellenince surum artar ve onbellek <em>dogal olarak</em> gecersizlesir.
/// Bu yuzden acik bir gecersiz kilma mantigi yoktur; eski surumun girdisi
/// <see cref="Evict"/> ile temizlenir.
/// </remarks>
public sealed class CompiledAgentCache
{
    private readonly ConcurrentDictionary<CacheKey, AIAgent> _entries = new();

    /// <summary>Onbellekteki girdi sayisi.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Agent'i onbellekten getirir; yoksa <paramref name="factory"/> ile uretip ekler.
    /// </summary>
    /// <param name="name">Agent adi.</param>
    /// <param name="version">Tanim surumu.</param>
    /// <param name="factory">Onbellekte yoksa cagrilan uretici.</param>
    /// <returns>Derlenmis agent.</returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    public AIAgent GetOrAdd(string name, int version, Func<AIAgent> factory)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(factory);

        // GetOrAdd(key, valueFactory) ayni anahtar icin fabrikayi birden cok kez
        // calistirabilir. Agent uretimi yan etkisizdir, bu yuzden sorun degil;
        // fazla uretilen ornek atilir.
        return _entries.GetOrAdd(new CacheKey(name, version), _ => factory());
    }

    /// <summary>Bir agent'in tum surumlerini onbellekten cikarir.</summary>
    /// <param name="name">Agent adi.</param>
    public void Evict(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (var key in _entries.Keys)
        {
            if (string.Equals(key.Name, name, StringComparison.Ordinal))
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    /// <summary>Onbellegi tamamen bosaltir.</summary>
    public void Clear() => _entries.Clear();

    private readonly record struct CacheKey(string Name, int Version);
}
