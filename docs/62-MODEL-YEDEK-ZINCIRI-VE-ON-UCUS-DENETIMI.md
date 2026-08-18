# Faz 62 — Model Yedek Zinciri ve Ön Uçuş Denetimi

> **Durum:** 📋 Planlandı (2026-08-18)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-44**, **F-59**
> **Önkoşul:** [Faz 8](08-SAGLAYICI-GENISLEMESI.md) — devre kesici ve sağlayıcı sağlığı bu fazın yarısını kurdu · [Faz 13](13-BAGLAM-SIKISTIRMA-VE-BELLEK.md) — `MaxContextWindowTokens`'ın bugünkü tek tüketicisi
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** Yok — yedek zinciri agent tanımının içindedir, tanım zaten `jsonb` olarak saklanır
> **Public API:** **büyüyor** — `ModelBinding`'e bir alan. `PublicAPI.Shipped.txt` bugün **boş** (ölçüldü: 1 satır), `EnablePublicApiTracking` `true`. `sealed record`'a alan eklemek **bugün bedava**, ilk yayından sonra bir sürüm kararıdır
> **Site etkisi:** `guides/model-providers.md`, `guides/reliability.md`, `reference/configuration.md`
> **Manuel test alanı:** `docs/manuel-test/27-MODEL-YEDEK-VE-ON-UCUS.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-032\|K-104\|K-208\|K-320\|K-158" docs/KARARLAR.md
   ```
   **K-032** (yerleşik model listesi yok — yedek zinciri bir model kataloğu
   **değildir**), **K-104** (`Microsoft.ML.Tokenizers` geçişli bağımlılıktadır
   ve sıkıştırma için ayrı paket alınmadı), **K-208** (`ProviderSettings`
   deseni — `ModelBinding`'e alan eklemenin emsali), **K-320** (boru hattındaki
   **konum** kabul edilmez, grep'le ölçülür), **K-158** (hız sınırı bilerek
   bellekte tutuldu — eşzamanlılık sınırı da öyle olacaktır)
