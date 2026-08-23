# Faz 60 — Public API Kapısı

> **Durum:** ✅ Tamamlandı (2026-08-16)
> **Kaynak:** Kullanıcı kararı, 2026-08-16 — süreç iyileştirme oturumu. Aday listesinden gelmez.
> **Önkoşul:** Yok. [Faz 7](07-SAGLAMLASTIRMA-VE-YAYIN.md) **beklemez** — bu faz yayın kararından bağımsızdır.
> **Paketler:** Yayınlanan 17 paketin tamamı
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — **daralıyor**. 10 metodun aşırı yükleme çifti sadeleşir (kırıcı; bugün bedava, yayından sonra pahalı)
> **Site etkisi:** `packages.md` (API kararlılığı vaadi) · `api/` üretilir, elle yazılmaz
> **Manuel test alanı:** [`docs/manuel-test/01-KURULUM-VE-PAKETLEME.md`](../../manuel-test/01-KURULUM-VE-PAKETLEME.md) (`PKG`)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-016\|K-068\|K-008\|K-353\|K-408" docs/KARARLAR.md
   ```
   **K-016** (`EnablePublicApiTracking` Faz 7'de açılır), **K-068** (Faz 7 sıradan
   çıkarıldı; takip `false` kalır), **K-008** (ön sürüm MAF paketleri yalnız
   `AspNetCore`'da), **K-353** (`UseMcp(IConfiguration, …)` aşırı yüklemesi —
   bu faz ona dokunur), **K-408** (kaynak dili sınırı)
3. [`07-SAGLAMLASTIRMA-VE-YAYIN.md`](07-SAGLAMLASTIRMA-VE-YAYIN.md) — yalnız
   başlıktaki uyarı bloğu (ilk 15 satır). Bu faz onun bir parçasını **öne alır**;
   Faz 7 yine de yayın fazı olarak kalır.
4. Alan hafızası: [`hafiza/build-ve-analyzer.md`](../../hafiza/build-ve-analyzer.md)
   (MSBuild, analyzer tanısı, `NoWarn` davranışı)
5. `Directory.Build.props` satır 52–60 — takibin bugünkü kapalı hâli

---

## Amaç

AgentPrism 17 NuGet paketi yayınlayacak bir kütüphane ailesidir. Bir tüketici
için **kırıcı API değişikliği bir üretim kusurudur** — kodun kendisi doğru olsa
bile. Bugün böyle bir değişikliği hiçbir kapı yakalamaz: `EnablePublicApiTracking`
`false`'tur ve dokuz `RS` kuralı `NoWarn` listesindedir.

Bu faz kapıyı kurar. Yayın kararını **vermez** — kapı ile yayın iki ayrı iştir.

- Public yüzeyin tamamı `PublicAPI.Unshipped.txt` dosyalarına yazılır
- `EnablePublicApiTracking` `true` olur; `NoWarn` listesi kalkar
- Yüzeyi değiştiren her faz bundan sonra o dosyanın **diff'ini** üretir — görünür, denetlenebilir, kasıtlı
- Bugün var olan 10 geri uyumluluk tuzağı (RS0026) kapanır

### Bugün ne çalışmıyor — doğrulanmış kanıt

Ölçüm komutu (2026-08-16'da koşuldu):

```bash
dotnet build AgentPrism.slnx -c Release --no-incremental \
  -p:EnablePublicApiTracking=true -p:TreatWarningsAsErrors=false \
  -p:AgentPrismFrontendEnabled=false
```

| Tanı | Benzersiz | Anlamı |
|---|---|---|
| `RS0016` | **5.052** | Public üye kayıtlı değil — taban çizgisi dolumu |
| `RS0026` | **20** (10 metot × 2 aşırı yükleme) | Opsiyonel parametreli çoklu aşırı yükleme — **gerçek tuzak** |
| `RS0041` | **51** | API'de `oblivious` referans tipi |
| `RS0037` | **10** | `PublicAPI.txt` yok veya `#nullable enable` taşımıyor |

