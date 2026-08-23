# Faz 89 — Tool Çıktısı Boyut Sınırı

> **Durum:** ✅ Tamamlandı (2026-08-23)
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
| [`McpResourceTrimming.cs`](../src/AgentPrism.Core/Tools/TextTrimming.cs) (plandan sonra `TextTrimming.cs`'e taşındı) | 🚨 **Tam bu işi yapan bir fonksiyon zaten var**: UTF-8 bayt sınırı, çok baytlı karakteri asla ortadan kesmiyor, saf ve durumsuz |
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
| **UTF-8 bayt** | ✅ `McpResourceTrimming.Trim(string, int maxBytes)` (plandan sonra [`TextTrimming.Trim`](../src/AgentPrism.Core/Tools/TextTrimming.cs)'e taşındı) **zaten var**: çok baytlı karakteri ortadan kesmez, saf, durumsuz, birim testli. Ayrıca `MaxInstructionsLength` ([`SkillEndpoints.cs:161`](../src/AgentPrism.AspNetCore/Endpoints/SkillEndpoints.cs#L161)) ve `MaxResourceBytesTotal` de bayt tabanlıdır — kurulum ayarlarının **hepsi** bu birimde |
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

- [x] Sınır **konmadığında** çıktı dokunulmadan geçer (K1; sözleşme testi sabitler) — `No_output_limit_anywhere_means_no_truncating_layer_is_installed` (Core + Mcp), gerçek koşuda doğrulandı
- [x] `maxOutputBytes` konduğunda çıktı zarfa sarılır ve `truncated` + `omittedBytes` **her zaman** taşınır — `TruncatingAIFunctionTests`, gerçek koşuda doğrulandı
- [x] Modele giden toplam boyut sınırı **aşmaz** — zarf yükü bütçeye dâhil (`MinimumEnvelopeBytes` kuruculta garanti eder; `Even_the_smallest_accepted_limit_never_produces_an_oversized_envelope`)
- [x] Kırpılan çıktı **geçerli JSON** olarak modele ulaşır (içerik ne olursa olsun) — tırnak/ters bölü ağırlıklı içerik testiyle sabitlendi
- [x] Çok baytlı karakter ortadan kesilmez — CJK testiyle sabitlendi (Türkçe literal `SourceLanguageTests`'i kırdığı için CJK'ye çevrildi)
- [x] 🚨 **MCP** tool'ları da kırpılır — her iki sarmalama zinciri aynı halkaları taşır — `McpTenantToolsTests` üç yeni test
- [x] `McpResourceTrimming` **taşındı**, kopyalanmadı; MCP çağrıları `Core`'a döndü ve var olan MCP testleri geçiyor (30/30)
- [x] `ToolOutputTruncated` olayı `run_events`'e yazılır; migration **gerekmedi** (`smallint`, değer 26)
- [x] Tool alanı kurulum varsayılanını ezer (`Timeout` emsali) — `A_registration_level_output_limit_overrides_the_installation_default`
- [x] Sınır yokken sıcak yolda ek tahsis **yok** — sarmalayıcı hiç kurulmaz (K1 testi `GetService<TruncatingAIFunction>()` `null` döner)
- [x] Dört doğrulama kapısı sıfır uyarı verir — build/test/pack/format, tam koşum + frontend dahil
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı (aşağıda)
- [x] `secret` taraması boş döndü (bu fazın dokunduğu dosyalarda; repodaki önceden var olan `Password=`/`sk-` test literalleri kapsam dışı — Faz 79/80/81/87 emsaliyle aynı)
- [x] Manuel kabul case'leri `docs/manuel-test/12-*` ve `18-*` içine eklendi (MT-OBS-048/049, MT-MCP-059); otomatikleştirilebilenler (048/049) gerçek koşuda doğrulandı, 059 👤 insan gerekir işaretiyle
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (üç 🔴 bulundu ve kapandı, ayrıntı aşağıda)
- [x] `docs-site/` güncellendi — *"gövdede kısıtlamak her zaman daha iyidir"* cümlesi `concepts/tools.md`'ye yazıldı
- [x] Arayüze dokunulduysa `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — rozet etiketi literal string'dir (emsal: `ContentMasked`/`ModelFallbackUsed`), yeni çeviri anahtarı gerekmedi; bundle 175,1 KB / 250 KB bütçe

### Doğrulama komutları — gerçek koşum (2026-08-23)

`samples/AgentPrism.Api`, gerçek bir OpenAI anahtarıyla, `AgentPrism__Tools__DefaultMaxOutputBytes=100`
ile başlatıldı. `support` agent'ına `get_order_status`'un doğal çıktısını
100 baytın üstüne çıkaracak uzunlukta bir sipariş numarasıyla soru soruldu.

```bash
# Kirpilan cikti gecerli JSON mu ve zarf 100 baytin altinda mi
curl -s -X POST http://localhost:5081/agentprism/api/agents/support/run \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"message":"What is the status of order ORD-0000000000000000000000000000000000000000000000000000-LONG?"}'
# -> functionResult.result =
#    {"truncated":true,"omittedBytes":57,"content":"Order ORD-00000000000000000000000000000000000000000"}
#    (tam 100 UTF-8 bayt — python3 -c "print(len(s.encode()))" ile ölçüldü)

# Kirpma olayi yazildi mi
curl -s http://localhost:5081/agentprism/api/runs/$RUN_ID/events -H "Authorization: Bearer $TOKEN" \
  | jq '.[] | select(.type=="ToolOutputTruncated")'
# -> {"type":"ToolOutputTruncated","text":"57 byte(s) omitted (limit 100)",
#     "toolName":"get_order_status","toolCallId":"call_6jX9qDFfy53VvPyfJqWHn1B4",
#     "payload":"{\"maxOutputBytes\":100,\"omittedBytes\":57}"}
# -> sequence 2, ToolInvoking (1) ile ToolInvoked (3) arasinda

# Sinir konmadan (K1) ayni tool cagrisi dokunulmadan geciyor mu
# -> ilk kosuda (sinir yok) functionResult.result ham metindi:
#    "Order ORD-1 has shipped. Estimated delivery: 2 days." (52 bayt, zarf yok)

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

1. **Ölçüm birimi Açık Soru 1'de planlanandan farklı çıktı** — plan `result?.ToString()`
   öneriyordu (ToolInvocationTracker emsali). Bağımsız denetim MEAI'nin
   gerçek tel serileştirmesinin bunu KULLANMADIĞINI kanıtladı; gerçekleşen
   kural yalnız `string` ve `JsonElement`'i ölçer, başka bir ham CLR nesnesi
   dokunulmadan geçer. Ayrıntı: K-594.
2. **`MinimumEnvelopeBytes` planda YOKTU** — bağımsız denetim, `budget <= 0`
   erken çıkışının zarfın gerçekten sığdığını hiç kontrol etmediğini
   (`maxOutputBytes=10` iken zarf 51 bayt) reflection ile kanıtladı. Kurucuya
   57 baytlık bir taban eklendi (K-595); bu, `TruncatingAIFunction`'a yeni bir
   `public static readonly int` alan ekledi — planın "Planlanan Public API"
   bölümünde yoktu, gerekçesi K-595'tedir.
3. **Manuel test dosyası yönlendirmesi değişti** — plan `docs/manuel-test/20-BELLEK-RAG-BAGLAM.md`'yi
   işaret ediyordu; MCP tool kırpması için daha iyi bir alan eşleşmesi olan
   `18-MCP-VE-A2A.md` (`MCP` alan kodu) kullanıldı. `12-*` (OBS) ve `18-*`
   (MCP) case'leri eklendi, `20-*`'ye dokunulmadı.
4. **Arayüz `ui.md` güncellenmedi** — `dokuman-bakim.py --site-denetle` bunu
   `arayuz` kuralı altında tetikledi. `run-detail.tsx`'e eklenen tek şey bir
   rozet etiketidir (`EVENT_STYLE` sabiti); `ui.md` hâlihazırda `ContentMasked`/
   `ModelFallbackUsed` gibi kardeş olay tiplerinin hiçbirini rozet düzeyinde
   belgelemiyor — tutarlılık için aynı kapsam dışı bırakıldı.
   `--site-gerekce-yazildi` ile geçildi.
5. **`[AgentPrismTool]`/`AddTool`'a `MaxOutputBytes` eklenmedi** — `SafeToRepeat`
   (Faz 87) emsaliyle aynı, kasıtlı boşluk: bugün yalnız `AgentPrismToolRegistration`'ın
   DI kurucusundan set edilebilir. Sonraki faza devir notuna yazıldı.

## Bu Fazda Verilen Kararlar

- **K-592** — Tool çıktısı boyut sınırı UTF-8 bayt biriminde ölçülür.
- **K-593** — Zarf `{"truncated","omittedBytes","content"}` alanlarını taşır;
  `Utf8JsonWriter` + `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` ile üretilir.
- **K-594** — Yalnız `string`/`JsonElement` ölçülür; başka ham CLR nesnesi
  dokunulmadan geçer (denetim bulgusu, Açık Soru 1 kapatıldı).
- **K-595** — `maxOutputBytes`, `MinimumEnvelopeBytes`'ın altındaysa kurucu
  `ArgumentOutOfRangeException` atar (denetim bulgusu).
- **K-596** — `McpResourceTrimming` `Core`'a `TextTrimming` adıyla taşındı,
  `public` yapıldı.

Tam gerekçeler: `docs/KARARLAR.md` K-592–K-596.

## Gerçekleşen Public API

```csharp
// AgentPrism.Abstractions
public sealed class AgentPrismToolRegistration
{
    public AgentPrismToolRegistration(
        AIFunctionDeclaration function,
        bool requiresApproval = false,
        string? source = null,
        ToolEffect effect = ToolEffect.Read,
        string? requiredPermission = null,
        TimeSpan? timeout = null,
        bool safeToRepeat = false,
        int? maxOutputBytes = null);   // YENİ, sekizinci parametre

    public int? MaxOutputBytes { get; }   // YENİ
}

public sealed record ToolDescriptor
{
    // ... mevcut alanlar ...
    public int? MaxOutputBytes { get; init; }   // YENİ
}

public enum RunEventType
{
    // ... mevcut 26 üye (0..25) ...
    ToolOutputTruncated = 26,   // YENİ
}
```

```csharp
// AgentPrism.Core
public sealed class AgentPrismToolOptions
{
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int? DefaultMaxOutputBytes { get; set; }   // YENİ
}

// Mcp'den TAŞINDI (kopyalanmadı), public yapıldı
public static class TextTrimming
{
    public static (string Text, bool Truncated) Trim(string text, int maxBytes);
}

// YENİ sınıf — planlanandan bir alan fazla: MinimumEnvelopeBytes
public sealed class TruncatingAIFunction : DelegatingAIFunction
{
    public static readonly int MinimumEnvelopeBytes;   // plan dışı, K-595

    public TruncatingAIFunction(AIFunction innerFunction, int maxOutputBytes);
}
```

HTTP yüzeyi değişmedi (`GET /api/tools` `maxOutputBytes` alanını, `GET
/api/runs/{id}/events` `ToolOutputTruncated` tipini taşır — ikisi de mevcut
uçlar, `docs/openapi/agentprism.json` otomatik güncellendi).

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/
├── Tools/AgentPrismToolRegistration.cs   (alan + kurucu parametresi)
├── Tools/ToolDescriptor.cs               (alan)
└── Runs/RunEventType.cs                  (üye)

src/AgentPrism.Core/
├── Tools/TruncatingAIFunction.cs         (yeni)
├── Tools/TextTrimming.cs                 (Mcp'den taşındı)
├── Tools/ToolRegistry.cs                 (dördüncü halka + descriptor alanı)
├── AgentPrismOptions.cs                  (DefaultMaxOutputBytes)
├── AgentPrismOptionsValidator.cs         (taban doğrulaması)
└── AgentPrismServiceCollectionExtensions.cs (BindTools)

src/AgentPrism.Mcp/
├── Internal/McpResourceTrimming.cs       (SİLİNDİ)
├── Internal/McpTenantTools.cs            (dördüncü halka + descriptor alanı)
├── Internal/McpToolCatalog.cs            (defaultMaxOutputBytes iletimi)
├── Internal/McpConnection.cs             (çağrı TextTrimming'e güncellendi)
└── McpResourceClient.cs                  (çağrı TextTrimming'e güncellendi)

src/AgentPrism.UI/frontend/src/
├── lib/run-event.ts                      (union üyesi)
└── screens/run-detail.tsx                (EVENT_STYLE girdisi)

tests/AgentPrism.Core.UnitTests/
├── Tools/TruncatingAIFunctionTests.cs        (yeni, 18 test)
├── Tools/TextTrimmingTests.cs                (Mcp'den taşındı)
├── Tools/ToolRegistryWrapperOrderTests.cs    (5 yeni test)
├── Tools/AgentPrismToolOptionsValidationTests.cs (yeni)
└── Configuration/AgentPrismOptionsBindingCoverageTests.cs (istisna + özel test)

tests/AgentPrism.Mcp.UnitTests/
├── McpResourceTrimmingTests.cs           (SİLİNDİ)
└── McpTenantToolsTests.cs                (3 yeni test)

docs-site/src/content/docs/
├── concepts/tools.md                     (Output size limit bölümü)
├── guides/context-and-memory.md          (tamamlayıcılık notu)
├── reference/configuration.md            (Tools:DefaultMaxOutputBytes satırı)
└── capabilities.md                       (Tools tablosuna satır)

docs/manuel-test/
├── 12-GOZLEMLENEBILIRLIK-MALIYET.md      (MT-OBS-048, MT-OBS-049)
├── 18-MCP-VE-A2A.md                      (MT-MCP-059)
└── 00-INDEKS.md                          (sayaç güncellemesi)

docs/KARARLAR.md                          (K-592..K-596)
```

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı agent, `.agents/skills/faz-denetim`) çalışma
ağacına karşı koştu ve dördü ölçülmüş, biri gerçek reflection çağrısıyla
kanıtlanmış üç 🔴 ve üç 🟡 bulgu üretti:

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | Ölçüm `result.ToString()` kullanıyordu; gerçek tel serileştirmesi bunu kullanmıyor (POCO için tip adı döner) | 🔴 | **Düzeltildi** — yalnız `string`/`JsonElement` ölçülür (K-594); `A_raw_non_string_non_JsonElement_result_always_passes_through_untouched` ve `A_JsonElement_result_over_the_limit_is_truncated_via_its_raw_text` testleriyle sabitlendi |
| 2 | `budget <= 0` erken çıkışı zarfın gerçekten sığdığını kontrol etmiyordu; `maxOutputBytes=10` iken zarf 51 bayt üretiyordu | 🔴 | **Düzeltildi** — `MinimumEnvelopeBytes` kurucuda garanti eder (K-595); `Constructor_throws_for_a_positive_limit_too_small_to_ever_hold_an_envelope`, `Even_the_smallest_accepted_limit_never_produces_an_oversized_envelope` |
| 3 | `dotnet test` kırmızıydı — test dosyasındaki Türkçe literal (`çığöşü`) `SourceLanguageTests` taban çizgisini kırıyordu | 🔴 | **Düzeltildi** — CJK örneğe (`你好世界`) çevrildi |
| 4 | Fonksiyonel `ToolTruncationRecordingTests` ("store hatası koşuyu durdurmaz") planlanmış ama yazılmamıştı | 🟡 | **Düzeltildi** — `A_store_failure_while_recording_the_truncation_event_does_not_change_the_returned_result` eklendi |
| 5 | `AgentPrismOptionsValidator`'ın yeni `DefaultMaxOutputBytes` dalı testsizdi | 🟡 | **Düzeltildi** — `AgentPrismToolOptionsValidationTests` eklendi |
| 6 | `docs-site/` bu diff'te hiç dokunulmamıştı | 🟡 | **Düzeltildi** — `tuketici-dokuman-senkronu` uygulandı (dört yüzey güncellendi, dört kapı yeşil) |

🔴 bulgular kapandıktan sonra dört doğrulama kapısı **yeniden koşuldu** ve
yeşil çıktı (tam koşum, frontend dahil).

## Sonraki Faza Devir Notu

- **Devralınan sözleşme — sarmalayıcı zinciri artık DÖRT halka taşır:**
  `Authorizing → Timeout → ApprovalRequired → Truncating → gerçek fonksiyon`,
  hem `ToolRegistry.cs` hem `McpTenantTools.cs`'te. Beşinci bir halka
  eklenirse **her iki** yere elle taşınmalı (K-487'nin devamı).
- 🚨 **`TruncatingAIFunction` yalnız `string`/`JsonElement` sonuçları
  kırpar.** Kod üreticisinin ham CLR nesnesi döndüren bir tool'u (POCO/record)
  bu sınırı hiç görmez — alanın belgelenmiş sınırıdır (K-594), kusur değil.
  Bu kapsamı genişletmek reflection gerektirir ve `AgentPrism.Core`'un AOT
  sözleşmesini bozar; önce tip-güvenli bir serileştirme yolu (kaynak üretilen
  `JsonSerializerContext`) gerekir.
- 🚨 **`TruncatingAIFunction.MinimumEnvelopeBytes` (bugün 57 bayt) bir
  taban, öneri değildir.** Hem `AgentPrismToolRegistration.MaxOutputBytes`
  hem `AgentPrismOptions.Tools.DefaultMaxOutputBytes` bunun altında kurucuda/
  `AgentPrismOptionsValidator`'da reddedilir. Manuel test/demo yazarken bu
  tabanın üstünde bir değer seçilmeli (bkz. MT-OBS-048'in 100 baytlık örneği).
- **`[AgentPrismTool]` ve `AddTool()` hâlâ `MaxOutputBytes` taşımıyor** —
  `SafeToRepeat`'in aynı, kasıtlı boşluğu (Faz 87). Bugün tek yol
  `services.AddSingleton(new AgentPrismToolRegistration(..., maxOutputBytes: N))`.
  Ayrıca `SafeToRepeat` de `AgentPrismGeneratedTools.Create()`'in ürettiği
  kayda hâlâ hiç yazılmıyor — kaynak üreteciye (`ToolCandidate.cs`,
  `SourceWriter.cs`) her iki alanı BİRLİKTE eklemek ayrı bir aday olarak
  değerlendirilmeli.
- **`docs/hafiza/cekirdek-calistirma.md` bütçenin sınırında** (15999/16000,
  Faz 88'den devralındı, bu faz büyütmedi). Bu dosyaya dokunan bir sonraki
  faz muhtemelen bütçeyi aşacak; gerçek bir bölme o zaman ele alınmalı.
- Sıradaki faz `docs/YOL-HARITASI.md`'de üretilir; kapanışta `python3
  scripts/dokuman-bakim.py` ile yeniden üretildi.
