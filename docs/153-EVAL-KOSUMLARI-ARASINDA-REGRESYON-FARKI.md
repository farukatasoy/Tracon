# Faz 153 — Eval Koşumları Arasında Regresyon Farkı

> **Durum:** ✅ Tamamlandı (2026-09-07)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-207**
> **Önkoşul:** Yok. [Faz 152](arsiv/fazlar/152-SKORUN-ADI-VE-SEKLI.md) ile **çakışmaz** — o `run_scores`'a, bu `eval_case_results`'a dokunur.
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.PostgreSql`, `AgentPrism.Sqlite`, `AgentPrism.SqlServer`, `AgentPrism.Cli`, `AgentPrism.Testing.Contracts.Xunit`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** Yok — hizalama anahtarı (`EvalCaseResult.CaseId`) ve gereken alanlar mevcut şemadadır
> **Public API:** Büyüyor — bir okuma tipi ailesi + bir `IEvalStore` üyesi + bir CLI seçeneği. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya **1 satır** (ölçüldü 2026-09-07): `IEvalStore`'a üye eklemek üçüncü taraf uygulayıcıyı kırar ve bu **`1.0` öncesi** yapılmalıdır.
> **Tüketici yüzeyi:** site: `docs-site/src/content/docs/concepts/evaluation.md` · üretilen: `http-api/`, `api/` · sevk edilen: `EvalEndpoints` `.WithDescription` metinleri, `EvalRun` XML dokümanı, `agentprism eval` yardım metni
> **Manuel test alanı:** `docs/manuel-test/17-EVAL-VE-DENEYLER.md` · `docs/manuel-test/34-ISTEMCI-VE-CLI.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-232\|K-421\|K-633\|K-641" docs/KARARLAR.md
   ```
   **K-232** (sunucu yanıtları çevrilmez — fark sonucu tek dillidir), **K-421**
   (public API takibi açık), **K-633** (üretilmiş istemcinin tipleri elle
   düzeltildi — yeni uç aynı zincirden geçer), **K-641** (`IJobHandler`
   at-least-once — aynı eval `run`'ı yeniden koşabilir, fark bunu gizlememelidir).
3. [`arsiv/fazlar/115-EVALIN-BASSIZ-KOSUCUSU.md`](arsiv/fazlar/115-EVALIN-BASSIZ-KOSUCUSU.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/115-EVALIN-BASSIZ-KOSUCUSU.md
   ```
   `agentprism eval` komutunun mutlak kapısı (`--min-pass-rate`/`--max-failures`)
   ve CLI test altyapısı (`RealHttpHost` · `CliRunner`) oradan devralınır.
