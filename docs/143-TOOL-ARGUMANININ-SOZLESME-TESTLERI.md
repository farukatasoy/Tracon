# Faz 143 — Tool Argümanının Sözleşme Testleri

> **Durum:** ✅ Tamamlandı (2026-09-04)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-189** (tüketici turu 3, B7)
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Testing.Contracts.Xunit`
> **Yeni paket:** Yok — mevcut sözleşme paketine ek (K-605 ile sevk edildi) · **Migration:** Yok
> **Public API:** Büyüyor — yeni `abstract` sözleşme sınıfları. Yalnız test paketinde; tüketicinin çalışma anı grafiğine **girmez**
> **Tüketici yüzeyi:** `docs-site/`: `guides/testing.md`, `guides/write-your-own-tool.md`, `packages.md` · sevk edilen: `src/AgentPrism.Testing.Contracts.Xunit/README.md`
> **Manuel test alanı:** [`docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md`](manuel-test/24-TEST-PAKETI-VE-SABLON.md)

---

## Bu Faza Başlarken

1. Bu doküman
2. Kararlar — yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-605\|K-615" docs/KARARLAR.md
   ```
   **K-605** (`AgentPrism.Testing.Contracts.Xunit` paketi ve ad alanı kararı),
   **K-615** (generator kendi `JsonSerializerContext`'ini kullanmaz; tool sahibi
   `AgentPrismToolAttribute.JsonSerializerContext` ile verir, vermezse derleme
   `APG0008` ile durur)
3. Mevcut sözleşme desenini oku — **yalnız bu iki dosya**:
   ```bash
   sed -n '1,60p' src/AgentPrism.Testing.Contracts.Xunit/Contracts/Tools/CustomToolContract.cs
   sed -n '1,40p' src/AgentPrism.Testing.Contracts.Xunit/Contracts/Tools/RepeatableToolContract.cs
   ```
4. Alan hafızası:
   [`hafiza/analyzer-yazimi.md`](hafiza/analyzer-yazimi.md) (şema üretimi)

---

## Amaç

AgentPrism iki yerde fail-closed davranış **vaat ediyor**: `throw` eden bir
argüman doğrulayıcı çağrıyı reddeder, `throw` eden bir yetkilendirme handler'ı
çağrıyı engeller. Bu vaatler AgentPrism'in **kendi** kodunda test ediliyor —
ama **tüketicinin** implementasyonunda test edilmiyor.

Tüketicinin ölçümü: yirmi iki tool'un yedisi yıkıcı. Ve deneyimleri şunu
söylüyor: AI'ın halüsinasyon ürettiği argüman, kod hatasından **daha sık** bir
gerçek hata kaynağı.

- **F-189** — Bir tool'un `JsonSchema`'sından argüman üretip fuzz eden ve
  tüketicinin doğrulayıcısının fail-closed davrandığını iddia eden sözleşme
  suite'i.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `ls src/AgentPrism.Testing.Contracts.Xunit/Contracts/Tools/` | Yalnız **iki** dosya: `CustomToolContract.cs`, `RepeatableToolContract.cs` |
| [`CustomToolContract.cs:6`](../src/AgentPrism.Testing.Contracts.Xunit/Contracts/Tools/CustomToolContract.cs) | `abstract class CustomToolContract : IAsyncLifetime` — kaydı kanıtlar, argüman güvenliğini **değil** |
| [`IToolArgumentsValidator.cs:25-27`](../src/AgentPrism.Abstractions/Tools/IToolArgumentsValidator.cs) | *"If this validator throws, the call is **rejected** (fail-closed)."* — vaat yazılı |
| [`ToolAuthorizationTypes.cs`](../src/AgentPrism.Abstractions/Tools/ToolAuthorizationTypes.cs) | *"If this handler throws, the call is **denied**."* — ikinci vaat |
| `ls .../Contracts/` | 33 store sözleşmesi var; tool tarafında argüman güvenliği yok |

> Kanıtlar 2026-09-03 tarihinde doğrulandı.

---

## 143.1 — Suite ne iddia eder

Sözleşme, tüketicinin **kendi** `IToolArgumentsValidator` ve
`IToolAuthorizationHandler` implementasyonunu hedefler.

