namespace AgentPrism;

/// <summary>
/// Bir workflow calistirmasinin bekledigi insan girdisi.
/// </summary>
/// <remarks>
/// <para>
/// Bekleyen istekler <strong>ayri bir tabloda tutulmaz</strong>; calistirmanin
/// <see cref="RunEventType.WorkflowRequest"/> olaylarindan okunur. Gerekce:
/// olay akisi zaten append-only ve kiraci filtrelidir (K-014), ikinci bir kayit
/// hatti ayni bilgiyi iki yerde tutup ayrisma riski uretirdi.
/// </para>
/// <para>
/// 🚨 <strong>Yanit yeni bir calistirma acar.</strong> Bekleyen bir istegi
/// yanitlamak, calistirmayi kontrol noktasindan sürdürur ve yeni bir
/// <c>runs</c> satiri uretir. Ayni satiri yeniden acmak olay akisinin
/// append-only kuralini bozardi.
/// </para>
/// </remarks>
public sealed record WorkflowPendingRequest
{
    /// <summary>Istegi ureten calistirmanin kimligi.</summary>
    public required Guid RunId { get; init; }

    /// <summary>
    /// Istegin kimligi. Yanit verirken bu deger gonderilir ve kontrol
    /// noktasindan sürdürulen yurutmede ayni kimlikle yeniden yayinlanan
    /// istekle eslestirilir.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>Istegi yayinlayan portun kimligi. Grafta bir dugume karsilik gelir.</summary>
    public required string PortId { get; init; }

    /// <summary>Istegin veri tipinin adi.</summary>
    public string? RequestType { get; init; }

    /// <summary>Beklenen yanit tipinin adi.</summary>
    public string? ResponseType { get; init; }

    /// <summary>
    /// Kullaniciya gosterilecek istek metni. Plan onayinda planin kendisidir.
    /// </summary>
    public string? Prompt { get; init; }

    /// <summary>
    /// Arayuzun hangi girdi alanini gosterecegini belirler.
    /// </summary>
    public required WorkflowRequestForm Form { get; init; }

    /// <summary>Istegin yayinlandigi an (UTC).</summary>
    public DateTimeOffset RequestedAt { get; init; }
}

/// <summary>
/// Bekleyen bir istegin arayuzde nasil sorulacagi.
/// </summary>
/// <remarks>
/// <para>
/// Bicim, portun <em>yanit tipinden</em> turetilir. Sunucu bunu bilerek
/// hesaplar: yanit tipini istemcinin cozmesi, .NET tip adlarini kablo
/// sozlesmesine sokmak olurdu.
/// </para>
/// <para>JSON'da ad olarak yazilir (K-040).</para>
/// </remarks>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<WorkflowRequestForm>))]
public enum WorkflowRequestForm
{
    /// <summary>Serbest JSON. Arayuz bir metin kutusu gosterir ve icerigi oldugu gibi gonderir.</summary>
    Json = 0,

    /// <summary>Duz metin yanit.</summary>
    Text = 1,

    /// <summary>Evet / hayir.</summary>
    Boolean = 2,

    /// <summary>
    /// Plan onayi: onayla ya da bir duzeltme metniyle geri gonder.
    /// <c>Magentic</c> deseninin plan gozden gecirme akisidir.
    /// </summary>
    PlanReview = 3,
}

/// <summary>Bekleyen bir istege verilen yanit.</summary>
/// <remarks>
/// Alanlar birbirini disliyor degildir: hangisinin kullanilacagini portun yanit
/// tipi belirler. Hicbiri portun bekledigi tipe cevrilemezse istek
/// <strong>reddedilir</strong> - yanlis tipte bir yaniti sessizce kabul etmek,
/// yurutmeyi anlasilmaz bir noktada bozardi.
/// </remarks>
public sealed record WorkflowRespondRequest
{
    /// <summary>Yanitlanan calistirmanin kimligi.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Yanitlanan istegin kimligi.</summary>
    public required string RequestId { get; init; }

    /// <summary>Evet/hayir yaniti; plan onayinda "planı onayla" anlamina gelir.</summary>
    public bool? Approved { get; init; }

    /// <summary>Metin yaniti; plan onayinda duzeltme talimatidir.</summary>
    public string? Text { get; init; }

    /// <summary>Serbest JSON yanit. Portun yanit tipine cozulur.</summary>
    public string? Json { get; init; }

    /// <summary>
    /// Sürdürulecek kontrol noktasinin kimligi. Bos birakilirsa calistirmanin
    /// en son kontrol noktasi kullanilir.
    /// </summary>
    public string? CheckpointId { get; init; }

    /// <summary>Yeni calistirmanin kimligi. Verilirse kayit bu kimlikle acilir.</summary>
    public Guid? NewRunId { get; init; }
}
