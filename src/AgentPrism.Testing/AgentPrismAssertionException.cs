namespace AgentPrism.Testing;

/// <summary>
/// Bir <see cref="RunAssertions"/> iddiasi karsilanmadiginda firlatilir.
/// </summary>
/// <remarks>
/// Her test cercevesi (xunit, NUnit, MSTest) firlatilan bir istisnayi test
/// basarisizligi sayar; paket bunun disinda hicbir cerceveye baglanmaz.
/// Gerekce: docs/39-TEST-PAKETI.md, bolum 39.2.
/// </remarks>
public sealed class AgentPrismAssertionException : AgentPrismException
{
    /// <summary>Yeni bir iddia hatasi olusturur.</summary>
    public AgentPrismAssertionException()
    {
    }

    /// <summary>Yeni bir iddia hatasi olusturur.</summary>
    /// <param name="message">Beklenen ve bulunan degeri anlatan mesaj.</param>
    public AgentPrismAssertionException(string message)
        : base(message)
    {
    }

    /// <summary>Yeni bir iddia hatasi olusturur.</summary>
    /// <param name="message">Beklenen ve bulunan degeri anlatan mesaj.</param>
    /// <param name="innerException">Asil hata.</param>
    public AgentPrismAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