`RS0016` paket dağılımı:

| Paket | Üye | Paket | Üye |
|---|---|---|---|
| `Abstractions` | 2.850 | `Voice` | 32 |
| `Core` | 1.164 | `Mcp` | 22 |
| `AspNetCore` | 531 | `Workflows` | 19 |
| `Testing` | 52 | `Sqlite` / `SqlServer` / `PostgreSql` | 16 (her biri) |
| `OpenAI` | 47 | `Sql.Shared` | 4 |
| `Google` | 45 | `UI` | 2 |
| `Anthropic` | 41 | `Generators` | 2 |
| `Azure` | 39 | | |

Ek doğrulanmış gözlemler:

| Kanıt | Gözlem |
|---|---|
| `src/AgentPrism.*/PublicAPI.Shipped.txt` | 15 pakette dosya **var ve boş** (yalnız `#nullable enable`) |
| `Mcp`, `Workflows`, `Sql.Shared`, `Templates`, `Generators` | Dosya **yok** — `RS0037`'nin kaynağı |
| `Directory.Build.props:58` | `EnablePublicApiTracking` `false` |
| `Directory.Build.props:59` | Dokuz kural `NoWarn`'da: `RS0016;RS0017;RS0022;RS0024;RS0025;RS0026;RS0027;RS0036;RS0037;RS0041` |
| `src/Directory.Build.props:13` | Üç hedef çerçeve: `net8.0;net9.0;net10.0` |
| `net8.0` ↔ `net10.0` yüzey karşılaştırması | `Abstractions` için **2.850 = 2.850, sıfır fark** → tek `PublicAPI` dosyası yeter, çerçeve başına dosya **gerekmez** |

> Kanıtlar 2026-08-16 tarihinde ölçüldü.

---

## 60.1 — Taban çizgisi `Unshipped.txt`'ye yazılır

**Karar (kullanıcı, 2026-08-16):** 5.052 üyenin tamamı `PublicAPI.Unshipped.txt`
dosyalarına gider. `Shipped.txt` **boş kalır**.

Gerekçe: AgentPrism hiçbir sürüm yayınlamadı. "Shipped" olmayan bir API'yi
`Shipped.txt`'ye yazmak dosyanın anlamını bozar ve K-068'in gerekçesiyle çelişir.
Faz 7 (yayın) geldiğinde `Unshipped` → `Shipped` taşınır; bu tek seferlik ve
mekanik bir adımdır.

Yan etki — **istenen** yan etki: her fazın API büyümesi `Unshipped.txt`'nin
`git diff`'inde görünür. `faz-denetim` 3.6 maddesi ("plan dışı public API") bugün
gözle yapılıyor; bu fazdan sonra **derleyici** yapar.

### Dolum yöntemi

Elle yazılmaz. Analyzer'ın kod düzeltmesi toplu uygulanır:

```bash
dotnet format analyzers AgentPrism.slnx --diagnostics RS0016 --severity info
```

> **Doğrulanmadı — uygulama anında ölçülmeli.** `dotnet format analyzers`'ın
> `PublicApiAnalyzers`'ın toplu kod düzeltmesini (`FixAll`) uygulayıp
> uygulamadığı bu repoda denenmedi. Uygulamazsa yedek yol: her paket için
> derleme çıktısındaki `RS0016` satırlarından sembol imzalarını üreten tek
> seferlik bir betik (`scripts/` altına **kalıcı olarak konmaz** — tek kullanımlık).

Dosyalar 15 pakette vardır; eksik beşine (`Mcp`, `Workflows`, `Sql.Shared`,
`Templates`, `Generators`) `#nullable enable` başlıklı boş dosya eklenir.
`Sql.Shared` bağlı kaynak taşır (K-185) — üyeleri üç sağlayıcı derlemesinde
görünür; hangi dosyaya yazılacağı **uygulama anında ölçülür**.

---

## 60.2 — RS0026: 10 tuzak kapanır

**Karar (kullanıcı, 2026-08-16):** bastırılmaz, **düzeltilir**.

