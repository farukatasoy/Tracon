namespace AgentPrism;

/// <summary>
/// <see cref="PatternContentGuard"/>'in yerlesik desen aileleri.
/// </summary>
/// <remarks>
/// Her aile <strong>ayri ayri</strong> acilir. Hepsini birlikte acmak yanlis pozitif
/// riskini toplar ve guard'in pratikte kapatilmasina yol acar.
/// </remarks>
[Flags]
public enum PiiPatterns
{
    /// <summary>Hicbir desen acik degil. Varsayilan.</summary>
    None = 0,

    /// <summary>E-posta adresi.</summary>
    Email = 1,

    /// <summary>IBAN. Iki harf ulke kodu + iki kontrol basamagi + 11-30 alfanumerik.</summary>
    Iban = 2,

    /// <summary>
    /// Kredi karti numarasi. 🚨 <strong>Luhn dogrulamasi yapar</strong>.
    /// </summary>
    /// <remarks>
    /// Yalniz <c>\d{16}</c> eslesmesi her siparis numarasini maskeler ve guard'i
    /// kullanilamaz hale getirir.
    /// </remarks>
    CreditCard = 4,

    /// <summary>
    /// TC kimlik numarasi. 🚨 <strong>Kontrol basamagi dogrulamasi yapar</strong>.
    /// </summary>
    /// <remarks>
    /// Rastgele 11 hane maskelenmez; yalnizca 10. ve 11. basamak kurallarina uyan
    /// bir numara maskelenir.
    /// </remarks>
    TurkishNationalId = 8,

    /// <summary>
    /// Saglayici API anahtari deseni: <c>sk-…</c>, <c>ghp_…</c>, <c>AKIA…</c>.
    /// </summary>
    /// <remarks>
    /// Bir kullanicinin yanlislikla anahtarini isteme yazmasi gorulen bir olaydir;
    /// o istem saglayiciya gider ve saglayicinin gunlugune dusebilir.
    /// </remarks>
    ProviderApiKey = 16,
}
