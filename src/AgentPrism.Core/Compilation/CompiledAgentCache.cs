using System.Collections.Concurrent;
using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Derlenmis agent'lari <c>(ad, surum, skill parmak izi)</c> anahtariyla onbellege alir.
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
        => GetOrAdd(name, version, string.Empty, factory);

    /// <summary>
    /// Agent'i bagimlilik parmak iziyle birlikte onbellekten getirir; yoksa
    /// <paramref name="factory"/> ile uretip ekler.
    /// </summary>
    /// <param name="name">Agent adi.</param>
    /// <param name="version">Tanim surumu.</param>
    /// <param name="dependencyFingerprint">
    /// Tanimin <em>disindaki</em> bagimliliklarin guncel parmak izi: skill'ler ve
    /// cagrilabilir alt agent'lar. Bunlar tanimin kendi surumunu artirmadan
    /// degisebildigi icin ayri bir anahtar bileseni gerekir.
    /// </param>
    /// <param name="factory">Onbellekte yoksa cagrilan uretici.</param>
    /// <returns>Derlenmis agent.</returns>
    public AIAgent GetOrAdd(string name, int version, string dependencyFingerprint, Func<AIAgent> factory)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(dependencyFingerprint);
        ArgumentNullException.ThrowIfNull(factory);

        // GetOrAdd(key, valueFactory) ayni anahtar icin fabrikayi birden cok kez
        // calistirabilir. Agent uretimi yan etkisizdir, bu yuzden sorun degil;
        // fazla uretilen ornek atilir.
        return _entries.GetOrAdd(new CacheKey(name, version, dependencyFingerprint), _ => factory());
    }

    /// <summary>Birden cok bagimlilik parmak izini tek bir anahtar bileseninde birlestirir.</summary>
    /// <param name="first">Ilk parmak izi.</param>
    /// <param name="second">Ikinci parmak izi.</param>
    /// <returns>Birlesik parmak izi.</returns>
    /// <remarks>
    /// Ayrac zorunludur: parmak izleri sabit uzunlukta olmayabilir ve dogrudan
    /// birlestirme iki farkli ciftin ayni dizeyi uretmesine izin verirdi.
    /// </remarks>
    public static string CombineFingerprints(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return second.Length == 0 ? first : string.Concat(first, "|", second);
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

    private readonly record struct CacheKey(string Name, int Version, string DependencyFingerprint);
}