```mermaid
flowchart TD
    A["Tool'un JsonSchema'sı"] --> B["Argüman üreteci"]
    B --> C1["eksik zorunlu alan"]
    B --> C2["fazla alan"]
    B --> C3["tip uyuşmazlığı"]
    B --> C4["sınır dışı sayı"]
    B --> C5["pattern ihlali"]
    C1 & C2 & C3 & C4 & C5 --> D["Tüketicinin validator'ı"]
    D --> E{"Sonuç"}
    E -- "reddetti" --> F["✅ geçer"]
    E -- "kabul etti" --> G["❌ düşer"]
    E -- "throw etti" --> H["çağrı reddedilmeli — ✅"]
```

🚨 **Suite bir doğrulayıcı sevk etmez.** Kütüphanenin duruşu korunur:
*"AgentPrism does not ship a built-in JSON Schema validator — validation stays
inside the consumer's own trust boundary."* Bu faz o duruşu değiştirmez;
tüketicinin **kendi** doğrulayıcısını sınar.

## 143.2 — Üreteç determinist olmalıdır

Fuzz üreteci rastgele değil **tohumlu** (seeded) çalışır. Gerekçe: düşen bir
sözleşme testi tekrar üretilebilir olmalıdır. Tohum test çıktısına yazılır;
düşen vaka tohumla birebir tekrarlanır.

🚨 **Rastgele bir suite kırılgan bir kapıdır** ve bu repo kırılgan testi
kusurdan ayırmak için ayrı bir protokol taşıyor (`kusur-giderme` Adım 2). Yeni
bir kırılganlık kaynağı eklenmez.

## 143.3 — Şema nereden gelir

Tool'un `JsonSchema`'sı zaten üretiliyor. 🚨 Ama K-615 uyarısı burada geçerli:
generator kendi `JsonSerializerContext`'ini kullanmaz. Suite şemayı **çalışma
anında** `AIFunction`'dan okur, generator'ın iç yapısına bağlanmaz.

**Doğrulanmadı — uygulamada ölçülmeli:** `AIFunction.JsonSchema`'nın tam tipi ve
üye adı. `maf-api-kesfi` ile doğrulanacak; tahmin edilmeyecek.

---

## Planlanan Public API

```csharp
// AgentPrism.Testing.Contracts.Tools
public abstract class ToolArgumentValidationContract : IAsyncLifetime
{
    /// <summary>Sınanacak tool.</summary>
    protected abstract AIFunction Tool { get; }

    /// <summary>Tüketicinin kendi doğrulayıcısı.</summary>
    protected abstract IToolArgumentsValidator Validator { get; }

    /// <summary>Tekrar üretilebilirlik için tohum. Varsayılan sabittir.</summary>
    protected virtual int Seed => 20260903;

    [Fact] public Task Missing_required_argument_is_rejected();
    [Fact] public Task Type_mismatch_is_rejected();
    [Fact] public Task Out_of_range_number_is_rejected();
    [Fact] public Task Pattern_violation_is_rejected();
    [Fact] public Task Unknown_extra_property_is_handled_deliberately();
    [Fact] public Task Throwing_validator_rejects_the_call();
}

public abstract class ToolAuthorizationContract : IAsyncLifetime
{
    protected abstract IToolAuthorizationHandler Handler { get; }

    [Fact] public Task Throwing_handler_denies_the_call();
    [Fact] public Task Denied_call_never_runs_the_tool_body();
    [Fact] public Task Handler_receives_the_calling_tenant();
}
```

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Testing.Contracts.Xunit/
├── Contracts/Tools/
│   ├── ToolArgumentValidationContract.cs  (yeni)
│   └── ToolAuthorizationContract.cs       (yeni)
├── Internal/
│   └── SchemaArgumentGenerator.cs         (yeni — tohumlu üreteç)
└── README.md                              (iki suite belgelenir)

