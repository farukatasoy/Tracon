namespace AgentPrism;

/// <summary>AgentPrism'in OpenAI saglayici ayarlari.</summary>
/// <remarks>
/// Dogrulama <see cref="OpenAIProviderOptionsValidator"/> icinde elle yapilir;
/// <c>DataAnnotations</c> kullanilmaz. Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </remarks>
public sealed class OpenAIProviderOptions
{
    /// <summary>Ayarlarin okundugu yapilandirma bolumunun tam yolu.</summary>
    public const string SectionName = "AgentPrism:Providers:OpenAI";

    /// <summary>OpenAI API anahtari.</summary>
    /// <remarks>
    /// <strong>Bu deger bir sirdir ve dosyaya yazilmaz.</strong> <c>dotnet user-secrets</c>,
    /// ortam degiskeni veya bir sir yoneticisi kullanin. Anahtar hicbir kosulda
    /// veritabanina yazilmaz, API'den donmez ve arayuzde gosterilmez.
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>
    /// <see cref="ModelBinding.Model"/> bos birakildiginda kullanilacak model adi.
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// Istek adresi. <see langword="null"/> ise OpenAI'in kendi adresi kullanilir.
    /// OpenAI uyumlu ara sunucular icin doldurulur.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Kurulus (organization) kimligi. Cok kuruluslu hesaplarda kullanilir.</summary>
    public string? Organization { get; set; }

    /// <summary>Tek bir istegin ust sure siniri. <see langword="null"/> ise kitaplik varsayilani kullanilir.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Arayuze gosterilecek model katalogu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AgentPrism yerlesik bir model listesi tasimaz; katalog tamamen buradan gelir.
    /// Model adlari ve fiyatlari, paketin yayin sikligindan cok daha hizli degisir.
    /// Gerekce: <c>docs/KARARLAR.md</c>, karar K-032.
    /// </para>
    /// <para>
    /// Bu liste bir <em>dogrulama listesi degildir</em>. Burada bulunmayan bir model
    /// adi da kullanilabilir; katalog yalnizca arayuzun model secim ekranini ve
    /// maliyet hesabini besler.
    /// </para>
    /// </remarks>
    public IList<ModelDescriptor> Models { get; } = [];

    /// <summary>
    /// <c>UseOpenAICompatible()</c> ile kaydedilen bir saglayici icin Responses API
    /// yuzeyini de acar mi.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Yalnizca <c>UseOpenAICompatible()</c> tarafindan okunur.</strong>
    /// <c>UseOpenAI()</c> bu alani yok sayar; resmi OpenAI saglayicisi her zaman
    /// iki yuzeyi de kaydeder (<see cref="OpenAIProviderNames.ChatCompletions"/> ve
    /// <see cref="OpenAIProviderNames.Responses"/>).
    /// </para>
    /// <para>
    /// Varsayilan <see langword="false"/>: cogu OpenAI uyumlu sunucu
    /// <c>/v1/responses</c> ucunu uygulamaz. Acildiginda ikinci bir saglayici
    /// <c>{ad}-responses</c> adiyla kaydedilir.
    /// </para>
    /// </remarks>
    public bool EnableResponsesSurface { get; set; }
}
