# Faz 26 — Anthropic (Claude) ve Google Gemini Sağlayıcıları

> **Durum:** ✅ Tamamlandı (2026-08-05)
> **Kaynak:** [BEYIN-FIRTINASI.md](arsiv/BEYIN-FIRTINASI.md) · **F-01**, **F-02**
> **Önkoşul:** [Faz 8](08-SAGLAYICI-GENISLEMESI.md) — sağlık denetimi ve devre kesici hazırdı
> **Paketler:** **`AgentPrism.Anthropic` (YENİ)**, **`AgentPrism.Google` (YENİ)** ·
> genişleyen: `AgentPrism.Abstractions`, `.Core`
> **Migration:** Yok

---

## Amaç ve Sonuç

Claude ve Gemini'ye **birinci sınıf** destek: sağlayıcıya özgü yetenekler
(prompt caching, `thinking` blokları, güvenlik eşikleri) ancak doğrudan SDK ile
kullanılabilir.

**Sonuç:** İki yeni paket üretiliyor. Beş sağlayıcı (`openai`, `openai-responses`,
`openrouter`, `anthropic`, `google`) aynı uygulamada çalışıyor; ikisiyle de gerçek
tool çağrısı, akışlı çalıştırma ve token sayımı doğrulandı. Güvenlik filtresiyle
boş dönen yanıt artık **açık bir hata** olarak kaydediliyor. Çıktılar aşağıda.

---

## 26.1 — Ölçüm: SDK'lar (ilk iş, sonucu planı değiştirdi)

Fazın tüm maliyeti "paket hazır bir `IChatClient` veriyor mu?" sorusuna bağlıydı.
**İki resmî SDK bulundu** — plan topluluk paketi varsayıyordu.

| Paket | Sürüm | Sahip | Lisans | `AsIChatClient` | Geçişli bağımlılık |
|---|---|---|---|---|---|
| `Anthropic` | 12.39.0 | **Anthropic** (resmî) | MIT | ✅ | 4 — hepsi Microsoft/System |
| `Google.GenAI` | 1.16.0 | **Google LLC** (resmî) | Apache-2.0 | ✅ | 11 — `Newtonsoft.Json`, `System.Management`, `Google.Apis.*` dahil |
| `Anthropic.SDK` | 5.10.0 | topluluk (tghamm) | — | kullanılmadı | — |
| `Google_GenerativeAI` | 3.6.7 | topluluk (gunpal5) | — | kullanılmadı | — |
| `GeminiDotnet.Extensions.AI` | 0.25.0 | topluluk, tek bakımcı | — | değerlendirildi, seçilmedi | 2 |

`Microsoft.Extensions.AI.Anthropic` / `.Google` paketleri **yoktur** (doğrulandı,
2026-08-05); resmî MEAI sağlayıcısı yalnız OpenAI içindir.

İkisi de `IsAotCompatible=true` altında **sıfır uyarı** derledi.

**Sonuç: iş küçük yol oldu** — `AgentPrism.OpenAI` ile birebir aynı şekil. Mesaj
eşlemesi, akış, tool çağrısı ve kullanım sayaçları AgentPrism'de yazılmadı.

---

## Gerçekleşen Public API

Plandaki taslak imzalar değil, **koddaki gerçek imzalar**.

### `AgentPrism.Abstractions` — yeni

```csharp
public sealed record ModelBinding
{
    // ...mevcut uyeler...

    // YENI. Anahtar "{saglayici}.{ayar}" bicimindedir; karsilastirma harfe duyarsiz.
    public IReadOnlyDictionary<string, JsonElement> ProviderSettings { get; init; }
        = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
}

// YENI. Iki saglayici paketi de bunu kullanir; Faz 27 (Azure) devralir.
public static class ModelProviderSettings
{
    public static void    Validate(ModelBinding binding, string providerPrefix, IReadOnlyCollection<string> supportedKeys);
    public static bool?   ReadBoolean(ModelBinding binding, string key);
    public static int?    ReadInt32(ModelBinding binding, string key);
    public static string? ReadString(ModelBinding binding, string key);
}

public class AgentPrismException : Exception
{
    // YENI, virtual. Varsayilan GetType().FullName -> bugunku davranis degismez.
    public virtual string ErrorType { get; }
}

// YENI istisna.
public sealed class AgentPrismContentFilteredException : AgentPrismException
{
    public const string ContentFilteredErrorType = "content_filtered";

    public string? ProviderName  { get; init; }
    public string? FinishReason  { get; init; }   // SIR TASIMAZ
    public override string ErrorType => ContentFilteredErrorType;
}
```

### `AgentPrism.Core` — yeni

```csharp
// internal. ModelProviderRegistry.CreateChatClient her istemciyi buna sarar,
// devre kesicinin DISINDA (gerekce: sapma S3).
internal sealed class ContentFilterDetectingChatClient : DelegatingChatClient;
```

### `AgentPrism.Anthropic`