3. [`08-SAGLAYICI-GENISLEMESI.md`](08-SAGLAYICI-GENISLEMESI.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/08-SAGLAYICI-GENISLEMESI.md
   ```
   Devre kesicinin sözleşmesi devralınır: yedek zinciri onun **açık** durumunu
   okur, kendi sağlık modelini kurmaz.
4. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/openai-saglayici.md`](hafiza/openai-saglayici.md) (sağlayıcı
   katalogları ve `ModelDescriptor` üretimi),
   [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md) (`run`
   kaydına olay yazma ve `span` kuralları)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI.md`](MIMARI.md) — model çağrı yolu bölümü

---

## Amaç

Bu faz model çağrısının **iki ucunu** kapatır: çağrıdan **önce** parayı
harcamadan reddetmek, çağrı **başarısız olduğunda** hizmeti ayakta tutmak.

Bugün ikisi de eksiktir. Sağlayıcı kesildiğinde devre kesici açılır ve
çalıştırma **yalnız hata verir**; ikinci bir sağlayıcıya geçiş yoktur. Bağlam
penceresi aşılacağı çağrıdan önce **bilinemez**; hata sağlayıcıdan, para
harcandıktan sonra gelir.

- **F-44** — `ModelBinding.Fallbacks` sıralı yedek zinciri ve sağlayıcı başına
  giden eşzamanlılık sınırı.
- **F-59** — Model üstverisinden bağlam penceresi türetme, çağrı öncesi token
  sayımı ve aşımda erken red.

İkisi tek fazdadır çünkü **tek bir entegrasyon noktasını** paylaşırlar:
[`ModelProviderRegistry.CreateChatClient`](../src/AgentPrism.Core/Models/ModelProviderRegistry.cs)
ve `ModelBinding` sözleşmesi. Ayrı fazlara bölünürse aynı `sealed record` iki
kez, aynı boru hattı iki kez değişir.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`ModelBinding.cs:9-82`](../src/AgentPrism.Abstractions/Agents/ModelBinding.cs) | Alanlar: `Provider`, `Model`, `Temperature`, `MaxOutputTokens`, `TopP`, `ReasoningEffort`, `ProviderSettings`, `ResponseFormat`. **`Fallbacks` yok** |
| [`ModelProviderRegistry.cs:104`](../src/AgentPrism.Core/Models/ModelProviderRegistry.cs) | `CreateChatClient(ModelBinding binding)` — boru hattının **tek** kurulum yeri. Kiracı veya çalıştırma bağlamı **almaz** |
| [`ModelProviderRegistry.cs:154`](../src/AgentPrism.Core/Models/ModelProviderRegistry.cs) | Devre kesici `Wrap` ile sarmalanır; açık devre bir **istisna** atar. Yedek yoktur, ikinci sağlayıcı denenmez |
| [`ModelDescriptor.cs:13`](../src/AgentPrism.Abstractions/Models/ModelDescriptor.cs) | `ContextWindowTokens` tanımlıdır |
| [`OpenAIProviderExtensions.cs:204`](../src/AgentPrism.OpenAI/OpenAIProviderExtensions.cs) · [`GoogleProviderExtensions.cs:174`](../src/AgentPrism.Google/GoogleProviderExtensions.cs) · [`AnthropicProviderExtensions.cs:178`](../src/AgentPrism.Anthropic/AnthropicProviderExtensions.cs) · [`AzureOpenAIProviderExtensions.cs:193`](../src/AgentPrism.Azure/AzureOpenAIProviderExtensions.cs) | **Dört sağlayıcının dördü de** `ContextWindowTokens`'ı yapılandırmadan **yazıyor** |
| `grep -rn "ContextWindowTokens" src/` | Hiçbir üretim kodu bu değeri **okumuyor**. Tek okuyan yer [`FakeModelProvider.cs:56`](../src/AgentPrism.Testing/FakeModelProvider.cs)'nın kendi sabiti |
| [`AgentDefinitionCompiler.cs:689`](../src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.cs) | `ContextWindow` sıkıştırma stratejisi seçilip `MaxContextWindowTokens` verilmezse **derleme hatası**. Kullanıcı sayıyı elle yazmak zorundadır — model üstverisi orada durduğu hâlde |
| [`HarnessSettings.cs:15`](../src/AgentPrism.Abstractions/Agents/HarnessSettings.cs) · [`CompactionSettings.cs:45`](../src/AgentPrism.Abstractions/Agents/CompactionSettings.cs) | Aynı sayı **iki ayrı yere** elle yazılır |
| `grep -rn "Tokenizer" src/` | AgentPrism `Microsoft.ML.Tokenizers`'ı **hiç doğrudan çağırmıyor**. K-104 onun geçişli olarak var olduğunu ölçtü; sıkıştırmada tokenizer'ı **MAF içeride** çözüyor |
| [`RunStatistics.cs:84`](../src/AgentPrism.Abstractions/Runs/RunStatistics.cs) | `ByModel` vardır — yedek devreye girdiğinde maliyet raporu hangi modelin çalıştığını gösterebilir |
| `PublicAPI.Shipped.txt` (1 satır) · [`Directory.Build.props:58`](../Directory.Build.props) | İzleme **açık**, yayınlanmış yüzey **boş**. `ModelBinding`'e alan eklemek bugün bedava |

> Kanıtlar 2026-08-18 tarihinde doğrulandı.

---

## 62.1 — Yedek zinciri nerede yaşar

Karar (kullanıcı, 2026-08-18): **`ModelBinding.Fallbacks`** — yedek listesi
agent tanımının parçasıdır.

Gerekçe: yedek bir **ürün kararıdır**, operasyonel bir ayar değil. "Bu agent
ucuz bir modele düşebilir, şu agent asla düşmemeli" ayrımını yalnız tanım
taşıyabilir. Sağlayıcı düzeyi tek bir zincir bu ayrımı yapamaz.

```mermaid
flowchart TD
    A["ModelBinding<br/>Provider openai · Model gpt-x"] --> B{"Devre kapali mi?"}
    B -->|"evet"| C["Cagri yapilir"]
    B -->|"hayir - devre acik"| D["Fallbacks 0"]
    D --> E{"Devre kapali mi?"}
    E -->|"evet"| F["Yedekle cagri yapilir<br/>ModelFallbackUsed olayi yazilir"]
    E -->|"hayir"| G["Fallbacks 1 ..."]
    G --> H["Zincir biterse<br/>son hata firlatilir"]
