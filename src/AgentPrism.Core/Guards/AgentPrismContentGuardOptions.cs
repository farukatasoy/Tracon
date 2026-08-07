namespace AgentPrism;

/// <summary>Icerik denetimi boru hattinin ayarlari — Faz 48.</summary>
/// <remarks>
/// <para>
/// <c>AgentPrism:ContentGuard</c> yapilandirma bolumunden okunur.
/// </para>
/// <para>
/// 🚨 Bu siniftaki hicbir ayarin <strong>hic guard kayitli degilken etkisi yoktur</strong>:
/// denetim sarmalayicisi boru hattina eklenmez ve bu nesne hic okunmaz. K1 (sifir
/// surpriz) kapisi bir bayrak degil, <em>kaydin kendisidir</em> — yerlesik guard
/// <c>AddPatternContentGuard()</c> ile veya <c>AgentPrism:ContentGuard:Pattern</c>
/// bolumu doldurularak acik bir tercihle eklenir.
/// </para>
/// </remarks>
public sealed class AgentPrismContentGuardOptions
{
    /// <summary>Yapilandirma bolumu adi.</summary>
    public const string SectionName = "AgentPrism:ContentGuard";

    /// <summary>
    /// Modele giden icerigi denetle. Varsayilan <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Tool sonuclari da bu yondedir: bir tool sonucu modele <em>ikinci</em> cagride
    /// girer ve denetim <c>IChatClient</c> katmaninda oldugu icin gorulur.
    /// </remarks>
    public bool InspectInput { get; set; } = true;

    /// <summary>
    /// Modelden gelen icerigi denetle. Varsayilan <see langword="true"/>.
    /// </summary>
    public bool InspectOutput { get; set; } = true;

    /// <summary>
    /// 🚨 Cikis denetimi acikken akisli yanit <strong>tamponlanir</strong>.
    /// Varsayilan <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bir cerceve istemciye gonderildikten sonra geri alinamaz. Kismi bir metin
    /// uzerinde desen eslesmez: <c>4539-</c> gorulur ve kart numarasi tamamlanmadan
    /// gecer. Tamponlama akisin canliligini kaybettirir ama denetimi dogru yapar.
    /// </para>
    /// <para>
    /// Bu ayari <see langword="false"/> yapmak cikis denetimini <em>yarim</em>
    /// birakir: guard yalnizca her cerceveyi tek basina gorur. Sessizce yarim
    /// denetim yapmak, denetim yapmamaktan kotudur — kullanici korundugunu sanir.
    /// Bu yuzden secim acik bir ayardir, gizli bir davranis degildir.
    /// </para>
    /// </remarks>
    public bool BufferStreamingOutput { get; set; } = true;
}