```csharp
public static class AnthropicProviderNames
{
    public const string Anthropic      = "anthropic";   // KARARLI: veritabaninda saklanir
    public const string SettingsPrefix = "anthropic";

    public const string PromptCachingSetting        = "anthropic.promptCaching";
    public const string ThinkingBudgetTokensSetting = "anthropic.thinking.budgetTokens";

    public static IReadOnlyList<string> SupportedSettings { get; }
}

public sealed class AnthropicProviderOptions
{
    public const string SectionName = "AgentPrism:Providers:Anthropic";

    public string?   ApiKey                 { get; set; }   // SIR
    public string?   DefaultModel           { get; set; }
    public Uri?      Endpoint               { get; set; }
    public int       DefaultMaxOutputTokens { get; set; } = 4096;   // max_tokens ZORUNLU
    public TimeSpan? Timeout                { get; set; }
    public int?      MaxRetries             { get; set; }
    public IList<ModelDescriptor> Models    { get; }
}

public sealed class AnthropicProviderOptionsValidator : IValidateOptions<AnthropicProviderOptions>;

public sealed class AnthropicChatClientFactory
{
    public AnthropicChatClientFactory(AnthropicProviderOptions options, ILoggerFactory? loggerFactory = null);
    public AnthropicChatClientFactory(IAnthropicClient client, string? defaultModel = null,
                                      int defaultMaxOutputTokens = 4096, ILoggerFactory? loggerFactory = null);

    public IChatClient CreateChatClient(ModelBinding binding);
    public static AnthropicClient CreateClient(AnthropicProviderOptions options);
}

public sealed class AnthropicModelProvider : IModelProvider, IModelProviderHealthCheck
{
    public AnthropicModelProvider(string name, AnthropicChatClientFactory chatClientFactory,
                                  IReadOnlyList<ModelDescriptor> models,
                                  ILogger<AnthropicModelProvider>? logger = null,
                                  AnthropicProviderOptions? healthCheckOptions = null);
}

public static class AnthropicModelCatalog { public static IReadOnlyList<ModelDescriptor> Build(AnthropicProviderOptions options); }

public static class AnthropicProviderExtensions
{
    public static IAgentPrismBuilder UseAnthropic(this IAgentPrismBuilder b, string apiKey, Action<AnthropicProviderOptions>? configure = null);
    public static IAgentPrismBuilder UseAnthropic(this IAgentPrismBuilder b, IConfiguration configurationSection);
    public static IAgentPrismBuilder UseAnthropic(this IAgentPrismBuilder b, Action<AnthropicProviderOptions> configure);
}

// internal
internal sealed class AnthropicProviderSettingsChatClient : DelegatingChatClient;
internal sealed class AnthropicProviderHealthCheck : IModelProviderHealthCheck
{
    internal const string AnthropicVersion = "2023-06-01";
    internal static Uri BuildModelsEndpoint(Uri? baseEndpoint);
    internal static ValueTask<IReadOnlyList<string>> ReadModelIdsAsync(HttpResponseMessage response, CancellationToken ct);
}
```

### `AgentPrism.Google`

Aynı şekil; farklar:

```csharp
public static class GoogleProviderNames
{
    public const string Google         = "google";   // "gemini" DEGIL — bkz. sapma S2
    public const string SettingsPrefix = "google";

    public const string SafetyHarassmentSetting        = "google.safety.harassment";
    public const string SafetyHateSpeechSetting        = "google.safety.hateSpeech";
    public const string SafetySexuallyExplicitSetting  = "google.safety.sexuallyExplicit";
    public const string SafetyDangerousContentSetting  = "google.safety.dangerousContent";
    public const string SafetyCivicIntegritySetting    = "google.safety.civicIntegrity";
    public const string ThinkingBudgetTokensSetting    = "google.thinking.budgetTokens";
    public const string ThinkingIncludeThoughtsSetting = "google.thinking.includeThoughts";

    public static IReadOnlyList<string> SupportedSettings { get; }
}

public sealed class GoogleProviderOptions
{
    public const string SectionName = "AgentPrism:Providers:Google";

    public string?   ApiKey       { get; set; }   // SIR
    public string?   DefaultModel { get; set; }
    public Uri?      Endpoint     { get; set; }
    public string?   ApiVersion   { get; set; }   // ornek: v1beta
    public TimeSpan? Timeout      { get; set; }
    public IList<ModelDescriptor> Models { get; }
}

// DIKKAT: fabrika IDisposable — Google.GenAI.Client bir HttpClient tasir.
public sealed class GoogleChatClientFactory : IDisposable
{
    public GoogleChatClientFactory(GoogleProviderOptions options, ILoggerFactory? loggerFactory = null);
    public GoogleChatClientFactory(Client client, string? defaultModel = null, ILoggerFactory? loggerFactory = null);

    public IChatClient CreateChatClient(ModelBinding binding);
    public static Client CreateClient(GoogleProviderOptions options);
}

public sealed class GoogleModelProvider : IModelProvider, IModelProviderHealthCheck;
public static class GoogleModelCatalog { public static IReadOnlyList<ModelDescriptor> Build(GoogleProviderOptions options); }

public static class GoogleProviderExtensions
{
    public static IAgentPrismBuilder UseGoogle(this IAgentPrismBuilder b, string apiKey, Action<GoogleProviderOptions>? configure = null);
    public static IAgentPrismBuilder UseGoogle(this IAgentPrismBuilder b, IConfiguration configurationSection);
    public static IAgentPrismBuilder UseGoogle(this IAgentPrismBuilder b, Action<GoogleProviderOptions> configure);
}

// internal
internal sealed class GoogleProviderSettingsChatClient : DelegatingChatClient;
internal static class GoogleSafetySettings { public static IReadOnlyList<SafetySetting> Read(ModelBinding binding); }
internal sealed class GoogleProviderHealthCheck : IModelProviderHealthCheck
{
    internal const string DefaultApiVersion = "v1beta";
    internal static Uri BuildModelsEndpoint(Uri? baseEndpoint, string? apiVersion);
    internal static ValueTask<IReadOnlyList<string>> ReadModelIdsAsync(HttpResponseMessage response, CancellationToken ct);
}
```

