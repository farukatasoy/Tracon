namespace AgentPrism;

/// <summary>
/// Metni sabit uzunlukta, ortusmeli parcalara boler.
/// </summary>
/// <remarks>
/// Bilerek basittir: baslik/anlam tabanli akilli parcalama kutuphane sinirinin
/// disindadir (bkz. <c>docs/51-VEKTOR-BELLEK-VE-RAG.md</c>, Acik Soru 4).
/// Tuketici kendi parcalarini dogrudan gonderebilir.
/// </remarks>
public static class TextChunker
{
    /// <summary>Metni ortusmeli parcalara boler.</summary>
    /// <param name="text">Parcalanacak metin.</param>
    /// <param name="chunkSize">Parca uzunlugu (karakter). Pozitif olmalidir.</param>
    /// <param name="chunkOverlap">
    /// Ardisik parcalarin ortusme uzunlugu (karakter). <paramref name="chunkSize"/>'dan
    /// kucuk olmalidir; aksi halde ilerleme durur.
    /// </param>
    /// <returns>Sirali parca metinleri. Bos metin icin bos liste.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="chunkSize"/> sifir veya negatifse, ya da <paramref name="chunkOverlap"/>
    /// negatifse veya <paramref name="chunkSize"/>'a esit/buyukse.
    /// </exception>
    public static IReadOnlyList<string> Split(string text, int chunkSize, int chunkOverlap)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(chunkSize, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkOverlap);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkOverlap, chunkSize);

        if (text.Length == 0)
        {
            return [];
        }

        var step = chunkSize - chunkOverlap;
        var chunks = new List<string>();

        for (var start = 0; start < text.Length; start += step)
        {
            var length = Math.Min(chunkSize, text.Length - start);
            chunks.Add(text.Substring(start, length));

            if (start + length >= text.Length)
            {
                break;
            }
        }

        return chunks;
    }
}