tests/AgentPrism.Testing.Contracts.Tests/
└── (suite'lerin kendi kanıtı — kasten hatalı bir validator DÜŞMELİ)
```

---

## Hata Modları ve Testler

> 🚨 Bu faz **test yazan** bir fazdır. En büyük riski **test tiyatrosudur**:
> her zaman geçen bir suite hiçbir şey kanıtlamaz.

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| 🚨 Suite kasten hatalı bir validator'da da **geçer** (tiyatro) | Fonksiyonel | `ContractSelfProofTests` — her `[Fact]` için **düşen** bir karşı örnek |
| Üreteç rastgele; suite kırılgan olur | Birim | `SchemaArgumentGeneratorTests` — aynı tohum aynı çıktı |
| Şema okunamayınca suite sessizce **atlar** | Birim | `SchemaArgumentGeneratorTests` — okunamayan şema **hata** vermeli |
| `throw` eden validator'da çağrı **geçer** | Fonksiyonel | `ToolArgumentValidationContract` kendi kanıtı |
| Reddedilen çağrıda tool gövdesi **çalışır** | Fonksiyonel | `ToolAuthorizationContract` kendi kanıtı |
| Nested object şeması üretilemez (K-615 sınırı) | Birim | `SchemaArgumentGeneratorTests` — desteklenmeyen şema **açıkça** atlanır ve sebebi yazılır |
| Suite tüketicinin çalışma anı grafiğine paket sızdırır | Paket | `DependencyDirectionTests` |
| Boş şemalı tool | Birim | `SchemaArgumentGeneratorTests` |
| İptal edilen doğrulama | Fonksiyonel | `ToolArgumentValidationContract` |

🚨 **`ContractSelfProofTests` bu fazın en önemli testidir.** Bir sözleşme
suite'i ancak **düşmesi gereken bir implementasyonda düştüğü** kanıtlanırsa
değerlidir. Her `[Fact]` için kasten kırık bir karşı örnek yazılır ve o karşı
örneğin **kırmızı** olduğu görülür.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Örnek tool + doğru validator | Suite'i türet ve koş | Altı `[Fact]` geçer |
| 2 | Aynı tool + her şeyi kabul eden validator | Suite'i koş | 🚨 **Düşer** — tiyatro yok |
| 3 | `throw` eden validator | Suite'i koş | "çağrı reddedilir" `[Fact]`'i geçer |
| 4 | Aynı suite, iki kez | İki kez koş | Aynı sonuç — tohum sabit |
| 5 | Nested object şemalı tool | Suite'i koş | Desteklenmeyen vaka **açıkça** atlanır, sebep yazılır |
| 6 | Tüketici projesi | Paketi referansla, çalışma anı grafiğini incele | Test paketi üretime **sızmaz** |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | "Fazla alan" reddedilmeli mi kabul mü? | A: Suite karar dayatmaz, tüketici beyan eder · B: Reddi zorunlu kıl | **A** — JSON Schema'da `additionalProperties` bir tercihtir; kütüphane kalite barı dayatmaz (F-168'in dersi) |
| 2 | Nested object desteklenmezse suite ne yapsın? | A: Açıkça atla + sebep · B: Düş | **A** — K-615 nested object'i zaten sınırlıyor; düşmek yanlış sinyal olur. Ama sessiz atlama da kabul edilmez: sebep **yazılır** |
| 3 | Suite `AIFunction` mı `ToolDescriptor` mü alsın? | A: `AIFunction` · B: `ToolDescriptor` | **A** — K3: MAF tipi doğrudan kullanılır, sarmalanmaz. 🚨 İmza `maf-api-kesfi` ile doğrulanmalı |

---

## Bitiş Ölçütleri (DoD)

- [x] Suite doğru bir validator'da geçer, **kabul eden** bir validator'da **düşer** (case 1 + 2) — MT-TEST-090/091
- [x] Her `[Fact]` için kasten kırık karşı örnek yazıldı ve kırmızı olduğu görüldü — 11 self-proof testi (`ToolContractSelfProofTests`), 4 kasten kırık implementasyon
- [x] Aynı tohum aynı argümanları üretir; suite kırılgan değil — `SchemaArgumentGeneratorTests` determinizm testleri, MT-TEST-092
- [x] Desteklenmeyen şema **açıkça** atlanır ve sebebi çıktıya yazılır — MT-TEST-093
- [x] `AIFunction.JsonSchema` imzası `maf-api-kesfi` ile doğrulandı (tahmin edilmedi) — `AIFunctionDeclaration.JsonSchema : JsonElement` (dump-api.sh)
- [x] Test paketi tüketicinin çalışma anı grafiğine sızmaz (`DependencyDirectionTests`) — MT-TEST-094, izin listesi hâlâ yalnız `AgentPrism.Abstractions`
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py kapanis` iki kez (denetim düzeltmesi öncesi/sonrası), ikisi de yeşil
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — aşağıda
- [x] `secret` taraması boş döndü — `kapi.py kapanis` içindeki `kapi.py tarama` adımı temiz
- [x] Manuel kabul case'leri `docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md` içine eklendi — MT-TEST-090..094
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 1×🟡 (kapandı), 1×🟢 (adaya yazıldı)
- [x] `docs-site/` güncellendi; `npm run check` (dört kapı: içerik/derleme/bağlantı/ağırlık) temiz

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 **Test tiyatrosu** — suite her zaman geçer | `ContractSelfProofTests`: her `[Fact]` için düşen karşı örnek. DoD'de ayrı satır |
| Rastgele üreteç kırılgan kapı üretir | Tohum sabit; aynı tohum aynı çıktı, testle kilitli |
| MAF imzası tahmin edilir | 🚨 `maf-api-kesfi` zorunlu; plan imzayı **doğrulanmadı** diye işaretledi |
| Suite bir doğrulayıcı sevk etmeye kayar | Kapsam açık: suite sınar, sevk etmez. Kütüphanenin güven sınırı duruşu korunur |
| K-615 nested object sınırı suite'i düşürür | Açık Soru 2 karara bağlar; sessiz atlama yasak |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     ============================================================ -->

## `samples/AgentPrism.Api` ile gerçek run kanıtı

Bu faz `samples/AgentPrism.Api`'nin çalışma anı davranışına dokunmaz (test-anı
paketidir) — bu adım fazın **regresyon üretmediğinin** kanıtıdır, yeni bir
davranışın değil.

