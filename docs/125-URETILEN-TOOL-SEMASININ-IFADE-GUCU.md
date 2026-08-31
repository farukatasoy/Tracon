# Faz 125 — Üretilen Tool Şemasının İfade Gücü

> **Durum:** ✅ Tamamlandı (2026-09-01)
> **Kaynak:** [kesif/2026-08-31-tuketici-raporu-faz-adaylari.md](kesif/2026-08-31-tuketici-raporu-faz-adaylari.md) · **T-1**, **T-2**
> **Önkoşul:** [Faz 52](arsiv/fazlar/52-KAYNAK-URETECI.md) (kaynak üreteci ve derleme anı doğrulama) — arşivde; yalnız grep'le okunur
> **Paketler:** `AgentPrism.Generators`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. Açıklama `System.ComponentModel.DescriptionAttribute`'tan okunur — AgentPrism yeni bir attribute **sevk etmez**
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/write-your-own-tool.md`, `troubleshooting.md` (APG0003 bölümü) · sevk edilen: `AnalyzerReleases.Unshipped.md` (yeni APG kuralı), `[AgentPrismTool]` XML dokümanı
> **Manuel test alanı:** `docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula.

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**:
   ```bash
   grep -n "K-347\|K-218" docs/KARARLAR.md
   ```
   **K-347** (`ToolMethodScanner` örnek metotları tarama anında reddeder — bu faz o kararı **açmaz**) · **K-218** (tool bağımlılıkları kurulum anında alınır)
3. Alan hafızası:
   [`hafiza/analyzer-yazimi.md`](hafiza/analyzer-yazimi.md) (üreteç ve tanı yazımının tuzakları) ·
   [`hafiza/build-ve-analyzer.md`](hafiza/build-ve-analyzer.md) (AOT ve analyzer kaçış merdiveni)
4. Gerektiğinde: `src/AgentPrism.Generators/AnalyzerReleases.Unshipped.md` — yeni kural buraya **eklenmezse derleme kırılır**

---

## Amaç

Bir tüketici ölçtü: kendi basit reflection şeması **parametre açıklaması
üretiyor**, AgentPrism'in kaynak üreteci üretmiyor. Modelin hangi argümanı
neyle dolduracağını en çok etkileyen alan budur ve bu eksende AgentPrism
bugün geridedir. Aynı üreteç yolu, rehberde *"the AOT-safe path"* diye
**önerilen** yoldur; önerdiğimiz yolun daha zayıf olması kabul edilemez.

İkinci kalem aynı yüzeydedir: üretecin neyi ifade **edemediği** hiçbir
tüketiciye dönük sayfada yazılı değil. Bir tüketici bu yüzden ifade
edilemeyen bir şeyi (iç içe nesne şeması) test etmeyi planladı.

- **T-1** — Üretilen şema parametre başına `description` taşır.
- **T-2** — Üretecin ifade sınırı rehberde ve tanı metninde **adıyla** ilan edilir.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`SourceWriter.cs:212-241`](../src/AgentPrism.Generators/SourceWriter.cs) | `BuildSchema` yalnız `properties`, `required` ve `additionalProperties:false` yazıyor |
| [`SourceWriter.cs:252-264`](../src/AgentPrism.Generators/SourceWriter.cs) | `BuildLeafSchemaNode` yedi yaprak biçimi üretiyor; hiçbirinde `description` yok |
| [`ParameterModel.cs:44-51`](../src/AgentPrism.Generators/ParameterModel.cs) | `ParameterModel` kaydında `Description` **alanı yok** — açıklama üretecin modeline hiç girmiyor |
| [`ToolCandidate.cs:186-232`](../src/AgentPrism.Generators/ToolCandidate.cs) | `ReadAttribute` yalnız **tool düzeyinde** açıklama okuyor |
| [`ToolDiagnostics.cs:36-42`](../src/AgentPrism.Generators/ToolDiagnostics.cs) | APG0003 desteklenen tipleri sayıyor ama `minimum`/`maximum`/`pattern`/iç içe nesnenin **hiç** ifade edilemediğini söylemiyor |
| [`ToolDiagnostics.cs:62-69`](../src/AgentPrism.Generators/ToolDiagnostics.cs) | Tool düzeyinde açıklama eksikliği zaten **uyarı** (APG0006); parametre düzeyinde karşılığı yok |
| [`guides/write-your-own-tool.md:6`](../docs-site/src/content/docs/guides/write-your-own-tool.md) | Üreteç yolu *"the AOT-safe path"* diye öneriliyor; sınırı yazılmıyor |