4. Alan hafızası (bu faz üç alana dokunuyor):
   [`hafiza/http-uc-tuzaklari.md`](hafiza/http-uc-tuzaklari.md) (yeni uç, sayfalama,
   `404` semantiği) · [`hafiza/nswag-istemci-uretimi.md`](hafiza/nswag-istemci-uretimi.md)
   (üretilen istemci beş geçişli post-process'ten geçer) ·
   [`hafiza/frontend.md`](hafiza/frontend.md) (fark görünümü, `en.ts`/`tr.ts`)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI.md`](MIMARI.md) — eval veri modeli

---

## Amaç

Ürün karşılaştırmayı **vaat ediyor ama yapmıyor**. Sevk edilen uç metni
*"Comparing entries over time is how a regression between agent versions is
spotted"* diyor; `IEvalStore` ise yalnız **tek bir** koşumu okuyabiliyor. İki
koşumu case bazında hizalamak tüketiciye kalıyor. Aynı boşluk CI kapısında da
var: kapı **mutlaktır**, göreli değildir — `--min-pass-rate 0.85` ayarlıyken
%95 → %90 düşüş kapıyı **geçer**.

Bu bir eksiklik değil bir **tutarsızlıktır**: ürün kendi sevk ettiği metinde bu
işi yaptığını söylüyor.

- **F-207** — iki `EvalRun`'ı case bazında hizalayan bir okuma işlemi, taban
  çizgisine göre CI kapısı ve arayüzde fark görünümü.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`EvalEndpoints.cs:141`](../src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs#L141) | Sevk edilen metin: *"Comparing entries over time is how a regression between agent versions is spotted."* |
| [`IEvalStore.cs:143`](../src/AgentPrism.Abstractions/Evaluation/IEvalStore.cs#L143) | `ListCaseResultsAsync` **tek** bir `evalRunId` alıyor; iki koşumu hizalayan üye yok |
| [`EvalCommand.cs:168`](../src/AgentPrism.Cli/Commands/EvalCommand.cs#L168) | `PassesThreshold(detail.Run, minPassRate, maxFailures)` — kapı **mutlak**; taban çizgisi kavramı yok |
| `grep -n "compare\|previous\|regress\|delta\|baseline" …/eval-run-detail.tsx` | **0 eşleşme** |
| [`EvalCaseResult.cs`](../src/AgentPrism.Abstractions/Evaluation/EvalCaseResult.cs) | `CaseId` · `Passed` · `Output` · `Scores` · `FailureReason` — hizalama anahtarı hazır |
| [`EvalRun.cs`](../src/AgentPrism.Abstractions/Evaluation/EvalRun.cs) | `AgentVersion` · `ModelId` · `Total`/`Passed`/`Failed` — karşılaştırma başlığı hazır |
| [`EvalEndpoints.cs:156-158`](../src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs#L156) | 🚨 *"Per-case results are a retention target, so an old eval run may keep its summary while its details are gone."* |

> Kanıtlar 2026-09-07 tarihinde doğrulandı.

---

## 153.1 — Fark dört kümedir, bir sayı değil

Fark sonucu case'leri **dört kümeye** ayırır. Tek bir "kaç tane bozuldu" sayısı
yetmez; nöbetçi mühendis hangi case'in bozulduğunu ister.

```mermaid
flowchart TD
    A["taban çizgisi koşumu<br/>(baseline)"] --> C{CaseId ile hizala}
    B["karşılaştırılan koşum<br/>(candidate)"] --> C
    C --> D["Fixed — tabanda düştü, adayda geçti"]
    C --> E["Regressed — tabanda geçti, adayda düştü"]
    C --> F["StillFailing — ikisinde de düştü"]
    C --> G["Added — yalnız adayda var"]
    C --> H["Removed — yalnız tabanda var"]
    C --> I["Unchanged — ikisinde de geçti"]
```

`Added`/`Removed` ayrı kümelerdir ve **sessizce yutulmaz**: suite'e case
eklenmesi pass-rate'i değiştirir ve bu bir regresyon değildir. Kapı bunu ayırt
edemezse yanlış alarm verir.

Her fark kalemi iki tarafın `FailureReason`'ını ve `RunId`'sini taşır — bir
regresyon doğrudan iki konuşmaya kadar izlenebilir.

## 153.2 — 🚨 Silinmiş ayrıntı sessiz boş sonuç üretmez

Bu fazın en büyük tuzağı budur ve **sözleşmeyle** çözülür, kodda bir `if` ile değil.

Sevk edilen metin per-case sonuçların bir **retention hedefi** olduğunu yazıyor:
eski bir eval koşumu özetini koruyup ayrıntısını kaybedebilir. Taban çizgisinin
ayrıntısı silinmişse fark **üretilemez**.

Yanlış davranış: boş bir fark döndürmek. Boş fark "hiçbir şey değişmedi" gibi
okunur ve CI kapısı **yeşil** yanar. Bu, bir regresyonu sessizce geçiren en
kötü hata modudur.

Doğru davranış: fark sonucu her iki tarafın ayrıntısının **var olup olmadığını**
açıkça taşır. Ayrıntı yoksa uç `409 Conflict` döner ve gövde hangi tarafın
ayrıntısının eksik olduğunu söyler. CLI bunu çıkış kodu **4** ile ayırır —
"kapı düştü" (3) ile "karşılaştırılamadı" (4) aynı şey değildir.

> Bu davranış `ProblemDetails` ile döner ve K-232 gereği **çevrilmez**.

## 153.3 — 🚨 Case içeriği değişirse fark elma-armut olur

İkinci tuzak: `EvalCaseResult.CaseId` bilerek bir FK taşımıyor. Case içeriği
(`Query`, `ExpectedOutput`, `Context`) iki koşum arasında düzenlenirse aynı
`CaseId` **farklı bir soruyu** gösterir ve fark yanıltır.

Aday metni bunu bir risk satırına taşımıştı; bu faz onu **görünür** kılar,
çözmez. Eval case sürümleme **kapsam dışıdır** (yeni tablo gerektirir).

Yapılan: fark sonucu, hizalanan her case için taban çizgisi tarafındaki ve aday
tarafındaki case **içeriğinin özetini** (`Query`'nin kararlı bir hash'i)
karşılaştırır ve farklıysa kalemi `ContentChanged` bayrağıyla işaretler. Kapı
bu bayraklı kalemleri regresyon **saymaz** ama sayısını rapor eder.

> Hash `Query` + `ExpectedOutput` + `Context` üzerinden hesaplanır ve **saklanmaz** —
> `EvalCase`'in bugünkü hâlinden okunur. Bu bir sınırdır: iki koşum arasında case
> **iki kez** değiştiyse ve bugünkü hâli taban çizgisininkiyle aynıysa bayrak
> yanmaz. Sözleşme bunu açıkça yazar; sessiz bir garanti verilmez.

## 153.4 — CI kapısı göreliye açılır

`agentprism eval` bugünkü iki mutlak seçeneğini **korur** ve bir üçüncüsü
kazanır:

| Seçenek | Anlam | Çıkış kodu |
|---|---|---|
| `--min-pass-rate <0..1>` | Mutlak (mevcut) | 3 |
| `--max-failures <n>` | Mutlak (mevcut) | 3 |
| `--baseline <runId\|previous>` | Karşılaştırılacak koşum | — |
| `--max-regressions <n>` | Taban çizgisine göre en çok kaç case bozulabilir | 3 |

`--baseline previous` aynı suite'in bir önceki **tamamlanmış** koşumunu seçer.
`--max-regressions` `--baseline` olmadan verilirse `CliArgumentException`.

Çıkış kodları: `0` geçti · `2` koşum tamamlanamadı (mevcut) · `3` kapı düştü ·
**`4` karşılaştırılamadı** (153.2).

> `--baseline previous` için "önceki koşum yok" durumu bir **hata değildir**:
> ilk koşumda kapı yalnız mutlak eşiklerle yargılar ve bunu `stderr`'e yazar.
> Aksi hâlde yeni bir suite'in ilk CI koşumu kırmızı yanar.

## 153.5 — Arayüzde fark görünümü

`eval-run-detail.tsx` bir taban çizgisi seçici ve dört kümeyi ayrı gösteren bir
görünüm kazanır. `Regressed` kümesi en üstte ve varsayılan açık gelir; nöbetçi
mühendisin aradığı budur.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions
public interface IEvalStore
{
    // ... mevcut on beş üye değişmez ...

    /// <summary>Aligns two eval runs case by case.</summary>
    /// <remarks>
    /// Throws when the per-case details of either run have been removed by
    /// retention: an empty diff would read as "nothing changed".
    /// </remarks>
    ValueTask<EvalRunDiff> DiffRunsAsync(
        Guid baselineRunId,
        Guid candidateRunId,
        CancellationToken cancellationToken = default);
}

/// <summary>The case-by-case difference between two eval runs.</summary>
public sealed record EvalRunDiff
{
    public required EvalRun Baseline { get; init; }
    public required EvalRun Candidate { get; init; }
    public required IReadOnlyList<EvalCaseDiff> Cases { get; init; }

    /// <summary>The number of cases whose content changed between the two runs.</summary>
    public int ContentChangedCount { get; init; }
}

/// <summary>One case's outcome on both sides.</summary>
public sealed record EvalCaseDiff
{
    public required Guid CaseId { get; init; }
    public required EvalCaseDiffKind Kind { get; init; }
    public bool? BaselinePassed { get; init; }
    public bool? CandidatePassed { get; init; }
    public Guid? BaselineRunId { get; init; }
    public Guid? CandidateRunId { get; init; }
    public string? BaselineFailureReason { get; init; }
    public string? CandidateFailureReason { get; init; }

    /// <summary>True when the case content differs between the two runs.</summary>
    public bool ContentChanged { get; init; }
}

public enum EvalCaseDiffKind
{
    Unchanged = 0,
    Fixed = 1,
    Regressed = 2,
    StillFailing = 3,
    Added = 4,
    Removed = 5,
}
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `GET` | `/api/evals/runs/{id}/diff?baseline={runId}` | Reader (`EvalsRead`) | İki koşumun case bazında farkını döner |

- `404` — koşumlardan biri yok ya da başka kiracıya ait
- `409` — bir tarafın per-case ayrıntısı retention ile silinmiş (153.2)
- `400` — iki koşum farklı suite'lere ait

### Arayüz payı

`eval-run-detail.tsx`'e taban çizgisi seçici + dört kümeli fark görünümü.
Bugünkü ölçüm (2026-09-07): **151.9 KB** brotli / 250 KB bütçe. Yeni metin
anahtarları `en.ts` **ve** `tr.ts`'e girer (K-228).

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Evaluation/
├── IEvalStore.cs           (değişir — DiffRunsAsync)
├── EvalRunDiff.cs          (yeni)
├── EvalCaseDiff.cs         (yeni)
└── EvalCaseDiffKind.cs     (yeni)

src/AgentPrism.Core/Evaluation/
├── InMemoryEvalStore.cs    (değişir)
└── EvalRunDiffBuilder.cs   (yeni — hizalama, üç store'un paylaştığı saf mantık)

src/AgentPrism.PostgreSql/…/PostgreSqlEvalStore.cs    (değişir)
src/AgentPrism.Sqlite/…/SqliteEvalStore.cs            (değişir)
src/AgentPrism.SqlServer/…/SqlServerEvalStore.cs      (değişir)

src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs  (değişir — yeni uç + metin düzeltmesi)
src/AgentPrism.Cli/Commands/EvalCommand.cs            (değişir — --baseline, --max-regressions)
src/AgentPrism.Testing.Contracts.Xunit/Contracts/EvalStoreContract.cs (değişir)
src/AgentPrism.UI/frontend/src/screens/eval-run-detail.tsx            (değişir)

tests/
├── AgentPrism.Core.Tests/Evaluation/EvalRunDiffBuilderTests.cs        (yeni)
├── AgentPrism.AspNetCore.FunctionalTests/Evals/EvalRunDiffTests.cs    (yeni)
└── AgentPrism.Cli.Tests/EvalBaselineGateTests.cs                      (yeni)
```

---

## Hata Modları ve Testler

> Seviyeyi plan seçer. Sınır geçen davranış (DI · HTTP · kiracı · akış · depo ·
> paket) birim testiyle kanıtlanamaz —
> [`.agents/ortak/test-seviyeleri.md`](../.agents/ortak/test-seviyeleri.md).

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| 🚨 Taban çizgisinin ayrıntısı silinmişken fark **boş** döner ⇒ CI yeşil yanar | Sözleşme + Fonksiyonel | `EvalStoreContract`, `EvalRunDiffTests` |
| Aday tarafın ayrıntısı silinmişken aynı hata | Sözleşme | `EvalStoreContract` |
| Suite'e eklenen case `Regressed` sayılır ⇒ yanlış alarm | Birim | `EvalRunDiffBuilderTests` |
| Suite'ten çıkarılan case `Fixed` sayılır | Birim | `EvalRunDiffBuilderTests` |
| İki koşum **farklı suite**'lere ait; fark yine üretilir | Fonksiyonel | `EvalRunDiffTests` |
| İçeriği değişmiş case `ContentChanged` işaretlenmez ⇒ elma-armut | Birim | `EvalRunDiffBuilderTests` |
| Tamamlanmamış (`Running`/`Queued`) koşum taban çizgisi seçilir | Fonksiyonel | `EvalRunDiffTests` |
| K-641: aynı eval `run`'ı yeniden koşulup case sonucu **iki kez** yazılmış; hizalama çift sayar | Sözleşme | `EvalStoreContract` |
| Başka kiracının koşumu taban çizgisi olarak verilir | Sözleşme | `TenantIsolationContract` |
| `--baseline previous` önceki koşum yokken **hata** verir | Fonksiyonel | `EvalBaselineGateTests` |
| `--max-regressions` `--baseline` olmadan sessizce yok sayılır | Fonksiyonel | `EvalBaselineGateTests` |
| Regresyon varken çıkış kodu 0 döner | Fonksiyonel | `EvalBaselineGateTests` |
| Karşılaştırılamama (`409`) çıkış kodu 3 ile karışır | Fonksiyonel | `EvalBaselineGateTests` |
| Binlerce case'li iki koşumda hizalama O(n²) olur | Birim | `EvalRunDiffBuilderTests` (sözlük tabanlı hizalama iddiası) |
| İptal: fark okuması ortasında `CancellationToken` iptal olur | Sözleşme | `EvalStoreContract` |
| `store` hata verir; uç `500` yerine sessiz boş döner | Fonksiyonel | `EvalRunDiffTests` |
| Fark görünümü `en.ts`/`tr.ts` anahtarı eksik | Birim | mevcut `LocaleParityTests` |

Beş soru: **iptal** ✅ · **eşzamanlılık** — fark saf bir okumadır, yazma yolu
yoktur; K-641 çift yazımı satır 8'de kapsanıyor · **boş/aşırı girdi** ✅ (satır
3, 4, 7, 14) · **başka kiracı** ✅ · **alt sistem hatası** ✅.

