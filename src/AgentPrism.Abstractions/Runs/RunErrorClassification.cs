namespace AgentPrism;

/// <summary>Bir <see cref="IRunErrorClassifier"/> siniflandirmasinin sonucu.</summary>
public sealed record RunErrorClassification
{
    /// <summary>Hatanin dustugu sinif.</summary>
    public required RunErrorClass Class { get; init; }

    /// <summary>
    /// Normallestirilmis hata mesajinin ozeti. Ayni arizanin tekrarlarini
    /// kumelemek icin kullanilir; kimlik, sayi, tarih ve tirnak ici metin
    /// gibi degisken parcalar kaldirildiktan sonra hesaplanir.
    /// </summary>
    public required string Fingerprint { get; init; }
}
