using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir agent tanimina baglanabilecek baglam sikistirma stratejisi turu.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompactionStrategyKind>))]
public enum CompactionStrategyKind
{
    /// <summary>Sikistirma yok. Varsayilan.</summary>
    None,

    /// <summary>En eski turleri, bir alt sinirin ustunde tutarak atar.</summary>
    SlidingWindow,

    /// <summary>Disarida birakilan gruplari sabit bir alt sinira kadar keser.</summary>
    Truncation,

    /// <summary>Yalnizca tool cagri/sonuc gruplarini kisaltir.</summary>
    ToolResult,

    /// <summary>Disarida birakilan gruplari bir modelle ozetler.</summary>
    Summarization,

    /// <summary>Model baglam penceresi sinirina gore otomatik tahliye/kesme uygular.</summary>
    ContextWindow,

    /// <summary>Sabit sirali bir zincir uygular: ToolResult, ardindan SlidingWindow, ardindan Summarization.</summary>
    Pipeline,
}