---

## Plandan Sapmalar

Yedi sapma var. Hepsi ölçüme veya kullanıcı kararına dayanıyor.

### S1 — Resmî SDK'lar kullanıldı, topluluk paketleri değil *(kullanıcı kararı)*

**Plan:** `Anthropic.SDK` 5.10.0 ve `Google_GenerativeAI` 3.6.7 (topluluk).
**Ölçüm:** İkisinin de **resmî** birinci taraf karşılığı var ve ikisi de kendi
`AsIChatClient` adaptörünü taşıyor. Karar K-204.

Google'ın resmî SDK'sı `Google.Apis.Auth` üzerinden `Newtonsoft.Json 13.0.3`,
`System.Management 7.0.2` ve `System.CodeDom 7.0.0` çeker — 11 geçişli bağımlılık.
Ağırlık **bilerek** kabul edildi ve tek pakette izole edildi; Gemini kullanmayan
tüketici hiçbirini almaz. Alternatif (tek bakımcılı, 1.0 öncesi bir topluluk
paketi) daha büyük bir uzun vadeli risk sayıldı. Karar K-205.

### S2 — Paket adı `AgentPrism.Google`, sağlayıcı adı `google` *(kullanıcı kararı)*

**Plan:** `.UseGemini(...)` örneği. **Yapılan:** `UseGoogle(...)` ve sağlayıcı adı
`google`. Vertex AI aynı pakete eklenebilir; `gemini` adı paketi tek bir model
ailesine kilitlerdi. Yapılandırma bölümü `AgentPrism:Providers:Google`. Karar K-207.

### S3 — İçerik filtresi tespiti `Core`'da ortak dekoratördür *(kullanıcı kararı)*

**Plan:** "AgentPrism bunu `RunRecord.ErrorType = "content_filtered"` olarak
kaydetmelidir" — nerede yapılacağı açık değildi.
**Yapılan:** `AgentPrism.Core` içinde `ContentFilterDetectingChatClient`; devre
kesiciyle **aynı desen** (Faz 8). Üç sağlayıcı da korumayı tek yerden alır ve
Faz 27 kural yazmadan devralır.

🚨 **Sarmalama sırası kritiktir: dekoratör devre kesicinin DIŞINDADIR.** Filtrelenmiş
bir yanıt sağlayıcının *sağlıklı* olduğunu gösterir; içeride olsaydı attığı istisna
ardışık hata sayacını artırır ve eşik sayısınca filtrelenen istek sağlayıcıyı
kapatırdı. Test: `ContentFilterDetectingChatClientTests.Devre_kesici_filtreyi_hata_saymaz`.

**Yalnız boş yanıt hataya çevrilir.** Model metin (veya tool çağrısı) üretip sonra
kesildiyse kullanıcının elinde kısmi bir cevap vardır; onu silmek bilgi kaybıdır.

`RunRecord` şeması **değişmedi**: `RunError.Type` alanı zaten vardı. `ErrorType`
`AgentPrismException` üzerinde `virtual` bir üye oldu ve varsayılanı tipin tam
adıdır — mevcut kayıtların biçimi değişmedi. Karar K-206.

### S4 — Sağlayıcıya özgü ayarlar `RawRepresentationFactory` ile taşınır

**Ölçüldü (2026-08-05):** Her iki SDK adaptörü de
`ChatOptions.RawRepresentationFactory` okur (metadata üye referansıyla doğrulandı,
sonra gerçek çağrıyla kanıtlandı). Bu, `Microsoft.Extensions.AI`'ın sağlayıcıya
özgü alanlar için resmî kaçış kapısıdır; AgentPrism ham HTTP yazmaz.

🚨 **Anthropic adaptörü bizim yazdığımız alanları KORUR ve üzerine yazmaz.**
Ölçüldü: yer tutucu `Model = "PLACEHOLDER-MODEL"` ile gönderilen bir istek gerçekten
o adla gitti ve `404 not_found_error: model: PLACEHOLDER-MODEL` döndü. Bu yüzden
`model` ve `max_tokens` değerlerini dekoratör **kendisi** yazmak zorundadır.
`messages` anahtarının **var olması** da gerekir (SDK'nın istemci tarafı doğrulaması
`'messages' cannot be absent` der); adaptör gerçek mesajları onun üzerine yazar.

