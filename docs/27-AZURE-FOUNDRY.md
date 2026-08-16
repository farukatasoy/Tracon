# Faz 27 — Azure OpenAI (ve ertelenen Azure AI Foundry)

> **Durum:** ✅ Tamamlandı (2026-08-05) — Azure OpenAI yapıldı, **Foundry ertelendi**
> **Kaynak:** [BEYIN-FIRTINASI.md](arsiv/BEYIN-FIRTINASI.md) · **F-04**
> **Önkoşul:** [Faz 8](08-SAGLAYICI-GENISLEMESI.md) · [Faz 26](26-ANTHROPIC-VE-GEMINI.md)
> **Paketler:** **`AgentPrism.Azure` (YENİ)**
> **Migration:** Yok

---

## Amaç ve Sonuç

Kurumsal .NET dünyasının varsayılan yolu Azure'dur. Planda **iki ayrı** yetenek vardı:

| Yetenek | Ne | Durum |
|---------|-----|-------|
| **Azure OpenAI** | Aynı modeller, Azure uç noktası ve kimliği | ✅ Yapıldı — bir `IModelProvider` |
| **Azure AI Foundry Agents** | Azure'da **barındırılan** agent'lar | ⏸️ **Ertelendi** — gerekçe ölçümle, K-212 |

**Sonuç:** `AgentPrism.Azure` paketi üretiliyor. Altı sağlayıcı (`openai`,
`openai-responses`, `openrouter`, `anthropic`, `google`, `azure-openai`) aynı
uygulamada çalışıyor. Azure yolunda gerçek çalıştırma, tool çağrısı, akışlı token
sayımı ve sağlık denetimi doğrulandı.

---

## 27.0 — Ölçüm: sonuç planı iki yerde değiştirdi

Fazın maliyeti iki soruya bağlıydı. İkisinin de cevabı planın beklediğinden farklı çıktı.

### Ölçüm 1 — Foundry'nin sorunu sürüm uyumu değil, **ağırlık**

Plan "`Microsoft.Agents.AI.Foundry` 1.5.0 ile MAF 1.16.0 uyuşmayabilir, o zaman
yapılmaz" diyordu. Ölçüldü (2026-08-05):

| Ölçüm | Sonuç |
|---|---|
| En son Foundry sürümü | **1.5.0** (MAF çekirdeği 1.16.0) |
| MAF 1.16.0 ile birlikte restore | ✅ sorunsuz — sürüm düşürme hatası yok |
| Derleme ve **tip yükleme** | ✅ `Microsoft.Agents.AI.Foundry, Version=1.5.0.0` yüklendi, tüm public tipler yansımayla listelendi |
| 🚨 Geçişli bağımlılık | **37 paket** — `Azure.AI.Projects`, `Azure.Storage.Blobs`, `Azure.Identity`, `Microsoft.Identity.Client`, `Google.Protobuf`, `Microsoft.ML.Tokenizers`, `System.Numerics.Tensors`, `Microsoft.Extensions.AI.Evaluation` |

Karşılaştırma: K-205'te "ağır" sayılıp uzun uzun tartışılan `Google.GenAI` **11**
geçişli bağımlılık taşıyor. Foundry bunun üç katı. Kararı bu ölçüm belirledi (K-212).

### Ölçüm 2 — 🚨 `Azure.AI.OpenAI` 2.1.0'ın Azure'a özgü uzantıları **çalışma anında kırık**

Bu, fazın en pahalı bulgusudur ve Faz 26'nın "ölç, varsayma" dersinin birebir tekrarıdır.

`Azure.AI.OpenAI` 2.1.0, `OpenAI` **2.1.0**'a karşı derlendi. AgentPrism `OpenAI`
**2.12.0** kullanıyor (`AgentPrism.OpenAI`'ın ihtiyacı; merkezî paket yönetimi tek
sürüm zorlar). NuGet çakışmayı sessizce 2.12.0'a çözer, **derleme sıfır uyarı verir**,
ama:

```
KIRIK   SetNewMaxCompletionTokensPropertyEnabled -> MissingMethodException:
        'OpenAI.Chat.ChatCompletionOptions.get_SerializedAdditionalRawData()' bulunamadi
KIRIK   GetDataSources                           -> ayni istisna
KIRIK   AddDataSource                            -> ayni istisna
```

`AzureChatExtensions`'ın **istek tarafı metotlarının tamamı** kırık.

**Temel sohbet yolu ise tam çalışıyor.** Sahte bir Azure ucuyla uçtan uca ölçüldü:

```
ISTEK : POST /openai/deployments/uretim-gpt/chat/completions?api-version=2024-10-21
BASLIK: api-key=SAHTE-AZURE-ANAHTARI-xyz789
GOVDE : {"temperature":0.3,"messages":[…],"model":"uretim-gpt","max_completion_tokens":64}
YANIT : 'merhaba'   token: giris=11 cikis=3
```

Sonuç: paket temel yolu kullanır, uzantıların hiçbirine dokunmaz ve bu yüzden
**hiçbir `ProviderSettings` anahtarı sunmaz** (K-211). `max_completion_tokens`
zaten doğru gidiyor — OpenAI SDK'sı bu adı kendisi kullanır, Azure uzantısına
gerek yoktur (ölçüldü).

### Bağımlılık ölçümü (Azure OpenAI yolu — hafif)

```
AgentPrism.Azure paketi -> 4 dogrudan bagimlilik
  AgentPrism.Core · Azure.AI.OpenAI 2.1.0 · Azure.Core 1.61.0 · Microsoft.Extensions.AI.OpenAI 10.8.3
```

`Azure.AI.OpenAI`'ın getirdiği tek **yeni** aile `Azure.Core`'dur; `OpenAI`,
`System.ClientModel`, `System.Memory.Data` ve `Microsoft.Bcl.AsyncInterfaces`
zaten grafikte vardı. Paket `IsAotCompatible=true` altında **sıfır uyarı** derledi.

---

## Gerçekleşen Public API

Plandaki taslak imzalar değil, **koddaki gerçek imzalar**.

```csharp
public static class AzureOpenAIProviderNames
{
    public const string AzureOpenAI    = "azure-openai";   // KARARLI: veritabaninda saklanir
    public const string SettingsPrefix = "azure-openai";

    // BOS. Azure'un ek alan yazma yolu kirik (K-211).
    public static IReadOnlyList<string> SupportedSettings { get; }
}

public sealed class AzureOpenAIProviderOptions
{
    public const string SectionName = "AgentPrism:Providers:AzureOpenAI";

    public Uri?                  Endpoint          { get; set; }   // ZORUNLU
    public string?               ApiKey            { get; set; }   // SIR
    public Func<TokenCredential>? CredentialFactory { get; set; }  // Azure.Core — Azure.Identity DEGIL
    public string?               DefaultDeployment { get; set; }   // model DEGIL, DEPLOYMENT
    public string?               Audience          { get; set; }   // egemen bulut token kapsami
    public TimeSpan?             Timeout           { get; set; }
    public IList<ModelDescriptor> Models           { get; }        // Name = DEPLOYMENT adi
}

public sealed class AzureOpenAIProviderOptionsValidator : IValidateOptions<AzureOpenAIProviderOptions>;

public sealed class AzureOpenAIChatClientFactory
{
    public AzureOpenAIChatClientFactory(AzureOpenAIProviderOptions options, ILoggerFactory? loggerFactory = null);
    public AzureOpenAIChatClientFactory(AzureOpenAIClient client, string? defaultDeployment = null,
                                        ILoggerFactory? loggerFactory = null);

    public IChatClient CreateChatClient(ModelBinding binding);
    public static AzureOpenAIClient CreateClient(AzureOpenAIProviderOptions options);
}

public sealed class AzureOpenAIModelProvider : IModelProvider, IModelProviderHealthCheck
{
    public AzureOpenAIModelProvider(string name, AzureOpenAIChatClientFactory chatClientFactory,
                                    IReadOnlyList<ModelDescriptor> models,
                                    ILogger<AzureOpenAIModelProvider>? logger = null,
                                    AzureOpenAIProviderOptions? healthCheckOptions = null);
}

public static class AzureOpenAIModelCatalog { public static IReadOnlyList<ModelDescriptor> Build(AzureOpenAIProviderOptions options); }

public static class AzureOpenAIProviderExtensions
{
    public static IAgentPrismBuilder UseAzureOpenAI(this IAgentPrismBuilder b, Uri endpoint, string apiKey,
                                                    Action<AzureOpenAIProviderOptions>? configure = null);
    public static IAgentPrismBuilder UseAzureOpenAI(this IAgentPrismBuilder b, IConfiguration configurationSection,
                                                    Action<AzureOpenAIProviderOptions>? configure = null);
    public static IAgentPrismBuilder UseAzureOpenAI(this IAgentPrismBuilder b, Action<AzureOpenAIProviderOptions> configure);
}

// internal
internal sealed class AzureOpenAIProviderHealthCheck : IModelProviderHealthCheck
{
    internal const string ApiVersion       = "2024-10-21";
    internal const string DefaultAudience  = "https://cognitiveservices.azure.com/.default";
    internal static Uri BuildModelsEndpoint(Uri baseEndpoint);
    internal static ValueTask<IReadOnlyList<string>> ReadModelIdsAsync(HttpResponseMessage response, CancellationToken ct);
}
```

