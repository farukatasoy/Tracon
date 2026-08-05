namespace AgentPrism;

/// <summary>Gercek zamanli konusma katmaninin ayarlari.</summary>
/// <remarks>
/// <para>
/// ⚠️ Bu yetenek <strong>barindirma modelini degistirir</strong>. Konusma
/// baglantisi dakikalarca acik kalir ve bir sunucu ornegine baglanir (yapiskan
/// oturum). Bu yuzden yetenek istege baglidir: <c>UseVoiceConversation()</c>
/// cagrilmadikca hicbir WebSocket ucu acilmaz ve davranis degismez.
/// </para>
/// <para>
/// 🚨 Tur bir <c>record</c> <strong>degildir</strong> (K-035): ayar siniflari
/// gunluge yazilabilecek bir <c>ToString</c> uretmemelidir.
/// </para>
/// </remarks>
public sealed class VoiceConversationOptions
{
    /// <summary>Yapilandirma bolumunun varsayilan adi.</summary>
    public const string SectionName = "AgentPrism:Voice:Conversation";

    /// <summary>
    /// Bir kiracinin ayni anda acabilecegi en fazla konusma baglantisi.
    /// Varsayilan 5.
    /// </summary>
    /// <remarks>
    /// Acik baglanti bir sunucu is parcacigi degil ama bir soket, bir arabellek
    /// ve bir agent oturumu tutar. Sinirsiz baglanti, tek bir kiracinin sunucuyu
    /// doldurmasina izin verirdi.
    /// </remarks>
    public int MaxConcurrentConnectionsPerTenant { get; set; } = 5;

    /// <summary>
    /// Bir baglantinin acik kalabilecegi en uzun sure. Varsayilan 30 dakika.
    /// </summary>
    /// <remarks>
    /// Sure dolunca baglanti duzgun kapatilir ve istemci yeniden baglanir.
    /// Sonsuz baglanti, sizan bir kaynagin hicbir zaman fark edilmemesi demektir.
    /// </remarks>
    public TimeSpan MaxConnectionDuration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Hicbir cerceve gelmeden gecebilecek en uzun sure. Varsayilan 2 dakika.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Tek bir konusma parcasinin (utterance) en uzun suresi. Varsayilan 60 saniye.
    /// </summary>
    /// <remarks>
    /// 🚨 Konusma sonu tespiti (VAD) <strong>istemcidedir</strong>; sunucu
    /// sinyal islemez. Bu sinir bir <em>guvenlik agidir</em>: istemcinin VAD'i
    /// hic tetiklenmezse parca kendiliginden kapanir ve cozume gonderilir.
    /// </remarks>
    public TimeSpan MaxUtteranceDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Tek bir konusma parcasinin en fazla kac bayt tutabilecegi. Varsayilan 8 MB.
    /// </summary>
    /// <remarks>
    /// Sure sinirinin yaninda bir de bayt siniri vardir: istemci sesi degil
    /// rastgele veri gonderirse sure hicbir zaman dolmayabilir.
    /// </remarks>
    public int MaxUtteranceBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// Konusmanin sesi <c>attachments</c> tablosuna yazilsin mi.
    /// Varsayilan <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// 🚨 <strong>Ses kisisel veridir.</strong> Varsayilan olarak saklanmamasi
    /// bilinclidir. Acildiginda Faz 25'in saklama politikasi uygulanir ve arayuz
    /// kullaniciya kaydin yapildigini <strong>gosterir</strong> — sessizce kayit
    /// yapilmaz.
    /// </remarks>
    public bool PersistAudio { get; set; }

    /// <summary>
    /// Yanitlari seslendirirken kullanilacak ses kimligi. Bos ise
    /// saglayicinin varsayilan sesi (<c>AgentPrism:Voice:DefaultVoiceId</c>).
    /// </summary>
    public string? VoiceId { get; set; }

    /// <summary>
    /// Sentezlenen sesin MIME turu. Varsayilan <c>audio/mpeg</c>.
    /// </summary>
    /// <remarks>
    /// 🚨 Deger, ses saglayicisinin <em>yapilandirilmis cikti bicimiyle</em>
    /// ayni olmalidir (<c>AgentPrism:Voice:OutputFormat</c>). Akisli sentez
    /// yalnizca ham baytlar dondurur ve turu bildirmez; istemci sesi cozebilmek
    /// icin turu bilmek zorundadir. Yanlis deger, tarayicida sessiz bir cozum
    /// hatasi uretir.
    /// </remarks>
    public string OutputMediaType { get; set; } = "audio/mpeg";

    /// <summary>
    /// Ham PCM gonderen istemcilerin ornekleme hizi (Hz). Varsayilan 16.000.
    /// </summary>
    /// <remarks>
    /// Ham PCM basliksizdir; cozum saglayicisina gonderilmeden once sunucu bir
    /// WAV basligi yazar ve o baslik bu degeri tasir. Yanlis deger, sesin yanlis
    /// hizda cozulmesine yol acar.
    /// </remarks>
    public int InputSampleRate { get; set; } = 16_000;

    /// <summary>
    /// Bir yanitin en fazla kac karakteri seslendirilsin. Varsayilan 5.000.
    /// </summary>
    /// <remarks>
    /// Sinir asilirsa kalan metin <strong>seslendirilmez</strong> ama altyazi
    /// olarak yine akar: kullanici cevabin tamamini gorur, sessizce kirpilmis
    /// bir cevap almaz.
    /// </remarks>
    public int MaxSpokenCharactersPerTurn { get; set; } = 5_000;
}