```

**Varsayılan kapalıdır (K1).** `Fallbacks` boşken bugünkü davranış **birebir**
korunur: devre açıksa istisna atılır. Boş liste hiçbir `if` çalıştırmaz.

🚨 **Sessiz model değişimi bir sürprizdir.** Yedek devreye girdiğinde çalıştırma
kaydına açık bir olay yazılır. Faz 20'nin maliyet raporu `ByModel` üzerinden
hangi modelin gerçekten çalıştığını zaten gösterebilir; bu faz o satırın
doğruluğunu **test eder**, yeni bir rapor yüzeyi açmaz.

### Yedek ne zaman denenir

| Durum | Yedeğe geçilir mi |
|---|---|
| Devre kesici **açık** | Evet |
| Sağlayıcı `HttpRequestException` veya 5xx | Evet |
| 429 (hız sınırı) | Evet |
| Kimlik doğrulama hatası (401/403) | **Hayır** — yapılandırma hatasıdır, yedek onu gizler |
| İçerik filtresi | **Hayır** — [`ContentFilterDetectingChatClient`](../src/AgentPrism.Core/Models/ContentFilterDetectingChatClient.cs) bunu zaten ayırıyor; sağlayıcı **sağlıklıdır** |
| İptal (`OperationCanceledException`) | **Hayır** |

Bu tablo bir davranış sözleşmesidir ve testle kapatılır.

## 62.2 — Yedek boru hattının hangi halkasında durur

🚨 **Konum kabul edilmez, ölçülür (K-320).** Bugünkü sıra
[`ModelProviderRegistry.cs:118-165`](../src/AgentPrism.Core/Models/ModelProviderRegistry.cs)
içinde şudur (içten dışa): ham istemci → `ContentGuardingChatClient` →
`UseFunctionInvocation` → `UseOpenTelemetry` → `AttachmentResolvingChatClient`
→ devre kesici → `ContentFilterDetectingChatClient`.

Yedek **devre kesicinin dışında** durmalıdır: yedeğe geçmek için önce birincil
sağlayıcının devresinin açık olduğunu **görmek** gerekir.

🚨 **Tool çağrı döngüsü içeride kalır.** Yedek en dışta olduğu için, yedeğe
geçildiğinde tur **baştan** başlar. Bu bilinçli bir karardır: tool döngüsünün
ortasında sağlayıcı değiştirmek yarım bir konuşma durumu üretir.

## 62.3 — Giden eşzamanlılık sınırı

Sağlayıcı başına eş zamanlı giden çağrı sayısı sınırlanır. Amaç 429 fırtınasını
**üretmemektir**; devre kesici onu yalnız fark eder.

- Varsayılan **sınırsız** (K1) — bugünkü davranış korunur.
- Sayaç **bellekte** yaşar. K-158'in gerekçesi birebir geçerlidir: dağıtık bir
  sayaç Redis bağımlılığı ister ve bir kütüphanede sıfır sürprizi bozar.
- Desen [`VoiceConnectionLimiter`](../src/AgentPrism.Core/) içinde vardır ve
  **uygulama anında okunmalıdır** — kopyalanmadan önce imzası doğrulanır.

## 62.4 — Ön uçuş denetimi: önce türetme, sonra sayım

İki ayrı iş vardır ve **birincisi tek başına değerlidir**.

**Birinci — türetme (ucuz, risksiz).** `MaxContextWindowTokens` verilmediyse
değer `ModelDescriptor.ContextWindowTokens`'tan **türetilir**. Bugün derleme
hatası veren yol ([`AgentDefinitionCompiler.cs:689`](../src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.cs))
artık yalnız model üstverisi de yoksa hata verir. Hata mesajı hangi iki yoldan
birinin doldurulacağını söyler.

**İkinci — sayım (yaklaşık, riskli).** Çağrıdan önce istem token'ları sayılır ve
eşik aşılırsa çağrı yapılmadan reddedilir.

🚨 **Token sayımı yaklaşıktır ve bu bir risktir.** Yanlış bir erken red çalışan
bir agent'ı durdurur. Bu yüzden:

- Sayım varsayılan **kapalıdır** (K1).
- Eşik **oransal** bir paydır (`ReserveRatio`), sabit bir sayı değil.
- Red bir `ProblemDetails` ile ve sayılan/izin verilen değerlerle döner.

🚨 **Tokenizer bir açık sorudur.** K-104 `Microsoft.ML.Tokenizers`'ın geçişli
olarak var olduğunu ölçtü, ama AgentPrism onu **hiç doğrudan çağırmıyor**.
Geçişli bir bağımlılığa doğrudan kod yazmak, üst paket onu bırakırsa derlemeyi
kırar. Açık Soru 1 bunu karara bağlar.

## 62.5 — Kestirim ucu

`POST /api/agents/{name}/estimate` çağrı **yapmadan** sayıyı döndürür: istem
token sayısı, modelin penceresi, kalan pay ve "bu çağrı reddedilir mi" yanıtı.

Bu uç ön uçuş denetiminin **teşhis yüzeyidir**. Kota (Faz 21) ve ağaç bütçesi
(Faz 12) aynı sayıyı çağrıdan **sonra** ölçer; bu uç öncesini gösterir.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions — ModelBinding'e iki alan
public sealed record ModelBinding
{
    // ... mevcut alanlar

    /// <summary>Ordered fallback bindings tried when the primary provider is unavailable. Empty by default.</summary>
    public IReadOnlyList<ModelFallback> Fallbacks { get; init; } = [];
}

/// <summary>One link of the fallback chain.</summary>
public sealed record ModelFallback
{
    public required string Provider { get; init; }
    public required string Model { get; init; }
}

// AgentPrism.Abstractions — on ucus ayarlari, varsayilan KAPALI
public sealed class AgentPrismPreflightOptions
{
    /// <summary>Whether the pre-flight context window check runs. Disabled by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>The share of the context window kept free for the answer. Default 0.2.</summary>
    public double ReserveRatio { get; set; } = 0.2;
}

// AgentPrism.Abstractions — saglayici basina giden esszamanlilik
public sealed class AgentPrismModelConcurrencyOptions
{
    /// <summary>Maximum concurrent outgoing calls per provider. Null means unlimited.</summary>
    public int? MaxConcurrentCallsPerProvider { get; set; }
}

// AgentPrism.Abstractions — kestirim sonucu
public sealed record ContextWindowEstimate
{
    public required int PromptTokens { get; init; }
    public required int? ContextWindowTokens { get; init; }
    public required bool WouldBeRejected { get; init; }
}
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol · kapsam | Ne yapar |
|---|---|---|---|
| `POST` | `/api/agents/{name}/estimate` | Reader · `RunsRead` | Model çağrısı **yapmadan** token kestirimi döner |

Yedek zinciri için yeni uç **yoktur**; alan agent tanımıyla birlikte var olan
`POST/PUT /api/agents` üzerinden gelir.

### Arayüz payı

Agent düzenleyicisine yedek listesi alanı ve model bölümüne kestirim rozeti
eklenir. Bugünkü kullanım **165,4 KB gzip / 250 KB** (Faz 61'de ölçüldü);
**artış uygulama anında ölçülüp bu belgeye yazılır**. Yeni sözlük anahtarları
`en.ts` **ve** `tr.ts` içine girer (K-228).

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/
├── Agents/ModelBinding.cs              (Fallbacks alani)
├── Agents/ModelFallback.cs             (yeni)
├── Models/ContextWindowEstimate.cs     (yeni)
└── Options/                            (AgentPrismPreflightOptions, AgentPrismModelConcurrencyOptions)

src/AgentPrism.Core/Models/
├── ModelProviderRegistry.cs            (yedek halkasi burada kurulur)
├── FallbackChatClient.cs               (yeni - en dis halka)
├── ProviderConcurrencyLimiter.cs       (yeni)
└── ContextWindowEstimator.cs           (yeni - turetme + sayim)

src/AgentPrism.Core/Compilation/
└── AgentDefinitionCompiler.cs          (MaxContextWindowTokens turetilir)

src/AgentPrism.AspNetCore/Endpoints/
└── AgentEndpoints.cs                   (estimate ucu)

src/AgentPrism.UI/frontend/src/
├── screens/agent-editor.tsx            (yedek listesi alani)
└── locales/{en,tr}.ts                  (yeni anahtarlar - K-228)
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| `Fallbacks` boşken davranış değişir (bugünkü hata yolu kaybolur) | Birim | `FallbackChatClientTests` |
| Kimlik doğrulama hatasında yedeğe geçilir (hata gizlenir) | Birim | `FallbackChatClientTests` |
| İçerik filtresi yedeği tetikler | Birim | `FallbackChatClientTests` |
| İptal edilen çağrı yedeği tetikler | Birim | `FallbackChatClientTests` |
| Zincirin tamamı düşer; **son** hata değil ilk hata fırlatılır | Birim | `FallbackChatClientTests` |
| Yedek çalıştı ama `run` kaydı **birincil** modeli yazıyor | Fonksiyonel | `FallbackRecordingTests` — `RunStatistics.ByModel` gerçek modeli gösterir |
| Yedek devreye girdi, maliyet **birincil** modelin fiyatıyla hesaplandı | Fonksiyonel | `FallbackRecordingTests` |
| Eşzamanlılık sınırı sıcak yolda tahsis üretir | Birim | `ProviderConcurrencyLimiterTests` |
| Eşzamanlılık sınırı altında istekler **kilitlenir** (deadlock) | Fonksiyonel | `ProviderConcurrencyLimiterTests` — sınır 1, iki eş zamanlı çalıştırma |
| Sınır dolu iken iptal gelirse yuva bırakılmaz (sızıntı) | Fonksiyonel | `ProviderConcurrencyLimiterTests` |
| Ön uçuş **kapalıyken** bir `if` bile çalışır | Birim | `ContextWindowEstimatorTests` |
| Türetme yanlış modelin penceresini alır | Birim | `ContextWindowEstimatorTests` |
| Model üstverisi yokken türetme çöker | Birim | `ContextWindowEstimatorTests` — anlaşılır derleme hatası |
| Erken red doğru çağrıyı öldürür (yanlış pozitif) | Fonksiyonel | `PreflightEndpointTests` — eşik altındaki istem geçer |
| `estimate` ucu başka kiracının agent'ını görür | Sözleşme (`TenantIsolationContract`) | dört koşumda birden |
| `estimate` ucu kapsamsız anahtarla çağrılır | Fonksiyonel | `PreflightEndpointTests` → `403` |
| Boş mesajla `estimate` çağrılır | Fonksiyonel | `PreflightEndpointTests` |
| Sözlük anahtarı eksik | Derleme | `tsc --noEmit` (K-228) |
| Yedek listesi arayüzden kaydedilir ve geri okunur | E2E (Playwright) | `UiTests` |

Sözleşme testi `tests/Shared/Contracts/` altına — hem bellek içi hem üç SQL
sağlayıcısı üzerinde koşar.

---

## Manuel Kabul Case'leri

> Kapanışta `docs/manuel-test/27-MODEL-YEDEK-VE-ON-UCUS.md` içine eklenecek
> case'lerin taslağı.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | `Fallbacks` boş | Sağlayıcıyı geçersiz anahtarla çalıştır | Bugünkü hata birebir korunur |
| 2 | `Fallbacks` bir yedek taşır, birincinin devresi açık | Çalıştır | Yanıt yedekten gelir; `run` kaydı yedek modeli yazar |
| 3 | 2'nin çıktısı | Maliyet raporunu aç | `ByModel` yedek modeli ve onun fiyatını gösterir |
| 4 | Yedek de düşer | Çalıştır | Hata döner; mesaj zincirin denendiğini söyler |
| 5 | Ön uçuş kapalı | Pencereden büyük bir istem gönder | Bugünkü davranış: hata sağlayıcıdan gelir |
| 6 | Ön uçuş açık | Aynı istem | Çağrı **yapılmadan** `400`; yanıt sayılan ve izin verilen token'ı yazar |
| 7 | — | `POST /api/agents/{ad}/estimate` | Sayı döner, model çağrısı **yapılmaz** (sağlayıcı logunda istek yok) |
| 8 | `MaxContextWindowTokens` boş, model üstverisi dolu | `ContextWindow` sıkıştırmalı agent kaydet | Derleme **geçer**; değer üstveriden türetilir |
| 9 | İkisi de boş | Aynı kayıt | Anlaşılır hata: hangi iki alandan birinin doldurulacağını söyler |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Token sayımı hangi tokenizer'la yapılır? | A: `Microsoft.ML.Tokenizers`'a **açık** `PackageReference` · B: geçişli olanı doğrudan çağır · C: karakter tabanlı kaba kestirim, tokenizer yok | **A.** Geçişli bir bağımlılığa doğrudan kod yazmak (B) üst paket onu bırakırsa derlemeyi kırar; K-104 yalnız **sıkıştırma** için "yeni paket alınmadı" dedi, çünkü orada tokenizer'ı MAF çözüyordu. Burada çağıran biziz. C ölçüsüzdür ve erken red için yeterince güvenilir değildir |
| 2 | Yedek zinciri `ModelBinding`'in tamamını mı yoksa yalnız sağlayıcı+model çiftini mi taşır? | A: `ModelFallback` yalnız `Provider`+`Model` · B: tam `ModelBinding` (sıcaklık, `ProviderSettings` dahil) | **A.** B özyinelemeli bir tip üretir (`Fallbacks` içinde `Fallbacks`) ve doğrulaması zorlaşır. Yedek modelin kendi ayarı gerekiyorsa ayrı bir kalemdir |
| 3 | Eşzamanlılık sınırı bekletsin mi, hemen reddetsin mi? | A: Bekle (kuyruk) · B: Hemen `429` | **A.** Sınırın amacı 429 üretmemektir; hemen reddetmek onu üretir. Bekleme süresi iptal token'ına bağlıdır |
| 4 | Ön uçuş reddi hangi kodu döner? | A: `400` · B: `413 Payload Too Large` | **A.** `413` gövde boyutu içindir; burada sorun gövde değil, modelin penceresidir |
| 5 | Yedek olayı `run_events`'e mi yoksa yalnız `span`'e mi yazılır? | A: İkisi · B: yalnız `span` | **A.** Gece 03:00'te nöbetçi mühendis `run` kaydına bakar; `span` her zaman toplanmış olmayabilir |

---

## Bitiş Ölçütleri (DoD)

- [ ] `Fallbacks` boşken bugünkü hata yolu **birebir** korunur (test kanıtlar)
- [ ] Devre açıkken yedek devreye girer; `run` kaydı ve `RunStatistics.ByModel` **gerçekten çalışan** modeli gösterir
- [ ] Kimlik doğrulama hatası, içerik filtresi ve iptal yedeği **tetiklemez**
- [ ] Zincir tükendiğinde hata mesajı denenen sağlayıcıları sayar
- [ ] Eşzamanlılık sınırı `null` iken sıcak yolda ek tahsis **yoktur**
- [ ] `MaxContextWindowTokens` boşken değer `ModelDescriptor`'dan türetilir; ikisi de boşsa hata iki yolu da söyler
- [ ] Ön uçuş **kapalı** varsayılandır; açıkken aşan istem model çağrısı **yapılmadan** `400` döner
- [ ] `POST /api/agents/{name}/estimate` sağlayıcıya istek **göndermeden** sayı döner
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/27-MODEL-YEDEK-VE-ON-UCUS.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi (`guides/model-providers.md`, `guides/reliability.md`, `reference/configuration.md`); `npm run build` + `check-links.mjs` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı

### Doğrulama komutları

```bash
# Kestirim - model cagrisi YAPILMAZ
curl -s -X POST http://localhost:5081/agentprism/api/agents/demo/estimate \
  -H 'Content-Type: application/json' \
  -d '{"message":"..."}'