`AgentPrism.Abstractions` ve `AgentPrism.Core` **değişmedi**. Faz 26'nın devrettiği
`ModelProviderSettings`, `ContentFilterDetectingChatClient` ve devre kesici olduğu
gibi çalıştı — devir notu doğru çıktı.

---

## Plandan Sapmalar

Altı sapma var. Hepsi ölçüme veya kullanıcı kararına dayanıyor.

### S1 — Foundry bu fazda **yapılmadı** *(kullanıcı kararı)*

Plan Foundry'yi koşullu bırakıyordu ve koşulu "sürüm uyumu" olarak tanımlıyordu.
Sürüm uyumu **sağlandı**; iş yine de ertelendi. Gerekçe üç katmanlıdır (K-212):

1. **37 geçişli paket.** K-205'te 11 için verilen kararın üç katı.
2. **Doğrulanamazlık.** `FoundryAgentSource` gerçek bir Azure aboneliği olmadan
   sahte istemciden öteye test edilemez; DoD'nin yarısı boş kalırdı.
3. **Ödünleşme.** Foundry agent'ı AgentPrism'in tool onayı, kalıcılık, kiracı
   yalıtımı ve replay garantilerini kaybettirir. Sessizce yarım çalışan bir
   özellik, hiç olmayan özellikten kötüdür.

Ertelenen işin devir notu aşağıdadır; ölçümler kaybolmadı.

### S2 — Sağlayıcı adı `azure-openai`, `azure` değil *(kullanıcı kararı)*