---

## Manuel Kabul Case'leri

> Kapanışta `docs/manuel-test/17-EVAL-VE-DENEYLER.md` ve
> `docs/manuel-test/34-ISTEMCI-VE-CLI.md` içine eklenecek taslak.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Bir suite iki kez koşulmuş, ikincide bir case bozulmuş | `GET /api/evals/runs/{ikinci}/diff?baseline={birinci}` | `Regressed` kümesinde tam o case; iki taraf da `runId` taşıyor |
| 2 | Aynı | Aynı çağrıyı ters sırayla yap | Aynı case `Fixed` kümesinde |
| 3 | İkinci koşumdan önce suite'e case eklenmiş | Fark al | Yeni case `Added`; `Regressed` **boş** |
| 4 | Taban çizgisi koşumunun per-case ayrıntısı retention ile silinmiş | Fark al | `409`; gövde hangi tarafın eksik olduğunu söylüyor — **boş fark değil** |
| 5 | İki koşum farklı suite'lerden | Fark al | `400` |
| 6 | Başka kiracının koşum id'si | Fark al | `404` |
| 7 | Regresyonlu iki koşum | `agentprism eval <suite> --baseline previous --max-regressions 0` | Çıkış kodu **3**; `stderr` bozulan case'leri sayıyor |
| 8 | Aynı, regresyon yok | Aynı komut | Çıkış kodu **0** |
| 9 | Suite ilk kez koşuluyor | `--baseline previous --max-regressions 0` | Çıkış kodu **0**; `stderr` "önceki koşum yok" diyor |
| 10 | Taban çizgisi ayrıntısı silinmiş | Aynı komut | Çıkış kodu **4** — 3 değil |
| 11 | İki koşum arasında bir case'in `Query`'si değiştirilmiş | Fark al | O kalem `contentChanged: true`; kapı onu regresyon **saymıyor** |
| 12 | 👤 insan gerekir | Arayüzde `eval-run-detail`'de taban çizgisi seç | Dört küme ayrı; `Regressed` en üstte ve açık |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `DiffRunsAsync` `IEvalStore`'da mı, ayrı bir okuma servisinde mi? | A: `IEvalStore` üyesi · B: `EvalRunDiffBuilder` + mevcut `ListCaseResultsAsync` | **B'nin mantığı + A'nın imzası**: hizalama `EvalRunDiffBuilder`'da saf kalır, `IEvalStore` uygulayıcıları onu çağırır. Böylece üçüncü taraf uygulayıcı hizalamayı yeniden yazmak zorunda kalmaz — üye eklemek yine de kırıcıdır ve `1.0` öncesi yapılır |
| 2 | Ayrıntı silinmişse `409` mu, `404` mü? | A: `409 Conflict` · B: `404` | **A** — kaynak vardır (koşum duruyor), karşılaştırma yapılamaz. `404` "koşum yok" ile karışır |
| 3 | `ContentChanged` hash'i neyi kapsar? | A: `Query` · B: `Query`+`ExpectedOutput`+`Context` | **B** — beklenen çıktı değişimi de farkı elma-armut yapar |
| 4 | Fark sayfalanır mı? | A: hayır, tam liste · B: `skip`/`take` | **B** — mevcut eval uçları offset tabanlı sayfalama kullanıyor; binlerce case'li suite tek yanıta sığmaz. Özet sayılar sayfalamadan bağımsız döner |
| 5 | `--baseline` bir agent **sürümü** de alabilsin mi (`--baseline v3`)? | A: yalnız `runId`/`previous` · B: sürüm de | **A** — sürüm başına birden çok koşum olabilir; hangisi seçilir sorusu belirsizdir. Kapsam dar tutulur |

