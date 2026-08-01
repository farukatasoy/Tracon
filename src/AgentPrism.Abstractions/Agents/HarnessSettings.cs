namespace AgentPrism;

/// <summary>
/// Harness yetenekleri icin ayarlar. Microsoft Agent Framework'un
/// <c>HarnessAgentOptions</c> yapisinin guvenli bir alt kumesini yansitir.
/// </summary>
/// <remarks>
/// Shell erisimi ve arka plan agent'lari bu ayarlarda <em>bilerek yoktur</em>.
/// Bu iki yetenek sunucuda kod calistirma yuzeyi acar ve ayri bir guvenlik
/// degerlendirmesi gerektirir; Faz 6'da ele alinir.
/// </remarks>
public sealed record HarnessSettings
{
    /// <summary>Baglam penceresinin token siniri. Asilinca sikistirma devreye girer.</summary>
    public int? MaxContextWindowTokens { get; init; }

    /// <summary>Tek yanitta uretilecek ust token siniri.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Tek bir istek icinde yapilabilecek ust yineleme sayisi.</summary>
    public int? MaximumIterationsPerRequest { get; init; }

    /// <summary>Harness'a verilecek ek talimatlar.</summary>
    public string? HarnessInstructions { get; init; }

    /// <summary>Baglam sikistirmayi kapatir.</summary>
    public bool DisableCompaction { get; init; }

    /// <summary>Todo takibini kapatir.</summary>
    public bool DisableTodoProvider { get; init; }

    /// <summary>Dosya bellegini kapatir.</summary>
    public bool DisableFileMemory { get; init; }

    /// <summary>Web aramasini kapatir.</summary>
    public bool DisableWebSearch { get; init; }

    /// <summary>
    /// Tool otomatik onayini kapatir. Kapatildiginda her tool cagrisi
    /// acik onay bekler.
    /// </summary>
    public bool DisableToolAutoApproval { get; init; }

    /// <summary>Agent skill saglayicisini kapatir.</summary>
    public bool DisableAgentSkillsProvider { get; init; }

    /// <summary>Agent mode saglayicisini kapatir.</summary>
    public bool DisableAgentModeProvider { get; init; }
}