```bash
curl -s -X POST http://localhost:5081/agentprism/api/agents/support/run \
  -H "Authorization: Bearer manuel-test-token-2026" -H "Content-Type: application/json" \
  -d '{"message":"Where is my order ORD-7?"}'
```

Gerçek OpenAI çağrısı (`gpt-5.4-mini`), SSE akışı üzerinden: model
`get_order_status` tool'unu `{"orderId":"ORD-7"}` argümanıyla çağırdı, tool
`"Order ORD-7 has shipped. Estimated delivery: 2 days."` döndürdü, model bunu
özetleyen bir metinle bitirdi (`finishReason: stop`, 398 girdi + 20 çıktı
token). Varsayılan kurulumda hiçbir `IToolArgumentsValidator`/
`IToolAuthorizationHandler` kayıtlı değildir — akış hiç engellenmeden aktı,
tam olarak dokümante edilen "hiçbir şey kaydetmeyen kurulum bugünkü davranışı
aynen korur" vaadiyle tutarlı.

## Plandan Sapmalar

1. **`Tool`/`Validator`/`Handler` plain sync abstract property değil, `CreateXAsync()` async
   factory + `IAsyncLifetime`.** Planın taslağı `protected abstract AIFunction Tool { get; }`
   yazıyordu ama `IAsyncLifetime`'ı da bildiriyordu — kendi içinde tutarsızdı. Paketteki
   HER diğer sözleşme (`CustomToolContract`, `RunJudgeContract`, `ModelProviderContract`)
   `ValueTask<T> CreateXAsync()` + `InitializeAsync` deseni kullanıyor; o yerleşik
   konvansiyon tercih edildi.
