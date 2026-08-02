namespace AgentPrism;

/// <summary>
/// Gomulu bir arayuz varliginin tanimi.
/// </summary>
/// <remarks>
/// Icerik derleme aninda uretildigi ve bir daha degismedigi icin tum alanlar
/// degismezdir; tek ornek tum isteklerce paylasilir.
/// </remarks>
internal sealed class UiAsset
{
    /// <summary>Varligin gomulu kaynak adi.</summary>
    public required string ResourceName { get; init; }

    /// <summary>Taban yola gore varlik yolu. Ornek: <c>assets/index-a1b2c3.js</c>.</summary>
    public required string Path { get; init; }

    /// <summary>Icerik tipi.</summary>
    public required string ContentType { get; init; }

    /// <summary>Icerik Brotli ile sikistirilmis olarak mi saklaniyor.</summary>
    public required bool IsBrotli { get; init; }

    /// <summary>
    /// Dosya adi icerigin ozetini tasiyor mu. Tasiyorsa yanit sonsuza kadar
    /// onbelleklenebilir.
    /// </summary>
    /// <remarks>
    /// Vite <c>assets/</c> altindaki her dosyaya icerik ozeti ekler; icerik
    /// degistiginde ad da degisir. Bu yuzden o dosyalar <c>immutable</c> olarak
    /// isaretlenir. <c>index.html</c> sabit adlidir ve her zaman yeniden dogrulanir.
    /// </remarks>
    public required bool Immutable { get; init; }
}
