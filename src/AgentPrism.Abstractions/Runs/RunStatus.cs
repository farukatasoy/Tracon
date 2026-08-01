namespace AgentPrism;

/// <summary>Bir calistirmanin durumu.</summary>
public enum RunStatus
{
    /// <summary>Calistirma devam ediyor.</summary>
    Running = 0,

    /// <summary>Calistirma basariyla tamamlandi.</summary>
    Completed = 1,

    /// <summary>Calistirma hata ile sonlandi.</summary>
    Failed = 2,

    /// <summary>Calistirma iptal edildi.</summary>
    Canceled = 3,
}
