namespace AgentPrism;

/// <summary>Metinden ses uretimi istegi.</summary>
public sealed record SpeechRequest
{
    /// <summary>Seslendirilecek metin.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Kullanilacak sesin kimligi. Bos ise <c>VoiceOptions.DefaultVoiceId</c>.
    /// </summary>
    public string? VoiceId { get; init; }

    /// <summary>
    /// Kullanilacak sentez modeli. Bos ise <c>VoiceOptions.SynthesisModelId</c>.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Saglayicinin cikti bicimi adi (ornegin <c>mp3_44100_128</c>).
    /// </summary>
    /// <remarks>
    /// 🚨 Bu bir MIME turu <strong>degildir</strong>. Ayrica her bicim ek olarak
    /// saklanamaz: <c>pcm_*</c> ve <c>ulaw_*</c> ciktilari basliksizdir, hicbir
    /// sihirli bayta uymaz ve <c>AttachmentTypeGuard</c> tarafindan reddedilir.
    /// Gerekce: <c>docs/28-SES-TOOLLARI.md</c>, bolum 28.0/G3.
    /// </remarks>
    public string? OutputFormat { get; init; }

    /// <summary>Dilin ISO 639-1 kodu. Bos ise saglayici kendisi sezer.</summary>
    public string? LanguageCode { get; init; }
}

/// <summary>Uretilmis ses.</summary>
/// <remarks>
/// Tur bir <c>record</c> <strong>degildir</strong>: <see cref="Data"/> alani buyuk
/// olabilecegi icin uretilen <c>ToString</c>/esitlik karsilastirmasinin yanlislikla
/// tum icerigi kopyalamasi istenmez. Ayni gerekce <c>AttachmentContent</c> icin de
/// gecerlidir.
/// </remarks>
public sealed class SpeechAudio
{
    /// <summary>Ham ses baytlari.</summary>
    public required ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>Dogrulanmis MIME turu (ornegin <c>audio/mpeg</c>).</summary>
    public required string MediaType { get; init; }

    /// <summary>Sesin suresi. Saglayici bildirmiyorsa <see langword="null"/>.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Faturalanan karakter sayisi.</summary>
    public int? CharactersBilled { get; init; }

    /// <summary>
    /// <see cref="CharactersBilled"/> degerinin nereden geldigi.
    /// </summary>
    /// <remarks>
    /// Saglayici sayiyi bildirmezse metnin uzunlugu kullanilir ve deger
    /// <see cref="SpeechUsageSource.Estimated"/> olur. Tahmini olcum gibi
    /// gostermek fiyat uydurmaktir (K-032).
    /// </remarks>
    public SpeechUsageSource UsageSource { get; init; }
}

/// <summary>Bir ses olcumunun kaynagi.</summary>
public enum SpeechUsageSource
{
    /// <summary>Olcum yok.</summary>
    Unknown = 0,

    /// <summary>Saglayici bildirdi.</summary>
    Provider = 1,

    /// <summary>Istekten tahmin edildi.</summary>
    Estimated = 2,
}

/// <summary>Sesten metin cevriminin sonucu.</summary>
public sealed record SpeechTranscript
{
    /// <summary>Cozulen metin.</summary>
    public required string Text { get; init; }

    /// <summary>Algilanan dilin kodu.</summary>
    public string? LanguageCode { get; init; }

    /// <summary>Dil algilamasinin guveni (0-1).</summary>
    public double? LanguageProbability { get; init; }

    /// <summary>
    /// Cozulen sesin suresi. Maliyet bundan hesaplanir.
    /// </summary>
    public TimeSpan? AudioDuration { get; init; }
}

/// <summary>Sesten metin cevrimi ayarlari.</summary>
public sealed record SpeechTranscriptionOptions
{
    /// <summary>Kullanilacak model. Bos ise <c>VoiceOptions.TranscriptionModelId</c>.</summary>
    public string? ModelId { get; init; }

    /// <summary>Beklenen dilin ISO 639-1 kodu. Bos ise saglayici sezer.</summary>
    public string? LanguageCode { get; init; }
}

/// <summary>Kullanilabilir bir sesin tanimi.</summary>
/// <remarks>
/// Saglayicinin dondurdugu <c>preview_url</c> alani BILEREK tasinmaz: arayuzde
/// calmak tarayiciyi saglayicinin adresine baglardi. Sonradan eklemek kirici
/// degildir, cikarmak kiricidir.
/// </remarks>
public sealed record VoiceDescriptor
{
    /// <summary>Ses kimligi. Agent tanimlarinda ve isteklerde bu deger kullanilir.</summary>
    public required string VoiceId { get; init; }

    /// <summary>Insan tarafindan okunabilir ad.</summary>
    public required string Name { get; init; }

    /// <summary>Saglayicinin verdigi kategori (ornegin <c>premade</c>).</summary>
    public string? Category { get; init; }
}

/// <summary>Ses saglayicisinin erisilebilirlik durumu.</summary>
/// <remarks>
/// <c>ModelProviderHealth</c> BILEREK yeniden kullanilmaz: ses saglayicisi bir
/// <c>IModelProvider</c> degildir ve <c>/api/models/health</c> ciktisinda
/// gorunmemelidir. Iki kaynak tek listede toplanirsa devre kesici ve model
/// katalogu yanlis davranir.
/// </remarks>
public sealed record VoiceHealth
{
    /// <summary>Saglayici adi.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Saglayici erisilebilir mi.</summary>
    public required bool IsHealthy { get; init; }

    /// <summary>Denetimin suresi.</summary>
    public required TimeSpan Latency { get; init; }

    /// <summary>Denetimin yapildigi an (UTC).</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Basarisizsa gerekce. 🚨 Metin ne API anahtari ne de adres tasir.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>Denetim sirasinda gorulen ses sayisi.</summary>
    public int? VoiceCount { get; init; }
}
