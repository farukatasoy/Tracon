namespace AgentPrism;

/// <summary>Yerlesik desen tabanli guard'in ayarlari — Faz 48.</summary>
/// <remarks>
/// <para>
/// <c>AgentPrism:ContentGuard:Pattern</c> yapilandirma bolumunden okunur.
/// </para>
/// <para>
/// 🚨 Bu sinifta <c>Enabled</c> bayragi <strong>yoktur</strong> ve bu bilinclidir.
/// K1'in kapisi kaydin kendisidir: <c>AddAgentPrism()</c> yerlesik guard'i
/// kaydetmez, bu yuzden varsayilan kurulumda denetim sarmalayicisi boru hattina
/// hic eklenmez ve maliyet <em>tam olarak</em> sifirdir. Bir <c>Enabled</c>
/// bayragi eklemek guard'in kayitli ama kapali olmasini gerektirirdi; o zaman
/// "hic guard kayitli degilse maliyet sifirdir" iddiasi olculemez hale gelirdi.
/// </para>
/// <para>
/// Guard'i calisir durumda tutup gecici olarak etkisizlestirmek gerekirse
/// <see cref="DeniedTerms"/> bosaltilir ve <see cref="MaskedPii"/>
/// <see cref="PiiPatterns.None"/> yapilir: guard hicbir kural gormez ve ilk
/// denetimde <see cref="ContentGuardResult.Allow"/> doner.
/// </para>
/// </remarks>
public sealed class PatternContentGuardOptions
{
    /// <summary>Yapilandirma bolumu adi.</summary>
    public const string SectionName = "AgentPrism:ContentGuard:Pattern";

    /// <summary>
    /// Yasak sozcukler. Eslesme <see cref="ContentGuardAction.Block"/> uretir.
    /// </summary>
    /// <remarks>
    /// Karsilastirma buyuk/kucuk harf duyarsizdir ve sozcuk parcasi olarak arar
    /// (<c>IndexOf</c>); regex degildir, bu yuzden tuketicinin yazdigi bir deger
    /// ReDoS riski tasimaz.
    /// </remarks>
    public IList<string> DeniedTerms { get; } = [];

    /// <summary>
    /// Acilacak yerlesik PII desenleri. Eslesme <see cref="ContentGuardAction.Mask"/>
    /// uretir. Varsayilan <see cref="PiiPatterns.None"/>.
    /// </summary>
    public PiiPatterns MaskedPii { get; set; } = PiiPatterns.None;

    /// <summary>
    /// Maskelenen eslesmenin yerine yazilacak metin. Varsayilan <c>[redacted]</c>.
    /// </summary>
    public string MaskReplacement { get; set; } = "[redacted]";
}