> Kanıtlar 2026-08-31 tarihinde `8105c00` üzerinde doğrulandı.

---

## 125.1 — Açıklama nereden okunur

**`System.ComponentModel.DescriptionAttribute`.** Gerekçe üç maddedir:

1. **AgentPrism yeni bir attribute sevk etmez.** Public yüzey büyümez.
2. `Microsoft.Extensions.AI`'ın kendi `AIFunctionFactory.Create` yolu da bu
   attribute'u okur — **doğrulanmadı, `maf-api-kesfi` ile ölçülmeli.** Doğruysa
   iki tool yazma yolu aynı kaynağı okur ve tüketici tek kural öğrenir.
3. Roslyn tarafı hazır: `IParameterSymbol.GetAttributes()`.

XML `<param>` yorumu yolu **reddedildi**: tüketicinin projesinde
`GenerateDocumentationFile` açık olmak zorundadır, kapalıysa açıklama sessizce
düşer. Sessizce düşen bir alan, hiç olmayan alandan kötüdür.

```csharp
[AgentPrismTool("submit_order", "Submits an order to the fulfillment system.")]
public static Task<OrderReceipt> SubmitOrderAsync(
    [Description("The identifier of the order to submit.")] string orderId,
    CancellationToken cancellationToken)
```

Üretilen şema:

```json
{"type":"object","properties":{
  "orderId":{"type":"string","description":"The identifier of the order to submit."}
},"required":["orderId"],"additionalProperties":false}
```

### Nereye yazılır — 🚨 dizi parametresinde tuzak

Açıklama **parametre düğümüne** yazılır, dizi elemanının yaprak düğümüne
değil:

```json
"tags":{"type":"array","description":"…","items":{"type":"string"}}
```

`BuildSchemaNode` bugün dizi için `{"type":"array","items":<yaprak>}` üretiyor
ve yaprağı `BuildLeafSchemaNode` kuruyor. Açıklamayı yaprağa yazmak onu
`items` içine gömer; model onu parametrenin açıklaması olarak okumaz. Bu
ayrım `SourceWriter` içinde tek satırlık bir hatadır ve testle sabitlenir.

`CancellationToken` şemaya hiç girmiyor; açıklaması da girmez.

### Gerçek çıktı — `samples/AgentPrism.Api`, 2026-09-01

`dotnet run` ile ayağa kaldırılan örnek uygulamaya karşı `GET /agentprism/api/tools`
çağrıldı (`curl -s http://localhost:5080/agentprism/api/tools -H "Authorization:
Bearer manuel-test-token-2026"`). Modelin gördüğü gerçek şema (`get_order_status`,
kısaltılmış):

```json
{
  "name": "get_order_status",
  "description": "Returns the shipping status of an order.",
  "jsonSchema": "{\"type\":\"object\",\"properties\":{\"orderId\":{\"description\":\"The order number.\",\"type\":\"string\"}},\"required\":[\"orderId\"],\"additionalProperties\":false}"
}
```

`cancel_order`, `list_recent_orders`, `get_slow_report` — `samples/AgentPrism.Api/OrderTools.cs`
içindeki kaynak-üretilen dört tool'un tamamı — aynı şekilde kendi parametrelerinde
`description` taşıdı. Manuel kabul case'i: `MT-TEST-087`.

## 125.2 — Eksik açıklama uyarısı (APG0009)

APG0006 tool düzeyinde açıklama eksikliğini **uyarı** yapıyor ve gerekçesini
yazıyor: *"The model cannot know when to call the tool without one."* Aynı
gerekçe parametre için de geçerlidir.