Plan `SettingsPrefix = "azure"` diyordu. Ad `ModelBinding.Provider` üzerinden
**veritabanında saklanır**; sonradan değiştirmek kayıtlı tüm agent tanımlarını
bozar (K-207'nin dersi). `azure` adı tüm Azure dünyasını tek ada kilitlerdi;
Foundry veya Azure AI Inference sonradan eklenirse yanıltıcı kalırdı. Karar K-210.

Paket adı plandaki gibi `AgentPrism.Azure` kaldı — paket ileride başka Azure
servislerini de barındırabilir.

### S3 — Kimlik fabrikası tüketiciden gelir; `Azure.Identity` alınmadı

Plan bunu zaten öneriyordu; uygulandı ve **ölçümle doğrulandı**.
`AzureOpenAIClient`'ın `(Uri, Azure.Core.TokenCredential, AzureOpenAIClientOptions)`
kurucusu var; `TokenCredential` `Azure.Core` derlemesindedir. Yani yönetilen kimlik
için `Azure.Identity` **gerekmiyor** — tüketici kendi `DefaultAzureCredential`'ını
verir. Karar K-210.

Fabrika istemci kurulurken **bir kez** çağrılır (test: `Kimlik_fabrikasi_kurulum_sirasinda_bir_kez_cagrilir`);
her derlemede yeni kimlik üretmek token önbelleğini boşa çıkarırdı.

Kimlik fabrikası API anahtarını **ezer**: ikisi de verildiğinde daha güvenli olan kazanır.

### S4 — Sağlayıcı **hiçbir** `ProviderSettings` anahtarı sunmaz

Plan "üç şey yazın: önek sabiti, `SupportedSettings` listesi, bir `DelegatingChatClient`"
diyordu. Ölçüm sonrası ikisi yazıldı, üçüncüsü **yazılmadı**: sunulacak bir ayar yok
(bkz. Ölçüm 2). `SupportedSettings` bilinçli olarak boştur ve
`ModelProviderSettings.Validate` bu durumu zaten karşılıyor:

> `'azure-openai' sağlayıcısı hiçbir ek ayar desteklemiyor.`

Dekoratör dosyası hiç oluşturulmadı. Karar K-211.

### S5 — Responses yüzeyi desteklenmiyor *(kullanıcı kararı)*

Açık soru 3'ün cevabı ölçümle geldi: `AzureOpenAIClient.GetResponsesClient()`
Azure'a özgü bir istemci **döndürmüyor** — `OpenAI.OpenAIClient+TopLevelResponsesClient`,
yani OpenAI'ın taban sınıfını döndürüyor. (Karşılaştırma: `GetChatClient()`
Azure'a özgü `Azure.AI.OpenAI.Chat.AzureChatClient` döndürüyor.) Azure'ın
yol/`api-version` şekline uyduğu **doğrulanamaz** ve gerçek bir Azure kaynağı
olmadan denenemez. Paket yalnız Chat Completions kullanır; ikinci bir sağlayıcı
adı kaydedilmez.

### S6 — `Audience` ayarı eklendi (planda yoktu)

Egemen bulutlar (Azure Government, Azure China) farklı bir token kapsamı ister.
İstemci `AzureOpenAIClientOptions.Audience`'ı içeride tuttuğu için tüketici bunu
dışarıdan düzeltemezdi. Tek bir `string?` ayar hem istemciye hem sağlık denetiminin
token kapsamına gider. Ölçüldü: `AzureOpenAIAudience.AzurePublicCloud` değeri zaten
tam kapsam metnidir (`https://cognitiveservices.azure.com/.default`), dönüşüm gerekmez.

---

## 🚨 Deployment ≠ model

Fazın taşıdığı tek büyük kavram karışıklığı budur ve üç yerde açıkça anlatılır:
ayar adı (`DefaultDeployment`), hata mesajı ve README.

Azure'da çağrılan şey **deployment adıdır** ve istegin yoluna girer:

```
POST {endpoint}/openai/deployments/{deployment}/chat/completions?api-version=2024-10-21
```

Adı Azure kaynağını kuran kişi seçer; aynı model iki kaynakta iki farklı adla
konuşlandırılmış olabilir. Bu yüzden yanlış bir ad "model bulunamadı" değil
**HTTP 404** verir. Alan boş bırakıldığında hata mesajı bunu söyler:

> Deployment adi bos ve varsayilan deployment tanimli degil. Azure OpenAI'da
> `ModelBinding.Model` alani bir MODEL adi degil, Azure kaynaginizda tanimli bir
> **DEPLOYMENT** adi bekler. …

Katalogdaki `Name` alanları da deployment adıdır. Katalog yine bir doğrulama
listesi **değildir** (K-032): Azure'da yeni bir deployment açmak AgentPrism
yapılandırmasının güncellenmesini beklememelidir.

---

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Azure/                          YENI PAKET
├── AgentPrism.Azure.csproj
├── AzureOpenAIProviderNames.cs                SupportedSettings BOS (K-211)
├── AzureOpenAIProviderOptions.cs              class (K-035)
├── AzureOpenAIProviderOptionsValidator.cs
├── AzureOpenAIModelCatalog.cs
├── AzureOpenAIChatClientFactory.cs
├── AzureOpenAIModelProvider.cs
├── AzureOpenAIProviderHealthCheck.cs          internal — GET {endpoint}/openai/models
├── AzureOpenAIProviderExtensions.cs
├── README.md
└── PublicAPI.{Shipped,Unshipped}.txt

tests/AgentPrism.Azure.UnitTests/              YENI PROJE (48 test)
├── AgentPrism.Azure.UnitTests.csproj
├── AzureOpenAIProviderExtensionsTests.cs
├── AzureOpenAIChatClientFactoryTests.cs
├── AzureOpenAIModelCatalogTests.cs
├── AzureOpenAIProviderHealthCheckTests.cs
├── SecretLeakTests.cs
└── Infrastructure/{TestData,RecordingLoggerProvider}.cs

tests/AgentPrism.Core.UnitTests/
└── Architecture/DependencyDirectionTests.cs   AgentPrism.Azure eklendi

tests/AgentPrism.AspNetCore.FunctionalTests/
├── MultiProviderTests.cs                      +1 test, 4 test guncellendi
└── *.csproj                                   Azure referansi

samples/AgentPrism.Api/
├── Program.cs                                 UseAzureOpenAI + "azure-destek" agent'i
├── appsettings.json                           AzureOpenAI semasi
└── AgentPrism.Api.csproj                      ProjectReference

AgentPrism.slnx, Directory.Packages.props      paket + test projesi + iki SDK surumu
```

**Dekoratör dosyası yoktur** — diğer iki sağlayıcı paketinden tek yapısal fark budur.

---

## Testler

**1700 test geçiyor** (+49). Karşılaştırma tabanı Faz 26'nın 1651'idir.
SQL Server'ın 213 testi bu makinede koşmuyor ve bu sayıya **dâhil değildir** —
bkz. DoD notu.

| Proje | Sayı | Faz 27'de eklenen |
|-------|------|-------------------|
| `AgentPrism.Azure.UnitTests` | 48 | tamamı yeni |
| `AgentPrism.AspNetCore.FunctionalTests` | 261 | +1 |
| diğerleri | değişmedi | — |

| Test sınıfı | Neyi doğrular |
|-------------|---------------|
| `AzureOpenAIProviderExtensionsTests` | Tek sağlayıcı kaydı, ikinci çağrı çoğaltmaz, tek fabrika paylaşımı, yapılandırmadan okuma, yapılandırma sonrası kimlik fabrikası verilebilmesi, adressiz/kimliksiz kayıt başlangıçta hata, anahtarsız ama kimlikli kaydın geçerli olması |
| `AzureOpenAIChatClientFactoryTests` | Boru hattı üyeleri, deployment→üstveri eşlemesi, varsayılan deployment, **hata mesajının DEPLOYMENT dediği**, kimlik fabrikasının bir kez çağrılması, kimliğin anahtarı ezmesi, `null` kimlik, egemen bulut kapsamı, **her ayarın reddedilmesi**, yanlış öneğin ayrı mesaj vermesi |
| `AzureOpenAIModelCatalogTests` | Yerleşik liste **yoktur**, sıralama, ad çakışması, adsız girdi, katalogda olmayan deployment reddedilmez |
| `AzureOpenAIProviderHealthCheckTests` | `openai/models?api-version=` birleştirmesi, `{"data":[…]}` ayrıştırma, bağlanamayan uçta **ne anahtar ne adres** sızmaz, adressiz denetim ağa çıkmaz, genel/egemen bulut kapsamı, kimlik nesnesinin denetimler arası paylaşılması |
| `SecretLeakTests` | Anahtar 8 farklı çıktıda görünmüyor; **doğrulama mesajı adres de taşımıyor**; ayar sınıfı kendi `ToString`'ini tanımlamıyor (K-035) |
| `MultiProviderTests` | Altı sağlayıcı aynı anda, katalog ayrımı (Azure'da deployment adı), sağlık ucu, anahtar sızmaz, Azure'un hiçbir ayar kabul etmediğini söylemesi |

**Gerçek model çağrısı yapan test yoktur** (Faz 3'ten beri geçerli karar).

---

## Bitiş Ölçütleri (DoD)

Elle doğrulama: `samples/AgentPrism.Api`, `ASPNETCORE_ENVIRONMENT=Development`,
2026-08-05. **Gerçek bir Azure aboneliği kullanılmadı** — bunun yerine Azure'un
veri düzlemi sözleşmesini birebir taklit eden yerel bir uç kullanıldı; gelen
istegin yolu, başlıkları ve gövdesi kaydedildi.

| Ölçüt | Durum | Kanıt |
|-------|-------|-------|
| `AgentPrism.Azure` paketi üretiliyor | ✅ | **14 paket** (Faz 26'da 13 idi); 4 doğrudan bağımlılık, geçişli sızıntı yok |
| Azure deployment'ı ile çalıştırma ve **tool çağrısı** | ✅ | aşağıdaki çıktı, 3 — `ToolInvoking`/`ToolInvoked` üretildi, iki HTTP turu |
| Akışlı çalıştırmada token kullanımı toplanıyor | ✅ | `isStreaming=true`, giriş=168 çıkış=9 toplam=177 |
| Managed identity yolu belgelendi ve **çalıştığı gösterildi** | ✅ | aşağıdaki çıktı, 6 — `Bearer` başlığı gitti, kapsam doğru |
| Deployment/model ayrımı README'de ve hata mesajında açık | ✅ | README bölüm "🚨 Deployment ≠ model"; test `…hata_model_degil_deployment_bekledigini_soyler` |
| Sağlık denetimi çalışıyor | ✅ | aşağıdaki çıktı, 2 |
| `ProviderSettings` davranışı açık | ✅ | aşağıdaki çıktı, 4 ve 5 — iki ayrı `400` mesajı |
| Bağımlılık sayısı ölçüldü ve karar defterine yazıldı | ✅ | K-211 (Azure), K-212 (Foundry, 37 paket) |
| Paket kontrol listesi tamam; sır taraması boş | ✅ | README + slnx + `DependencyDirectionTests`; `grep` taraması boş; API çıktılarında ve günlükte anahtar **0 kez** |
| Dört doğrulama kapısı sıfır uyarı | ✅ | build / test / pack / format → 0 uyarı, 0 hata |
| (Foundry) uzak agent katalogda salt okunur görünüyor | ⏸️ | **Yapılmadı** — K-212, bkz. sapma S1 |

> ⚠️ **SQL Server sözleşme testleri bu makinede koşmadı.** `mssql/server` konteyneri
> Apple Silicon üzerinde başlamıyor (`Testcontainers` `TimeoutException`) — Faz 23'ten
> beri bilinen ortam sınırı, bu fazın değişikliğiyle ilgisi yoktur.
>
> ⚠️ **Gerçek Azure aboneliğiyle doğrulama yapılmadı.** Sözleşme (yol, `api-version`,
> `api-key`/`Bearer`, deployment adı, `max_completion_tokens`) taklit uçla
> doğrulandı; gerçek kaynakta kalan tek belirsizlik sağlık denetiminin döndüğü
> liste biçimidir (aşağıya bakın).

### Gerçek çıktı (2026-08-05)

```
# 1) Alti saglayici ayni uygulamada
$ curl -s localhost:5391/agentprism/api/models
  anthropic          ['claude-haiku-4-5-20251001', 'claude-opus-5', 'claude-sonnet-5']
  azure-openai       ['uretim-gpt']                 # <- DEPLOYMENT adi
  google             ['gemini-3.1-flash-lite', 'gemini-3.1-pro-preview', 'gemini-3.6-flash']
  openai             ['gpt-5.4-mini', 'gpt-5.6-luna', 'gpt-5.6-terra']
  openai-responses   ['gpt-5.4-mini', 'gpt-5.6-luna', 'gpt-5.6-terra']
  openrouter         ['openai/gpt-5.4-mini']

# 2) Saglik denetimi — ucret uretmez, GET {endpoint}/openai/models?api-version=2024-10-21
$ curl -s ".../api/models/health?refresh=true"
  anthropic          Healthy  gecikme=00:00:00.475  model=10
  azure-openai       Healthy  gecikme=00:00:00.006  model=3
  google             Healthy  gecikme=00:00:00.370  model=50
  openai             Healthy  gecikme=00:00:00.990  model=3
  openai-responses   Healthy  gecikme=00:00:00.573  model=3
  openrouter         Healthy  gecikme=00:00:00.213  model=200

# 3) GERCEK CALISTIRMA — akisli, tool cagrili
  durum : Completed  akisli=True  model=uretim-gpt
  token : giris=168 cikis=9 toplam=177
    seq=0 RunStarted
    seq=1 ToolInvoking  tool=get_order_status  yuk=orderId=ORD-42
    seq=2 ToolInvoked   tool=get_order_status  yuk=ORD-42 numarali siparis kargoya verildi...
    seq=3 MessageDelta  "ORD-42 siparisiniz "
    seq=4 MessageDelta  "kargoya verildi."
    seq=5 RunCompleted

# Azure ucuna ULASAN istekler (tool dongusu iki tur):
  POST /openai/deployments/uretim-gpt/chat/completions?api-version=2024-10-21
       api-key=SAHTE-AZURE-ANAHTARI-xyz789  akisli=True
       model=uretim-gpt  max_completion_tokens=1024  tool_sayisi=3
  POST /openai/deployments/uretim-gpt/chat/completions?api-version=2024-10-21   (ayni)

# 4) Taninmayan ayar -> HTTP 400
  'azure-bilinmeyen-ayar' agent'i derlenemedi: ModelBinding.ProviderSettings icinde
   su anahtarlar taninmiyor: azure-openai.yokBoyleAyar.
   'azure-openai' saglayicisi hicbir ek ayar desteklemiyor.

# 5) Yanlis onekli ayar -> AYRI mesaj, HTTP 400
  'azure-yanlis-onek' agent'i derlenemedi: ModelBinding.ProviderSettings icinde
   su anahtarlar 'azure-openai' saglayicisina ait degil: anthropic.promptCaching.
   …saglayici degistirildiginde eski ayarlar temizlenmelidir.
   'azure-openai' saglayicisi hicbir ek ayar desteklemiyor.

# 6) YONETILEN KIMLIK (Entra) — AgentPrism'in kendi fabrikasindan gecerek
  YANIT  : 'ORD-42 siparisiniz kargoya verildi.'
  MODEL  : uretim-gpt      TOKEN: giris=120 cikis=18
  KAPSAM : https://cognitiveservices.azure.com/.default
  Azure ucuna ulasan istek: api-key=None  (Authorization: Bearer kullanildi)

# Sir taramasi — API ciktilari ve uygulama gunlugu
  /api/models · /api/models/health · /api/meta · /api/agents · /api/runs -> anahtar 0 kez
  uygulama gunlugu                                                       -> anahtar 0 kez
```

---

## Kullanım

```csharp
// API anahtari ile
builder.AddAgentPrism()
       .UseAzureOpenAI(configuration.GetSection(AzureOpenAIProviderOptions.SectionName));

// Yonetilen kimlik ile — Azure.Identity TUKETICININ paketi
builder.AddAgentPrism()
       .UseAzureOpenAI(o =>
       {
           o.Endpoint = new Uri("https://benim-kaynagim.openai.azure.com/");
           o.CredentialFactory = static () => new DefaultAzureCredential();
           o.DefaultDeployment = "uretim-gpt";
       })
       .AddAgent(new AgentDefinition
       {
           Name = "azure-destek",
           Instructions = "Sen bir destek asistanisin.",
           Model = new ModelBinding
           {
               Provider = AzureOpenAIProviderNames.AzureOpenAI,
               Model = "uretim-gpt",       // 🚨 DEPLOYMENT adi, model adi DEGIL
               MaxOutputTokens = 1024,
           },
           ToolNames = ["get_order_status"],
       });
```

---

## Bu Fazda Verilen Kararlar

Karar defterine yazıldı (`docs/KARARLAR.md`, **K-210 – K-213**):

1. **K-210** — Sağlayıcı adı `azure-openai`; kimlik fabrikası tüketiciden gelir, `Azure.Identity` alınmadı.
2. **K-211** — Azure sağlayıcısı hiçbir `ProviderSettings` anahtarı sunmaz; `AzureChatExtensions` çalışma anında kırık.
3. **K-212** — Azure AI Foundry ertelendi; gerekçe sürüm uyumu değil, 37 geçişli paket ve doğrulanamazlık.
4. **K-213** — Azure'ın Responses yüzeyi desteklenmiyor; SDK Azure'a özgü bir `ResponsesClient` sunmuyor.

---

## Riskler — kapanış durumu

| Risk | Sonuç |
|------|-------|
| `Microsoft.Agents.AI.Foundry` sürüm uyumsuzluğu | **Gerçekleşmedi.** 1.5.0 + MAF 1.16.0 sorunsuz yüklendi. Yerine ağırlık sorunu çıktı (K-212) |
| `Azure.Identity` bağımlılık şişmesi | **Ortadan kalktı.** Kimlik fabrikası tüketiciden; paket yalnız `Azure.Core`'a bağlı |
| Foundry ile garantiler sessizce kaybolur | **Gerçekleşmedi** — iş yapılmadı; ödünleşme tablosu aşağıda korundu |
| Gerçek Azure olmadan test edilemez | **Kısmen gerçekleşti.** Sözleşme taklit uçla uçtan uca doğrulandı; kalan tek belirsizlik sağlık denetiminin liste biçimi |
| Deployment adı karışıklığı | README + ayar adı + hata mesajı + test ile üç yerden kapatıldı |
| 🆕 SDK sürüm kayması | **Gerçekleşti ve ölçüldü.** `AzureChatExtensions` çalışma anında kırık; paket o yüzeye hiç dokunmaz (K-211) |

---

## Sonraki Faza Devir Notu

- **Sağlayıcı ailesi tamamlandı:** `openai`, `openai-responses`, `openai-compatible`
  (adlandırılmış), `anthropic`, `google`, `azure-openai`. Yeni bir sağlayıcı eklemek
  artık **10 dosyalık** bir kalıptır; en kısa örnek `AgentPrism.Azure`'dur
  (dekoratörü olmayan tek paket).
- 🚨 **Bir SDK'yı merkezî sürüm yönetimi altında kullanırken, o SDK'nın *başka bir
  SDK'nın* tipini genişlettiği yerleri çalışma anında deneyin.** `Azure.AI.OpenAI`
  2.1.0 + `OpenAI` 2.12.0 derlemede sıfır uyarı verdi, kullanımda
  `MissingMethodException` attı. Derleme yeşilliği burada hiçbir şey kanıtlamaz.
- **`Azure.AI.OpenAI` yükseltilirse `SupportedSettings` yeniden değerlendirilmelidir.**
  Uzantılar OpenAI 2.12.0 ile uyumlu bir sürüme gelirse `azure-openai.dataSources`
  gibi anahtarlar açılabilir. `AzureOpenAIProviderNames.SupportedSettings` boş liste
  olduğu için genişleme kırıcı değildir.
- **`AzureOpenAIClientOptions.ServiceVersion` yalnız iki değer taşıyor**
  (`V2024_06_01`, `V2024_10_21`) ve varsayılan sonuncusudur. Yeni bir Azure API
  sürümü gerektiğinde `Azure.AI.OpenAI` yükseltilmelidir; AgentPrism bu enum'u
  public yüzeyine **almadı** (kaçış kapısı: hazır `AzureOpenAIClient` alan kurucu).
- **Sağlık denetiminin döndürdüğü liste model listesidir, deployment listesi
  değildir.** Gerçek bir abonelikte biçim doğrulanmalıdır; beklenmeyen gövde boş
  liste döndürür ve denetimi `Healthy` bırakır (Google'ın OpenAI-biçimi düşüşüyle
  aynı desen).
- **`DependencyDirectionTests.AllowedReferences` hâlâ `AgentPrism.SqlServer`,
  `AgentPrism.Sqlite` ve `AgentPrism.Sql.Shared` paketlerini içermiyor**
  (Faz 23/24'ten kalan boşluk, Faz 26'da da açıktı). Bu fazda `AgentPrism.Azure`
  eklendi; SQL paketleri hâlâ açık.

### Ertelenen iş — Azure AI Foundry (ölçümler korunmuştur)

Yapılacaksa **`AgentPrism.Azure.Foundry` adıyla ayrı bir paket** olmalıdır (37
geçişli bağımlılık `AgentPrism.Azure`'a bulaşmamalıdır). Ölçülmüş gerçekler:

```csharp
// Microsoft.Agents.AI.Foundry 1.5.0 — MAF 1.16.0 ile yuklenir (dogrulandi)
namespace Microsoft.Agents.AI.Foundry;

sealed class FoundryAgent : AIAgent
{
    FoundryAgent(Uri projectEndpoint, AuthenticationTokenProvider credential, string model,
                 string instructions, AIProjectClientOptions clientOptions, string name,
                 string description, IList<AITool> tools, Func<…> clientFactory,
                 ILoggerFactory loggerFactory, IServiceProvider services);
    FoundryAgent(Uri agentEndpoint, AuthenticationTokenProvider credential, …);

    ValueTask<AgentSession> CreateSessionAsync(string conversationId, CancellationToken ct);
    Task<AgentSession>      CreateConversationSessionAsync(CancellationToken ct);
}

// Uzaktaki bir agent kaydini AIAgent'a cevirir — IAgentSource'un ihtiyaci budur.
static FoundryAgent   AgentClientExtensions.AsAIAgent(AIProjectClient c, ProjectsAgentRecord r, …);
static FoundryAgent   AgentClientExtensions.AsAIAgent(AIProjectClient c, ProjectsAgentVersion v, …);
static ChatClientAgent AgentClientExtensions.AsAIAgent(AIProjectClient c, ChatClientAgentOptions o, …);

// Ayrica: FoundryAITool (Bing/SharePoint/AzureAISearch/MCP/CodeInterpreter/…),
//         FoundryMemoryProvider, FoundryEvals, HostedMcpToolboxAITool
```

🚨 **Kimlik `Azure.Core.TokenCredential` değil, `System.ClientModel`'in
`AuthenticationTokenProvider`'ıdır.** Azure OpenAI tarafındaki kimlik fabrikası
deseni olduğu gibi kopyalanamaz.

Foundry agent'ı **uzakta yaşar**: talimatı, tool'ları ve konuşma durumu Azure
tarafındadır. AgentPrism onu derlemez, **keşfeder** — yani bir `IAgentSource`'tur
(K-019). Kaybedilen garantiler:

| Konu | Durum |
|------|-------|
| Agent tanımı | **Salt okunur** — arayüzden düzenlenemez, sürümlenemez |
| Tool'lar | Azure tarafında tanımlıdır; AgentPrism'in tool defteri geçerli değildir |
| Konuşma durumu | Azure'da tutulur — K-030'un aynı gerilimi: kalıcılık, kiracı yalıtımı ve replay vaatleri **zayıflar** |
| Çalıştırma kaydı | `runs` satırı yazılır; olaylar akıştan alınır. Span'ler eksik olabilir |
| Onay akışı | AgentPrism'in tool onayı **uygulanamaz** — tool'lar uzakta çalışır |

Arayüz bu agent'ları **ayrı bir rozetle** göstermeli ve detay ekranında hangi
özelliklerin geçerli olmadığını **listelemelidir**. Sessizce yarım çalışan bir
özellik, hiç olmayan özellikten kötüdür.

- **AAD/Entra rollerinin AgentPrism rolleriyle (Faz 9) eşlenmesi yapılmadı ve
  yapılmamalıdır** (açık soru 4): eşleme tüketicinin policy tanımıdır, AgentPrism
  varsayım yapmaz. Karar değişmedi.
- Faz 20'nin fiyat çözümlemesi Azure'da farklıdır (kurumsal anlaşma fiyatı);
  `AgentPrism:Pricing` bölümü bunu zaten karşılar — katalogdaki `Name` deployment
  adı olduğu için fiyat da deployment başına verilir.
