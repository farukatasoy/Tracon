# Faz 131 — Yapısal Yanıt Doğrulama Seam'i

> **Durum:** 📋 Planlandı (2026-09-01)
> **Kaynak:** [kesif/2026-09-01-tuketici-feature-talepleri.md](kesif/2026-09-01-tuketici-feature-talepleri.md) — **F-174**
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.AspNetCore` (yalnız `RunEventType` yüzeyi), `.UI` (olay etiketi)
> **Yeni paket:** Yok · **Migration:** Yok — yeni `RunEventType` ve `RunErrorClass` değerleri mevcut `smallint` sütunlarına yazılır
> **Public API:** büyüyor — yeni arayüz, options, olay ve hata sınıfı. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya 1 satır; shipped giriş sıfır olduğu için bugün eklemek ucuz
> **Tüketici yüzeyi:** `docs-site/`: `guides/structured-output.md`, `concepts/runs.md` (olay listesi), `capabilities.md`, `reference/configuration.md` · sevk edilen: `IStructuredResponseValidator` XML `<example>`'ı, `en.ts`/`tr.ts` olay etiketi
> **Manuel test alanı:** [`docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md`](manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır.

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-603\|K-232" docs/KARARLAR.md
   ```
   **K-603** (`RunErrorClass`'ın 9 numarası emekli bir boşluktur, yeniden
   kullanılamaz), **K-232** (sunucu yanıtları çevrilmez).