| Kural | Kategori | Seviye | Metin |
|---|---|---|---|
| APG0009 | `AgentPrism.Tools` | **Warning** | Parameter '{1}' of tool '{0}' has no description. The model has only the parameter name to go on; add `[Description]`. |

Uyarı, hata değil: var olan tüketici kodunu **kırmaz**, yalnız görünür kılar.
Bu K1'in ("sıfır sürpriz") gereğidir.

🚨 Kural `AnalyzerReleases.Unshipped.md`'ye eklenmezse **derleme kırılır** —
`RS2008`. Bu, bu repo'da tanı ekleyen her fazın ilk tökezlediği yerdir.

## 125.3 — İfade sınırının ilanı

Kullanıcı kararı (2026-08-31): **yalnız ilan.** `[Range]`/`[StringLength]`
şemaya **yazılmaz**. Gerekçe: Faz 127'nin argüman kapısı sevk edilene kadar
şemada yazan bir kısıtı hiçbir şey zorlamaz, ve zorlanmayan bir kısıt
tüketiciye yanlış güven verir.

İlan üç yerde yapılır ve **aynı cümleyi** söyler:

| Yer | Ne yazar |
|---|---|
| `guides/write-your-own-tool.md` | Üretecin ifade **edebildikleri**: skaler, `string`, `Guid`, `DateTime(Offset)`, `enum` ve bunların dizileri; artı `description` ve `required`. İfade **edemedikleri**: iç içe nesne, `minimum`/`maximum`/`pattern`, koşullu şema. Nesne parametresi için önerilen yol: `AddTool(AIFunctionFactory.Create(...))` + `JsonSerializerContext` |
| APG0003 tanı metni | Bugünkü "supported types" listesine, **neyin ifade edilemediği** eklenir; kaçış yolu zaten yazılı |
| `troubleshooting.md` APG0003 bölümü | Aynı sınırın uzun anlatısı ve çalışan kaçış örneği |

Bu üç metnin **aynı** şeyi söylediğini bir kapı doğrulamaz; bu yüzden
tekrarlanan cümle değil, tek bir sınır listesi yazılır ve diğer ikisi ona
bağlanır.

---

## Planlanan Public API

**Public API büyümüyor.** Üreteç `System.ComponentModel.DescriptionAttribute`
okur; AgentPrism yeni bir tip sevk etmez.

Değişen `internal` üreteç modeli:

```csharp
// AgentPrism.Generators — internal
internal sealed record ParameterModel(
    string Name,
    ParameterShape Shape,
    LeafType? Leaf,
    bool IsRequired,
    string? DefaultValueLiteral,
    bool IsConcreteArray = false,
    string? Description = null);   // YENİ
```

🚨 `ParameterModel` bir **artımlı üreteç** modelidir. `EquatableArray` ve
`record` eşitliği üzerinden önbelleğe alınır; yeni alan eşitliğe otomatik
girer (positional record), ama uygulayan oturum bunu bir üreteç önbellek
testiyle **doğrulamalıdır** — eşitliğe girmeyen alan, açıklaması değişen bir
projede eski şemayı üretir.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Generators/
├── ParameterModel.cs                (değişir — Description alanı)
├── ParameterTypeValidator.cs        (değişir — [Description] okuma)
├── SourceWriter.cs                  (değişir — şemaya description yazma)
├── ToolCandidate.cs                 (değişir — APG0009 tanısı)
├── ToolDiagnostics.cs               (değişir — APG0009 + APG0003 metni)
└── AnalyzerReleases.Unshipped.md    (değişir — APG0009 satırı)

tests/AgentPrism.Generators.UnitTests/     (mevcut proje adı uygulama anında doğrulanır)
└── ToolSchemaDescriptionTests.cs    (yeni)