Kural şunu söyler: aynı ada sahip iki aşırı yükleme opsiyonel parametre
taşıyorsa, sonradan bir parametre eklemek çağıranı **derleme hatası vermeden**
başka bir aşırı yüklemeye kaydırabilir. Bu kırılma sessizdir.

Bugün kırıcı değildir — hiçbir sürüm yayınlanmadı. Yayından sonra düzeltmek bir
sürüm kararıdır. **En ucuz an şimdidir.**

| Metot | Dosya |
|---|---|
| `IAgentCatalog.ResolveAsync` | `Abstractions/Agents/IAgentCatalog.cs:26,45` |
| `CompositeAgentCatalog.ResolveAsync` | `Core/Catalog/CompositeAgentCatalog.cs:75,102` |
| `IAgentPrismBuilder.AddTool` | `Core/IAgentPrismBuilder.cs:30,48` |
| `UseVoiceConversation` | `Core/Voice/VoiceConversationBuilderExtensions.cs:36,71` |
| `UseMcp` | `Mcp/AgentPrismMcpBuilderExtensions.cs:47,87` |
| `UseAzureOpenAI` | `Azure/AzureOpenAIProviderExtensions.cs:26,60` |
| `AnthropicChatClientFactory` (ctor) | `Anthropic/AnthropicChatClientFactory.cs:43,61` |
| `AzureOpenAIChatClientFactory` (ctor) | `Azure/AzureOpenAIChatClientFactory.cs:54,69` |
| `GoogleChatClientFactory` (ctor) | `Google/GoogleChatClientFactory.cs:39,54` |
| `OpenAIChatClientFactory` (ctor) | `OpenAI/OpenAIChatClientFactory.cs:38,52` |

Her kalem için üç yoldan biri seçilir ve **gerekçesi dokümana yazılır**:

1. **Birleştir** — iki aşırı yükleme tek imzaya iner (opsiyonel parametreler tek yerde)
2. **Ayrıştır** — biri opsiyonel parametrelerini kaybeder; çağrı yerleri güncellenir
3. **Yeniden adlandır** — ikisi ayrı ad alır (`UseMcp` / `UseMcpFromConfiguration` gibi)

