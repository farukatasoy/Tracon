namespace AgentPrism;

/// <summary>Bir yapilandirma anahtarinin cozulup cozulmedigi. DEGER TASIMAZ (K-059).</summary>
public sealed record ConfigurationDiagnostic
{
    /// <summary>Yapilandirma anahtarinin tam yolu. Ornek: <c>AgentPrism:Providers:OpenAI:ApiKey</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Anahtar bos olmayan bir degere cozuldu mu.</summary>
    public required bool Resolved { get; init; }

    /// <summary>Cozulmediyse anahtari nasil ayarlayacagina dair ipucu. Sunucudan gelir, arayuzde cevrilmez (K-232).</summary>
    public string? Hint { get; init; }
}