docs-site/src/content/docs/guides/write-your-own-tool.md   (değişir)
docs-site/src/content/docs/troubleshooting.md              (değişir)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Açıklama şemaya hiç yazılmıyor | Birim (anlık görüntü) | `ToolSchemaDescriptionTests` |
| Dizi parametresinde açıklama `items` içine gömülüyor | Birim | `ToolSchemaDescriptionTests` |
| Açıklamadaki `"` ve `\` JSON'u bozuyor | Birim | `ToolSchemaDescriptionTests` — `JsonEscape` yolu |
| Açıklamadaki Unicode / çok satırlı metin bozuluyor | Birim | `ToolSchemaDescriptionTests` |
| `CancellationToken` şemaya sızıyor | Birim | mevcut üreteç testi regresyon oracle'ı |
| Açıklaması **olmayan** parametre derlemeyi kırıyor (uyarı olması gerekirken hata) | Birim | `ToolSchemaDescriptionTests` — `DiagnosticSeverity.Warning` sabitlenir |
| APG0009 `AnalyzerReleases.Unshipped.md`'ye eklenmemiş | Derleme kapısı | `RS2008` — dört kapıda görünür |
| Açıklama değişince üreteç eski şemayı önbellekten veriyor | Birim | `ToolSchemaDescriptionTests` — artımlı üreteç eşitlik testi |
| Üretilen kod AOT'ta yansımaya düşüyor | Kapı | mevcut AOT kapısı; `[Description]` yalnız **derleme anında** okunur, çalışma anında değil |

Beş soru: **iptal** → üreteç yolu iptal taşımaz, gerekçe budur ·
**eşzamanlılık** → üreteç saf fonksiyondur, paylaşılan durum yok ·
**boş/aşırı girdi** → boş açıklama (`[Description("")]`) ve çok uzun açıklama
testte · **başka kiracı** → üreteç kiracı sınırı görmez, gerekçe budur ·
**alt sistem hatası** → alt sistem yok.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | `[Description]` taşıyan bir `[AgentPrismTool]` metodu | `dotnet build`; `GET /api/tools` | Tool şemasında parametre açıklaması görünür |
| 2 | Açıklamasız bir parametre | `dotnet build` | APG0009 **uyarısı**; derleme **başarılı** |
| 3 | Nesne parametreli bir metot | `dotnet build` | APG0003 hatası; metin iç içe nesnenin ifade edilemediğini ve kaçış yolunu **adıyla** söyler |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | MEAI'ın `AIFunctionFactory.Create` yolu gerçekten `[Description]` okuyor mu? | Ölçüm sorusu | **Ölçüldü — EVET.** Küçük bir probe projesiyle (`Microsoft.Extensions.AI.Abstractions` 10.9.0) doğrulandı: `AIFunctionFactory.Create(delegate, name, description)` parametre üzerindeki `[Description]`'ı okuyup şemaya `"description"` olarak yazıyor. 125.1'in 2. gerekçesi doğrulandı; rehbere ve `troubleshooting.md`'ye yazıldı |
| 2 | APG0009 varsayılan seviyesi `Warning` mi `Info` mu? | A: Warning (APG0006 ile simetrik) · B: Info | **A** — APG0006 zaten tool düzeyinde uyarı; parametre düzeyinde daha yumuşak olması tutarsız olurdu |
| 3 | `enum` parametrelerinde üye başına açıklama (`enum` + `description` dizisi) yazılmalı mı? | A: Hayır · B: Evet | **A** — JSON Schema'da üye başına açıklama standart değildir; sağlayıcılar arasında davranış farklıdır. İstenirse ayrı kalem |

---

## Bitiş Ölçütleri (DoD)

- [x] `[Description]` taşıyan bir parametre üretilen şemada `description` alanı taşır — anlık görüntü testi (`ToolSchemaDescriptionTests.A_Description_attribute_on_a_scalar_parameter_reaches_the_schema`)
- [x] Dizi parametresinde açıklama **dizi düğümünde**, `items` içinde değil (`An_array_parameters_description_is_written_on_the_array_node_not_inside_items`)
- [x] Açıklamasız parametre APG0009 **uyarısı** üretir; derleme başarılıdır (`DiagnosticTests.APG0009_warns_when_a_parameter_description_is_missing_but_does_not_block_generation`)
- [x] APG0003 metni ifade edilemeyenleri adıyla sayar ve kaçış yolunu gösterir (`DiagnosticTests.APG0003_message_names_what_the_generator_can_never_express_125_3`)
- [x] `guides/write-your-own-tool.md` üretecin sınırını ilan eder
- [x] Açıklama değişince üreteç yeni şema üretir (`Changing_only_the_description_produces_a_fresh_schema_not_a_stale_cached_one`)
- [x] Dört doğrulama kapısı sıfır uyarı verir (`dotnet build`/`test`/`pack`/`format --verify-no-changes`, 2026-09-01)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı; modelin gördüğü şema çıktısı belgeye yazıldı (§125.1 "Gerçek çıktı")
- [x] `secret` taraması boş döndü (`python3 scripts/kapi.py tarama` → `✅ temiz`)
- [x] Manuel kabul case'leri `docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md` içine eklendi (MT-TEST-087/088/089)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run check` (`check:content`+`build`+`check:links`+`check:weight`) temiz

