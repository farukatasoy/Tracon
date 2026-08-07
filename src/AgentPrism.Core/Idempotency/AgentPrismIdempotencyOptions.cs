namespace AgentPrism;

/// <summary><c>Idempotency-Key</c> destegi ayarlari — Faz 43.</summary>
/// <remarks>
/// <para>
/// <c>AgentPrism:Idempotency</c> yapilandirma bolumunden okunur.
/// </para>
/// <para>
/// 🚨 <strong>Varsayilan aciktir</strong> ve bu bilincli bir K1 yorumudur.
/// Hiz siniri ve kotanin aksine, bu ozellik yalnizca istemci <c>Idempotency-Key</c>
/// basligi GONDERDIGINDE devreye girer — baslik tasimayan bir istek icin hicbir
/// ek maliyet veya davranis degisikligi yoktur. Kapali gelseydi, basligi gonderen
/// bir istemci korundugunu SANIP korunmazdi; asil sessiz surpriz bu olurdu. Karar:
/// <c>docs/KARARLAR.md</c>, ilgili karar numarasi faz kapanisinda eklenir.
/// </para>
/// </remarks>
public sealed class AgentPrismIdempotencyOptions
{
    /// <summary>Yapilandirma bolumu adi.</summary>
    public const string SectionName = "AgentPrism:Idempotency";

    /// <summary>
    /// Idempotency destegi acik mi. Aciksa bile <c>Idempotency-Key</c> basligi
    /// TASIMAYAN istek hicbir ek maliyet odemez. Varsayilan <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Anahtarin en fazla uzunlugu. Asilirsa <c>400</c> donulur.</summary>
    public int MaxKeyLength { get; set; } = 255;
}
