# Faz 125 — Üretilen Tool Şemasının İfade Gücü

> **Durum:** 📋 Planlandı (2026-08-31)
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
| 1 | MEAI'ın `AIFunctionFactory.Create` yolu gerçekten `[Description]` okuyor mu? | Ölçüm sorusu | `maf-api-kesfi` ile **ilk iş** ölç. Okumuyorsa 125.1'in 2. gerekçesi düşer; karar değişmez ama rehber cümlesi değişir |
| 2 | APG0009 varsayılan seviyesi `Warning` mi `Info` mu? | A: Warning (APG0006 ile simetrik) · B: Info | **A** — APG0006 zaten tool düzeyinde uyarı; parametre düzeyinde daha yumuşak olması tutarsız olurdu |
| 3 | `enum` parametrelerinde üye başına açıklama (`enum` + `description` dizisi) yazılmalı mı? | A: Hayır · B: Evet | **A** — JSON Schema'da üye başına açıklama standart değildir; sağlayıcılar arasında davranış farklıdır. İstenirse ayrı kalem |

---

## Bitiş Ölçütleri (DoD)

- [ ] `[Description]` taşıyan bir parametre üretilen şemada `description` alanı taşır — anlık görüntü testi
- [ ] Dizi parametresinde açıklama **dizi düğümünde**, `items` içinde değil
- [ ] Açıklamasız parametre APG0009 **uyarısı** üretir; derleme başarılıdır
- [ ] APG0003 metni ifade edilemeyenleri adıyla sayar ve kaçış yolunu gösterir
- [ ] `guides/write-your-own-tool.md` üretecin sınırını ilan eder
- [ ] Açıklama değişince üreteç yeni şema üretir (artımlı önbellek testi)
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı; modelin gördüğü şema çıktısı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md` içine eklendi
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz

### Doğrulama komutları

```bash
# Üretilen şemayı gerçekten gör
curl -s http://localhost:5081/agentprism/api/tools | python3 -m json.tool

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

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
