namespace AgentPrism;

/// <summary>
/// Bir agent'in konusma gecmisini nasil sikistiracagini belirler.
/// </summary>
/// <remarks>
/// <see langword="null"/> birakilirsa hicbir sikistirma uygulanmaz; konusma
/// gecmisi model baglam penceresine sigmadiginda hata verir. Gecersiz bir
/// alan birlesimi (ornegin <see cref="CompactionStrategyKind.Summarization"/>
/// secilip hicbir tetikleyici verilmemesi) derleme aninda
/// <see cref="AgentPrismCompilationException"/> ile reddedilir, sessizce yok
/// sayilmaz.
/// </remarks>
public sealed record CompactionSettings
{
    /// <summary>Uygulanacak strateji turu.</summary>
    public CompactionStrategyKind Strategy { get; init; } = CompactionStrategyKind.None;

    /// <summary>Bu token sayisi asilinca sikistirma tetiklenir.</summary>
    public int? TriggerTokens { get; init; }

    /// <summary>Bu mesaj sayisi asilinca sikistirma tetiklenir.</summary>
    public int? TriggerMessages { get; init; }

    /// <summary>Bu tur sayisi asilinca sikistirma tetiklenir.</summary>
    public int? TriggerTurns { get; init; }

    /// <summary>
    /// <see cref="CompactionStrategyKind.SlidingWindow"/> icin korunacak en az
    /// tur sayisi. Belirtilmezse 2 kullanilir.
    /// </summary>
    public int? MinimumPreservedTurns { get; init; }

    /// <summary>
    /// <see cref="CompactionStrategyKind.Truncation"/>, <see cref="CompactionStrategyKind.ToolResult"/>
    /// ve <see cref="CompactionStrategyKind.Summarization"/> icin korunacak en
    /// az grup sayisi. Belirtilmezse 4 kullanilir.
    /// </summary>
    public int? MinimumPreservedGroups { get; init; }

    /// <summary>
    /// <see cref="CompactionStrategyKind.ContextWindow"/> icin zorunludur;
    /// diger stratejilerde yok sayilir.
    /// </summary>
    public int? MaxContextWindowTokens { get; init; }

    /// <summary>
    /// <see cref="CompactionStrategyKind.ContextWindow"/> icin ust uretim
    /// token siniri. Belirtilmezse agent'in kendi model baglantisindaki
    /// <see cref="ModelBinding.MaxOutputTokens"/>, o da yoksa 4096 kullanilir.
    /// </summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>
    /// <see cref="CompactionStrategyKind.Summarization"/> ve
    /// <see cref="CompactionStrategyKind.Pipeline"/> icin ozetleme istemine
    /// eklenecek ek talimat. <see langword="null"/> ise MAF'in varsayilan
    /// istemi kullanilir.
    /// </summary>
    public string? SummarizationPrompt { get; init; }

    /// <summary>
    /// Ozetleme cagrisinda kullanilacak model. Bos birakilirsa sira izlenir:
    /// uygulama genelindeki yardimci model ayari, o da yoksa agent'in kendi
    /// modeli.
    /// </summary>
    public ModelBinding? SummarizationModel { get; init; }
}