Ham gövde parçaları küçük `record`'lardan kaynak üreteci ile serileştirilir
(`AnthropicRawJsonContext`); yansımaya dayanan aşırı yükleme AOT uyumunu bozardı.

**Cağıranın `ChatOptions` örneği değiştirilmez** — `Clone()` kullanılır. Derlenmiş
bir agent tek bir `ChatOptions` örneğini tüm çağrılarda paylaşır; üzerine yazmak eş
zamanlı çalıştırmaları birbirine karıştırırdı.

Ayar yoksa dekoratör **hiç eklenmez**; sade yol her istekte kopya üretmez.

### S5 — Bilinmeyen anahtar iki ayrı hata sınıfı üretir

Doküman "bilinmeyen anahtar derleme hatası verir" diyordu. Uygulama iki durumu
**ayırır**, çünkü düzeltmeleri farklıdır:

| Durum | Mesaj |
|---|---|
| Yanlış önek (`anthropic.*` ama sağlayıcı `google`) | "…'google' sağlayıcısına ait değil… sağlayıcı değiştirildiğinde eski ayarlar temizlenmelidir" |
| Tanınmayan anahtar (`anthropic.yokBoyleAyar`) | "…tanınmıyor. Desteklenen anahtarlar: …" |

Değer tipi de doğrulanır (`mantıksal` / `tam sayı` / `metin` beklenir) ve Gemini'nin
güvenlik eşiği tanınmazsa geçerli değerler listelenir.

### S6 — `AnthropicProviderOptions.DefaultMaxOutputTokens` eklendi (planda yoktu)

🚨 Anthropic Messages API'sinde `max_tokens` **zorunludur**; OpenAI'da olduğu gibi
atlanamaz. `ModelBinding.MaxOutputTokens` boş bırakılırsa bu değer kullanılır
(varsayılan **4096**). Doğrulayıcı sıfır ve negatifi reddeder.

### S7 — `GoogleChatClientFactory` `IDisposable`

`Google.GenAI.Client` bir `HttpClient` taşır ve `IDisposable`/`IAsyncDisposable`
uygular. Fabrika `IDisposable` uygular; kapsayıcı kapanırken istemci de kapanır.
`OpenAIChatClientFactory`'de bu gerekmiyordu (`OpenAIClient` disposable değildir).
Hazır istemciyle kurulan fabrika istemciyi **kapatmaz** — ömrü çağırana aittir.

Taban adres `HttpOptions` üzerinden verilir. SDK'nın `Client.setDefaultBaseUrl`
statik metodu **bilerek kullanılmaz**: süreç genelinde durum değiştirir ve aynı
uygulamada iki farklı Gemini ucu kullanılamaz hale gelirdi.

---

## Doğrulanmış SDK İmzaları

Reflection ile çıkarıldı (`Anthropic` 12.39.0, `Google.GenAI` 1.16.0).

```csharp
// Anthropic 12.39.0 — 1718 public tip; IChatClient adaptoru INTERNAL bir tiptir,
// yalniz uzanti metodu uzerinden erisilir.
static IChatClient AsIChatClient(this IAnthropicClient client,
                                 string? defaultModelId = null,
                                 int? defaultMaxOutputTokens = null);   // Microsoft.Extensions.AI ad alaninda

sealed class AnthropicClient : IAnthropicClient
{
    AnthropicClient(Anthropic.Core.ClientOptions options);
    IMessageService Messages { get; }
    IModelService   Models   { get; }   // Task<ModelListPage> List(...)
}

struct Anthropic.Core.ClientOptions   // struct, class DEGIL
{
    string    ApiKey     { get; set; }
    string    BaseUrl    { get; set; }   // null KABUL ETMEZ
    TimeSpan? Timeout    { get; set; }
    int?      MaxRetries { get; set; }
}

// 🚨 MessageCreateParams'in Thinking/CacheControl uyeleri INIT-ONLY'dir
// (reflection "set" gosterir, derleyici CS8852 verir). Kosullu alan yazmak icin
// FromRawUnchecked kullanilir.
sealed class MessageCreateParams
{
    required long MaxTokens; required IReadOnlyList<MessageParam> Messages; required ApiEnum<string,Model> Model;
    CacheControlEphemeral CacheControl { get; init; }
    ThinkingConfigParam   Thinking     { get; init; }

    static MessageCreateParams FromRawUnchecked(
        IReadOnlyDictionary<string,JsonElement> rawHeaderData,
        IReadOnlyDictionary<string,JsonElement> rawQueryData,
        IReadOnlyDictionary<string,JsonElement> rawBodyData);
}

// Google.GenAI 1.16.0
static IChatClient AsIChatClient(this Client client, string? defaultModelId = null);

sealed class Client : IDisposable, IAsyncDisposable
{
    Client(bool? enterprise = null, bool? vertexAI = null, string? apiKey = null,
           ICredential? credential = null, string? project = null, string? location = null,
           HttpOptions? httpOptions = null, ClientOptions? clientOptions = null);
    Models Models { get; }   // Task<Pager<Model,...>> ListAsync(...)
}

class HttpOptions { string? BaseUrl; string? ApiVersion; int? Timeout; Dictionary<string,string>? Headers; }

// HarmCategory / HarmBlockThreshold / FinishReason ENUM DEGIL — string tasiyan
// struct'lardir; AllValues ile listelenir, Value ile karsilastirilir.
HarmBlockThreshold.AllValues => HARM_BLOCK_THRESHOLD_UNSPECIFIED, BLOCK_LOW_AND_ABOVE,
                                BLOCK_MEDIUM_AND_ABOVE, BLOCK_ONLY_HIGH, BLOCK_NONE, OFF

// Ikisi de guvenlik/refuse durumunu ChatFinishReason.ContentFilter'a esler.
```

