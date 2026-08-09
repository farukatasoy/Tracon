namespace AgentPrism;

/// <summary>
/// Bir deneyin kanarya kolu icin otomatik geri alma ve kademeli trafik artirma kurallari.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Yalnizca <strong>iki kollu</strong> deneylerde tanimlanabilir:
/// <see cref="CanaryVariant"/> kanarya, kalan TEK kol kontrol sayilir. Ucten fazla
/// kolda kontrol agirliginin orantili geri dagitimi oturum kararliligini
/// (bkz. <c>docs/56-KANARYA-YAYINI-VE-OTOMATIK-GERI-ALMA.md</c>, bolum 56.4) garanti
/// edemezdi; bu kisitla kanarya arailigi her zaman <c>[0, kanaryaAgirligi)</c>'da
/// sabit kalir ve kontrol araligi tek parcadir.
/// </para>
/// <para>
/// Karsilastirma <strong>gorecelidir</strong>: kanaryanin hata orani kontrolun
/// KENDISINDEN <see cref="MaxErrorRateDelta"/> kadar yuksekse geri alinir. Mutlak
/// bir esik (ornegin "hata orani %5'i gecerse dur"), kontrolun de kotu oldugu bir
/// agent'ta kanaryayi haksiz yere oldururdu.
/// </para>
/// </remarks>
public sealed record CanaryPolicy
{
    /// <summary>Kanarya kolunun adi. Deneyin <c>Variants</c> listesinde bulunmalidir.</summary>
    public required string CanaryVariant { get; init; }

    /// <summary>
    /// Kanaryanin hata orani kontrolden bu kadar YUKSEKSE geri alinir (mutlak fark,
    /// 0.0-1.0). <see langword="null"/> ise hata orani denetlenmez.
    /// </summary>
    public double? MaxErrorRateDelta { get; init; }

    /// <summary>
    /// Kanaryanin ortalama puani (0-100) bu esigin ALTINDAYSA geri alinir.
    /// <see langword="null"/> ise puan denetlenmez.
    /// </summary>
    public int? MinScore { get; init; }

    /// <summary>
    /// Bir karar verilmeden once hem kanarya hem kontrol kolunun ulasmasi gereken
    /// asgari sonuclanmis calistirma sayisi.
    /// </summary>
    /// <remarks>
    /// 🚨 Faz 49'un <c>OnlineEvaluationOptions.MinSampleSize</c> kuralinin
    /// AYNISIDIR — ayni varsayilan (<c>20</c>), ayni gerekce (az orneklemde esik
    /// gurultuye tepki verir). Ayni deger kademeli artirmada bir sonraki adima
    /// gecis icin de kullanilir; ikinci bir esik alani ACILMAZ.
    /// </remarks>
    public int MinSampleSize { get; init; } = 20;

    /// <summary>
    /// Kanarya agirliginin zamanla artacagi adimlar (ornek: <c>[5, 25, 50, 100]</c>).
    /// Bos ise kademeli artirma calismaz; kanarya agirligi <c>SaveAsync</c>'te
    /// tanimlanan degerde sabit kalir ve yalniz geri alma denetlenir.
    /// </summary>
    public IReadOnlyList<int> RampSteps { get; init; } = [];

    /// <summary>Adimlar arasindaki asgari sure.</summary>
    public TimeSpan RampInterval { get; init; } = TimeSpan.FromHours(1);
}
