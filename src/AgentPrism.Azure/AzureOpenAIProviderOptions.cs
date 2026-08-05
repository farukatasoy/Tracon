using Azure.Core;

namespace AgentPrism;

/// <summary>AgentPrism'in Azure OpenAI saglayici ayarlari.</summary>
/// <remarks>
/// <para>
/// Bu tip bilincli olarak bir <c>class</c>'tir, <c>record</c> degil: <c>record</c>'un
/// urettigi <c>ToString</c> tum ozellikleri yazar ve tek bir gunluk satiri API
/// anahtarini ifsa ederdi. Gerekce: <c>docs/KARARLAR.md</c>, karar K-035.
/// </para>
/// <para>
/// Dogrulama <see cref="AzureOpenAIProviderOptionsValidator"/> icinde elle yapilir;
/// <c>DataAnnotations</c> yansimaya dayanir ve AOT uyumunu bozar (karar K-006).
/// </para>
/// </remarks>
public sealed class AzureOpenAIProviderOptions
{
    /// <summary>Ayarlarin okundugu yapilandirma bolumunun tam yolu.</summary>
    public const string SectionName = "AgentPrism:Providers:AzureOpenAI";

    /// <summary>
    /// Azure OpenAI kaynaginin adresi. Ornek:
    /// <c>https://benim-kaynagim.openai.azure.com/</c>.
    /// </summary>
    /// <remarks>
    /// Zorunludur; Azure'un tek bir genel adresi yoktur, her kaynagin kendi adresi
    /// vardir. Adres bir sir degildir ama kurumsal bir topolojiyi acik eder; hata
    /// mesajlarinda ve saglik denetimi detayinda <strong>gosterilmez</strong>.
    /// </remarks>
    public Uri? Endpoint { get; set; }

    /// <summary>Azure OpenAI kaynaginin API anahtari.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Bu deger bir sirdir ve dosyaya yazilmaz.</strong> <c>dotnet user-secrets</c>,
    /// ortam degiskeni veya bir sir yoneticisi kullanin.
    /// </para>
    /// <para>
    /// <see cref="CredentialFactory"/> verilmisse bu alan <strong>kullanilmaz</strong>.
    /// Ikisinden en az biri doldurulmalidir.
    /// </para>
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Microsoft Entra kimligini ureten fabrika. Verilirse API anahtari yerine bu
    /// kullanilir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🚨 <strong>Fabrika bilincli olarak tuketiciden gelir.</strong> Yonetilen
    /// kimlik icin gereken <c>DefaultAzureCredential</c> tipi <c>Azure.Identity</c>
    /// paketindedir; o paket kucuk degildir ve <c>Microsoft.Identity.Client</c>
    /// zincirini de getirir. <c>AgentPrism.Azure</c> onu bagimlilik olarak almaz;
    /// yalnizca <c>Azure.Core</c> soyutlamasina baglanir ve kimligi tuketicinin
    /// secmesine birakir. Gerekce: <c>docs/KARARLAR.md</c>, karar K-210.
    /// </para>
    /// <example>
    /// <code>
    /// // Tuketicinin projesinde: &lt;PackageReference Include="Azure.Identity" /&gt;
    /// options.CredentialFactory = static () =&gt; new DefaultAzureCredential();
    /// </code>
    /// </example>
    /// <para>
    /// Fabrika istemci kurulurken <strong>bir kez</strong> cagrilir. Donen
    /// <see cref="TokenCredential"/> token yenilemesini kendi yonetir.
    /// </para>
    /// </remarks>
    public Func<TokenCredential>? CredentialFactory { get; set; }

    /// <summary>
    /// <see cref="ModelBinding.Model"/> bos birakildiginda kullanilacak
    /// <strong>deployment</strong> adi.
    /// </summary>
    /// <remarks>
    /// 🚨 Azure'da cagrilan sey model adi degil, <strong>deployment adidir</strong>.
    /// Ayni model farkli adlarla konuslandirilabilir ve deployment adini kaynagi
    /// kuran kisi secer. Ayrinti: <see cref="AzureOpenAIChatClientFactory"/>.
    /// </remarks>
    public string? DefaultDeployment { get; set; }

    /// <summary>
    /// Entra kimligiyle istenecek token kapsami (audience).
    /// <see langword="null"/> ise Azure genel bulutu kullanilir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Yalnizca <see cref="CredentialFactory"/> yolunu etkiler; API anahtari
    /// yolunda kullanilmaz. Egemen bulutlar (Azure Government, Azure China) farkli
    /// bir kapsam ister.
    /// </para>
    /// <para>
    /// Genel bulut degeri: <c>https://cognitiveservices.azure.com/.default</c>.
    /// Azure Government: <c>https://cognitiveservices.azure.us/.default</c>.
    /// </para>
    /// </remarks>
    public string? Audience { get; set; }

    /// <summary>Tek bir istegin ust sure siniri. <see langword="null"/> ise kitaplik varsayilani kullanilir.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Arayuze gosterilecek model katalogu. Girdilerin <c>Name</c> alani
    /// <strong>deployment adidir</strong>.
    /// </summary>
    /// <remarks>
    /// AgentPrism yerlesik bir model listesi tasimaz; katalog tamamen buradan gelir.
    /// Bu liste bir <em>dogrulama listesi degildir</em>: burada bulunmayan bir
    /// deployment adi da kullanilabilir. Gerekce: <c>docs/KARARLAR.md</c>, karar K-032.
    /// </remarks>
    public IList<ModelDescriptor> Models { get; } = [];
}