---

## Bilinen Davranış Farkları (ölçüldü)

| Konu | Anthropic | Gemini |
|------|-----------|--------|
| `max_tokens` | **zorunlu** — atlanamaz | isteğe bağlı |
| Sistem mesajı | ayrı `system` alanı | `systemInstruction` alanı |
| Tool çağrı biçimi | `tool_use` / `tool_result` | `functionCall` / `functionResponse` |
| Akışta kullanım | artımlı | sonda toplu |
| Boş yanıt | ender | güvenlik filtresinde **sık** |
| Düşünme + sıcaklık | 🚨 düşünme açıkken `temperature` yalnız **1** olabilir | kısıt yok |
| Düşünme bütçesi | `MaxOutputTokens`'tan **küçük** olmalı | `[-1, 65535]`; `-1` modele bırakır, `0` kapatır |
| Model adı ömrü | uzun | **kısa** — `gemini-2.5-flash` artık yeni kullanıcıya kapalı |

---

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/
├── Agents/ModelBinding.cs                     ProviderSettings eklendi
├── Agents/ModelProviderSettings.cs            YENI — okuma + dogrulama yardimcilari
└── AgentPrismException.cs                     ErrorType (virtual) + AgentPrismContentFilteredException

src/AgentPrism.Core/
├── Models/ContentFilterDetectingChatClient.cs YENI — internal
├── Models/ModelProviderRegistry.cs            dekorator EN DISA baglandi
└── Recording/RunRecordingAgent.cs             ToRunError artik ErrorType okur

src/AgentPrism.Anthropic/                      YENI PAKET
├── AgentPrism.Anthropic.csproj
├── AnthropicProviderNames.cs
├── AnthropicProviderOptions.cs
├── AnthropicProviderOptionsValidator.cs
├── AnthropicModelCatalog.cs
├── AnthropicChatClientFactory.cs
├── AnthropicProviderSettingsChatClient.cs     internal — RawRepresentationFactory
├── AnthropicModelProvider.cs
├── AnthropicProviderHealthCheck.cs            internal — GET {endpoint}/models
├── AnthropicProviderExtensions.cs
├── README.md
└── PublicAPI.{Shipped,Unshipped}.txt

src/AgentPrism.Google/                         YENI PAKET
├── AgentPrism.Google.csproj
├── GoogleProviderNames.cs
├── GoogleProviderOptions.cs
├── GoogleProviderOptionsValidator.cs
├── GoogleModelCatalog.cs
├── GoogleChatClientFactory.cs                 IDisposable
├── GoogleProviderSettingsChatClient.cs        internal
├── GoogleSafetySettings.cs                    internal — esik cozumleme
├── GoogleModelProvider.cs
├── GoogleProviderHealthCheck.cs               internal — GET {endpoint}/{apiVersion}/models
├── GoogleProviderExtensions.cs
├── README.md
└── PublicAPI.{Shipped,Unshipped}.txt

tests/AgentPrism.Anthropic.UnitTests/          YENI PROJE (39 test)
tests/AgentPrism.Google.UnitTests/             YENI PROJE (43 test)

tests/AgentPrism.Core.UnitTests/
├── Models/ContentFilterDetectingChatClientTests.cs   YENI (7 test)
├── Models/ModelProviderSettingsTests.cs              YENI (16 test)
├── Recording/RunRecordingAgentTests.cs               +2 test
└── Architecture/DependencyDirectionTests.cs          iki yeni paket eklendi

tests/AgentPrism.AspNetCore.FunctionalTests/
├── MultiProviderTests.cs                      YENI (6 test)
└── *.csproj                                   Anthropic + Google referansi

tests/Shared/Contracts/AgentDefinitionStoreContract.cs   ProviderSettings jsonb iddiasi

samples/AgentPrism.Api/
├── Program.cs                                 UseAnthropic + UseGoogle + 4 yeni agent
├── appsettings.json                           Anthropic ve Google semasi
└── AgentPrism.Api.csproj                      iki yeni ProjectReference

