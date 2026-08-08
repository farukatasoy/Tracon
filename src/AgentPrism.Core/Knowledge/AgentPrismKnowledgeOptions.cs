namespace AgentPrism;

/// <summary>Bilgi tabani (anlamsal arama) ayarlari — Faz 51.</summary>
/// <remarks>
/// <c>AgentPrism:Knowledge</c> yapilandirma bolumunden okunur. Ayri bir
/// <c>Use...()</c> cagrisi gerektirmez (zamanlama/kota ile ayni gerekce); yalniz
/// <see cref="IVectorSearchStore"/> ve <c>IEmbeddingGenerator</c> kayitliyken
/// islevsel hale gelir.
/// </remarks>
public sealed class AgentPrismKnowledgeOptions
{
    /// <summary>Yapilandirma bolumu adi.</summary>
    public const string SectionName = "AgentPrism:Knowledge";

    /// <summary>
    /// Gomu boyutu. 🚨 Sema ile birlikte SABITLENIR; degistirmek tum gomuleri
    /// gecersiz kilar (bkz. <c>docs/51-VEKTOR-BELLEK-VE-RAG.md</c>).
    /// </summary>
    public int Dimensions { get; set; } = 1536;

    /// <summary>Parca uzunlugu (karakter).</summary>
    public int ChunkSize { get; set; } = 1000;

    /// <summary>Ardisik parcalarin ortusme uzunlugu (karakter).</summary>
    public int ChunkOverlap { get; set; } = 100;

    /// <summary><c>search_knowledge</c> tool'unun dondurecegi en fazla sonuc sayisi.</summary>
    public int MaxResults { get; set; } = 5;
}
