namespace AgentPrism;

/// <summary>AgentPrism'in Anthropic (Claude) saglayici ayarlari.</summary>
/// <remarks>
/// <para>
/// Bu tip bilincli olarak bir <c>class</c>'tir, <c>record</c> degil: <c>record</c>'un
/// urettigi <c>ToString</c> tum ozellikleri yazar ve tek bir gunluk satiri API
/// anahtarini ifsa ederdi. Gerekce: <c>docs/KARARLAR.md</c>, karar K-035.
/// </para>
/// <para>
/// Dogrulama <see cref="AnthropicProviderOptionsValidator"/> icinde elle yapilir;
/// <c>DataAnnotations</c> yansimaya dayanir ve AOT uyumunu bozar (karar K-006).
/// </para>
/// </remarks>
public sealed class AnthropicProviderOptions
{
    /// <summary>Ayarlarin okundugu yapilandirma bolumunun tam yolu.</summary>
    public const string SectionName = "AgentPrism:Providers:Anthropic";

    /// <summary>Anthropic API anahtari.</summary>
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
    /// Istek adresi. <see langword="null"/> ise Anthropic'in kendi adresi kullanilir.
    /// Vekil sunucu veya ag gecidi arkasindaki kurulumlar icin doldurulur.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// <see cref="ModelBinding.MaxOutputTokens"/> verilmediginde kullanilacak ust sinir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🚨 Anthropic Messages API'sinde <c>max_tokens</c> <strong>zorunludur</strong>;
    /// OpenAI'da oldugu gibi atlanabilir bir alan degildir. Bu yuzden AgentPrism bir
    /// varsayilan tasir ve varsayilan <see langword="null"/> olamaz.
    /// </para>
    /// <para>
    /// Deger uretimin ust sinirini belirler, harcanan token'i degil. Yine de cok
    /// yuksek bir sinir bazi saglayicilarda "en kotu durum" maliyeti olarak
    /// degerlendirilir (bkz. Faz 8'de OpenRouter ile yasanan <c>HTTP 402</c>).
    /// </para>
    /// </remarks>
    public int DefaultMaxOutputTokens { get; set; } = 4096;

    /// <summary>Tek bir istegin ust sure siniri. <see langword="null"/> ise kitaplik varsayilani kullanilir.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// SDK'nin bir istegi kendiliginden kac kez yeniden deneyecegi.
    /// <see langword="null"/> ise kitaplik varsayilani kullanilir.
    /// </summary>
    /// <remarks>
    /// Devre kesici ham istek sayisina degil, <see cref="Microsoft.Extensions.AI.IChatClient"/>
    /// duzeyindeki basarisizliga bakar; bu ayar onu etkilemez. Ancak testlerde ham
    /// istek sayan bir olcum yapiliyorsa deger <c>0</c> verilmelidir.
    /// </remarks>
    public int? MaxRetries { get; set; }

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
