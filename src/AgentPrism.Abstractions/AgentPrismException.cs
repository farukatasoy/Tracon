namespace AgentPrism;

/// <summary>AgentPrism'in urettigi tum hatalarin taban sinifi.</summary>
public class AgentPrismException : Exception
{
    /// <summary>Yeni bir hata olusturur.</summary>
    public AgentPrismException()
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    public AgentPrismException(string message)
        : base(message)
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    /// <param name="innerException">Asil hata.</param>
    public AgentPrismException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Bir agent tanimi calistirilabilir bir agent'a donusturulemedigi zaman atilir.
/// </summary>
/// <remarks>
/// En sik sebep: tanimda kodda kayitli olmayan bir tool adinin bulunmasi.
/// Bu durum sessizce atlanmaz — bilinmeyen bir tool ile calisan agent,
/// kullanicinin bekledigini yapmayan agent demektir.
/// </remarks>
public sealed class AgentPrismCompilationException : AgentPrismException
{
    /// <summary>Yeni bir derleme hatasi olusturur.</summary>
    public AgentPrismCompilationException()
    {
    }

    /// <summary>Yeni bir derleme hatasi olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    public AgentPrismCompilationException(string message)
        : base(message)
    {
    }

    /// <summary>Yeni bir derleme hatasi olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    /// <param name="innerException">Asil hata.</param>
    public AgentPrismCompilationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Derlenemeyen agent'in adi.</summary>
    public string? AgentName { get; init; }
}

/// <summary>
/// Bir model saglayicisi devre kesici tarafindan gecici olarak kapatildiginda atilir.
/// </summary>
/// <remarks>
/// Devre <c>Open</c> durumdayken atilir; saglayiciya <strong>hicbir istek gitmez</strong>.
/// Ardisik hata sayisi <c>FailureThreshold</c>'u astiginda devre acilir ve
/// <c>BreakDuration</c> sonunda tek bir deneme icin yari-acik duruma gecer.
/// Gerekce: <c>docs/08-SAGLAYICI-GENISLEMESI.md</c>, bolum 8.3.
/// </remarks>
public sealed class AgentPrismProviderUnavailableException : AgentPrismException
{
    /// <summary>Yeni bir hata olusturur.</summary>
    public AgentPrismProviderUnavailableException()
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    public AgentPrismProviderUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    /// <param name="innerException">Asil hata.</param>
    public AgentPrismProviderUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Devresi acik olan saglayicinin adi.</summary>
    public string? ProviderName { get; init; }

    /// <summary>Devrenin yari-acik duruma gecip yeniden denenecegi zaman.</summary>
    public DateTimeOffset? RetryAfter { get; init; }
}