---

## Bitiş Ölçütleri (DoD)

- [x] `GET /api/evals/runs/{id}/diff?baseline={runId}` altı kümeyi ayrı ayrı döner
- [x] Suite'e eklenen case `Added`'dir, `Regressed` **değildir**
- [x] Taban çizgisinin ayrıntısı silinmişken uç `409` döner — **boş fark dönmez**
- [x] `agentprism eval <suite> --baseline previous --max-regressions 0` regresyonda çıkış kodu **3** verir
- [x] Karşılaştırılamama çıkış kodu **4** verir; 3 ile karışmaz
- [x] İlk koşumda `--baseline previous` kapıyı kırmızı yakmaz
- [x] ~~İçeriği değişen case `contentChanged` ile işaretlenir~~ — **kapsamdan çıkarıldı** (K-716 👤). Plan kendisiyle çelişiyordu ve bayrak hiçbir zaman yanamazdı; sınır `EvalRunDiff` XML dokümanına ve siteye yazıldı
- [x] `EvalEndpoints.cs:141`'deki sevk edilen metin artık **doğrudur** (uç gerçekten karşılaştırıyor)
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek iki eval koşumu + fark alındı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `17-EVAL-VE-DENEYLER.md` ve `34-ISTEMCI-VE-CLI.md` içine eklendi
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/concepts/evaluation.md` güncellendi; `npm run build` + `check-links.mjs` temiz
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı
- [x] OpenAPI → NSwag → TypeScript zinciri yeniden üretildi; üretilen istemci derleniyor

### Doğrulama komutları

```bash
# Fark dört kümeyi ayırıyor mu
curl -s "http://localhost:5081/agentprism/api/evals/runs/$SECOND/diff?baseline=$FIRST" \
  | jq '[.cases[] | .kind] | group_by(.) | map({(.[0]): length}) | add'