> 🚨 `UseMcp`'nin ikinci aşırı yüklemesi **K-353** ile bilerek eklendi
> (değerlendirme raporu §2.2'nin düzeltmesi). O kararın gerekçesi bozulmadan
> çözülmelidir — muhtemelen (3) uygundur. Karar bu fazda yeniden verilir ve
> K-353'e atıfla yazılır.

Dört sağlayıcı fabrikası aynı deseni taşır (`ctor` çifti); dördü **aynı** çözümü
almalıdır, yoksa sağlayıcılar arası tutarlılık bozulur.

---

## 60.3 — RS0041 ve RS0037

`RS0037` mekaniktir: eksik `PublicAPI` dosyaları eklenince kapanır (60.1).

`RS0041` (51 kalem) `oblivious` referans tipi bildirir. Örnek semboller
`WebhookEventPayload`, `WebhookRunSummary`, `WebhookQuotaSummary` ve kota
tiplerinin `get` erişimcilerinde kümeleniyor.

> **Kök sebep doğrulanmadı — uygulama anında ölçülmeli.** Tanı adları
> (`String.get`, `Int32.get`, `DateTimeOffset.get`) üye adını değil dönüş tipini
> gösteriyor; bu, tanının bir üretilmiş veya `partial` kaynağa işaret ettiğini
> düşündürür. Ölçmeden düzeltme yazma.

Çözülemezse `RS0041` **tek başına** ve gerekçesiyle bastırılabilir — diğer sekiz
kural açık kalır. Bastırma bir karardır; `docs/KARARLAR.md`'ye yazılır.

---

## 60.4 — Kapı açılır

```xml
<PropertyGroup Label="Public API takibi">
  <EnablePublicApiTracking>true</EnablePublicApiTracking>
</PropertyGroup>
```

`NoWarn` koşullu satırı **silinir**. `TreatWarningsAsErrors` zaten açıktır; yani
kayıtsız bir public üye bundan sonra **derlemeyi kırar**.

`Directory.Build.props`'taki açıklama yorumu ("Public API takibi Faz 7'de
açılır") güncellenir — yoksa doküman koda karşı yalan söyler.

---

## Planlanan Public API

Bu faz yeni API **eklemez**. Değişiklikler 60.2'nin on kalemidir ve her biri
mevcut bir imzayı sadeleştirir. Gerçekleşen imzalar kapanışta yazılır.

### Arayüz payı

Yok — arayüze dokunulmaz.

---

## Planlanan Dosya Listesi

```
Directory.Build.props                      (değişir — takip açılır, NoWarn kalkar)
src/AgentPrism.<17 paket>/PublicAPI.Unshipped.txt   (dolar)
src/AgentPrism.Mcp/PublicAPI.{Shipped,Unshipped}.txt        (yeni)
src/AgentPrism.Workflows/PublicAPI.{Shipped,Unshipped}.txt  (yeni)
src/AgentPrism.Sql.Shared/…                                 (ölçüme göre)
src/AgentPrism.Core/IAgentPrismBuilder.cs                   (RS0026)
src/AgentPrism.Abstractions/Agents/IAgentCatalog.cs         (RS0026)
… 60.2 tablosundaki sekiz dosya daha
```

---

## Hata Modları ve Testler

> Bu faz **davranış** değiştirmez; kapı ve imza işidir. Hata modları da
> bu yüzden derleme ve paketleme seviyesindedir.

| Ne bozulabilir | Seviye | Test |
|---|---|---|
| Taban çizgisi eksik kalır; kapı ilk fazda 5.000 hata verir | Kapı | `dotnet build` sıfır uyarı |
| `Sql.Shared` üyeleri üç derlemede birden sayılır ve çift kayıt ister | Kapı | `dotnet build` üç sağlayıcı için yeşil |
| RS0026 düzeltmesi bir çağıranı kırar | Birim + Fonksiyonel | Mevcut 3.700+ test; kırılan çağrı derlemede çıkar |
| `UseMcp` sadeleşmesi K-353'ün çözdüğü bağlama senaryosunu geri kırar | Fonksiyonel | `McpConfigurationBindingTests` (varsa; yoksa yazılır) |
| Şablon (`dotnet new`) sadeleşen imzayı kullanıyorsa paket kırılır | Fonksiyonel | `AgentPrism.Templates.Tests` |
| Paketlenmiş tüketici farklı bir yüzey görür | Manuel | `PKG` alanına case |

Bu faz yeni bir kod yolu üretmediği için beş soru (iptal · eşzamanlılık ·
boş girdi · kiracı · alt sistem hatası) **uygulanmaz**. Gerekçe budur.

---

## Manuel Kabul Case'leri

> Kapanışta [`docs/manuel-test/01-KURULUM-VE-PAKETLEME.md`](../../manuel-test/01-KURULUM-VE-PAKETLEME.md)
> (`PKG`) içine eklenir.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Temiz çalışma ağacı | `dotnet build AgentPrism.slnx -c Release` | Sıfır uyarı; `RS` tanısı yok |
| 2 | Taban çizgisi dolu | Bir `public` metoda yeni bir `public` üye ekle, derle | Derleme **kırılır**, `RS0016` verir ve üye adını söyler |
| 3 | (2)'deki üye `Unshipped.txt`'ye eklendi | Yeniden derle | Yeşil |
| 4 | Paketler üretildi | `dotnet pack` → yerel besleme → scratch projede tüket | Sadeleşen imzalar tüketiciden görünür ve derlenir |

Case 2 ve 3 kapının **gerçekten** çalıştığını kanıtlar; yalnız yeşil bir build
bunu kanıtlamaz.

---

## Bitiş Ölçütleri (DoD)

- [x] `EnablePublicApiTracking` `true`; `NoWarn` koşullu satırı silindi
- [x] `PublicAPI.Unshipped.txt` 16 pakette dolu (meta `AgentPrism` kendi derlenmiş üyesi olmadığı için boş — beklenen); 17. paket olan `Templates` K-424 gereği dosya almıyor (kasıtlı hariç tutma); `Shipped.txt` boş
- [x] `RS0026`'nın 10 kalemi kapandı; her biri için seçim ve gerekçe dokümanda (K-422, "Plandan Sapmalar" §4)
- [x] `RS0041` ya kapandı ya gerekçesiyle **tek başına** bastırıldı (karar defterinde — K-423)
- [x] Manuel case 2: yeni bir public üye derlemeyi **kırıyor** (gerçek çıktı yazıldı — `MT-PKG-091`)
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `PKG` alanına eklendi; otomatikleştirilebilenler koşuldu (`MT-PKG-090..093`, dördü de koşuldu)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/packages.md` API kararlılığı vaadini anlatıyor

### Doğrulama komutları

```bash
# Kapı gerçekten kapalı mı: kayıtsız üye derlemeyi kırmalı
dotnet build AgentPrism.slnx -c Release 2>&1 | grep -c "RS0016"   # 0 beklenir

# Sonra bir public üye ekleyip tekrar: sıfırdan büyük olmalı
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Toplu kod düzeltmesi 5.052 kalemi doldurmaz | Yedek yol planlı (60.1); derleme çıktısından üretim |
| `Sql.Shared`'ın bağlı kaynağı çift kayıt ister | Uygulamada **önce ölç**, sonra yaz (K-185) |
| RS0026 düzeltmesi K-353'ü geri alır | `UseMcp` için yeniden adlandırma; karar K-353'e atıfla yazılır |
| Faz 7 geldiğinde `Unshipped` → `Shipped` taşıması unutulur | Faz 7 dokümanına devir notu olarak yazılır (kapanış adımı) |
| 5.052 satırlık diff denetimi imkânsız kılar | Taban çizgisi **ayrı bir commit**; imza değişiklikleri ayrı commit'te |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `Generators` ve `Templates` takibe girsin mi? | A: Girsin (paket olarak yayınlanıyorlar) · B: Girmesin (biri analyzer, biri şablon; public yüzeyleri tüketici API'si değil) | **B** — ikisinin de "API"si `dotnet new` ve tanı kimlikleridir; `AnalyzerReleases.*.md` zaten `Generators`'ın kendi disiplinidir |
| 2 | `UI` paketi (2 üye) takibe girsin mi? | A: Girsin · B: Girmesin | **A** — iki üye ucuzdur ve tutarlılık değerlidir |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