3. [Faz 127](arsiv/fazlar/127-TOOL-KAYIT-YUZEYI.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/127-TOOL-KAYIT-YUZEYI.md
   ```
   `IToolArgumentsValidator`'ı o faz sevk etti. Bu faz **aynı seam biçimini**
   yanıt tarafında tekrarlar; iki arayüz birbirine benzemek zorundadır.
4. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md) (decorator
   sırası, `AsyncLocal` tuzağı, akışlı yol),
   [`hafiza/model-boru-hatti.md`](hafiza/model-boru-hatti.md) (`IChatClient`
   halkalarının sırası ve neden bu faz oraya girmiyor).
5. Gerektiğinde: [`MIMARI.md`](MIMARI.md) — çalıştırma yolu bölümü.

---

## Amaç

`AgentResponseFormat` bugün yalnız sağlayıcıya bir kısıt gönderir. Dönen
yanıtı **hiçbir şey doğrulamaz**. Model boş içerik döndürdüğünde, JSON kesik
geldiğinde veya şemaya uymayan bir alan geldiğinde `run` **başarılı** kapanır.
Tüketici parse hatasını sonra alır. Böylece AgentPrism'in `run` sonucu ile
gerçek kullanım sonucu ayrışır.

Bu faz, `run`'ın kapanmadan önce yanıtı doğrulamasını sağlayan genişleme
noktasını ekler.

- **F-174** — opt-in yapısal yanıt doğrulama seam'i, kararı taşıyan `run`
  olayı ve kararlı bir hata sınıfı.

### AgentPrism ne yapar, ne yapmaz

| AgentPrism | Tüketici |
|---|---|
| Doğrulayıcıyı **ne zaman** çağıracağına karar verir | **Neyin geçerli** olduğuna karar verir |
| Geçersizde `run`'ı ne yapacağına karar verir | Şema kurallarını yazar |
| Kararı `run` kanıtına yazar | Domain kurallarını (`score` aralığı, izin) kendi uygular |

**AgentPrism yerleşik bir JSON Schema doğrulayıcı sevk etmez.** Bu, uydurulmuş
bir sınır değil, `IToolArgumentsValidator`'ın sevk edilmiş XML dokümanında
yazılı pozisyondur: doğrulama tüketicinin güven sınırında kalır. Üç ölçülmüş
gerekçe: `Directory.Packages.props` içinde JSON Schema doğrulayıcı yoktur ve
BCL'de de yoktur (yerleşik doğrulayıcı = yeni geçişli paket); `Abstractions`
ve `Core` AOT uyumlu kalmak zorundadır, mevcut doğrulayıcılar `reflection`
kullanır; iki yerde iki farklı politika tutarsız bir ürün anlatısı olur.

### Kapsam dışı — bilerek

| Kalem | Neden bu fazda değil |
|---|---|
| Bounded repair (geçersiz yanıt için ikinci model çağrısı) | `run` içinde **yeni bir model çağrısı** açar; bütçe (`AgentRunBudget` token/cost/duration + `Deadline`), `FallbackChatClient`'ın tur içi tool defteri, cost attribution ve iptal ile kesişir. Ayrı faz |
| Yerleşik şema doğrulayıcı | Yukarıdaki bölüm |
| Akışta içeriği geri alma | Teknik olarak mümkün değil; 131.5'te açıkça yazılır |

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`AgentDefinitionCompiler.ChatOptions.cs:108`](../src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.ChatOptions.cs) | `options.ResponseFormat = BuildResponseFormat(definition)` — kısıt sağlayıcıya gider |
| [`AgentDefinitionCompiler.ChatOptions.cs:138-191`](../src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.ChatOptions.cs) | `BuildResponseFormat` yalnız `ChatResponseFormat` üretir; dönen yanıtla ilgili **hiçbir kod yok** |
| [`ResponseFormat.cs`](../src/AgentPrism.Abstractions/Agents/ResponseFormat.cs) | `AgentResponseFormat.Schema`'nın "içeriği doğrulanmaz" olduğu XML'de yazılı |
| [`AgentDescriptor.cs:32`](../src/AgentPrism.Abstractions/Agents/AgentDescriptor.cs) | `public ModelBinding? Model { get; init; }` — **decorator şemaya buradan ulaşır**; ayrı bir taşıma yolu gerekmez |
| [`AgentDecoratorPipeline.cs`](../src/AgentPrism.Core/Catalog/AgentDecoratorPipeline.cs) | `decorators.OrderByDescending(d => d.Order)` — **büyük `Order` önce uygulanır**, yani en içte kalır |
| `RunRecordingAgentDecorator.cs:114` · `OpenTelemetryAgentDecorator.cs:37` · `ToolApprovalAgentDecorator.cs:38` | `Order` değerleri sırasıyla **0 · 10 · 20**. En dıştaki kayıt (`0`) `run` kaydıdır |
| [`RunEventType.cs`](../src/AgentPrism.Abstractions/Runs/RunEventType.cs) | En büyük değer **26** (`ToolOutputTruncated`) |
| [`RunErrorClass.cs`](../src/AgentPrism.Abstractions/Runs/RunErrorClass.cs) | En büyük değer **13** (`ToolTimeout`). 🚨 **9 emekli bir boşluktur** ve yeniden kullanılamaz (K-603); `RunErrorClassContractTests` bunu sabitler |
| [`IToolArgumentsValidator.cs`](../src/AgentPrism.Abstractions/Tools/IToolArgumentsValidator.cs) | Sevk edilmiş pozisyon: *"AgentPrism does not ship a built-in JSON Schema validator — validation stays inside the consumer's own trust boundary."* Ayrıca fail-closed davranışı burada tanımlı |
| [`SafeErrorText.cs`](../src/AgentPrism.Abstractions/Diagnostics/SafeErrorText.cs) | Kalıcı hata metnini güvenli hâle getiren yardımcı **zaten var**; bu faz onu kullanır, yenisini yazmaz |

> Kanıtlar 2026-09-01 tarihinde doğrulandı.

---

## 131.1 — Doğrulama nerede çalışır

Doğrulama bir **`IAgentDecorator`**'dır, bir `IChatClient` halkası **değildir**.

🚨 Bu, planın yapısal iddiasıdır ve gerekçesi ölçülmüştür. `IChatClient`
katmanı tool çağrı turlarının **her birini** görür; yapısal çıktı yalnız
**son** turda gelir. O katmanda "bu son tur mu" sorusunu function-call
içeriğine bakarak tahmin etmek gerekirdi. `AIAgent` katmanı son yanıtı
doğrudan görür. Ayrıca sonraki fazın repair'i bir **yeni tur** açacaktır; tur
açmak agent seviyesinin işidir, `FallbackChatClient`'ın tur ortasına girmek
değil.

```mermaid
flowchart TD
    R["RunRecordingAgent · Order 0<br/>(en dışta)"] --> O["OpenTelemetryAgent · Order 10"]
    O --> A["ToolApprovalAgent · Order 20"]
    A --> V["StructuredResponseValidatingAgent · Order 30<br/>(en içte — YENİ)"]
    V --> C["Derlenmiş ChatClientAgent"]
    V -. "geçersiz → istisna" .-> A
    A -. "yayılır" .-> O
    O -. "span hatayı görür" .-> R
    R -. "run Failed + olay + hata sınıfı" .-> DB[("runs · run_events")]
```

`Order = 30` seçimi zorunludur: doğrulama en içte durmalı ki attığı istisna
telemetri **ve** `run` kaydı tarafından görülsün. En dışta dursaydı `run`
başarılı yazıldıktan sonra hata atardı.

## 131.2 — Ne zaman çalışır

Üç koşulun **üçü** birden gerekir:

1. `AgentPrismStructuredResponseOptions.Enabled` `true`'dur — varsayılanı
   `false`'tur (K1).
2. `descriptor.Model?.ResponseFormat?.Kind` `Json` veya `JsonSchema`'dır.
3. `run` bir yanıt üretmiştir (hata veya iptal ile bitmemiştir).

Üçü sağlanmazsa decorator **hiçbir şey yapmaz** ve iç agent'ı doğrudan çağırır.
Ayar yapılmayan bir kurulumda davranış Faz 130 ile birebir aynıdır.

## 131.3 — Seam

```csharp
public interface IStructuredResponseValidator
{
    ValueTask<StructuredResponseValidationResult> ValidateAsync(
        StructuredResponseValidationContext context,
        CancellationToken cancellationToken = default);
}
```

Varsayılan kayıt `TryAddSingleton` ile eklenen bir **no-op**'tur ve her zaman
`Valid` döner — `IToolArgumentsValidator` ile birebir aynı şekil. Tüketici
kaydı değiştirerek kendi kuralını koyar (K4).

**Fail-closed.** Doğrulayıcı istisna atarsa yanıt **geçersiz** sayılır.
Hata veren bir kapı, kapı değildir. Bu da `IToolArgumentsValidator` ile aynı
kuraldır.

**`Reason` metni ham yanıt taşımaz.** Kalıcı hâle gelen metin
`SafeErrorText.ForPersistence` üzerinden geçer; ham model çıktısı yalnız
mevcut `run` içerik saklama politikası neye izin veriyorsa oraya yazılır, hata
metnine **yazılmaz**.

## 131.4 — `run` kanıtı

| Yüzey | Değişiklik |
|---|---|
| `RunEventType` | Yeni değer: `StructuredResponseRejected`. Listenin **sonuna** eklenir; bugünkü en büyük değer 26 |
| `RunErrorClass` | Yeni değer: `StructuredResponseInvalid`. Listenin **sonuna** eklenir; bugünkü en büyük değer 13. 🚨 **9 kullanılamaz** |
| `RunStatus` | Değişmez — `run` `Failed` olur |
| Yeni istisna | `AgentPrismStructuredResponseException : AgentPrismException` |
| Arayüz | Olay etiketi `en.ts` **ve** `tr.ts`'e girer (eksik anahtar derleme hatasıdır, K-228) |

Olay yükü: `kind` (`Json`/`JsonSchema`), `schemaName`, `reason` (güvenli metin),
`provider`, `model`. Ham yanıt **yükte yoktur**.

## 131.5 — Akış (streaming) davranışı — sınır açıkça yazılır

Akışlı `run`'da içerik istemciye **üretildikçe** gider. Doğrulama son güncelleme
yayınlandıktan sonra çalışır. Yani:

- `run` `Failed` kapanır, olay yazılır, hata sınıfı doğrudur.
- Ama **istemciye giden içerik geri alınamaz.**

Bu bir eksiklik değil, akışın doğasıdır ve `guides/structured-output.md`
sayfasında açıkça yazılır: yapısal çıktıyı **kapı** olarak kullanan bir agent
akış kullanmamalıdır.

🚨 Akışlı yolda decorator, son yanıtı toplamak için güncellemeleri biriktirir.
`Activity.Current` veya `AsyncLocal` bu birikim döngüsünde **açılmaz**; yazım
çağırana geri akmaz ve akışlı yolda her `MoveNextAsync` öncesi tekrarlanması
gerekir (`docs/hafiza/cekirdek-calistirma.md`).

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bölüme yazılır.

```csharp
// AgentPrism.Abstractions
public interface IStructuredResponseValidator
{
    ValueTask<StructuredResponseValidationResult> ValidateAsync(
        StructuredResponseValidationContext context,
        CancellationToken cancellationToken = default);
}

public sealed record StructuredResponseValidationContext
{
    public required string AgentName { get; init; }
    public required Guid RunId { get; init; }
    public string? SessionId { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public required AgentResponseFormatKind Kind { get; init; }
    public JsonElement? Schema { get; init; }
    public string? SchemaName { get; init; }
    public required string ResponseText { get; init; }
}

public sealed record StructuredResponseValidationResult
{
    public static StructuredResponseValidationResult Valid { get; }
    public static StructuredResponseValidationResult Invalid(string reason);
    public bool IsValid { get; }
    public string? Reason { get; }
}

public sealed class AgentPrismStructuredResponseOptions
{
    public const string SectionName = "AgentPrism:StructuredResponse";
    public bool Enabled { get; set; }          // varsayılan false (K1)
}

public sealed class AgentPrismStructuredResponseException : AgentPrismException { }

public enum RunEventType { /* … */ StructuredResponseRejected = <sonraki boş> }
public enum RunErrorClass { /* … */ StructuredResponseInvalid = <sonraki boş> }
```

`Schema` `JsonElement?` olarak geçer — `AgentResponseFormat.Schema` ile aynı
tip. Yeni bir şema temsili üretilmez.

### HTTP `endpoint`'leri

Yeni uç yoktur. `GET /api/runs/{id}/events` yeni olay türünü döndürür;
OpenAPI → TypeScript zinciri yeniden üretilir.

### Arayüz payı

Yalnız bir olay etiketi ve bir hata sınıfı adı. Bugünkü paket:
`index-*.js.br` 148 807 B (bütçe 250 KB gzip). Kapanışta yeniden ölçülür.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/
├── Agents/IStructuredResponseValidator.cs          (yeni)
├── Agents/StructuredResponseValidationContext.cs   (yeni)
├── Agents/StructuredResponseValidationResult.cs    (yeni)
├── Exceptions/AgentPrismStructuredResponseException.cs (yeni)
├── Runs/RunEventType.cs                            (yeni değer)
└── Runs/RunErrorClass.cs                           (yeni değer)

src/AgentPrism.Core/
├── Compilation/StructuredResponseValidatingAgent.cs         (yeni · AIAgent decorator)
├── Compilation/StructuredResponseValidatingAgentDecorator.cs (yeni · IAgentDecorator, Order 30)
├── Compilation/NoOpStructuredResponseValidator.cs           (yeni · TryAddSingleton)
├── AgentPrismStructuredResponseOptions.cs + Validator       (yeni)
├── AgentPrismServiceCollectionExtensions.Registration.Core.cs (kayıt)
├── AgentPrismCoreJsonContext.cs                             (olay yükü)
└── Runs/DefaultRunErrorClassifier.cs                        (yeni sınıfın kuralı)

src/AgentPrism.UI/frontend/src/locales/{en,tr}.ts
tests/AgentPrism.Core.UnitTests/… · tests/AgentPrism.AspNetCore.FunctionalTests/…
docs-site/src/content/docs/guides/structured-output.md · concepts/runs.md · capabilities.md · reference/configuration.md
```

---

## Hata Modları ve Testler

> Doğrulama DI · akış · `run` kaydı sınırlarını geçer. Birim testi bunu
> kanıtlamaz — [`.agents/ortak/test-seviyeleri.md`](../.agents/ortak/test-seviyeleri.md).

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Ayar kapalıyken davranış değişir | Fonksiyonel | `StructuredResponseDisabledTests` — Faz 130 çıktısıyla aynı |
| Geçersiz yanıt başarılı `run` olarak kapanır | Fonksiyonel | `StructuredResponseValidationTests` (fake sağlayıcı ile) |
| Decorator yanlış sırada yerleşir; `run` başarılı yazılır sonra hata atar | Fonksiyonel | `StructuredResponseOrderingTests` — `run` kaydında `Failed` **ve** olay birlikte olmalı |
| Doğrulayıcı istisna atar, yanıt geçerli sayılır | Fonksiyonel | `StructuredResponseFailClosedTests` |
| Akışta doğrulama hiç çalışmaz | Fonksiyonel | `StructuredResponseStreamingTests` |
| `ResponseFormat` `null` iken doğrulayıcı çağrılır | Birim | `StructuredResponseGatingTests` |
| Ham yanıt hata metnine sızar | Fonksiyonel | `StructuredResponseRedactionTests` |
| Yeni `RunErrorClass` değeri 9'u kullanır | Sözleşme | mevcut `RunErrorClassContractTests` |
| Yeni olay OpenAPI/TS istemcisine çıkmaz | E2E | mevcut OpenAPI tazelik kapısı |
| Olay etiketi bir dilde eksik | Birim | mevcut sözlük kapısı (`Messages` tipi, K-228) |
| İptal doğrulama sırasında yok sayılır | Fonksiyonel | `StructuredResponseCancellationTests` |
| Başka kiracının `run`'ında doğrulayıcı yanlış bağlam alır | Fonksiyonel | `StructuredResponseTenantTests` |
| Doğrulayıcı `run` kaydını yazamayınca `run` durur | Fonksiyonel | `StructuredResponseStoreFailureTests` — gözlemlenebilirlik işlevi bozmaz |

Beş soru: **iptal** — doğrulayıcıya `run`'ın `CancellationToken`'ı geçer,
iptal `Canceled` olarak kapanır, `StructuredResponseInvalid` **değil**;
**eşzamanlılık** — doğrulayıcı singleton'dır ve durum tutmamalıdır, XML bunu
yazar; **boş/aşırı girdi** — boş metin ve çok büyük yanıt case'leri;
**başka kiracı** — bağlam kiracıyı taşımaz, doğrulayıcı `ITenantContext`'i
kendisi okur (`IToolArgumentsValidator` ile aynı kural);
**alt sistem hatası** — olay yazımı hata verirse `run` devam eder, hata loglanır.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Ayar yok | `JsonSchema` biçimli agent'ı çalıştır | Davranış Faz 130 ile aynı; yeni olay yok |
| 2 | `Enabled: true`, doğrulayıcı kayıtlı, model geçersiz JSON döndürür | `POST /api/agents/x/run` | `run` `Failed`; hata sınıfı `StructuredResponseInvalid`; `StructuredResponseRejected` olayı var |
| 3 | Aynı, model geçerli JSON döndürür | Aynı | `run` `Completed`; yeni olay yok |
| 4 | Doğrulayıcı istisna atar | Aynı | `run` `Failed` (fail-closed) |
| 5 | Akışlı `run`, geçersiz yanıt | `text/event-stream` ile çağır | İçerik akar; akış bitince `run` `Failed` kapanır; olay yazılır |
| 6 | Arayüz | `run` detayını aç | Olay ve hata sınıfı iki dilde doğru görünür 👤 |
| 7 | Geçersiz yanıt | `run` kaydını oku | Ham model çıktısı hata metninde **yok** |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | AgentPrism yalnız seam mi sevk etsin, yoksa bir de **iyi biçimlilik** (boş değil + `JsonDocument.Parse` geçiyor) kontrolü mü? | A: yalnız seam · B: seam + iyi biçimlilik | **B.** `System.Text.Json` zaten bağımlılıkta, `reflection` yok, yeni paket yok. Tüketicinin saydığı iki hata modunu (boş içerik, kesik akış) doğrudan kapatır ve "şema doğrulaması" değildir. Kullanıcı 2026-09-01'de "yalnız seam" dedi; bu soru o kararın **bu dar kısmını** yeniden sorar, çünkü bedeli sıfır ölçüldü |
| 2 | `Json` (şemasız) biçiminde de doğrulayıcı çağrılsın mı? | A: evet · B: yalnız `JsonSchema` | **A.** Bağlamda `Kind` zaten var; doğrulayıcı isterse geçebilir. İki farklı kapı yazmak yerine tek kapı |
| 3 | Doğrulama sonucu `run` metadata'sına da yazılsın mı, yalnız olaya mı? | A: yalnız olay · B: olay + `run` alanı | **A.** Olay dizisi `IRunStore.ReadEventsAsync` ile okunabilir; `runs` tablosuna sütun eklemek migration açar ve bu fazın migration'ı yok |

---

## Bitiş Ölçütleri (DoD)

- [ ] `Enabled: false` iken davranış Faz 130 ile **birebir** aynıdır (case 1)
- [ ] Geçersiz yanıt `run`'ı `Failed` kapatır; `StructuredResponseInvalid` sınıfı ve `StructuredResponseRejected` olayı yazılır (case 2)
- [ ] Geçerli yanıt hiçbir olay üretmez (case 3)
- [ ] Doğrulayıcı istisnası **geçersiz** sayılır (case 4)
- [ ] Akışta doğrulama son güncellemeden sonra çalışır ve `run` `Failed` kapanır (case 5)
- [ ] Ham model çıktısı hata metnine yazılmaz (case 7)
- [ ] Yeni `RunErrorClass` değeri 9'u kullanmaz; `RunErrorClassContractTests` yeşil
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi (`guides/structured-output.md`, `concepts/runs.md`, `capabilities.md`, `reference/configuration.md`); `npm run build` + `check-links.mjs` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı

### Doğrulama komutları

```bash
# Geçersiz yanıt sonrası run kaydı
curl -s http://localhost:5081/agentprism/api/runs/<id> | jq '.status, .error.class'
# beklenen: "Failed", "StructuredResponseInvalid"

# Olay dizisi
curl -s http://localhost:5081/agentprism/api/runs/<id>/events | jq '.[].type'
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Decorator yanlış katmana konur ve tool turlarını doğrular | `Order = 30` ve `IAgentDecorator` seçimi 131.1'de gerekçesiyle yazılı; `StructuredResponseOrderingTests` konumu ölçer |
| Akışta biriktirme büyük yanıtta bellek şişirir | Yapısal çıktı tek belgedir; sınır `AgentPrismStructuredResponseOptions`'a bir üst sınır olarak eklenmelidir — uygulamada karara bağlanır ve kapanışta yazılır |
| Yerleşik doğrulayıcı beklentisi doğar | `guides/structured-output.md` sınırı açıkça yazar; `capabilities.md` satırı seam olduğunu söyler |
| Yeni `RunErrorClass` değeri arayüz ve TypeScript şemasına geçmez | K-603'ün vakası tam buydu; OpenAPI tazelik kapısı ve sözlük kapısı ikisini de yakalar |
| Doğrulayıcı `run` yolunu yavaşlatır | Doğrulayıcı yalnız `run` sonunda **bir kez** çağrılır; sıcak yolda değildir |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