# Göreli kapı regresyonda düşüyor mu
agentprism eval smoke --baseline previous --max-regressions 0; echo "exit=$?"
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 Silinmiş ayrıntı sessiz boş fark üretir ⇒ regresyon CI'dan geçer | `409` + çıkış kodu 4; sözleşme testi dört koşumda birden |
| 🚨 Case içeriği değişince fark elma-armut olur | `ContentChanged` bayrağı; kapı bayraklı kalemi regresyon saymaz; sınırı sözleşmede yazılı |
| `IEvalStore`'a üye eklemek üçüncü taraf uygulayıcıyı kırar | `PublicAPI.Shipped.txt` **boştur** (ölçüldü) — `1.0` öncesi yapılır. Hizalama mantığı `EvalRunDiffBuilder`'da paylaşılır, yeniden yazılmaz |
| Yeni çıkış kodu (4) mevcut CI script'lerini bozar | Kod **eklenir**, mevcutların anlamı değişmez; yardım metni ve site tablosu güncellenir |
| Binlerce case'te hizalama yavaşlar | Sözlük tabanlı tek geçiş; sayfalama (Açık Soru 4) |
| Fark yanıtı iki koşumun `Output` metnini taşırsa yanıt şişer | `Output` fark kalemine **girmez**; yalnız `RunId` ve `FailureReason` |
| Üretilen istemcinin tipi elle düzeltme ister (K-633) | `nswag-postprocess-client.py` koşumu DoD'de; `hafiza/nswag-istemci-uretimi.md` okunur |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

| Plan | Gerçek | Neden |
|---|---|---|
| Dosya listesi üç ayrı SQL store'a (`PostgreSqlEvalStore`, `SqliteEvalStore`, `SqlServerEvalStore`) dokunacağını söylüyordu | **Tek** `src/AgentPrism.Sql.Shared/Stores/SqlEvalStore.cs` değişti | Plan bayattı: Faz 94 (SQL Tek Kaynak) üç store'u birleştirmişti. Üç uygulama yerine iki oldu (bellek içi + paylaşılan SQL) |
| `EvalRunDiffBuilder` `AgentPrism.Core`'a konacaktı | `AgentPrism.Abstractions`'a kondu ve **public** | Açık Soru 1 "üçüncü taraf uygulayıcı hizalamayı yeniden yazmak zorunda kalmasın" diyordu. Üçüncü taraf `Core`'u değil `Abstractions`'ı referans eder; `Core`'da olsaydı kurallar yeniden yazılırdı (K-714) |
| `DiffRunsAsync(Guid baselineRunId, Guid candidateRunId, ct)` | `DiffRunsAsync(EvalRunDiffQuery query, ct)` | Planın imzasında **kiracı yoktu** — depo kiracı yalıtımını zorlayamazdı, oysa planın kendi test tablosu `TenantIsolationContract` istiyordu. Sayfalama da (Açık Soru 4: evet) imzaya girmeliydi. `EvalRunQuery`'nin var olan deseni izlendi |
| `ContentChanged` / `ContentChangedCount` public API'ye girecekti | **Girmedi** (kullanıcı kararı) | Plan kendisiyle çelişiyordu: hash "bugünkü `EvalCase`'ten" okunacaktı ama iki taraf aynı case'i okur, bayrak hiç yanamazdı. Ayrıca ölçüldü — `EvalCaseInput` `Id` taşımaz, `PUT /cases` her düzenlemede yeni id atar, yani sevk edilen yüzeyde içerik değişimi zaten `Added`+`Removed`'dır. Sınır sözleşmeye yazıldı (K-716) |
| Tamamlanmamış koşumun beklenen sonucu yazılı değildi | Yalnız `Completed` karşılaştırılır; diğeri `400` (kullanıcı kararı) | `Pending` bir koşumun `Total`'ı 0'dır ve "ayrıntı silinmiş" sezgisini yanlış tetikliyordu (K-717) |
| Fark sonucu yalnız `Cases` + `ContentChangedCount` taşıyacaktı | Altı kümenin her biri için ayrı sayaç + `TotalCases` | Açık Soru 4 sayfalamayı seçti; sayfalanan bir listeden küme sayıları okunamaz. Sayaçlar sayfalamadan bağımsızdır |
| DoD `agentprism eval <suite>` yazıyordu | Komut `--suite <ad>` alır | Faz 115'ten devralınan mevcut imza; değiştirmek kırıcı olurdu |
| `EvalCaseDiff` `Output` taşımayacaktı | Taşımıyor | Plan korundu — yanıt şişmez |
| Planda olmayan: `EvalRunDiffUnavailableException` + `EvalRunDiffUnavailableReason` | Eklendi | Plan "throws" diyordu ama tipi adlandırmıyordu. Uç `409`/`400` ayrımını yapabilmek için sebep makine tarafından okunabilir olmalı (K-715) |
| Planda olmayan: kültür bağımsız biçimleme düzeltmesi (3 yer) | Yapıldı | Faz DoD'sinin "örnek uygulamayla gerçek koşum" adımında bulundu: `agentprism eval` tr-TR bir makinede "in 3,7 s" yazıyordu. Sınıf **ölçülerek** daraltıldı — `0.0`/`0.000000`/`P0` riskli, `F0`/`0` değil; ilk taramada şüphelenilen üç `:F0` yeri geri alındı (K-720) |
| Planda olmayan: `karar-damit`'in "işaretçisi var, atla" kuralı kaldırıldı | Yapıldı | Karar defteri bütçesi aşıldı; kural "taşı, silme" olduğu için önce taşıma denendi ve aracın kendisi kusurluydu (K-721) |

## Bu Fazda Verilen Kararlar