1. **Ölçülen taban çizgisi plandan büyüktü.** Plan 5.052 `RS0016` bekliyordu
   (2026-08-16'da ölçülmüştü); uygulama anında (aynı gün, Faz 58/59 sonrası)
   6.792 çıktı. Neden: aradan iki faz geçmiş, kod büyümüştü. Mekanizma
   (`dotnet format analyzers` toplu düzeltmesi) sayıdan bağımsız çalıştığı için
   plan değişmedi.
2. **`dotnet format analyzers` tek koşumda bitmedi — 13 iterasyon gerekti.**
   Plan bunu "uygulama anında ölçülmeli" diye işaretlemişti (doğrulanmadı).
   Ölçüldü: her koşum yalnız BİR sonraki projenin diagnostiklerini çözüyor,
   çözüm bağımlılık sırasına yakın ilerliyor. Yedek betik gerekmedi.
3. **`Generators`/`Templates`'in dosya eklenmemesi YETMEDİ.** Plan bu ikisini
   "yalnız `PublicAPI.txt` eklenmez" diyerek hariç tutmayı öngörüyordu; ölçüm
   `src/Directory.Build.props`'taki analyzer referansının KOŞULSUZ olduğunu
   ve `Generators`'ın gerçekten `RS0016` ürettiğini gösterdi — bu da toplu
   düzeltmenin `System.NotSupportedException` ile çökmesine sebep oldu (K-424).
   Gerçek bir MSBuild özelliği eklendi.
4. **RS0026'nın çözümü tek strateji değil, dört aileydi.** Plan üç seçenek
   sundu (birleştir/ayrıştır/yeniden adlandır) ama uygulama sırasında RS0027
   ("opsiyonel parametre en uzun aşırı yüklemede olmalı") kuralı ölçüldü ve
   ilk denemeleri (kısa aşırı yüklemede bırakmak) kırdı. Ayrıntı: K-422.
5. **RS0041'in kök nedeni doğrulandı ve tek satırlık bastırma yeterli çıktı**
   — plan "çözülemezse bastırılabilir" diyordu; kök neden (System.Text.Json
   kaynak üreteci) elle düzeltilemeyecek türden çıktı, bastırma uygulandı.
6. **`UseMcp`'nin ikinci aşırı yüklemesi yeniden ADLANDIRILMADI.** Plan
   "muhtemelen (3) uygundur [rename]" diye öngörmüştü; ölçüm gösterdi ki
   yalnız `configure` parametresinin varsayılanını kaldırmak (isim aynı
   kalarak) RS0026/27'yi temizliyor VE K-353'ün "tek çağrıda config+kod"
   gerekçesini bozmuyor — rename gereksiz kapsam büyümesiydi.

## Bu Fazda Verilen Kararlar

- **K-421** — Public API takibi Faz 60'ta, yayından bağımsız açıldı (K-016/K-068'i günceller).
- **K-422** — RS0026'nın 10 kalemi dört stratejiyle çözüldü (birleştir · varsayılan kaldır · sıfır-opsiyonel üçlü bölünme · `private` ctor + `FromClient`).
- **K-423** — RS0041, tek kaynağa (üretilmiş `WebhookEventPayloadJsonContext`) izlenip `AgentPrism.Abstractions.csproj`'da tek satırla bastırıldı.
- **K-424** — `Generators`/`Templates`, yeni `AgentPrismPublicApiTrackingEnabled` MSBuild özelliğiyle takipten hariç tutuldu.