AgentPrism.slnx, Directory.Packages.props      iki paket + iki test projesi
```

---

## Testler

**1651 test geçiyor** (+113). Karşılaştırma tabanı: Faz 25 sonunda `MIMARI.md`
1509 yazıyordu ve arayüz E2E testlerini (29) saymıyordu; aynı kapsamda önceki
toplam 1538'dir. SQL Server'ın 213 testi bu makinede koşmuyor ve bu sayıya
**dâhil değildir** — bkz. DoD notu.

| Proje | Sayı | Faz 26'da eklenen |
|-------|------|-------------------|
| `AgentPrism.Anthropic.UnitTests` | 39 | tamamı yeni |
| `AgentPrism.Google.UnitTests` | 43 | tamamı yeni |
| `AgentPrism.Core.UnitTests` | 465 | +25 |
| `AgentPrism.AspNetCore.FunctionalTests` | 260 | +6 |
| `AgentPrism.PostgreSql.IntegrationTests` | 440 | sözleşme testi genişledi |
| `AgentPrism.Sqlite.IntegrationTests` | 214 | sözleşme testi genişledi |
| `AgentPrism.Ui.E2ETests` | 29 | değişmedi |

| Test sınıfı | Neyi doğrular |
|-------------|---------------|
| `AnthropicProviderExtensionsTests` | Tek sağlayıcı kaydı, ikinci çağrı çoğaltmaz, tek fabrika paylaşımı, yapılandırmadan okuma, anahtarsız kayıt başlangıçta hata verir |
| `AnthropicChatClientFactoryTests` | Boru hattı üyeleri, model çözümü, `Endpoint` eşlemesi, desteklenen/tanınmayan/yabancı ayar, geçersiz bütçe, sıfır `DefaultMaxOutputTokens` |
| `AnthropicModelCatalogTests` | Yerleşik liste **yoktur**, sıralama, ad çakışması, adsız girdi |
| `AnthropicProviderHealthCheckTests` | Adres birleştirme, `{"data":[…]}` ayrıştırma, bağlanamayan uçta **ne anahtar ne adres** sızmaz |
| `SecretLeakTests` (×2) | Anahtar 8 farklı çıktıda görünmüyor; ayar sınıfı kendi `ToString`'ini tanımlamıyor (K-035) |
| `GoogleChatClientFactoryTests` | Aynısı + tanınmayan güvenlik eşiği geçerli değerleri listeler, bütçe sınır değerleri (`-1`, `0`, `65535`) |
| `GoogleProviderHealthCheckTests` | `{"models":[{"name":"models/…"}]}` ayrıştırma, `models/` önekinin temizlenmesi, OpenAI biçimi sessizce boş döner |
| `ContentFilterDetectingChatClientTests` | Boş filtreli yanıt hata atar; metin/tool içeren filtreli yanıt geçer; akışta karar sonda verilir; **devre kesici filtreyi hata saymaz** |
| `ModelProviderSettingsTests` | Doğrulama iki hata sınıfı, harf duyarsızlığı, tip uyuşmazlığı, atanmamış `JsonElement` yok sayılır, sıralı karşılaştırıcılı sözlükte de okur |
| `RunRecordingAgentTests` (+2) | Filtrelenmiş yanıt `RunError.Type = "content_filtered"` olarak kaydedilir (akışlı ve akışsız) |
| `MultiProviderTests` | Dört sağlayıcı aynı anda, katalog ayrımı, sağlık ucu, anahtar sızmaz, `ProviderSettings` HTTP→depo→HTTP gidip gelir, tanınmayan ayar 400 döner |
| `AgentDefinitionStoreContract` | `ProviderSettings` `jsonb` içinde bozulmadan gidip gelir (bellek içi, PostgreSQL, SQLite) |

**Gerçek model çağrısı yapan test yoktur** (Faz 3'ten beri geçerli karar). Ağ
gerektiren doğrulama elle yapıldı — aşağıda.

---

## Bitiş Ölçütleri (DoD)

Elle doğrulama: gerçek Anthropic + Google anahtarı, `samples/AgentPrism.Api`,
`ASPNETCORE_ENVIRONMENT=Development`, 2026-08-05.

| Ölçüt | Durum | Kanıt |
|-------|-------|-------|
| İki paket üretiliyor; `dotnet pack` sayısı doğru | ✅ | **13 paket** (Faz 25'te 11 idi); her yeni pakette **2 doğrudan bağımlılık**, geçişli sızıntı yok |
| Her iki sağlayıcıyla gerçek çalıştırma ve **tool çağrısı** | ✅ | aşağıdaki çıktı, 1–2 |
| Akışlı çalıştırmada token kullanımı doğru toplanıyor | ✅ | tüm çalıştırmalar `isStreaming=true`; Claude 862/49/911, Gemini 369/24/434 |
| Sağlık denetimi ve devre kesici üç sağlayıcıda da çalışıyor | ✅ | aşağıdaki çıktı, 3 ve 6 |
| Güvenlik filtresi yanıtı hata olarak kaydediliyor | ✅ | aşağıdaki çıktı, 5 — `RunError.Type = "content_filtered"` |
| `ProviderSettings` ile en az bir sağlayıcıya özgü ayar uçtan uca çalışıyor | ✅ | aşağıdaki çıktı, 4 — geçersiz bütçe **API tarafından** reddedildi |
| Paket kontrol listesi tamam; sır taraması boş | ✅ | README + slnx + `DependencyDirectionTests`; `grep` taraması boş; API çıktılarında ve günlükte anahtar **0 kez** |
| Dört doğrulama kapısı sıfır uyarı | ✅ | build / test / pack / format → 0 uyarı, 0 hata |

> ⚠️ **SQL Server sözleşme testleri bu makinede koşmadı.** `mssql/server` konteyneri
> Apple Silicon üzerinde başlamıyor (`Testcontainers` zaman aşımı) — Faz 23'ten
> beri bilinen ortam sınırı, bu fazın değişikliğiyle ilgisi yoktur. Paylaşılan SQL
> katmanı **SQLite (214)** ve **PostgreSQL (440)** ile doğrulandı; ikisi de yeni
> `ProviderSettings` iddiasını içerir.

### Gerçek çıktı — beş sağlayıcı, gerçek yanıt (2026-08-05)

```
$ curl -s localhost:5080/health
{"status":"healthy", ... ,"openRouter":true,"anthropic":true,"google":true}