K-714 · K-715 · K-716 👤 · K-717 👤 · K-718 · K-719 · K-720 · K-721 · K-722 👤 · K-723 —
[`KARARLAR-INDEKS.md`](KARARLAR-INDEKS.md).

## Gerçekleşen Public API

```csharp
// AgentPrism.Abstractions
public interface IEvalStore
{
    // ... mevcut on beş üye değişmedi ...
    ValueTask<EvalRunDiff?> DiffRunsAsync(EvalRunDiffQuery query, CancellationToken cancellationToken = default);
}

public sealed record EvalRunDiffQuery
{
    public required string TenantId { get; init; }
    public required Guid BaselineRunId { get; init; }
    public required Guid CandidateRunId { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 50;
}

public sealed record EvalRunDiff
{
    public required EvalRun Baseline { get; init; }
    public required EvalRun Candidate { get; init; }
    public required IReadOnlyList<EvalCaseDiff> Cases { get; init; }   // istenen sayfa
    public required int TotalCases { get; init; }
    public required int UnchangedCount { get; init; }
    public required int FixedCount { get; init; }
    public required int RegressedCount { get; init; }
    public required int StillFailingCount { get; init; }
    public required int AddedCount { get; init; }
    public required int RemovedCount { get; init; }
}

public sealed record EvalCaseDiff
{
    public required Guid CaseId { get; init; }
    public required EvalCaseDiffKind Kind { get; init; }
    public bool? BaselinePassed { get; init; }
    public bool? CandidatePassed { get; init; }
    public Guid? BaselineRunId { get; init; }
    public Guid? CandidateRunId { get; init; }
    public string? BaselineFailureReason { get; init; }
    public string? CandidateFailureReason { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<EvalCaseDiffKind>))]
public enum EvalCaseDiffKind { Unchanged = 0, Fixed = 1, Regressed = 2, StillFailing = 3, Added = 4, Removed = 5 }

public static class EvalRunDiffBuilder
{
    public static EvalRunDiff Build(
        EvalRun baseline, EvalRun candidate,
        IReadOnlyList<EvalCaseResult> baselineResults, IReadOnlyList<EvalCaseResult> candidateResults,
        int skip = 0, int take = int.MaxValue);
}

public sealed class EvalRunDiffUnavailableException : AgentPrismException
{
    public const string EvalRunDiffUnavailableErrorType = "eval_run_diff_unavailable";
    public EvalRunDiffUnavailableException();
    public EvalRunDiffUnavailableException(string message);
    public EvalRunDiffUnavailableException(string message, Exception innerException);
    public EvalRunDiffUnavailableException(EvalRunDiffUnavailableReason reason, string message);
    public EvalRunDiffUnavailableReason Reason { get; }
    public override string ErrorType { get; }
}

public enum EvalRunDiffUnavailableReason { Unspecified = 0, DifferentSuites = 1, RunNotCompleted = 2, DetailsRemoved = 3 }
```

Yedi yeni public tip; `AgentPrism.Abstractions` 381 → **388**
(`public-surface-baseline.txt`).

### HTTP

| Metot | Yol | Rol · scope | Yanıtlar |
|---|---|---|---|
| `GET` | `/api/evals/runs/{id:guid}/diff?baseline={runId}&skip=&take=` | Reader · `EvalsRead` | `200` `EvalRunDiff` · `400` farklı suite / tamamlanmamış koşum · `404` bilinmeyen ya da başka kiracının koşumu · `409` ayrıntı retention ile silinmiş |

`take` `1..500` arasına kelepçelenir (varsayılan 50); `skip` negatif olamaz.

### CLI

`agentprism eval` iki seçenek kazandı — `--baseline <runId|previous>` ve
`--max-regressions <n>` — ve dördüncü bir çıkış kodu: **`4` = karşılaştırılamadı**.

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/Evaluation/
├── IEvalStore.cs                       (değişti — DiffRunsAsync + EvalRunDiffQuery)
├── EvalRunDiff.cs                      (yeni)
├── EvalRunDiffBuilder.cs               (yeni — plan Core diyordu)
└── EvalRunDiffUnavailableException.cs  (yeni — planda yoktu)

src/AgentPrism.Core/Evaluation/InMemoryEvalStore.cs      (değişti)
src/AgentPrism.Sql.Shared/Stores/SqlEvalStore.cs         (değişti — üç dialekt tek dosya)
src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs     (değişti — yeni uç + metin düzeltmesi)
src/AgentPrism.Cli/Commands/EvalCommand.cs               (değişti)
src/AgentPrism.Cli/Program.cs · README.md                (değişti — yardım metni)
src/AgentPrism.Testing.Contracts.Xunit/Contracts/EvalStoreContract.cs (değişti — 10 sözleşme testi)
src/AgentPrism.UI/frontend/src/screens/eval-run-detail.tsx            (değişti)
src/AgentPrism.UI/frontend/src/lib/server-types.ts                    (değişti)
src/AgentPrism.UI/frontend/src/locales/{en,tr}/workflows.ts           (değişti — 20 anahtar)
scripts/dokuman-bakim.py                                              (değişti — K-721)
docs-site/src/content/docs/capabilities.md · guides/cli.md · concepts/evaluation.md

Kültür düzeltmesi (K-720, faz dışı kusur):
src/AgentPrism.Abstractions/Runs/AgentRunBudget.cs
src/AgentPrism.AspNetCore/RateLimiting/PreflightGate.cs

tests/
├── AgentPrism.Core.UnitTests/Evaluation/EvalRunDiffBuilderTests.cs      (yeni — 17)
├── AgentPrism.Core.UnitTests/Architecture/InvariantShippedTextTests.cs  (yeni — 1)
├── AgentPrism.AspNetCore.FunctionalTests/EvalRunDiffTests.cs            (yeni — 10)
├── AgentPrism.Cli.FunctionalTests/EvalBaselineGateTests.cs              (yeni — 10)
└── src/AgentPrism.UI/frontend/src/screens/eval-run-detail.test.tsx      (yeni — 1)

