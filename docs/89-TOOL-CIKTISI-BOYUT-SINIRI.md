# Faz 89 — Tool Çıktısı Boyut Sınırı

> **Durum:** 📋 Planlandı (2026-08-21)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-143** — Dalga 14 Küme Ö
> **Önkoşul:** [Faz 69](69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) — `Timeout` alanı ve sarmalayıcı zinciri; yeni alan onun **kardeşidir** ve aynı yerde yaşar · [Faz 13](arsiv/fazlar/13-BAGLAM-SIKISTIRMA-VE-BELLEK.md) (compaction — **tamamlayıcıdır, rakip değil**)
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.Mcp`
> **Yeni paket:** Yok · **Migration:** Yok — kırpma olayı `RunEventType`'a bir üye ekler ve `run_events.type` bir `smallint`'tir (ölçüldü)
> **Public API:** Büyüyor — 1 tool alanı, 1 kurulum ayarı, 1 olay tipi üyesi, 1 taşınan yardımcı. `PublicAPI.Shipped.txt` toplamı **16 satır** (yalnız başlıklar; ölçüldü 2026-08-21) → Faz 7'den önce eklemek **bedava**
> **Tüketici yüzeyi:** `docs-site/` → `concepts/tools.md`, `guides/context-and-memory.md`, `reference/configuration.md`, `capabilities.md`
> · sevk edilen: yeni alan ve ayarın XML dokümanı, `src/AgentPrism.Core/README.md`. `api/` ve `http-api/` **üretilir**
> **Manuel test alanı:** [`docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md`](manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md) · [`docs/manuel-test/20-BELLEK-RAG-BAGLAM.md`](manuel-test/20-BELLEK-RAG-BAGLAM.md)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-368\|K-014\|K-421" docs/KARARLAR.md
   ```
   **K-368** (onay kararı **yeni bir koşu** olarak sürer — sarmalayıcı sırasının
   gerekçesi budur), **K-014** (`run_events` append-only), **K-421** (public API
   takibi açık).
3. [`69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md`](69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) — yalnız **69.1** ve devir notu:
   ```bash
   awk '/^## Sonraki Faza Devir Notu/,0' docs/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md
   ```
   Sarmalayıcı sırası ve gerekçesi oradadır. Bu faz o zincire **dördüncü** bir
   halka ekler; sırayı bozmadan eklemek fazın birinci işidir.
4. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md) (tool çalıştırma yolu) ·
   [`hafiza/mcp-a2a-sunucu.md`](hafiza/mcp-a2a-sunucu.md) (🚨 MCP tool'ları **ikinci** bir sarmalama zinciri kurar)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI.md`](MIMARI.md) — tool kayıt yolu
6. Faz 88 devir notu — yalnız ilgili bölüm:
   ```bash
   awk '/^## Sonraki Faza Devir Notu/,0' docs/88-GORSEL-URETIM-TOOLU.md
   ```
   `ToolRegistry.Create(...)` images açıksa kodla tanımlı `generate_image`
   tool'unu `ToolEffect.External` ile ekler. Faz 89 bu factory yoluna halka
   eklerken bu koşullu kaydı ve effect bilgisini korumalıdır.

---

## Faz 88'den Devralınan Sözleşmeler

| Sözleşme | Faz 89 etkisi |
|---|---|
| `ToolRegistry.Create(IServiceProvider)` images açıkken `generate_image`ı kayıt listesine ekler | Yeni output sınırı sarmalayıcısı, statik kayıtlarla birlikte bu built-in tool'a da uygulanmalıdır. Ayrı bir kayıt yolu açma. |
| `generate_image` `ToolEffect.External` taşır | Devam koşusunda otomatik tekrar edilmez. Kırpma bu effect veya `SafeToRepeat` kararını değiştiremez. |
| Image attachment yazısı run scope `TenantId`si ile yapılır; URI indirimi bounded ve guard'lıdır | Tool sonucunu kırpmak, attachment kimliği üretildikten sonraki metin yolundadır. Attachment yazma/egress yoluna ikinci bir kopya eklenmez. |
| `IImageGenerator` MEAI001 deneysel yüzeyidir | Faz 89 bunu kullanmaz. `ToolRegistry`teki mevcut dar pragma'yı genişletme. |

🚨 Faz 89 sarmalayıcı zincirini değiştirirken `ToolRegistry`teki conditional
image registration'ı normal `AgentPrismToolRegistration` gibi ele almalıdır.
Kayıt yalnız factory aşamasında eklenir; sarmalayıcı iki ayrı yola bölünürse
Faz 88'in `External`/kayıt/ölçüm sözleşmesi sessizce kayar.

---

## Amaç

Bir tool'un döndürdüğü metin **sınırsızdır** ve doğrudan bağlama girer.
[Faz 69](69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) zamanı kısıtladı, **hacmi
kısıtlamadı**. `CompactionSettings` bağlamı **sonradan** toparlar — ama
toparlanacak token zaten harcanmıştır ve sıkıştırmanın kendisi bir model
çağrısıdır.

Değer doğrudan FinOps'tur: tek bir kaçak tool bir kiracının kotasını yiyebilir
ve bu, unutulduğunda sessizce pahalıya patlayan türden bir kuraldır.

- **F-143** — `AgentPrismToolRegistration` üzerinde bir çıktı boyutu alanı;
  aşımda çıktı kırpılır ve modele **açık bir işaretle** verilir; kırpma olayı
  kaydedilir.

### 🚨 Karşı görüş kabul edilir ve tasarımı şekillendirir

`ADAYLAR.md`'nin karşı görüşü şudur: *"Tool gövdesi çıktısını zaten
kısıtlayabilir ve orada kısıtlamak daha iyidir: tool kendi verisini bilir,
kütüphane yalnız bayt sayar."* **Bu doğrudur.**

Sonuç plana yazılır: bu faz tool yazarının işini **devralmaz**, ona bir **ağ**
gerer. Sevk edilen doküman şunu açıkça söyler — *sınırı tool'un kendi gövdesinde
uygulamak her zaman daha iyidir; bu alan, unutulduğunda faturayı sınırlayan son
savunmadır.* Varsayılan **sınırsızdır** (K1); sınır açıkça konur.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`AgentPrismToolRegistration.cs:63-84`](../src/AgentPrism.Abstractions/Tools/AgentPrismToolRegistration.cs) | **Tam altı** alan: `Function`, `RequiresApproval`, `Source`, `Effect`, `RequiredPermission`, `Timeout`. **Çıktı boyutu yok** |
| [`ToolRegistry.cs:57`](../src/AgentPrism.Core/Tools/ToolRegistry.cs#L57) | Sarmalama sırası: `Authorizing` (dış) → `Timeout` → `ApprovalRequired` (iç) → gerçek fonksiyon |
| [`ToolRegistry.cs:46`](../src/AgentPrism.Core/Tools/ToolRegistry.cs#L46) | Kurulum varsayılanı `AgentPrismOptions.Tools.DefaultTimeout`'tan gelir — yeni ayar **aynı yerden** alınır |
| 🚨 [`McpTenantTools.cs:86`](../src/AgentPrism.Mcp/Internal/McpTenantTools.cs#L86) | **İKİNCİ** bir sarmalama zinciri kurar ve aynı `TimeoutAIFunction`'ı takar |
| [`TimeoutAIFunction.cs`](../src/AgentPrism.Core/Tools/TimeoutAIFunction.cs) | `public sealed class : DelegatingAIFunction`; `InvokeCoreAsync` **`object?`** döner — kırpma bir **dönüş değeri dönüşümüdür**, istisna atmak değil |
| [`ToolInvocationRecord.cs:46`](../src/AgentPrism.Abstractions/Runs/ToolInvocationRecord.cs#L46) | `Result` alanı: *"Raw text; may not be valid JSON"* — çıktının JSON olduğu **varsayılamaz** |
| [`McpResourceTrimming.cs`](../src/AgentPrism.Mcp/Internal/McpResourceTrimming.cs) | 🚨 **Tam bu işi yapan bir fonksiyon zaten var**: UTF-8 bayt sınırı, çok baytlı karakteri asla ortadan kesmiyor, saf ve durumsuz |
| `RunEventType` | 24 üye (`0`…`23`), `run_events.type smallint` → yeni olay tipi migration **istemez** |

> Kanıtlar 2026-08-21 tarihinde yeniden ölçüldü. Aday listesinin altı alan
> iddiası birebir doğrulandı. İki emsal (`McpResourceTrimming` ve MCP'nin ikinci
> sarmalama zinciri) aday listesinde **yazılı değildi** ve tasarımı değiştiriyor.

---

## 89.1 — Birim: UTF-8 bayt, ve kod ZATEN YAZILI

👤 **Karar (2026-08-21): UTF-8 bayt.**

Üç seçenek ölçüldü:

| Birim | Durum |
|---|---|
| **UTF-8 bayt** | ✅ [`McpResourceTrimming.Trim(string, int maxBytes)`](../src/AgentPrism.Mcp/Internal/McpResourceTrimming.cs) **zaten var**: çok baytlı karakteri ortadan kesmez, saf, durumsuz, birim testli. Ayrıca `MaxInstructionsLength` ([`SkillEndpoints.cs:161`](../src/AgentPrism.AspNetCore/Endpoints/SkillEndpoints.cs#L161)) ve `MaxResourceBytesTotal` de bayt tabanlıdır — kurulum ayarlarının **hepsi** bu birimde |
| Token | `Microsoft.ML.Tokenizers` `AgentPrism.Core`'da **zaten referanslı** ([`ContextWindowEstimator.cs:31`](../src/AgentPrism.Core/Models/ContextWindowEstimator.cs#L31)) → paket maliyeti sıfır. İki bedeli var: sayaç yalnız `o200k_base` için **kesin**, diğer model ailelerinde tahmindir; ve her tool çıktısını tokenize etmek sıcak yolda maliyet üretir (mercek 4) |
| Karakter | Emsal yok. Çok baytlı metinde gerçek yükü yarıya kadar yanlış gösterir — Türkçe ve CJK içerikte sınır hiç tutmaz |

### 🚨 `McpResourceTrimming` TAŞINIR, kopyalanmaz

Fonksiyon bugün `AgentPrism.Mcp` içinde `internal`'dır. `AgentPrism.Core`'a
taşınır ve MCP onu oradan çağırır.

**Kopyalanması yasaktır.** Bu repo'da "senkronizasyon kopyası" **beş kez**
yaşandı; iki kopya kaçınılmaz olarak ayrışır ve ayrışan kopya sessizce yanlış
kırpar.

Taşıma bir bağımlılık yönü sorusu açar: `AgentPrism.Mcp` zaten `Core`'a
bağlıdır (ölçülmeli — `DependencyDirectionTests` bunu zorlar), yani yön
doğrudur.

## 89.2 — Alan `Timeout`'un kardeşidir

Yeni alan `AgentPrismToolRegistration` üzerinde, `Timeout`'un yanında yaşar ve
kurulum varsayılanını **aynı yerden** alır (`AgentPrismOptions.Tools`).

```csharp
public AgentPrismToolRegistration(
    AIFunctionDeclaration function,
    bool requiresApproval = false,
    string? source = null,
    ToolEffect effect = ToolEffect.Read,
    string? requiredPermission = null,
    TimeSpan? timeout = null,
    int? maxOutputBytes = null)   // YENI
```

🚨 **Bu bir ikili-kırıcı değişikliktir.** `AgentPrismToolRegistration` isteğe
bağlı parametreli bir **kurucu** taşır, `init` property değil; yeni bir
parametre kaynak-uyumludur ama ikili imzayı değiştirir. `Shipped.txt` boş
olduğu için bugün **bedavadır**; Faz 7'den sonra bir sürüm kararıdır.

**Varsayılan `null` = sınırsızdır** (K1). Kurulum ayarı da varsayılan olarak
sınırsızdır; bir yükseltme hiçbir tool'un çıktısını sessizce kesmez.

## 89.3 — Sarmalayıcı zincire dördüncü halka

Bugünkü sıra ([`ToolRegistry.cs:57`](../src/AgentPrism.Core/Tools/ToolRegistry.cs#L57)):

```
Authorizing (dis) -> Timeout -> ApprovalRequired (ic) -> gercek fonksiyon
```

Kırpma **gerçek fonksiyonun hemen dışında** durur:

```
Authorizing -> Timeout -> ApprovalRequired -> Truncating -> gercek fonksiyon
```

Gerekçe: kırpma yalnız **gerçek tool çıktısını** görmelidir. Daha dışta
dursaydı `ApprovalRequiredAIFunction`'ın ürettiği bekleyen-onay işaretini de
görürdü — o bir tool çıktısı değildir ve kırpılması anlamsızdır.

🚨 **Zincir İKİ yerde kuruluyor.** [`ToolRegistry.cs:71`](../src/AgentPrism.Core/Tools/ToolRegistry.cs#L71)
ve [`McpTenantTools.cs:86`](../src/AgentPrism.Mcp/Internal/McpTenantTools.cs#L86)
aynı sarmalamayı ayrı ayrı yapar. Yeni halka **ikisine birden** eklenmezse MCP
tool'ları sınırsız kalır ve boşluk sessizce açık kalır. Bu, planın izlediği
"imza değiştirmek ile gövdeyi kullanmak iki ayrı adımdır" kuralının tam
örneğidir.

## 89.4 — Zarf: bozuk JSON modele hiç ulaşmaz

👤 **Karar (2026-08-21):** kırpma olunca çıktı bir **zarfa** sarılır.

Aday listesinin karşı görüşü bunu en büyük risk olarak işaret ediyordu: *"Bayt
bazlı kırpma bir JSON çıktısını ortasından kesebilir; o hâlde model bozuk JSON
okur ve 'açık işaret' bunu kurtarmayabilir."*

Zarf bu riski **yapısal olarak** kapatır: kırpılmış metin artık bir JSON
**string değeridir**, dolayısıyla içeriği ne olursa olsun üst seviyeyi bozamaz.

```json
{
  "truncated": true,
  "omittedBytes": 128400,
  "content": "...kirpilmis metin..."
}
```

**Zarf yalnız kırpma olduğunda uygulanır.** Kırpılmayan çıktı **hiç
dokunulmadan** geçer — sınır konmamış tool'lar için davranış bugünküyle
birebir aynıdır.

🚨 **Zarfın kendi boyutu bütçeye dâhildir.** Sınır N bayt ise içerik
`N - (zarf yükü)` bayta kırpılır; aksi hâlde sınır konan tool sınırı **aşan**
bir çıktı üretir ve alan kendi vaadini bozar.

🚨 **Sessiz kırpma yasaktır.** Kalemin en kolay yanlış yapılan yeri budur:
`truncated` bayrağı ve `omittedBytes` **her zaman** taşınır. Model kesildiğini
bilmezse yanlış sonuç üretir ve bunu bir kesinlikle söyler.

## 89.5 — Kırpma olayı kaydedilir

Ölçme–iyileştirme tarafı budur: **sürekli kırpılan bir tool, yanlış tasarlanmış
bir tool'dur.** Operatör hangi tool'un sürekli kırpıldığını görebilmelidir.

`RunEventType` bugün 24 üye taşır ve `run_events.type` bir `smallint`'tir →
yeni üye **migration istemez**.

Olay tool'un **adını**, sınırı ve atlanan bayt sayısını taşır. İçeriğin
atılan kısmını **taşımaz** — kırpmanın amacı hacmi düşürmektir; atılanı kayda
yazmak o amacı `run_events` tarafında geri alır.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions
public sealed class AgentPrismToolRegistration
{
    // ... bugunku alti alan ...

    /// <summary>
    /// The most bytes (UTF-8) this tool's result may carry, or
    /// <see langword="null"/> to use the installation default. A result over
    /// the limit is trimmed and handed to the model inside an envelope that
    /// states how many bytes were dropped.
    /// </summary>
    /// <remarks>
    /// Bounding the output inside the tool's own body is always better: the
    /// tool knows its data, this only counts bytes. This limit is the last
    /// defence for the day that bound is forgotten.
    /// </remarks>
    public int? MaxOutputBytes { get; }
}

public enum RunEventType
{
    // ... bugunku 24 uye (0..23) ...

    /// <summary>A tool result was trimmed to its byte limit.</summary>
    ToolOutputTruncated = 24,
}
```

```csharp
// AgentPrism.Core — AgentPrismOptions.Tools altinda
public sealed class AgentPrismToolOptions
{
    // ... DefaultTimeout ...

    /// <summary>
    /// The default byte limit for a tool result. <see langword="null"/> means
    /// unlimited, which is the default: an upgrade never silently cuts output.
    /// </summary>
    public int? DefaultMaxOutputBytes { get; set; }
}

// AgentPrism.Mcp'den TASINIR — kopyalanmaz
public static class TextTrimming
{
    /// <summary>Trims text to a UTF-8 byte limit without cutting a character in half.</summary>
    public static (string Text, bool Truncated) Trim(string text, int maxBytes);
}

public sealed class TruncatingAIFunction : DelegatingAIFunction { }
```

### HTTP `endpoint`'leri

Yeni uç **yoktur**. `GET /api/runs/{id}/events` yeni olay tipini taşır.

### Arayüz payı

Transcript'te kırpma olayı için küçük bir işaret. 🚨 Bundle payı
**ölçülmelidir**; bütçe **250 KB gzip**. Yeni metin `en.ts` **ve** `tr.ts`'e
girer (K-228).

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/
├── Tools/AgentPrismToolRegistration.cs   (alan + kurucu parametresi)
└── Runs/RunEventType.cs                  (uye eklenir)

src/AgentPrism.Core/
├── Tools/TruncatingAIFunction.cs         (yeni — TimeoutAIFunction emsali)
├── Tools/TextTrimming.cs                 (Mcp'den TASINIR)
├── Tools/ToolRegistry.cs                 (zincire dorduncu halka)
└── AgentPrismOptions.cs                  (DefaultMaxOutputBytes)

src/AgentPrism.Mcp/
├── Internal/McpResourceTrimming.cs       (SILINIR — cagri Core'a doner)
├── Internal/McpResourceContextProvider.cs (cagri guncellenir)
├── McpResourceClient.cs                  (cagri guncellenir)
└── Internal/McpTenantTools.cs            (🚨 IKINCI zincire de halka)

src/AgentPrism.UI/frontend/src/
├── (transcript kirpma isareti)
└── locales/{en,tr}.ts
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| 🚨 MCP tool'ları sınırsız kalır (ikinci zincire halka eklenmemiş) | Sözleşme | `ToolWrapperChainContract` — **her iki** zincir aynı halkaları taşımalı |
| 🚨 Sessiz kırpma: `truncated` bayrağı yok | Birim | `TruncatingAIFunctionTests` |
| Zarf yükü bütçeye katılmamış; çıktı sınırı **aşıyor** | Birim | `TruncatingAIFunctionTests` |
| Çok baytlı karakter ortadan kesilir | Birim | `TextTrimmingTests` — taşınan fonksiyonun var olan testi korunur |
| Kırpılmayan çıktı da zarfa sarılır (davranış değişir) | Birim | `TruncatingAIFunctionTests` |
| Kırpılmış içerikteki tırnak/ters bölü zarfı bozar | Birim | `TruncatingAIFunctionTests` — içerik JSON string olarak **kaçırılmalı** |
| Sınır `0` veya negatif | Birim | `TruncatingAIFunctionTests` |
| Varsayılan sınırsız değil; yükseltme çıktıyı sessizce keser | Sözleşme | `ToolDefaultsContract` — 🚨 K1 ihlali en pahalı hatadır |
| Tool `null` veya boş döner | Birim | `TruncatingAIFunctionTests` |
| Tool bir `string` değil, bir **nesne** döner | Birim | `TruncatingAIFunctionTests` — `InvokeCoreAsync` `object?` döner (Açık Soru 1) |
| Kırpma olayı yazılmaz | Fonksiyonel | `ToolTruncationRecordingTests` |
| Kırpma olayı `store` hatası verirse **koşu durur** | Fonksiyonel | `ToolTruncationRecordingTests` — gözlemlenebilirlik işlevselliği bozmaz |
| Başka kiracının kırpma olayı görünür | Sözleşme | `TenantIsolationContract` |
| İki eşzamanlı tool çağrısında sayaçlar karışır | Fonksiyonel | `ToolTruncationConcurrencyTests` |
| İptal edilen çağrıda kırpma çalışır | Birim | `TruncatingAIFunctionTests` |
| Kırpma sıcak yolda tahsis üretir | Fonksiyonel | 🚨 sınır **yokken** hiçbir ek iş yapılmamalı — ölçülmeli |
| MCP kaynak kırpması davranışı değişir | Sözleşme | var olan MCP testleri korunur |

Beş soru ve cevapları:

| Soru | Cevap |
|---|---|
| **İptal** | Kırpma sarmalayıcı gerçek fonksiyonun dönüşünü işler; iptal edilen çağrıda hiç çalışmaz |
| **Eşzamanlılık** | Sarmalayıcı **durumsuzdur**; sayaç tutmaz. `TextTrimming` saf bir fonksiyondur |
| **Boş/aşırı girdi** | `null`/boş çıktı dokunulmadan geçer; sınır `0` ise kırpma sınırdan **büyük** her çıktıyı boşaltır — bu bir yapılandırma hatasıdır ve doğrulayıcı başlangıçta bildirir |
| **Başka kiracı** | Kırpma olayı koşuya bağlıdır; kiracı yalıtımı koşudan gelir |
| **Alt sistem hatası** | Olay yazılamazsa **kırpma yine yapılır** ve hata loglanır; koşu devam eder |

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Sınır **konmamış** (varsayılan) | Büyük çıktı veren bir tool çağrılır | Çıktı **dokunulmadan** modele gider — bugünkü davranış |
| 2 | Tool'a `maxOutputBytes: 1024` konur | Aynı tool çağrılır | Çıktı zarfa sarılır; `truncated: true`, `omittedBytes` dolu |
| 3 | Aynı senaryo | Modele giden metin ölçülür | Toplam boyut **1024 baytı aşmaz** (zarf dâhil) |
| 4 | Tool geçerli bir JSON döndürür | Aynı senaryo | Modele giden metin **geçerli JSON**'dur |
| 5 | Tool içinde tırnak ve ters bölü olan metin döndürür | Aynı senaryo | Zarf bozulmaz; içerik kaçırılmış görünür |
| 6 | Çok baytlı (Türkçe/CJK) çıktı, sınır bir karakterin ortasına denk gelir | Aynı senaryo | Karakter **ortadan kesilmez**; tam karakter atılır |
| 7 | Sınır konmuş bir tool | `run_events` okunur | `ToolOutputTruncated` olayı tool adını ve atlanan baytı taşır |
| 8 | 🚨 **MCP** tool'una sınır konur | MCP tool'u büyük çıktı döndürür | Kırpma **çalışır** — ikinci sarmalama zinciri de halkayı taşır |
| 9 | Kurulum ayarı konur, tool alanı boş | Tool çağrılır | Kurulum varsayılanı uygulanır |
| 10 | Tool alanı konur, kurulum ayarı da var | Tool çağrılır | **Tool alanı kazanır** (`Timeout` emsali) |
| 11 | 👤 insan gerekir | Konsolda transcript açılır | Kırpma işareti görünür; hangi tool ve kaç bayt okunabilir |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | 🚨 `InvokeCoreAsync` **`object?`** döner. Boyut nerede ölçülür? | A: sarmalayıcı nesneyi metne çevirip ölçer · B: yalnız `string` dönen çıktılar kırpılır, diğerleri geçer | **Önce ölç:** A çift serileştirme üretebilir (bir kez bizim için, bir kez aşağı akışta) ve bu sıcak yol maliyetidir. B daha ucuz ama bir `object` döndüren kaçak tool'u kapsamaz — yani alanın vaadini yarım bırakır. Ölçüm `FunctionInvokingChatClient`'ın sonucu **nerede** metne çevirdiğini okumakla başlar |
| 2 | Zarf alan adları ne olsun? | A: `truncated` · `omittedBytes` · `content` · B: `_agentprism_truncated` … | **A** — çakışma riski gerçek ama düşük; önek modelin okuduğu metni gürültülendirir |
| 3 | Taşınan fonksiyonun adı ne olsun? | A: `TextTrimming` · B: `McpResourceTrimming` adı korunur | **A** — artık MCP'ye özgü değildir; eski ad yanlış yer işaret eder |
| 4 | `TruncatingAIFunction` public mi `internal` mi? | A: `public` (`TimeoutAIFunction` emsali) · B: `internal` | **A** — emsal public'tir ve tüketici zinciri kendi kurabilir. Ama `TimeoutAIFunction`'ın public olması bir **karardı**, bir varsayılan değil; gerekçesi okunmalı |
| 5 | Sınır aşımı bir **kota** olayı da üretsin mi? | A: hayır, yalnız `run_events` · B: kota sayacına da yazılsın | **A** — kırpma harcamayı **azaltır**; kota olayı üretmek anlamı tersine çevirir |
| 6 | `DefaultMaxOutputBytes` için önerilen bir değer dokümanda verilsin mi? | A: evet, ölçülmüş bir örnek · B: hayır | **A** — ama sayı **ölçülmeden yazılmaz**. Ölçüm: tipik bir tool çıktısının bayt dağılımı örnek uygulamada okunur |

---

## Bitiş Ölçütleri (DoD)

- [ ] Sınır **konmadığında** çıktı dokunulmadan geçer (K1; sözleşme testi sabitler)
- [ ] `maxOutputBytes` konduğunda çıktı zarfa sarılır ve `truncated` + `omittedBytes` **her zaman** taşınır
- [ ] Modele giden toplam boyut sınırı **aşmaz** — zarf yükü bütçeye dâhil
- [ ] Kırpılan çıktı **geçerli JSON** olarak modele ulaşır (içerik ne olursa olsun)
- [ ] Çok baytlı karakter ortadan kesilmez
- [ ] 🚨 **MCP** tool'ları da kırpılır — her iki sarmalama zinciri aynı halkaları taşır (`ToolWrapperChainContract`)
- [ ] `McpResourceTrimming` **taşındı**, kopyalanmadı; MCP çağrıları `Core`'a döndü ve var olan MCP testleri geçiyor
- [ ] `ToolOutputTruncated` olayı `run_events`'e yazılır; migration **gerekmedi**
- [ ] Tool alanı kurulum varsayılanını ezer (`Timeout` emsali)
- [ ] Sınır yokken sıcak yolda ek tahsis **yok** (ölçüldü)
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/12-*` ve `20-*` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi — *"sınırı tool gövdesinde uygulamak daha iyidir"* cümlesi yazıldı
- [ ] Arayüze dokunulduysa `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı

### Doğrulama komutları

```bash
# Kirpilan cikti gecerli JSON mu
curl -s http://localhost:5081/agentprism/api/runs/$RUN_ID/tools \
  | jq -r '.[] | select(.toolName=="buyuk_cikti") | .result' | jq .

# Kirpma olayi yazildi mi
curl -s http://localhost:5081/agentprism/api/runs/$RUN_ID/events \
  | jq '.[] | select(.type=="ToolOutputTruncated")'

# Iki sarmalama zinciri de halkayi tasiyor mu
grep -n "TruncatingAIFunction" src/AgentPrism.Core/Tools/ToolRegistry.cs \
                               src/AgentPrism.Mcp/Internal/McpTenantTools.cs
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 MCP zinciri unutulur ve boşluk sessizce açık kalır | `ToolWrapperChainContract` **her iki** zinciri karşılaştırır. Doğrulama komutu grep ile de bakar |
| 🚨 Sessiz kırpma model­i yanlış sonuca götürür | `truncated` + `omittedBytes` her zaman taşınır; birim testi bunu sabitler |
| Bayt kırpma JSON'u ortadan keser | Zarf yapısal olarak kapatır: kırpılmış metin bir JSON **string değeridir** |
| Zarf yükü sınırı aşırır | İçerik `N - zarf yükü` bayta kırpılır; birim testi toplam boyutu ölçer |
| `McpResourceTrimming` kopyalanır ve ayrışır | **Taşınır.** Bu repo'da senkronizasyon kopyası beş kez yaşandı; kopya yasağı DoD'ye bağlandı |
| K1 ihlali: yükseltme çıktıyı sessizce keser | Varsayılan **sınırsız**; sözleşme testi bunu sabitler |
| Sıcak yolda tahsis (mercek 4) | Sınır yokken sarmalayıcı **hiç takılmaz** — `Timeout` için de aynı desen sorgulanmalı |
| `object?` dönüşü metne çevirmek çift serileştirme üretir | Açık Soru 1 — uygulayan oturum **önce ölçer** |
| Alan tool yazarının işini devralır gibi görünür | Sevk edilen metin sınırı açıkça yazar: gövdede kısıtlamak her zaman daha iyidir; bu alan son savunmadır |

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
