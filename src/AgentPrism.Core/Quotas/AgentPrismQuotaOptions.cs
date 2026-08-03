namespace AgentPrism;

/// <summary>Kota alt sisteminin (Faz 21) ayarlari.</summary>
/// <remarks>
/// <c>AgentPrism:Quotas</c> yapilandirma bolumunden okunur.
/// </remarks>
public sealed class AgentPrismQuotaOptions
{
    /// <summary>Yapilandirma bolumu adi.</summary>
    public const string SectionName = "AgentPrism:Quotas";

    /// <summary>
    /// Kota denetimi etkin mi. Kapaliyken kural kaydedilebilir ama hicbir
    /// calistirma reddedilmez ve sayac artmaz.
    /// </summary>
    /// <remarks>
    /// Varsayilan <see langword="true"/> olmasi guvenlidir: kural tanimlanmadigi
    /// surece hicbir sey reddedilmez. "Varsayilan kota yok" kurali
    /// <em>kurallarin bos olmasiyla</em> saglanir, alt sistemi kapatarak degil.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Donem sinirlarinin hesaplandigi saat dilimi. Windows ve IANA adlari
    /// kabul edilir.
    /// </summary>
    /// <remarks>
    /// "Gunluk kota" diyen bir yonetici kendi is gununu kasteder; bu yuzden
    /// varsayilan UTC olsa da tuketicinin degistirmesi beklenir.
    /// </remarks>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>
    /// Kota sayaci bu yuzdeleri asinca <c>quota.threshold</c> olayi yayilir.
    /// </summary>
    /// <remarks>
    /// Her esik, her donemde <strong>bir kez</strong> yayilir; sayac her
    /// calistirmada arttigi icin aksi halde esigin ustundeki her calistirma
    /// yeni bir olay uretirdi.
    /// </remarks>
    public IList<int> ThresholdPercents { get; } = [80, 100];

    /// <summary>
    /// Kota denetimi bir hatayla karsilasirsa calistirmaya izin verilsin mi.
    /// </summary>
    /// <remarks>
    /// Varsayilan <see langword="true"/>: veritabani gecici olarak
    /// erisilemezse hizmet durmaz. Sikı bir kurulum bunu
    /// <see langword="false"/> yaparak "kotayi dogrulayamiyorsam calistirma"
    /// davranisini secebilir.
    /// </remarks>
    public bool AllowOnStoreFailure { get; set; } = true;

    /// <summary>Cozulmus saat dilimini dondurur; ad taninmazsa UTC'ye duser.</summary>
    /// <returns>Saat dilimi.</returns>
    /// <remarks>
    /// Taninmayan bir ad hata vermez: kota, yanlis bir ad yuzunden tum trafigi
    /// kesmemelidir. Dogrulama <c>AgentPrismQuotaOptionsValidator</c> icinde
    /// baslangicta yapilir ve orada hata verir.
    /// </remarks>
    public TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        }
        catch (Exception exception) when (
            exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
