namespace AgentPrism;

/// <summary>Metinden ses ureten bir saglayici.</summary>
/// <remarks>
/// <para>
/// ElevenLabs bir <em>uygulamadir</em>, bir bagimlilik degil. Baska bir saglayici
/// isteyen kendi uygulamasini kaydeder; tool'lar yalnizca bu sozlesmeyi bilir.
/// </para>
/// <para>
/// Sozlesme <c>AgentPrism.Voice</c>'ta degil BURADA yasar: HTTP katmani
/// (<c>AgentPrism.AspNetCore</c>) ses uclarini sunarken bu tipleri gorur ama
/// <c>AgentPrism.Voice</c>'a referans VEREMEZ (paket yonu kurali). Ayni desen
/// MCP soyutlamalarinda da uygulandi — K-174.
/// </para>
/// </remarks>
public interface ISpeechSynthesizer
{
    /// <summary>Saglayici adi. Maliyet kaydinda ve saglik denetiminde kullanilir.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Tek bir <see cref="SynthesizeAsync"/> cagrisinda kabul edilen en fazla
    /// karakter sayisi.
    /// </summary>
    /// <remarks>
    /// Tool cagrisi (<c>speak</c>) VE dogrudan HTTP operator ucu
    /// (<c>POST /api/voice/speak</c>) AYNI degeri okur — sinir iki yoldan da
    /// tutarli uygulanir.
    /// </remarks>
    int MaxCharactersPerRequest { get; }

    /// <summary>Bir metni seslendirir.</summary>
    /// <param name="request">Istek.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Uretilen ses.</returns>
    ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default);

    /// <summary>Bir metni seslendirir ve sesi parca parca akitir.</summary>
    /// <param name="request">Istek.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Ses parcalari.</returns>
    /// <remarks>
    /// <para>
    /// Akisli yol dusuk gecikme icindir ve Faz 29'un konusma katmani bunu
    /// kullanir. Imza <strong>simdi</strong> tanimlanir: bir arayuze sonradan
    /// uye eklemek, o arayuzu uygulayan tuketiciler icin kirici degisikliktir.
    /// </para>
    /// <para>
    /// 🚨 Akan ses <strong>ek olarak saklanmaz</strong>. Faturalanan miktar akis
    /// bittiginde bilinmez ve parcalar tek basina gecerli bir dosya degildir;
    /// <c>speak</c> tool'u bu yuzden akissiz yolu kullanir.
    /// </para>
    /// </remarks>
    IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
        SpeechRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Kullanilabilir sesleri listeler.</summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Ada gore sirali sesler.</returns>
    ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Sesten metin cozen bir saglayici.</summary>
/// <remarks>
/// Sozlesme tek atimlidir. Faz 29'un isteyecegi <em>artimli</em> cozum ayri bir
/// arayuz olmalidir; buraya uye eklemek tuketici uygulamalarini kirar.
/// </remarks>
public interface ISpeechTranscriber
{
    /// <summary>Saglayici adi.</summary>
    string ProviderName { get; }

    /// <summary>Bir ses akisini metne cevirir.</summary>
    /// <param name="audio">Ses akisi.</param>
    /// <param name="mediaType">Sesin MIME turu.</param>
    /// <param name="options">Ayarlar.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Cozulen metin.</returns>
    ValueTask<SpeechTranscript> TranscribeAsync(
        Stream audio,
        string mediaType,
        SpeechTranscriptionOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Yapilandirilmis ses modelinin fiyatini okur.
/// </summary>
/// <remarks>
/// HTTP katmani ses fiyatini gostermek icin bu soyutlamayi kullanir; fiyat
/// cozumu <c>AgentPrism.Voice</c> icindedir ve HTTP katmani o pakete referans
/// veremez (paket yonu kurali). AgentPrism fiyat uydurmaz (K-032): karsilik
/// yoksa <see langword="null"/> doner — sifir <strong>degil</strong>.
/// </remarks>
public interface IVoicePricingReader
{
    /// <summary>Raporlarda gosterilecek para birimi etiketi.</summary>
    string? Currency { get; }

    /// <summary>Karakter bazli bir uretimin maliyetini hesaplar.</summary>
    /// <param name="characters">Faturalanan karakter sayisi.</param>
    /// <returns>Tutar; fiyat tanimsizsa <see langword="null"/>.</returns>
    decimal? ForCharacters(decimal characters);
}

/// <summary>Operator eylemi olarak seslendirme istegi.</summary>
public sealed record SpeakRequest
{
    /// <summary>Seslendirilecek metin.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Ekin baglanacagi oturum. 🚨 Bos birakilirsa ek sahipsiz sayilir ve
    /// saklama politikasi tarafindan silinir.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>Kullanilacak sesin kimligi. Bos ise varsayilan ses.</summary>
    public string? VoiceId { get; init; }
}

/// <summary>Seslendirme sonucu.</summary>
public sealed record SpeakResponse
{
    /// <summary>Kaydedilen ek.</summary>
    public required AttachmentDescriptor Attachment { get; init; }

    /// <summary>Faturalanan karakter sayisi.</summary>
    public required int Characters { get; init; }

    /// <summary>Karakter sayisi tahmin mi.</summary>
    public required bool IsEstimated { get; init; }

    /// <summary>Hesaplanan tutar. Fiyat tanimsizsa <see langword="null"/>.</summary>
    public decimal? Cost { get; init; }

    /// <summary>Para birimi etiketi.</summary>
    public string? Currency { get; init; }
}

/// <summary>Ses saglayicisinin erisilebilirligini denetler.</summary>
public interface IVoiceHealthCheck
{
    /// <summary>Saglayiciya erisimi denetler.</summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Durum.</returns>
    /// <remarks>Denetim ucret <strong>uretmez</strong>: ses uretilmez, liste okunur.</remarks>
    ValueTask<VoiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
}
