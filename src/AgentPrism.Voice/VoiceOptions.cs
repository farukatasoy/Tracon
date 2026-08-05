namespace AgentPrism;

/// <summary>Ses tool'larinin ayarlari.</summary>
/// <remarks>
/// 🚨 Tur bir <c>record</c> <strong>olamaz</strong> (K-035): derleyicinin urettigi
/// <c>ToString</c> tum ozellikleri yazar ve tek bir <c>LogDebug("{Options}", o)</c>
/// cagrisi <see cref="ApiKey"/> degerini gunluge ifsa ederdi.
/// <c>SecretLeakTests</c> tipin kendi <c>ToString</c>'ini tanimlamadigini denetler.
/// </remarks>
public sealed class VoiceOptions
{
    /// <summary>Yapilandirma bolumunun varsayilan adi.</summary>
    public const string SectionName = "AgentPrism:Voice";

    /// <summary>Varsayilan cikti bicimi.</summary>
    /// <remarks>
    /// 🚨 MP3 secilmistir cunku ek deposu tur denetimini <strong>sihirli bayttan</strong>
    /// yapar. <c>pcm_*</c> ve <c>ulaw_*</c> ciktilari basliksizdir ve reddedilir.
    /// </remarks>
    public const string DefaultOutputFormat = "mp3_44100_128";

    /// <summary>Saglayici adi. Su an yalnizca <c>elevenlabs</c> desteklenir.</summary>
    public string Provider { get; set; } = VoiceProviderNames.ElevenLabs;

    /// <summary>
    /// API anahtari. Bir <strong>sirdir</strong>: yalnizca
    /// <c>dotnet user-secrets</c> veya ortam degiskeninde yasar.
    /// </summary>
    /// <remarks>
    /// Anahtarin <em>adi</em> degil <em>degeri</em> tutulur. K-059 sirlarin
    /// VERITABANINA yazilmasini yasaklar; ses yapilandirmasi veritabanina hic
    /// girmez ve diger dort saglayici paketi de duz <c>ApiKey</c> tasir.
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Taban adres. Bos ise saglayicinin genel adresi kullanilir.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Istek ses kimligi vermezse kullanilacak ses.</summary>
    public string? DefaultVoiceId { get; set; }

    /// <summary>Varsayilan sentez modeli.</summary>
    public string? SynthesisModelId { get; set; }

    /// <summary>Varsayilan cozum modeli.</summary>
    public string? TranscriptionModelId { get; set; }

    /// <summary>Cikti bicimi.</summary>
    public string OutputFormat { get; set; } = DefaultOutputFormat;

    /// <summary>
    /// Tek istekte seslendirilebilecek en fazla karakter. Varsayilan 5.000.
    /// </summary>
    /// <remarks>
    /// Sinir asilirsa tool <strong>hata dondurur</strong>, metni sessizce
    /// kirpmaz. Kirpma, kullanicinin duymadigi bir cumle uretir ve sebebi
    /// gorunmez olur.
    /// </remarks>
    public int MaxCharactersPerRequest { get; set; } = 5000;

    /// <summary>Ayni anda kac ses istegi yapilabilecegi. Varsayilan 2.</summary>
    public int MaxConcurrentRequests { get; set; } = 2;

    /// <summary>
    /// Ses tool'lari cagri oncesi acik onay istesin mi. Varsayilan <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Tool geri alinamaz bir dis etki yaratmaz — dosya uretir ve ucret harcar.
    /// Ucret bir gerekce olabilir; bu yuzden ayar vardir ve varsayilan kapalidir.
    /// </remarks>
    public bool RequireApproval { get; set; }

    /// <summary>Istek zaman asimi. Bos ise 100 saniye.</summary>
    public TimeSpan? Timeout { get; set; }
}

/// <summary>Bilinen ses saglayicilarinin adlari.</summary>
public static class VoiceProviderNames
{
    /// <summary>ElevenLabs. 🚨 KARARLI ad: olcum kayitlarinda saklanir.</summary>
    public const string ElevenLabs = "elevenlabs";
}