### Doğrulama komutları

```bash
# Üretilen şemayı gerçekten gör
curl -s http://localhost:5080/agentprism/api/tools -H "Authorization: Bearer manuel-test-token-2026" | python3 -m json.tool

# Üreteç testleri
dotnet build AgentPrism.slnx -c Release
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| `[Description]` MEAI yolunda okunmuyorsa iki yol farklı davranır | Açık Soru 1'de ölçülür; sonuç rehbere yazılır |
| Artımlı üreteç önbelleği yeni alanı görmez ve bayat şema üretir | Eşitlik testi DoD'de ayrı satır |
| APG0009 var olan tüketici derlemelerinde uyarı seli üretir | Uyarıdır, hata değil; `WarningsAsErrors` kullanan tüketici için rehberde bastırma yolu yazılır |
| Sınır ilanı üç metinde kayar | Tek sınır listesi yazılır; diğer ikisi ona bağlanır |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

- **`ToolRegistrationGenerator.cs` planın dosya listesinde yoktu, ama değişmek
  zorunda kaldı.** Plan `DescriptorsById` adlı bir dispatch tablosunun varlığını
  bilmiyordu: `ToolCandidate.Create` bir `DiagnosticInfo` üretse bile,
  `ToolRegistrationGenerator.ReportDiagnostic` id'yi bu **private** sözlükte
  bulamazsa sessizce `return` eder — ne derleme hatası, ne test kırılması, ne
  log çıkışı. APG0009 önce `ToolDiagnostics.cs`'e eklendi ve **hiç
  raporlanmadı**; yalnız tanıyı bizzat arayan bir test (`ToolSchemaDescriptionTests`)
  bunu yakaladı. Aynı kusur sınıfının bir daha yaşanmaması için
  `DiagnosticIntegrityTests.Every_DiagnosticInfo_routed_tool_diagnostic_is_wired_into_the_generators_dispatch_table`
  eklendi — reflection ile private tabloyu okuyup her `AgentPrism.Tools` tanısının
  (doğrudan raporlanan `DuplicateName`/`NoToolsFound` hariç) orada olduğunu
  doğrular. Not: `docs/hafiza/analyzer-yazimi.md`.
- **`node.Substring(1)`, plandaki `node[1..]` değil.** `AgentPrism.Generators`
  `netstandard2.0`'ı hedefliyor; bu TFM'de `System.Range`/`System.Index` yok,
  dizin aralığı operatörü `CS0518` veriyor. Aynı sınıftan bir tuzak zaten
  `IsExternalInit` için biliniyordu (Faz 52); bu faz onu dizin aralığı
  operatörüne genişletti. Not: `docs/hafiza/analyzer-yazimi.md`.
- **Örnek/şablon/paket-testi dosyaları planda yoktu, dogfooding için değişti.**
  `samples/AgentPrism.Api/OrderTools.cs`, `samples/AgentPrism.Samples.CustomTool/OrderPreviewTools.cs`,
  `src/AgentPrism.Templates/content/AgentPrism.Starter/Tools/OrderTools.cs`,
  `tests/AgentPrism.Package.Tests/Infrastructure/ConsumerProject.cs` —
  APG0009 devreye girince bu dosyalardaki parametreler uyarı üretmeye başladı;
  ana repo `TreatWarningsAsErrors=true` taşıdığından `samples/AgentPrism.Api`
  için bu gerçek bir **derleme hatasıydı**. Dördüne de `[Description]` eklendi;
  ayrıca beş mevcut üreteç testi (`GeneratedOutputTests`, `IncrementalityTests`)
  aynı sebeple güncellendi.
- **APG0003 mesajı ve `troubleshooting.md`, `AIFunctionFactory.Create`'in
  `JsonSerializerOptions` alan overload'unu (kaynak-üretilmiş `JsonSerializerContext`
  ile AOT-güvenli) örnekliyor** — plan yalnız "register manually with
  `AddTool(AIFunctionFactory.Create(...))`" diyordu, hangi overload'ın nesne
  parametresini AOT-güvenli işlediğini söylemiyordu. Bir probe projesiyle
  ölçülüp (`AIFunctionFactory.Create(Delegate, string, string, JsonSerializerOptions)`)
  doğrulandı; `guides/write-your-own-tool.md` ve `troubleshooting.md`'ye
  çalışan bir örnek olarak yazıldı.

## Bu Fazda Verilen Kararlar

Bu fazda public API/uyumluluk sözleşmesi, güvenlik veya kiracı sınırı, kalıcı
veri/migration ya da geri dönüşü pahalı bir sistem kararı **verilmedi** —
yalnız yerel implementation tercihleri (yukarıdaki sapmalar). `docs/KARARLAR.md`'ye
yeni bir `K-*` kaydı açılmadı.

## Gerçekleşen Public API

Plandaki gibi: **büyümedi.** `AgentPrism.Generators`'ın internal modeli
(`ParameterModel`) planla birebir aynı şekilde `Description` alanı kazandı.

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Generators/
├── ParameterModel.cs                     (değişti — Description alanı)
├── ParameterTypeValidator.cs             (değişti — [Description] okuma)
├── SourceWriter.cs                       (değişti — şemaya description yazma, JsonEscape control-char güvenli hale geldi)
├── ToolCandidate.cs                      (değişti — APG0009 tanısı)
├── ToolDiagnostics.cs                    (değişti — APG0009 + APG0003 metni)
├── ToolRegistrationGenerator.cs          (değişti — planda YOKTU; DescriptorsById dispatch tablosu)
└── AnalyzerReleases.Unshipped.md         (değişti — APG0009 satırı)

tests/AgentPrism.Generators.UnitTests/
├── ToolSchemaDescriptionTests.cs         (yeni — 10 test)
├── DiagnosticTests.cs                    (değişti — APG0009 + APG0003 mesaj testi)
├── DiagnosticIntegrityTests.cs           (değişti — dispatch tablosu regresyon testi)
├── GeneratedOutputTests.cs               (değişti — mevcut fixture'lara [Description])
└── IncrementalityTests.cs                (değişti — aynı sebep)

docs-site/src/content/docs/guides/write-your-own-tool.md   (değişti)
docs-site/src/content/docs/troubleshooting.md               (değişti — APG0003 genişledi, APG0009 bölümü yeni)
docs-site/public/llms.txt, llms-full.txt                    (üretildi — build-agent-map.mjs)

samples/AgentPrism.Api/OrderTools.cs                                          (değişti — planda yoktu, dogfooding)
samples/AgentPrism.Samples.CustomTool/OrderPreviewTools.cs                    (değişti — planda yoktu, dogfooding)
src/AgentPrism.Templates/content/AgentPrism.Starter/Tools/OrderTools.cs       (değişti — planda yoktu, dogfooding)
tests/AgentPrism.Package.Tests/Infrastructure/ConsumerProject.cs              (değişti — planda yoktu, paketlenmiş consumer'da da kanıt)

docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md   (değişti — MT-TEST-087/088/089)
docs/hafiza/analyzer-yazimi.md                 (değişti — iki yeni tuzak)
docs/hafiza/dokumantasyon.md                   (değişti — ağırlık bütçesi gözlemi)
```

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı `general-purpose` agent), taban `9621eeb`,
2026-09-01. Tam rapor bu oturumun geçmişinde; özet:

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | Hata-modu tablosu "çok uzun açıklama testte" diyordu ama böyle bir test yoktu | **Düzeltildi** — `A_very_long_description_round_trips_through_the_generated_schema` eklendi (209 → 210 test) |
| 2 | 🟢 | `troubleshooting.md` bu fazda büyüyünce `check:weight` tavanının **%96**'sına ulaştı | **Devredildi** — `docs/hafiza/dokumantasyon.md`'ye gözlem notu (kapı bugün yeşil, ölçülmeden büyütülmedi; yeni içerik eklerken kontrol edilmeli) |

