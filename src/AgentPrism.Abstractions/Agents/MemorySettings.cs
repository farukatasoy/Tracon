namespace AgentPrism;

/// <summary>
/// Bir agent'a baglanacak bellek saglayicilarini belirler.
/// </summary>
/// <remarks>
/// MAF'in <c>ChatHistoryMemoryProvider</c>'i (vektor tabanli oturum ici bellek)
/// bilerek burada yoktur; <see cref="EnableVectorSearch"/> ile karistirilmamalidir
/// — o, kalici bir bilgi tabaninda anlamsal arama tool'unu acar, MAF'in o
/// belirli sozlesmesini baglamaz. Gerekce ve sinir: <c>docs/KARARLAR.md</c>.
/// </remarks>
public sealed record MemorySettings
{
    /// <summary>
    /// Dosya tabanli bellegi acar. Bu fazda yalnizca bellek ici depo ile
    /// calisir; kalici surum ileri bir faza birakildi.
    /// </summary>
    public bool EnableFileMemory { get; init; }

    /// <summary>Todo takibini acar.</summary>
    public bool EnableTodo { get; init; }

    /// <summary>
    /// Dosya deposu uzerinde metin aramasini acar. Arama, kayitli
    /// <c>AgentFileStore</c> uzerinde calisir; bu fazda varsayilan olarak
    /// bellek ici depodur.
    /// </summary>
    public bool EnableTextSearch { get; init; }

    /// <summary>
    /// <c>search_knowledge</c> tool'unu acar (Faz 51). 🚨 Yalniz PostgreSQL:
    /// <see cref="IVectorSearchStore"/>'un tek somut uygulamasi
    /// <c>AgentPrism.PostgreSql</c> icindedir. Baska bir saglayici kayitliyken
    /// bu bayrak acilirsa derleme <see cref="AgentPrismCompilationException"/>
    /// ile durur; sessizce bos sonuc donmez.
    /// </summary>
    public bool EnableVectorSearch { get; init; }

    /// <summary>
    /// Aranacak koleksiyon adi. Bos ise agent adi kullanilir.
    /// </summary>
    public string? VectorCollection { get; init; }
}
