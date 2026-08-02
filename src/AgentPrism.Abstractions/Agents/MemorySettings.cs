namespace AgentPrism;

/// <summary>
/// Bir agent'a baglanacak bellek saglayicilarini belirler.
/// </summary>
/// <remarks>
/// Vektor tabanli anlamsal bellek (MAF'in <c>ChatHistoryMemoryProvider</c>'i)
/// bilerek burada yoktur: gercek kurucusu bir vektor deposu ve embedding
/// boyutu ister, depoda somut bir vektor deposu implementasyonu yoktur. Karar
/// gerekcesi: <c>docs/KARARLAR.md</c>.
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
}