🔴 bulgu **yok**. Denetçinin bağımsız doğruladığı: `dotnet build`/`test`
(Generators, 208/208), `dotnet format --verify-no-changes`, `build-agent-map.mjs --check`,
`npm run check`, `secret` taraması, imza-gövde takibi (`ParameterModel.Description`
üç üretim + iki tüketim noktası, kayma yok), `DescriptorsById` düzeltmesinin
kalıcılığı. Ayrıca doğrulandı: `AgentPrism.Ui.E2ETests`'teki tek düşen test
(`Playground_voice_mode_opens_microphone_and_shows_transcript`, paralel koşumda
30s timeout) bu fazın dokunmadığı dosyalarda — izole koşumda geçti, önceden var
olan kırılganlık, bu fazın kapsamı dışı.

## Sonraki Faza Devir Notu

- **Faz 126 (Kalıcı Payload Sürüm Sözleşmesi)** bu fazın dokunduğu hiçbir
  dosyaya bağımlı değil; bağımsız başlanabilir.
- **`[Description]` artık iki tool yazma yolunda da (kaynak üreteci VE
  `AIFunctionFactory.Create`) okunuyor** — ölçüldü (Açık Soru 1). Gelecekte
  ikisinden biri değişirse (MEAI sürüm yükseltmesi) `maf-api-kesfi` ile
  yeniden ölçülmeli; `docs/hafiza/analyzer-yazimi.md`'ye not düşülmedi çünkü
  bu bir üretici-tüketici sözleşmesi değil, MEAI'ın kendi davranışı.