# 3) Saglik denetimi — ucret uretmez, GET {endpoint}/models
$ curl -s "localhost:5080/agentprism/api/models/health?refresh=true"
  anthropic            Healthy    gecikme=00:00:00.593  model=10  ornek=['claude-fable-5', 'claude-haiku-4-5-20251001']
  google               Healthy    gecikme=00:00:00.336  model=50  ornek=['antigravity-preview-05-2026', 'aqa']
  openai               Healthy    gecikme=00:00:00.678  model=3
  openai-responses     Healthy    gecikme=00:00:00.699  model=3
  openrouter           Healthy    gecikme=00:00:00.148  model=200

# 1) ANTHROPIC — gercek calistirma + tool cagrisi
  yanit  : 'ORD-7 siparişiniz kargoya verildi. Tahmini teslim süreniz 2 gün...'
  durum  : Completed  akisli=True  model=claude-haiku-4-5-20251001
  token  : giris=862 cikis=49 toplam=911
    seq=0 RunStarted
    seq=1 ToolInvoking  tool=get_order_status  yuk=orderId=ORD-7
    seq=2 ToolInvoked   tool=get_order_status  yuk=ORD-7 numarali siparis kargoya verildi...
    seq=3..5 MessageDelta
    seq=6 RunCompleted

# 2) GEMINI — gercek calistirma + tool cagrisi
  yanit  : 'ORD-9 numaralı siparişiniz kargoya verildi. Tahmini teslimat süresi 2 gündür.'
  durum  : Completed  akisli=True  model=gemini-3.6-flash
  token  : giris=369 cikis=24 toplam=434
    seq=0 RunStarted
    seq=1 ToolInvoking  tool=get_order_status  yuk=orderId=ORD-9
    seq=2 ToolInvoked   tool=get_order_status
    seq=3..4 MessageDelta
    seq=5 RunCompleted

# 4) ProviderSettings uctan uca — ayarin GERCEKTEN API'ye ulastiginin kaniti.
#    Arayuzden kaydedilen agent: anthropic.thinking.budgetTokens = 99999,
#    MaxOutputTokens = 512. Zincir: HTTP -> jsonb -> derleyici -> saglayici ->
#    dekorator -> RawRepresentationFactory -> SDK -> Anthropic API.
  durum : Failed
  hata  : type=Anthropic.Exceptions.AnthropicBadRequestException
          {"type":"invalid_request_error","message":"`max_tokens` must be greater ..."}

#    Gecerli butce ile (2048) ayni yol calisiyor:
  yanit : "17 ile 23'ün çarpımını adım adım hesaplayalım... = 391"
  token : giris=78 cikis=308 toplam=386     # cikis dusunme token'larini icerir

#    Prompt caching acik agent (anthropic.promptCaching = true):
  yanit : 'Selam! 👋'   token: giris=4634 cikis=10 toplam=4644
#    Ham SDK olcumu ayni ayarla: cache_creation_input_tokens=4209 (kapaliyken bu sayac hic gelmez)

#    Taninmayan anahtar -> HTTP 400, gecerli anahtarlar listelenir:
  "'claude-bilinmeyen-ayar' agent'i derlenemedi: ModelBinding.ProviderSettings icinde
   su anahtarlar taninmiyor: anthropic.yokBoyleAyar. Desteklenen anahtarlar:
   anthropic.promptCaching, anthropic.thinking.budgetTokens."

#    Yanlis onek -> ayri bir mesaj:
  "…su anahtarlar 'google' saglayicisina ait degil: anthropic.promptCaching.
   …saglayici degistirildiginde eski ayarlar temizlenmelidir. Desteklenen anahtarlar: google.safety…"

# 5) GUVENLIK FILTRESI — tum esikler BLOCK_LOW_AND_ABOVE
  yanit  : ''   (bos)
  durum  : Failed  akisli=True  model=gemini-3.6-flash
  hata   : type=content_filtered
           "'google' saglayicisi yaniti icerik filtresiyle kesti ve hicbir icerik dondurmedi..."
    seq=0 RunStarted
    seq=1 RunFailed

# 6) DEVRE KESICI — olmayan model, 6 ardisik istek
  deneme 1..6
  GET /api/models/health/anthropic
  {"providerName":"anthropic","status":"Unhealthy",
   "detail":"Devre kesici acik. 30 sn sonra yeniden denenecek."}