2. **`ToolAuthorizationContract` planın 3 satırlık taslağından önemli ölçüde
   sapıyor: `DeniedRequest` VE `AllowedRequest` adında iki abstract üye eklendi.**
   Yetkilendirmenin `ToolArgumentValidationContract`'ın aksine şemadan türetilebilen
   bir "geçersiz istek" kavramı yok — yetkilendirme kararı tüketicinin kendi iş
   kuralıdır. Zemin gerçeği olmadan hiçbir `[Fact]` anlamlı bir karşı örnekle
   kırmızıya düşürülemezdi (`CustomToolContract.ExpectedResultText`'in aynı deseni).
   `AllowedRequest` **denetimde eklendi** (aşağıya bak) — ilk taslak yalnız
   `DeniedRequest` taşıyordu ve `Handler_receives_the_calling_tenant`
   tenant'ı sessizce yok sayan bir handler'ı yakalayamıyordu.
3. **`A_pre_cancelled_token_is_honored` — planın 6 `[Fact]`'lik taslağında yok, Hata
   Modları tablosunun "İptal edilen doğrulama" satırında var.** Tablo plandan daha
   yetkili kabul edildi; `RunJudgeContract`'ın aynı adı taşıyan testiyle aynı desen.
4. **`tests/AgentPrism.Testing.Contracts.Tests/` değil `tests/AgentPrism.Testing.Contracts.Xunit.UnitTests/`.**
   Plan proje adını tahmin ediyordu. Gerçek konvansiyon `src/Directory.Build.props`'taki
   `InternalsVisibleTo Include="$(MSBuildProjectName).UnitTests"` — `SchemaArgumentGenerator`
   kasıtlı `internal` olduğu için (fuzzing altyapısı, sevk edilen genişleme noktası değil)
   bu adı taşıyan bir proje **zorunluydu**, plandaki ad çalışmazdı.
5. **`ContractSelfProofTests` `tests/AgentPrism.Testing.Contracts.Tests/`e değil,
   `tests/AgentPrism.Core.UnitTests/Tools/`e (`ToolContractSelfProofTests` adıyla)
   kondu.** Paketteki her diğer sözleşmenin (`CustomToolContractTests`,
   `DependencyDirectionTests`, `ToolContractCoverageTests`) dogfood'landığı **tek**
   yer orası; ayrı bir proje açmak aynı deseni ikiye bölerdi.
6. **`xunit.v3.assert` yeni bir paket referansı olarak eklendi (planda yoktu).**
   `xunit.v3.extensibility.core` `[Fact]`/`IAsyncLifetime` taşır ama `Assert` sınıfını
   (dolayısıyla `Assert.SkipWhen`) taşımaz — K-615 sınırını **açıkça** atlamak için
   dinamik skip zorunluydu. Paket yalnız assertion kütüphanesidir, `<OutputType>Exe</OutputType>`
   zorlamaz (nuspec'inde `buildTransitive` yok, ölçüldü).
7. **`SchemaArgumentGenerator` `JsonSerializer.SerializeToElement` yerine elle
   `Utf8JsonWriter` + `JsonDocument.Parse` kullanır.** İlkinin tek argümanlı
   (primitif) overload'ı bile reflection tabanlı üye çözümlemesi gerektirir ve
   IL2026/IL3050 uyarısı verir — paket AOT-uyumlu listede.
8. **🚨 Ölçülen kusur: MEAI 10.9.0'da nullable bir C# parametre (`string?`)
   `"type":"string"` değil `"type":["string","null"]` üretir.** Generator'ın ilk
   hâli yalnız `ValueKind == String` dalını okuyordu ve nullable HER parametreyi
   sessizce "desteklenmiyor" sayıyordu — plan probu 9.9.1 ile yazılmıştı, gerçek
   pakette (10.9.0) davranış farklıydı. `PrimaryType` yardımcı metodu (dizi formunu
   da okur) ile düzeltildi; `docs/hafiza/tool-onay-ve-yetkilendirme.md`'ye yazıldı.
9. **`Unknown_extra_property_is_handled_deliberately` — Açık Soru 1'in A seçeneği,
   `protected virtual bool ExtraPropertyIsRejected => true` ile uygulandı.** Suite karar
   dayatmıyor (tüketici override edebilir), ama sessiz bir no-op da değil — validator'ın
   davranışı beyan edilen değerle **eşleşmezse** kırmızı olur.

## Bu Fazda Verilen Kararlar

Hiçbiri `docs/KARARLAR.md`'ye girmedi — hepsi bu fazın kendi kapsamındaki yerel
implementation tercihi (test paketinin iç tasarımı); public API/uyumluluk
sözleşmesi, kiracı/güvenlik sınırı veya kalıcı veri kararı **değil**. Plandaki
"Açık Sorular" tablosunun üç maddesi de plan zaten seçenek A'yı önermişti; bu
faz onu doğruladı, yeniden tartışmadı.

## Gerçekleşen Public API

```csharp
// AgentPrism.Testing.Contracts.Tools
public abstract class ToolArgumentValidationContract : IAsyncLifetime
{
    protected AIFunction Tool { get; }                              // CreateToolAsync()'ten
    protected IToolArgumentsValidator Validator { get; }            // CreateValidatorAsync()'ten

    protected abstract ValueTask<AIFunction> CreateToolAsync();
    protected abstract ValueTask<IToolArgumentsValidator> CreateValidatorAsync();
    protected virtual int Seed => 20260903;
    protected virtual bool ExtraPropertyIsRejected => true;

    [Fact] public Task Missing_required_argument_is_rejected();
    [Fact] public Task Type_mismatch_is_rejected();
    [Fact] public Task Out_of_range_number_is_rejected();
    [Fact] public Task Pattern_violation_is_rejected();
    [Fact] public Task Unknown_extra_property_is_handled_deliberately();
    [Fact] public Task Throwing_validator_rejects_the_call();
    [Fact] public Task A_pre_cancelled_token_is_honored();          // plan taslağında yoktu (bkz. Plandan Sapmalar #3)
}

public abstract class ToolAuthorizationContract : IAsyncLifetime
{
    protected IToolAuthorizationHandler Handler { get; }            // CreateHandlerAsync()'ten

    protected abstract ValueTask<IToolAuthorizationHandler> CreateHandlerAsync();
    protected abstract ToolAuthorizationRequest DeniedRequest { get; }
    protected abstract ToolAuthorizationRequest AllowedRequest { get; }   // planda yoktu (bkz. Plandan Sapmalar #2)

    [Fact] public Task Denied_call_never_runs_the_tool_body();
    [Fact] public Task Throwing_handler_denies_the_call();
    [Fact] public Task Handler_receives_the_calling_tenant();
}
```

`SchemaArgumentGenerator` (`Internal/`) **sevk edilmez** — `internal sealed
class`, tohumlu (`Random`), `Baseline`/`MissingRequiredProperty`/`TypeMismatch`/
`OutOfRangeNumber`/`PatternViolation`/`ExtraProperty`/`Poison` metotları.

### Arayüz payı

Yok — plan gibi.

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Testing.Contracts.Xunit/
├── Contracts/Tools/
│   ├── ToolArgumentValidationContract.cs   (yeni, public abstract)
│   └── ToolAuthorizationContract.cs        (yeni, public abstract)
├── Internal/
│   └── SchemaArgumentGenerator.cs          (yeni, internal — tohumlu fuzz üreteci)
├── AgentPrism.Testing.Contracts.Xunit.csproj  (xunit.v3.assert eklendi)
├── PublicAPI.Unshipped.txt                    (16 yeni satır)
└── README.md                                  (iki yeni sözleşme belgelendi)

tests/AgentPrism.Testing.Contracts.Xunit.UnitTests/   (YENİ PROJE — plandan sapma #4)
├── AgentPrism.Testing.Contracts.Xunit.UnitTests.csproj
└── SchemaArgumentGeneratorTests.cs         (13 test — determinizm, unreadable-schema throw, nested-object skip)

tests/AgentPrism.Core.UnitTests/Tools/
├── ToolArgumentValidationContractTests.cs  (yeni — ReferenceToolArgumentsValidator + pozitif dogfood, 7 test)
├── ToolAuthorizationContractTests.cs       (yeni — ReferenceToolAuthorizationHandler + pozitif dogfood, 3 test)
└── ToolContractSelfProofTests.cs           (yeni — ContractSelfProofTests'in yerini tutar, 11 test)

AgentPrism.slnx                              (yeni test projesi eklendi)
Directory.Packages.props                     (xunit.v3.assert PackageVersion eklendi)

docs-site/src/content/docs/guides/write-your-own-tool.md   (validator/handler sözleşmesi bölümü)
docs-site/src/content/docs/guides/testing.md                (Read next bağlantısı)
docs-site/src/content/docs/packages.md                       (paket satırı güncellendi)
docs-site/public/llms.txt, llms-full.txt, src/AgentPrism.Core/buildTransitive/AgentPrism.AgentMap.md  (üretildi)

docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md  (MT-TEST-090..094)
docs/manuel-test/00-INDEKS.md                  (satır güncellendi: 55→60 case, Faz 143 eklendi)
docs/hafiza/tool-onay-ve-yetkilendirme.md      (nullable-union şema + xunit private-class keşif deseni)
```

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı ayrı agent, `dotnet build` + hedefli `dotnet test`
ile ampirik doğrulama) **0×🔴** buldu.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | `Handler_receives_the_calling_tenant`'ın adı/XML doc'u "TenantId gerçekten veri olarak okunuyor" iddia ediyordu ama assertion yalnız `ShouldNotBeNull()` kontrol ediyordu — TenantId'yi tamamen yok sayıp sabit bir karar dönen bir handler bu testi sessizce geçerdi. Kendi self-proof'u da bu boşluğu görmüyordu (`TenantLockedHandlerFixture` yalnız İKİNCİ tenant'ta throw eden bozuk implementasyonu yakalıyordu, sessizce yok sayanı değil). | **Düzeltildi.** `AllowedRequest` abstract üyesi eklendi; fact artık `DeniedRequest`/`AllowedRequest`'in **zıt** kararlar ürettiğini kanıtlıyor. Yeni self-proof karşı örneği: `AlwaysDenyToolAuthorizationHandler` (her zaman reddeder — `AllowedRequest`'i de yanlışlıkla reddederek yakalanır). |
| 2 | 🟢 | `SchemaArgumentGenerator.MismatchedValue`'nin `_ => IntegerElement(12345)` varsayılan kolu (bir `enum` property için `PrimaryType` `null` döndüğünde) hiçbir testte tetiklenmiyor. | `docs/ADAYLAR.md`'ye yazılmadı — kapsam dışı köşe durumu, davranış makul ama kanıtsız; küçük ölçekli bir F-NN açmaya değecek boyutta değil, not olarak burada bırakıldı. |

Düzeltme sonrası dört doğrulama kapısı **yeniden** koşuldu (bkz. DoD).

## Sonraki Faza Devir Notu

- **Devraldığı sözleşmeler:** `ToolArgumentValidationContract` (7 `[Fact]`) ve
  `ToolAuthorizationContract` (3 `[Fact]`), `AgentPrism.Testing.Contracts.Tools`
  ad alanında, `ContractCoverage.ToolContracts` kapsamında. İkisi de MAF/AgentPrism
  çalışma anı grafiğine **girmez** — yalnız test paketinde.
- **Davranış sözleşmeleri:**
  - `IToolArgumentsValidator`/`IToolAuthorizationHandler` fail-closed vaadi artık
    yalnız AgentPrism'in kendi wrapper'ında değil, **tüketicinin implementasyonunda**
    da sınanabilir bir hâle geldi.
  - `SchemaArgumentGenerator` (internal) yalnız düz `string`/`integer`/`number`/
    `boolean`/`enum` şema özelliklerini modelliyor; nested object/array **her
    zaman** açık bir `SkipReason` ile atlanır — K-615/K-655 sınırıyla tutarlı.
- **🚨 Bilinen tuzak (sonraki faz bu alana dokunursa):** `AIFunction.JsonSchema`'da
  `"type"` bir DİZİ olabilir (`["string","null"]`) — bkz. Plandan Sapmalar #8 ve
  `docs/hafiza/tool-onay-ve-yetkilendirme.md`. Şema TÜKETEN (üreten değil) yeni bir
  kod yazarken bu köşe durumunu unutma.
- **🚨 İkinci tuzak:** xunit.v3 yalnız `public` sınıfları test olarak keşfeder;
  `private`/`internal` bir sözleşme türevi normal koşuma hiç karışmaz. Bu, "sözleşme
  suite'i kasten kırık implementasyonda KIRMIZI olmalı" desenini (self-proof) YAZMANIN
  standart yoludur — tekrar kullan.
- **Yer tutucu / açık uç:** Yok. Plan'ın altı açık sorusunun üçü de (A/A/A) uygulandı;
  yedinci `[Fact]` (cancellation) ve `AllowedRequest` (denetim) planın **üstüne** eklendi,
  planın **altında** kalan bir madde yok.