Tam gerekçeler: `docs/KARARLAR.md`, K-421 – K-424.

## Gerçekleşen Public API

Plan "büyümüyor, daralıyor" diyordu; gerçekleşen tam olarak budur — 10
metodun aşırı yükleme kümesi sadeleşti, hiçbir yeni public tip eklenmedi.

| Önce | Sonra |
|---|---|
| `IAgentCatalog.ResolveAsync(string, CancellationToken = default)` + `(string, int?, CancellationToken = default)` | `(string, CancellationToken)` [zorunlu] + `(string, int?, CancellationToken = default)` [değişmedi] |
| `IAgentPrismBuilder.AddTool(AIFunction, bool requiresApproval = false)` | `AddTool(AIFunction, bool requiresApproval)` [zorunlu] |
| `UseVoiceConversation(builder, Action<T>? = null)` + `(builder, IConfiguration, Action<T>? = null)` | `UseVoiceConversation(builder)` + `(builder, Action<T>)` [zorunlu] + `(builder, IConfiguration)` |
| `UseMcp(builder, Action<T>? = null)` + `(builder, IConfiguration, Action<T>? = null)` | `UseMcp(builder)` + `(builder, Action<T>)` [zorunlu] + `(builder, IConfiguration, Action<T>?)` [zorunlu, nullable kaldı] |
| `UseAzureOpenAI(builder, IConfiguration, Action<T>? = null)` | `UseAzureOpenAI(builder, IConfiguration)` — `configure` kaldırıldı |
| `new AnthropicChatClientFactory(IAnthropicClient, string?, int, ILoggerFactory?)` | `private` ctor + `AnthropicChatClientFactory.FromClient(IAnthropicClient, string? = null, int = 4096, ILoggerFactory? = null)` |
| `new AzureOpenAIChatClientFactory(AzureOpenAIClient, string?, ILoggerFactory?)` | `private` ctor + `AzureOpenAIChatClientFactory.FromClient(AzureOpenAIClient, string? = null, ILoggerFactory? = null)` |
| `new GoogleChatClientFactory(Client, string?, ILoggerFactory?)` | `private` ctor + `GoogleChatClientFactory.FromClient(Client, string? = null, ILoggerFactory? = null)` |
| `new OpenAIChatClientFactory(OpenAIClient, string?, ILoggerFactory?)` | `private` ctor + `OpenAIChatClientFactory.FromClient(OpenAIClient, string? = null, ILoggerFactory? = null)` |