Üretilen: docs/openapi/agentprism.json · packages/agentprism-client/src/schema.ts ·
src/AgentPrism.Client/Generated/* · docs-site/public/llms*.txt
```

## Örnek Uygulama Koşumu (kanıt)

`samples/AgentPrism.Api` gerçek bir OpenAI binding'iyle ayağa kaldırıldı
(`gpt-5.4-mini`), `diff-demo` takımı iki vakayla kuruldu ve iki kez koşuldu.
İkinci koşumdan önce **yalnız takımın `checks` alanı** sıkıldı — vakaların
kendisi değil, çünkü `PUT /cases` her düzenlemede yeni vaka kimliği atar ve
düzenlenmiş bir vaka regresyon değil `Added`+`Removed` olur.

```
$ curl -s ".../evals/runs/$SECOND/diff?baseline=$FIRST" | jq '[.cases[].kind] | group_by(.) | ...'
{'Regressed': 2}
$ curl -s ".../evals/runs/$FIRST/diff?baseline=$SECOND" | jq ...      # ters yön
{'Fixed': 2}
$ curl -s -o /dev/null -w "%{http_code}" ".../diff?baseline=<olmayan>"
404

$ agentprism eval --url ... --suite diff-demo --baseline $FIRST --max-regressions 0
Completed: 0/2 passed in 3.7 s.
  FAILED case 01a07b03-1547-7ea3-…: keyword_check: Missing keywords: zzz-never-said
  FAILED case 01a07b03-1547-7434-…: keyword_check: Missing keywords: zzz-never-said
vs baseline 01a07b03-3889-77f2-…: 2 regressed, 0 fixed, 0 added, 0 removed.
  regressed: case 01a07b03-1547-7434-…: keyword_check: Missing keywords: zzz-never-said
  regressed: case 01a07b03-1547-7ea3-…: keyword_check: Missing keywords: zzz-never-said
exit=3

$ agentprism eval ... --baseline previous --max-regressions 0   # önceki koşum da düşüktü
vs baseline 01a07b03-a27c-7bf0-…: 0 regressed, 0 fixed, 0 added, 0 removed.
exit=0

$ agentprism eval ... --max-regressions 0                       # --baseline YOK
'--max-regressions' needs '--baseline <runId|previous>'; …
exit=1
$ agentprism eval ... --baseline yesterday
exit=1
```

Bu koşum K-720'yi de ortaya çıkardı: `in 3,7 s` satırı tr-TR bir makinede
ondalık virgülle yazılıyordu (yukarıdaki blokta düzeltme sonrası hâli).

**Arayüz payı** (faz sonrası ölçüm): **153.4 KB** brotli / 250 KB bütçe
(faz öncesi 151.9 KB; 18 sözlük anahtarı ve fark görünümü +1.5 KB).

## Denetim Bulguları

Bağımsız denetçi (taze bağlam, yalnız DoD + diff) **üç 🔴 ve beş 🟡** buldu.
Hepsi kapatıldı; hiçbiri gerekçeyle geçilmedi.

### 🔴

| Bulgu | Kök neden | Sonuç |
|---|---|---|
| **Kısmen budanmış koşum `409` vermiyordu.** `RequireDetails` yalnız "hiç satır yok" durumunu reddediyordu | `IRetentionStore.DeleteBatchAsync` **parça parça** siler ve sweep iki parti arasında durabilir; kalan satırlarla fark üretmek silinen her vakayı `Removed` yapar ve regresyon sayısını sessizce daraltır — kapının yeşil yandığı yol | Denetim SAYIYOR: `distinct case < run.Total` ⇒ `DetailsRemoved`. Birim + sözleşme testi eklendi (`A_PARTLY_trimmed_run_…`, `DiffRunsAsync_throws_when_retention_removed_only_SOME_…`). Tekrarların yanlış alarm üretmediği ayrıca test edildi |
| **`--json --baseline` birlikte verilince `stdout` ayrıştırılamaz oluyordu** | Özet satırı koşulsuz `Console.WriteLine`'daydı; `MT-CLI-021`'in sözünü bozuyordu | `--json` altında özet `stderr`'e gider. `Json_output_stays_a_single_parseable_document_with_a_baseline` |
| **`docs/manuel-test/34-ISTEMCI-VE-CLI.md` hiç güncellenmemişti** — DoD satırı `[x]` ama CLI case'leri yoktu | Yeni çıkış kodu ve iki seçenek manuel kabul setinde ölçülmüyordu | `MT-CLI-023`…`031` eklendi (dokuzu da otomasyon karşılığıyla) |

### 🟡

| Bulgu | Sonuç |
|---|---|
| **Test tiyatrosu:** `An_exhausted_token_budget_…` hiçbir koşulda düşemezdi — `long` + format belirteci yok | Kaldırıldı. Yerine **ölçüm** kondu: tr-TR'de `0.0`/`0.000000`/`P0` farklı, **`F0` ve `0` DEĞİL**. Bu yüzden ilk taramada "düzeltilen" üç `:F0` yeri **geri alındı** — orada kusur yoktu. Gerçek sınıf üç yerdir; biri (`AgentRunBudget`) düşen-sonra-geçen bir testle kilitli, diğer ikisi (`PreflightGate`, `EvalCommand`) host/süreç sınırının arkasında ve **kapsanmadıkları açıkça yazıldı** — kültür kapsamlı bir test paralel testlere sızardı |
| **Arayüzde sayaç tüm farkı, tablo yalnız ilk sayfayı gösteriyordu** — rozet "200" derken gövde "bu grupta durum yok" diyebilirdi; fazın kendi tezinin arayüz karşılığı | İstemci `take=500` (sunucu tavanı) ister; ayrıca grup `N/M` söyler ve sayfa dışı kalan varsa bunu yazar (`evals.diff.partialPage`, `evals.diff.beyondPage`) |
| **İki DoD satırı kanıtsız `[x]`** (örnek uygulama çıktısı, bundle ölçümü) | Yukarıdaki "Örnek Uygulama Koşumu" bölümü eklendi; bundle faz sonrası yeniden ölçüldü |
| **Bütçe tavanı yükseltildi, oysa kural "taşı"** | Önce TAŞIMA denendi: `karar-damit`'in "işaretçisi var, atla" kuralı kaldırıldı (işaretçi ≠ sınıra indirilmiş) ve 1.527 B geri kazanıldı. Yetmedi — kalan büyüme satır **iskeletinde** ve o kesilmez. Tavan ölçümle yeniden kondu ve **K-721** olarak yazıldı |
| **`capabilities.md` yeni yeteneği yazmıyordu** | İki satır eklendi (koşum karşılaştırması · göreli CI kapısı) ve CLI satırı güncellendi. Bu iki satır **iki ayrı kusuru** açığa çıkardı: sevk edilen agent haritası Faz 148'den beri tavanına dayalıydı ve en kısa satır bile taşırıyordu (tavan 10 → 11 KiB, **K-722** 👤), ve üreteç bir hücredeki kaçırılmış boruda (`\|`) hücreyi kesip haritaya sarkan bir ters bölü sevk ediyordu (**K-723**, regresyon testi `build-agent-map.test.mjs`) |

### 🟢 (kapsam dışı gözlem, yine de kapatıldı)

- Yeni karar satırları tablonun dördüncü sütununu kaybetmişti (benim düzenleme
  hatam) — geri kondu.
- `ApplyBaselineGateAsync` **her** HTTP hatasını `4` sayıyordu; artık yalnız
  ucun karşılaştırmayı REDDETTİĞİ üç kod (`400`/`404`/`409`) `4`'tür, kimlik ve
  sunucu arızası `2` kalır (README'nin zaten söylediği şey).
- `--baseline` verilip `--max-regressions` verilmeyince komut rapor üretir ama
  düşmez; bu artık yardım metninde, README'de ve sitede **yazılı**.

Kapı koşumu ayrıca üç taban çizgisi/kapsam kalemi buldu (izole koşumda da düştüler):

| Bulgu | Kök neden | Düzeltme |
|---|---|---|
| `ShippedDocumentationSelfContainmentTests` | `EvalRunDiffUnavailableException`'ın XML dokümanında 🚨 vardı; sevk edilen metin geliştirme günlüğünün sesini taşıyamaz | Emoji kaldırıldı — taban çizgisi yükseltilmedi, kaynak düzeltildi |
| `PublicSurfaceBaselineTests` | Yedi yeni public tip | Taban çizgisi 381 → 388 (bilinçli yüzey büyümesi) |
| `TenantCoverageTests` | Yeni depo metodu ne test edilmiş ne muaf tutulmuştu | `DiffRunsAsync` kapsanan listeye eklendi — `EvalStoreContract` onu üç dialektte de kiracı sınırında ölçüyor |

Yazarken bulunan iki kusur:

- **Bellek içi depo `CancellationToken`'ı görmüyordu.** Sözleşme testi
  (`DiffRunsAsync_observes_cancellation`) düştü; iki uygulamaya da açık
  `ThrowIfCancellationRequested()` eklendi. Birim testi bunu kanıtlayamazdı —
  planın test tablosu bu satırı doğru biçimde **sözleşme** seviyesine koymuştu.
- **Kültür bağımlı biçimleme (K-720).** DoD'nin örnek uygulama adımında görüldü;
  sınıf tarandı, altı yer düzeltildi, davranışsal bir regresyon testi yazıldı ve
  düzeltme geri alınarak testin GERÇEKTEN kırmızı olduğu doğrulandı.

`docs/KARARLAR.md` bütçesi bu fazda aşıldı (389.987 B, 13 bayt boşluk).
`karar-damit` geri kazanamaz — 720 satırın tamamı işaretçili olduğu için
"zaten damıtılmış" sayılıp atlanıyor (`--sinir 330` kuru koşumda yalnız 129 B).
Sınır 420.000'e yükseltildi; gerekçe `scripts/dokuman-bakim.py` içinde yazılı.

## Sonraki Faza Devir Notu

- **`EvalRunDiffBuilder` artık üçüncü tarafın hizalama sözleşmesidir.** Yeni bir
  küme eklemek (`EvalCaseDiffKind`'a append) hem sayaç alanı hem arayüz grubu
  hem site tablosu ister; enum değer sırası **değişmez**.
- **Fark, case içeriğini koşum başına saklamaz** (K-716). Biri gerçekten
  elma-armut tespiti isterse yol `eval_case_results`'a nullable bir
  `case_content_hash` sütunu (üç dialekt + migration) ve eval koşucusunun onu
  yazmasıdır; eski satırlar `null` kalır ve bu sözleşmede yazılı olmalıdır.
- **Kültür sınıfının kapısı yoktur.** CA1305 format belirteçli interpolasyonu
  GÖRMEZ (ölçüldü: `warning`'e çekildiğinde altı ihlal dururken sıfır bulgu).
  Bugünkü guard davranışsaldır (`InvariantShippedTextTests`, iki vaka). Kalıcı
  kapı bir kaynak taraması olurdu — `docs/ADAYLAR.md` kalemi hak eder.
- **`--baseline previous` en yeni 50 koşuma bakar** (`PreviousRunSearchWindow`).
  Bir suite 50'den fazla ardışık başarısız/iptal koşum biriktirirse taban çizgisi
  bulunamaz ve kapı atlanır (sessizce değil — `stderr`'e yazar).