- 🚨 **Yeni bir `APG*` tanısı eklerken `ToolDiagnostics.cs`'e eklemek
  YETMEZ.** `ToolRegistrationGenerator.cs`'deki private `DescriptorsById`
  sözlüğüne de eklenmeli, yoksa tanı sessizce hiç raporlanmaz —
  `DiagnosticIntegrityTests.Every_DiagnosticInfo_routed_tool_diagnostic_is_wired_into_the_generators_dispatch_table`
  bunu şimdi yakalıyor, ama tanının **kendisi** neden raporlanmadığını
  söylemez, yalnız "eksik" der. Kaçırma riski hâlâ var.
- **`troubleshooting.md` ağırlık bütçesinin %96'sında** (bkz. Denetim
  Bulguları #2). Bu sayfaya yeni bir bölüm eklemeden önce `npm run check:weight`
  çıktısını kontrol et.
- Faz 127 (Tool Kayıt Yüzeyi) `[Description]`'ın yaşadığı aynı üreteç
  boru hattına dokunacak (`ParameterModel`, `SourceWriter`) — bu fazda eklenen
  `Description` alanının artımlı önbellek eşitliğine **otomatik** girdiği
  (positional record) bilgisi hâlâ geçerli, ama yeni bir alan eklerken yine
  aynı önbellek testi deseni tekrarlanmalı (`Changing_only_the_description_produces_a_fresh_schema_not_a_stale_cached_one`).