#    BreakDuration sonunda kendiliginden kapandi ve sonraki cagri basarili oldu.

# Sir taramasi
  API ciktilarinda Anthropic anahtari: 0
  API ciktilarinda Google anahtari   : 0
  Uygulama gunlugunde anahtar        : 0
```

---

## Kullanım

```csharp
builder.AddAgentPrism()
       .UseAnthropic(configuration.GetSection(AnthropicProviderOptions.SectionName))
       .UseGoogle(configuration.GetSection(GoogleProviderOptions.SectionName))
       .AddAgent(new AgentDefinition
       {
           Name = "claude-destek",
           Instructions = "Sen bir destek asistanisin.",
           Model = new ModelBinding
           {
               Provider = AnthropicProviderNames.Anthropic,
               Model = "claude-sonnet-5",
               MaxOutputTokens = 1024,          // Anthropic'te max_tokens ZORUNLU
               ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
               {
                   [AnthropicProviderNames.ThinkingBudgetTokensSetting] = JsonSerializer.SerializeToElement(2048),
               },
           },
           ToolNames = ["get_order_status"],
       });
```

---

## Bu Fazda Verilen Kararlar

Karar defterine yazıldı (`docs/KARARLAR.md`, **K-204 – K-209**):

1. **K-204** — İki resmî SDK kullanıldı, topluluk paketleri değil.
2. **K-205** — `Google.GenAI`'ın geçişli ağırlığı bilerek kabul edildi ve izole edildi.
3. **K-206** — İçerik filtresi tespiti `Core`'da ortak dekoratördür; devre kesicinin dışındadır.
4. **K-207** — Paket `AgentPrism.Google`, sağlayıcı adı `google`.
5. **K-208** — `ModelBinding.ProviderSettings` sözleşmeye eklendi; bilinmeyen anahtar derleme hatasıdır.
6. **K-209** — Meta paket iki yeni sağlayıcıyı **içermez** (K-185 deseni).

---

## Riskler — kapanış durumu

| Risk | Sonuç |
|------|-------|
| Topluluk SDK'sı bakımsız kalır | **Ortadan kalktı.** İkisi de resmî birinci taraf paketi (K-204) |
| `IChatClient` adaptörü yoksa iş büyür | **Gerçekleşmedi.** İkisi de adaptör sunuyor; iş küçük yol oldu |
| Tool eşlemesi sessizce bozulur | Gerçek model testiyle kapatıldı — iki sağlayıcıda da tool çağrısı doğrulandı |
| SDK AOT uyumsuz | **Gerçekleşmedi.** İkisi de `IsAotCompatible=true` altında sıfır uyarı |
| `ProviderSettings` çöp kutusuna döner | Bilinmeyen anahtar **hata** verir; her sağlayıcı desteklediği anahtarları README'de listeler |
| Google'ın geçişli ağırlığı | **Gerçekleşti.** 11 geçişli bağımlılık; tek pakette izole (K-205) |

---

## Sonraki Faza Devir Notu

- **Faz 27 (Azure) `ModelProviderSettings` yardımcısını hazır bulur.** Yeni bir
  sağlayıcı yalnız üç şey yazar: önek sabiti, desteklenen anahtar listesi ve bir
  `DelegatingChatClient`. Doğrulama, tip denetimi ve hata mesajları paylaşılır.
- **İçerik filtresi ve devre kesici `ModelProviderRegistry` düzeyindedir.** Yeni
  sağlayıcı paketi ikisini de kod yazmadan alır — tek şart `IModelProvider`
  singleton olarak DI'da kayıtlı olmak.
- 🚨 **Bir SDK'nın ham gösterimini kullanırken alanın üzerine yazılıp yazılmadığını
  ÖLÇ.** Anthropic adaptörü bizim `model` alanımızı korudu ve istek yer tutucu
  adla gitti. Varsayım yerine bir yer tutucu değerle gerçek çağrı yapın.
- **Anthropic'in prompt caching sayaçları `RunUsage`'a yansımıyor.** SDK
  `UsageDetails.AdditionalCounts` içinde `CacheCreationInputTokens` /
  `CacheReadInputTokens` veriyor; AgentPrism'in `RunUsage` kaydı yalnız
  giriş/çıkış/toplam taşıyor. Maliyet raporu önbellekli girdiyi normal girdi
  fiyatından sayıyor — Faz 20'nin fiyat çözümlemesi için açık bir kalem.
- **`DependencyDirectionTests.AllowedReferences` `AgentPrism.SqlServer`,
  `AgentPrism.Sqlite` ve `AgentPrism.Sql.Shared` paketlerini içermiyor** (Faz 23/24'ten
  kalan boşluk). Bu fazda iki yeni sağlayıcı eklendi; SQL paketleri hâlâ açık.
- **Gemini'nin model adları hızlı eskir.** `gemini-2.5-flash` ölçüm sırasında
  *"no longer available to new users"* döndü. K-032 burada somut bir bedel olarak
  yaşandı; katalog yapılandırmadan gelir ve bir doğrulama listesi değildir.