Yeni yardımcı MSBuild özelliği (public API değil, geliştirme aparatı):
`AgentPrismPublicApiTrackingEnabled` (`src/Directory.Build.props`).

## Dosya Listesi (gerçekleşen)

```
Directory.Build.props                          (EnablePublicApiTracking=true, NoWarn kalkti, TreatWarningsAsErrors korunuyor)
src/Directory.Build.props                       (AgentPrismPublicApiTrackingEnabled ozelligi eklendi)
src/AgentPrism.*/PublicAPI.Unshipped.txt        (15 paket dolduruldu)
src/AgentPrism.Mcp/PublicAPI.{Shipped,Unshipped}.txt        (yeni)
src/AgentPrism.Workflows/PublicAPI.{Shipped,Unshipped}.txt  (yeni)
src/AgentPrism.Generators/AgentPrism.Generators.csproj      (takip disi)
src/AgentPrism.Templates/AgentPrism.Templates.csproj        (takip disi)
src/AgentPrism.Abstractions/AgentPrism.Abstractions.csproj  (RS0041 bastirma)
src/AgentPrism.Abstractions/Agents/IAgentCatalog.cs
src/AgentPrism.Core/Catalog/CompositeAgentCatalog.cs
src/AgentPrism.Core/IAgentPrismBuilder.cs
src/AgentPrism.Core/AgentPrismBuilder.cs
src/AgentPrism.Core/Voice/VoiceConversationBuilderExtensions.cs
src/AgentPrism.Mcp/AgentPrismMcpBuilderExtensions.cs
src/AgentPrism.Azure/AzureOpenAIProviderExtensions.cs
src/AgentPrism.Azure/AzureOpenAIChatClientFactory.cs
src/AgentPrism.Anthropic/AnthropicChatClientFactory.cs
src/AgentPrism.Google/GoogleChatClientFactory.cs
src/AgentPrism.OpenAI/OpenAIChatClientFactory.cs
src/AgentPrism.OpenAI/OpenAINamedChatClientFactoryCache.cs    (FromClient cagrisina guncellendi)
samples/AgentPrism.Api/Program.cs                             (UseMcp/UseAzureOpenAI cagrilari)
tests/…                                                       (~14 dosya — cagri yeri ve fake guncellemesi)
docs-site/src/content/docs/packages.md                        (API kararlilik bolumu)
docs/manuel-test/01-KURULUM-VE-PAKETLEME.md                   (MT-PKG-090..093)
docs/KARARLAR.md                                               (K-421..K-424)
```

## Testler

Yeni bir davranış eklenmedi (faz "büyümüyor, daralıyor" — bkz. yukarı); bu
yüzden yeni test SINIFI açılmadı. Var olan testler değişen imzalara uyarlandı
ve dört kapı + örnek uygulama + paketlenmiş tüketici koşumu davranışın
korunduğunu doğruladı:

| Kapsam | Sonuç |
|---|---|
| `dotnet build AgentPrism.slnx -c Release` | 0 uyarı, 0 hata |
| `dotnet test` (Core, Anthropic, Azure, Google, OpenAI, Mcp, Workflows unit; AspNetCore functional) | 789 + 41 + 48 + 45 + 82 + 21 + 70 + 480 = 1576 test, tamamı geçti |
| `dotnet pack AgentPrism.slnx -c Release` | 17 paket üretildi |
| `dotnet format --verify-no-changes` | değişiklik yok |
| `samples/AgentPrism.Api` gerçek `dotnet run` | PostgreSQL'e bağlandı, `UseMcp(section, configure: null)` ile MCP discovery tamamlandı, `/agentprism/api/diagnostics` `401` (auth middleware canlı) döndürdü |
| Scratch tüketici (`.nupkg`'dan, `ProjectReference` değil) | `AnthropicChatClientFactory.FromClient(...)` ve `UseMcp()` derlendi ve çalıştı |

## Denetim Bulguları

İki bağımsız, taze bağlamlı denetçi çalıştırıldı (biri izole `worktree`'de, biri
gerçek çalışma ağacına karşı). Her ikisi de dört kapıyı ve DoD'nin her satırını
bağımsızca yeniden koştu/doğruladı.

**🔴 (kapanmadan faz bitmez):** Yok — her iki denetçide de.

**🟡 (aynı fazda kapanır):** Birinci denetçi üç bulgu verdi, hepsi kapatıldı:
1. `docs/KARARLAR.md`'deki K-016/K-068 satırları K-421'in çelişen eski iddiasını
   taşıyordu → her ikisine de "(yeniden açıldı: 2026-08-16, K-421)" notu eklendi.
2. `docs-site/getting-started/tools.md`'deki `AddTool(AIFunctionFactory.Create(...))`
   örneği artık derlenmiyordu (`requiresApproval`'ın varsayılanı kaldırıldığı
   için) → `requiresApproval: false` eklenerek düzeltildi. (İkinci denetçi bu
   dosyayı 🟢 olarak ayrıca doğruladı.)
3. `docs/KARARLAR-INDEKS.md` K-421–424'ü henüz içermiyordu → bulgu zaten
   bayattı; `python3 scripts/dokuman-bakim.py` bu bulgudan önce koşulmuştu.

**🟢 (aday/not, kapatmayı gerektirmez):**
1. `docs-site/packages.md`/`concepts/index.md`/`getting-started/index.md`
   diff'inde Faz 60'ın kapsamı dışında (`UseOpenAICompatible`/self-hosted model
   anlatımı) içerik var. Doğrulandı: bu içerik bu **oturumdan önce**, çalışma
   ağacında commit edilmemiş olarak zaten duruyordu (bu fazın konusu değil,
   dokunulmadı — birinci denetçinin `worktree` izolasyonu bunu ayırt edemedi).
2. İkinci denetçi, bu diff'in dışında, `QuotaUsageObserverTests`'te
   (`AgentPrism.Core.UnitTests`, son değişikliği Faz 57) ara sıra görülen bir
   `ObjectDisposedException` (SemaphoreSlim) yarışını gözlemledi — iki yeniden
   koşumda geçti. Faz 60'ın diff'inde yok, bu fazı bloklamaz; ayrı bir kusur
   olarak `docs/ADAYLAR.md`'ye değil, doğrudan bir sonraki
   `kusur-giderme` oturumuna bırakılır.

Denetim sonrası dört kapı yeniden koşuldu: `dotnet build` 0/0, `dotnet format
--verify-no-changes` exit 0, `dotnet pack` 17 paket.

## Sonraki Faza Devir Notu

🚨 **Faz 7'ye (yayın) devredilecek tek satır:** `PublicAPI.Unshipped.txt` →
`PublicAPI.Shipped.txt` taşıması yayın anında, tek seferlik ve mekanik bir
adımdır — bugün `Shipped.txt` her pakette bilerek boş bırakıldı (K-421).

Diğer notlar:
- `AgentPrismPublicApiTrackingEnabled` özelliği yalnız `Generators`/`Templates`'te
  `false`; yeni bir paket eklenirse varsayılan (`true`) otomatik uygulanır —
  hariç tutma gerekiyorsa bilinçli eklenmeli.
- `UseMcp`'nin `IConfiguration` aşırı yüklemesi artık `configure`'ı zorunlu
  ister (nullable tip korunur, `null` geçilebilir). Yeni bir `Use*` uzantısı
  yazarken bu üçlü deseni (bare / `Action<T>` zorunlu / `IConfiguration`)
  örnek al — RS0026/27'yi baştan önler.
