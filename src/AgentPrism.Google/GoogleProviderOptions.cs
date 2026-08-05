namespace AgentPrism;

/// <summary>AgentPrism'in Google Gemini saglayici ayarlari.</summary>
/// <remarks>
/// <para>
/// Bu tip bilincli olarak bir <c>class</c>'tir, <c>record</c> degil: <c>record</c>'un
/// urettigi <c>ToString</c> tum ozellikleri yazar ve tek bir gunluk satiri API
/// anahtarini ifsa ederdi. Gerekce: <c>docs/KARARLAR.md</c>, karar K-035.
/// </para>
/// <para>
/// Dogrulama <see cref="GoogleProviderOptionsValidator"/> icinde elle yapilir;
/// <c>DataAnnotations</c> yansimaya dayanir ve AOT uyumunu bozar (karar K-006).
/// </para>
/// </remarks>
public sealed class GoogleProviderOptions
{
    /// <summary>Ayarlarin okundugu yapilandirma bolumunun tam yolu.</summary>
    public const string SectionName = "AgentPrism:Providers:Google";

    /// <summary>Gemini Developer API anahtari.</summary>
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
    /// Istek adresi. <see langword="null"/> ise Google'in kendi adresi
    /// (<c>https://generativelanguage.googleapis.com</c>) kullanilir.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Kullanilacak API surumu. <see langword="null"/> ise SDK varsayilani gecerlidir.
    /// Ornek: <c>v1beta</c>.
    /// </summary>
    public string? ApiVersion { get; set; }

    /// <summary>Tek bir istegin ust sure siniri. <see langword="null"/> ise kitaplik varsayilani kullanilir.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Arayuze gosterilecek model katalogu.
    /// </summary>
    /// <remarks>
    /// AgentPrism yerlesik bir model listesi tasimaz; katalog tamamen buradan gelir.
    /// Bu liste bir <em>dogrulama listesi degildir</em>: burada bulunmayan bir model
    /// adi da kullanilabilir. Gerekce: <c>docs/KARARLAR.md</c>, karar K-032.
    /// </remarks>
    public IList<ModelDescriptor> Models { get; } = [];
}
