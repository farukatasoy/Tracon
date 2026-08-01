namespace AgentPrism;

/// <summary>
/// Bir calistirma sirasinda uretilen olay tipleri. Arayuz bu tipleri dogrudan
/// gorsel ogelere esler, bu yuzden degerler kararli tutulmalidir.
/// </summary>
public enum RunEventType
{
    /// <summary>Calistirma basladi.</summary>
    RunStarted = 0,

    /// <summary>Modelden metin parcasi geldi. Yalnizca akisli calistirmalarda uretilir.</summary>
    MessageDelta = 1,

    /// <summary>Bir mesaj tamamlandi.</summary>
    MessageCompleted = 2,

    /// <summary>Bir tool cagrilmak uzere. Argumanlar olay yukunde bulunur.</summary>
    ToolInvoking = 3,

    /// <summary>Bir tool basariyla tamamlandi. Sonuc olay yukunde bulunur.</summary>
    ToolInvoked = 4,

    /// <summary>Bir tool hata verdi.</summary>
    ToolFailed = 5,

    /// <summary>Calistirma basariyla tamamlandi.</summary>
    RunCompleted = 6,

    /// <summary>Calistirma hata ile sonlandi.</summary>
    RunFailed = 7,
}
