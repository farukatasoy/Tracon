# Faz 26 — Anthropic (Claude) ve Google Gemini Sağlayıcıları

> **Durum:** ✅ Tamamlandı (2026-08-05)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-01**, **F-02**
> **Önkoşul:** [Faz 8](08-SAGLAYICI-GENISLEMESI.md) — sağlık denetimi ve devre kesici hazırdı
> **Paketler:** **`Tracon.Anthropic` (YENİ)**, **`Tracon.Google` (YENİ)** ·
> genişleyen: `Tracon.Abstractions`, `.Core`
> **Migration:** Yok

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/26-ANTHROPIC-VE-GEMINI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç ve Sonuç

Claude ve Gemini'ye **birinci sınıf** destek: sağlayıcıya özgü yetenekler (prompt caching, `thinking` blokları, güvenlik eşikleri) ancak doğrudan SDK ile kullanılabilir. **Sonuç:** İki yeni paket üretiliyor.

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

### S2 — Paket adı `Tracon.Google`, sağlayıcı adı `google` *(kullanıcı kararı)*

**Plan:** `.UseGemini(...)` örneği. **Yapılan:** `UseGoogle(...)` ve sağlayıcı adı
`google`. Vertex AI aynı pakete eklenebilir; `gemini` adı paketi tek bir model
ailesine kilitlerdi. Yapılandırma bölümü `Tracon:Providers:Google`. Karar K-207.

### S3 — İçerik filtresi tespiti `Core`'da ortak dekoratördür *(kullanıcı kararı)*

**Plan:** "Tracon bunu `RunRecord.ErrorType = "content_filtered"` olarak
kaydetmelidir" — nerede yapılacağı açık değildi.
**Yapılan:** `Tracon.Core` içinde `ContentFilterDetectingChatClient`; devre
kesiciyle **aynı desen** (Faz 8). Üç sağlayıcı da korumayı tek yerden alır ve
Faz 27 kural yazmadan devralır.

🚨 **Sarmalama sırası kritiktir: dekoratör devre kesicinin DIŞINDADIR.** Filtrelenmiş
bir yanıt sağlayıcının *sağlıklı* olduğunu gösterir; içeride olsaydı attığı istisna
ardışık hata sayacını artırır ve eşik sayısınca filtrelenen istek sağlayıcıyı
kapatırdı. Test: `ContentFilterDetectingChatClientTests.Devre_kesici_filtreyi_hata_saymaz`.

**Yalnız boş yanıt hataya çevrilir.** Model metin (veya tool çağrısı) üretip sonra
kesildiyse kullanıcının elinde kısmi bir cevap vardır; onu silmek bilgi kaybıdır.

`RunRecord` şeması **değişmedi**: `RunError.Type` alanı zaten vardı. `ErrorType`
`TraconException` üzerinde `virtual` bir üye oldu ve varsayılanı tipin tam
adıdır — mevcut kayıtların biçimi değişmedi. Karar K-206.

### S4 — Sağlayıcıya özgü ayarlar `RawRepresentationFactory` ile taşınır

**Ölçüldü (2026-08-05):** Her iki SDK adaptörü de
`ChatOptions.RawRepresentationFactory` okur (metadata üye referansıyla doğrulandı,
sonra gerçek çağrıyla kanıtlandı). Bu, `Microsoft.Extensions.AI`'ın sağlayıcıya
özgü alanlar için resmî kaçış kapısıdır; Tracon ham HTTP yazmaz.

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

## Bitiş Ölçütleri (DoD)

Elle doğrulama: gerçek Anthropic + Google anahtarı, `samples/Tracon.Api`,
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
$ curl -s "localhost:5080/tracon/api/models/health?refresh=true"
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
builder.AddTracon()
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
4. **K-207** — Paket `Tracon.Google`, sağlayıcı adı `google`.
5. **K-208** — `ModelBinding.ProviderSettings` sözleşmeye eklendi; bilinmeyen anahtar derleme hatasıdır.
6. **K-209** — Meta paket iki yeni sağlayıcıyı **içermez** (K-185 deseni).

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
  `CacheReadInputTokens` veriyor; Tracon'in `RunUsage` kaydı yalnız
  giriş/çıkış/toplam taşıyor. Maliyet raporu önbellekli girdiyi normal girdi
  fiyatından sayıyor — Faz 20'nin fiyat çözümlemesi için açık bir kalem.
- **`DependencyDirectionTests.AllowedReferences` `Tracon.SqlServer`,
  `Tracon.Sqlite` ve `Tracon.Sql.Shared` paketlerini içermiyor** (Faz 23/24'ten
  kalan boşluk). Bu fazda iki yeni sağlayıcı eklendi; SQL paketleri hâlâ açık.
- **Gemini'nin model adları hızlı eskir.** `gemini-2.5-flash` ölçüm sırasında
  *"no longer available to new users"* döndü. K-032 burada somut bir bedel olarak
  yaşandı; katalog yapılandırmadan gelir ve bir doğrulama listesi değildir.
