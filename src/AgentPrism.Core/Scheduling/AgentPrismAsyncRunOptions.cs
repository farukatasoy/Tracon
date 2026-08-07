namespace AgentPrism;

/// <summary>Kuyruga alinan (dayanikli) calistirma ayarlari — Faz 46.</summary>
/// <remarks>
/// <para>
/// <c>AgentPrism:AsyncRun</c> yapilandirma bolumunden okunur.
/// </para>
/// <para>
/// 🚨 <strong>Varsayilan aciktir</strong> ve bu Faz 43'un <c>Idempotency-Key</c>
/// yorumuyla aynidir: <c>Prefer: respond-async</c> basligi TASIMAYAN bir istek
/// icin hicbir ek maliyet veya davranis degisikligi yoktur — sorgu bile
/// atilmaz. Kapali gelseydi, basligi gonderen bir istemci arka planda
/// kosulduğunu SANIP kosulmazdi; asil sessiz surpriz bu olurdu.
/// </para>
/// </remarks>
public sealed class AgentPrismAsyncRunOptions
{
    /// <summary>Yapilandirma bolumu adi.</summary>
    public const string SectionName = "AgentPrism:AsyncRun";

    /// <summary>
    /// <c>Prefer: respond-async</c> taniniyor mu. Kapaliysa basligi tasiyan
    /// istek <c>501</c> alir; sessizce akisa dusmez. Varsayilan <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Kuyruga alinan bir calistirmanin en fazla deneme sayisi.
    /// </summary>
    /// <remarks>
    /// 🚨 Varsayilan <strong>1</strong>: kira dolup is yeniden alindiginda yan
    /// etkili bir tool'un ikinci kez tetiklenmesini engeller. Yukseltmek,
    /// tool'larin idempotent olmasini gerektirir — "dayanikli" kelimesi
    /// "is asla kaybolmaz" degil, "is sessizce kaybolmaz" demektir.
    /// </remarks>
    public int MaxAttempts { get; set; } = 1;
}