# On ucus acikken asan istem reddedilir
curl -s -o /dev/null -w '%{http_code}\n' -X POST \
  http://localhost:5081/agentprism/api/agents/demo/run \
  -H 'Content-Type: application/json' \
  -d '{"message":"<pencereden buyuk istem>"}'
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 Sessiz model değişimi tüketiciyi şaşırtır | Varsayılan kapalı; `run` kaydına açık olay; maliyet raporu gerçek modeli gösterir ve bu **test edilir** |
| 🚨 Yanlış erken red çalışan agent'ı durdurur | Varsayılan kapalı; oransal pay; red yanıtı sayıları yazar; eşik gevşek |
| Yedek farklı fiyatlıdır, fatura sürprizi olur | `ByModel` ayrımı testle kapatılır; belge fiyat farkını açıkça yazar |
| Tokenizer geçişli bağımlılıktan kaybolur | Açık Soru 1 → açık `PackageReference` |
| Eşzamanlılık sınırı kilitlenme üretir | İptal token'ına bağlı bekleme; sınır 1 ile eş zamanlı iki çalıştırma testi |
| Yedek halkası boru hattında yanlış yere konur | 🚨 K-320: konum **grep'le ölçülür**; devre kesicinin dışında olduğu testle kanıtlanır |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Her satır: bulgu · seviye
> (🔴/🟡/🟢) · sonuç (düzeltildi / gerekçelendi / F-NN olarak devredildi).
> Bulgu yoksa "🔴 ve 🟡 yok" yazılır; boş bırakılmaz.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.
