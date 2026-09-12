# Faz 155 — Kalibre Edilmiş Evaluator Kataloğu

> **Durum:** ✅ Tamamlandı (2026-09-08)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-210**
> **Önkoşul:** [Faz 152](152-SKORUN-ADI-VE-SEKLI.md) — adlı ve tipli skor kaydını (`RunScore.Name`/`Kind`/`Value`) getirir. 🚨 Yalnız **kayıt** şeklini açtı; yargıcın **dönüş** şeklini açmadı — bu fazın ilk işi odur.
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core`
> **Yeni paket:** `Microsoft.Extensions.AI.Evaluation.Quality` 10.9.0 — K-007 gerekçesi §155.4'te; net 1 paket, geçişli ağırlık 0 (ölçüldü 2026-09-07) · **Migration:** Yok — `run_scores` şekli yeterlidir
> **Public API:** Büyüyor **ve bir sözleşmeyi değiştiriyor** — `RunJudgment`. Bugün ucuz: `wc -l src/*/PublicAPI.Shipped.txt` = **17 satır** (yalnız `#nullable enable` başlıkları), yani hiçbir yüzey yayımlanmadı. İlk yayından sonra aynı değişiklik **kırıcıdır**.
> **Tüketici yüzeyi:** `docs-site/src/content/docs/concepts/evaluation.md` (yargıç bölümü) · sevk edilen: `IRunJudge`/`RunJudgment` XML dokümanı, `capabilities.md` satırı
> **Manuel test alanı:** [`docs/manuel-test/17-EVAL-VE-DENEYLER.md`](../../manuel-test/17-EVAL-VE-DENEYLER.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show b16729d5:docs/arsiv/fazlar/155-KALIBRE-EDILMIS-EVALUATOR-KATALOGU.md
> ```
>
> Damıtıldı 2026-09-08 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon'in bugün **tek** yerleşik yargıcı var ve o da elle yazılmış tek bir genel kalite prompt'u. Microsoft on bir kalibre edilmiş evaluator sevk ediyor; üçü doğrudan agent işidir. Bu faz o kataloğu bağlanabilir kılar. Tüketici "cevap alakalı mı", "tool doğru çağrıldı mı", "görev yerine getirildi mi" sorularını **kendi prompt'unu yazmadan** ölçer.

## Bitiş Ölçütleri (DoD)

- [x] Kayıtlı bir `.Quality` evaluator'ı ile koşulan run, **metrik başına** ayrı `run_scores` satırı üretir; `GET /api/runs/{id}/feedback` çıktısı belgeye yazıldı (§ *Gerçek koşum kanıtı*)
- [x] Hiçbir evaluator kaydedilmediğinde davranış bugünküyle **birebir** aynı (K1) — `Registering_no_evaluator_leaves_the_run_exactly_as_it_was` + manuel EVAL-137
- [x] Ölçüm üretmeyen metrik `Value = null` yazar, `0` değil (K-711) — birim + fonksiyonel test, manuel EVAL-138
- [x] `maf-api-kesfi` koşuldu; `IEvaluator`/`IAgentEvaluator` gerçek imzaları belgeye yazıldı (§ *Ölçülen MAF imzaları*) — üç tespit planı değiştirdi
- [x] Yeni paketin geçişli ağırlığı **gerçek restore** ile sayıldı ve belgeye yazıldı (§ *Yeni paketin gerçek ağırlığı*) — sevk edilen grafiğe giren yeni paket: **0**
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 628d3c71`
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (gerçek OpenAI modeli), çıktı belgeye yazıldı (§ *Gerçek koşum kanıtı*)
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` → `Tarama: ✅ temiz`
- [x] Manuel kabul case'leri `docs/manuel-test/17-EVAL-VE-DENEYLER.md` içine eklendi (**EVAL-136…142**); otomatikleştirilebilenlerin tamamı `EvaluatorJudgeEndToEndTests` olarak koşuldu
- [x] `faz-denetim` koşuldu; **iki 🔴 bulundu ve kapatıldı** (§ *Denetim Bulguları*), her ikisi de düşen testle kanıtlandı
- [x] `docs-site/` güncellendi (`concepts/evaluation.md` iki yeni bölüm · `guides/write-your-own-judge.md` yeni sözleşme · `capabilities.md`); `npm run check` dördü de temiz (content · build · links · weight)

### Doğrulama komutları

```bash
# Metrik başına ayrı skor satırı (🚨 uç `/scores` DEĞİL `/feedback`'tir)
curl -s http://localhost:5081/tracon/api/runs/$RUN_ID/feedback | jq '[.[] | {name, kind, value}]'

# Yeni paketin gerçek geçişli ağırlığı
dotnet list src/Tracon.Core package --include-transitive | grep -c Evaluation
```

---

## Plandan Sapmalar

| Plan ne diyordu | Ne oldu | Neden |
|---|---|---|
| **Ad şeması `{judge}:{metrik}`** (Açık Soru 2, öneri B) | `{judge}.{metrik}` | 🚨 `:` **yasaktır.** `RunScoreRules.IsValidName` yalnız `[A-Za-z0-9._-]` kabul eder ve `OnlineEvalCheckpointTests` `"a:b"` adını zaten reddediyordu. Planın önerdiği ayırıcı yazma anında `ArgumentException` üretirdi; kararın **özü** (önek ile yapısal çakışma engeli) korundu, ayırıcı `.` oldu |
| **`IAgentEvaluator` `Microsoft.Extensions.AI.Evaluation` içinde** | `Microsoft.Agents.AI` içinde | `maf-api-kesfi` ölçtü. Seam bu yüzden **hiç yeni paket istemiyor** — `Microsoft.Agents.AI` zaten referanslı |
| **`AddAgentEvaluator<T>() where T : IAgentEvaluator`** | `AddEvalEvaluatorFactory` + yeni public `IEvalEvaluatorFactory` | 🚨 Çıplak bir `IAgentEvaluator` singleton'ı suite'in `Checks` alanını **sessizce yok sayardı** — `EvalJobHandler` check'siz suite'i zaten reddediyor. Fabrika suite'in derlenmiş check'lerini alır; yok sayması artık kaza değil, tercih. K1 (sıfır sürpriz) bunu gerektirdi |
| **`.Quality` `Core`'a referans olur** (Açık Soru 4, öneri A) | Hiçbir sevk edilen pakete girmedi; `Core` yalnız `.Evaluation`'ı **açık** referansladı | Kullanıcı kararı. Köprü yalnız `IEvaluator` arayüzüne bağlıdır; katalog isteyen tüketici paketi kendisi ekler. Katalog kullanmayanın grafiği büyümez |
| Plan `JudgeScore.Comment` için bir kural yazmıyordu | `Interpretation`/`Diagnostics` yorumu besliyor | Ölçüm üretmeyen bir metrik `Value = null` yazıyor; **neden** ölçemediği yalnız `Diagnostics` içindedir. Onsuz `null` satır okuyucuya açıklamasız ulaşırdı |
| Plan yargıç metrik ölçümüne değinmiyordu | 🚨 **Manşet skor kuralı** eklendi | Kalibre evaluator'lar **1-5** ölçeğinde puan verir; `RecordScoreAsync`/`tracon.judge.score` **0-100** tanımlıdır. Köprü skorları o ortalamaya girseydi 4 puanlık sağlıklı bir koşum `LowScoreThreshold`'un altına düşer ve `run.score.low` alarmı **sürekli** yanlış çalardı. Kural yapısaldır: yalnız adı yargıcın adına **eşit** olan skor pencereye girer |
| Plan `A_judge_name_..._is_refused_at_the_score_write` davranışını korumayı öngörmüyordu | Davranış **bilerek** değişti: `ArgumentException` fırlatmak yerine `judge_contract` `JudgeFailure`'ı raporlanıyor | Tek bozuk yargıç tüm işi düşürmemelidir (Manuel case 5 ile aynı kural). Ayrıca doğrulama **ilk yazmadan önce** toplu yapılıyor: yarım yazılmış bir skor kümesi hiç yazılmamış olandan kötüdür |
| `RecordJudgeScore`/`RecordScoreAsync` `int` alıyordu | `double` | `JudgeScore.Value` `double?` (K-711). Histogram zaten `Histogram<double>` idi; `int` imzası tek daraltma noktasıydı |
| Plan `EvaluatorRunJudge` için ayrı bir çağrı bütçesi bağlaması öngörüyordu | Ek kod **gerekmedi** | Ölçüldü: `OnlineEvalJobHandler.JudgeOneAsync` `CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` + `CancelAfter(JudgeTimeout)` ile bütçeyi zaten kuruyor ve `budget.Token`'ı yargıca geçiriyor. Köprünün tek görevi o token'ı `EvaluateAsync`'e iletmekti |

### 🚨 Denetimde çıkan bir kapı tuzağı

`ExampleCompilationTests` sevk edilen her `<example>` bloğunu **gerçekten
derler**. `RelevanceEvaluator`'ı örnekte anmak, `tests/Tracon.Generators.UnitTests`
projesine `.Quality` referansı eklemeyi gerektirdi — referans kümesi o projenin
`TRUSTED_PLATFORM_ASSEMBLIES`'inden geliyor. Sevk edilen bir paket hâlâ katalogu
referanslamıyor; yalnız iki test projesi referanslıyor.

İkinci tuzak: `ShippedDocumentationSelfContainmentTests` bir **cırcır**tır ve
XML dokümanında 🚨 ile `K-NNN` referansını reddeder. Taban çizgisi tazelenmedi;
üç dosyanın XML metni tüketicinin sesine çevrildi (aynı bilgi `//` yorumunda kaldı).

## Bu Fazda Verilen Kararlar

| Karar | Gerekçe |
|---|---|
| **K-727 — `.Quality` katalogu sevk edilen HİÇBİR pakete girmez; `Core` yalnız `Microsoft.Extensions.AI.Evaluation`'ı açık referanslar** (kullanıcı kararı) | Köprü yalnız `IEvaluator` arayüzüne bağlıdır. `M.E.AI.Evaluation` 10.9.0 `Core` grafiğinde **zaten vardı** (`Microsoft.Agents.AI` 1.20.0 üzerinden, ölçüldü 2026-09-07); referansı açık hale getirmek geçişli ağırlığı **0** artırır ve MAF onu bir gün bırakırsa köprü kırılmaz. Katalogu isteyen tüketici `.Quality`'yi kendisi ekler; istemeyenin grafiği büyümez. AOT duruşu da korunur — sevk edilen grafiğe yeni bir paket girmediği için ölçülecek yeni bir AOT yüzeyi yoktur |
| **K-728 — `RunJudgment.Score`/`Reason` KALDIRILDI; bir yargıç `IReadOnlyList<JudgeScore>` döndürür** (kullanıcı kararı) | 🔴 KIRICI ve bilerek şimdi yapıldı: `PublicAPI.Shipped.txt` toplamı 17 satırdır ve hepsi `#nullable enable` başlığıdır — hiçbir yüzey yayımlanmamıştır. İki doğruluk kaynağı (`Score` yanında `Scores`) her okuma yolunda "hangisi kazanır" sorusunu tekrarlardı. Etkilenen yargıç sayısı ikiydi (`ModelRunJudge` + örnek) |
| **K-729 — Köprü metrik adını `{judge}.{metrik}` olarak önekler; `:` KULLANILAMAZ** (kullanıcı kararı) | `RunScore.Name` tekillik anahtarına girer (K-710); önek olmadan iki evaluator'ın aynı `Relevance` metriği birbirini ezerdi. Planın önerdiği `:` ayırıcısı `RunScoreRules.IsValidName`'den geçmez |
| **K-730 — Yalnız MANŞET skor (adı yargıcın adına eşit olan) online değerlendirme penceresine ve `tracon.judge.score` histogramına girer** | İkisi de **0-100** ölçeğinde tanımlıdır; kalibre evaluator'lar 1-5 verir. Köprü skorları ortalamaya girseydi 4 puanlık sağlıklı bir koşum `LowScoreThreshold`'un altına düşer ve alarm yanlış çalardı. Kural yapısaldır (ad karşılaştırması), sezgisel değil — bir ölçek tahmini içermez. Skorlar yine de saklanır ve `GET /api/evaluation/scores/summary` onları `(name, kind)` kırılımıyla raporlar |
| **K-731 — `IEvalEvaluatorFactory` public'tir; seam çıplak bir `IAgentEvaluator` DEĞİL bir FABRİKADIR** | Suite `Checks` alanını beyan eder ve `EvalJobHandler` check'siz suite'i reddeder. Çıplak bir singleton o check'leri sessizce yok sayardı (K1 ihlali). Fabrika onları argüman olarak alır; yok sayması artık açık bir tercihtir |
| **K-732 — Sözleşmeyi ihlal eden bir yargıç FIRLATMAZ; `judge_contract` `JudgeFailure` olarak raporlanır ve doğrulama İLK YAZMADAN ÖNCE toplu yapılır** | Tek bozuk yargıç tüm online eval işini düşürmemelidir. Toplu doğrulama, tekrarlanan bir adın kendi kümesinin önceki satırını ezmesini de engeller: yarım yazılmış bir küme hiç yazılmamış olandan kötüdür |

## Denetim Bulguları

`faz-denetim` koşuldu (2026-09-08, taze bağlamlı bağımsız denetçi). **İki 🔴, beş
🟡, üç 🟢.** İkisi de gerçek kusurdu ve ikisi de kapatıldı.

### 🔴 — kapatıldı

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | **Manşet skor kuralı yapısal değildi: yalnız ADA bakıyordu, `Kind`'a bakmıyordu.** `Name = judge.Name, Kind = Stars, Value = 5` döndüren bir yargıç `TryValidate`'ten geçer ve 0-100 penceresine girerdi — 5 < 60, yani `run.score.low` webhook'u **her sağlıklı koşumda** çalardı. K-730'un önlediğini iddia ettiği hatanın tam kendisi; üstelik sevk edilen `write-your-own-judge.md` "her skor kendi `Kind`'ını söyler" diyerek bunu davet ediyordu | **DÜZELTİLDİ.** Manşet testine `judgeScore.Kind != RunScoreKind.Numeric` eklendi. Test: `A_non_numeric_headline_score_stays_out_of_the_zero_to_hundred_window` — düzeltme geri alınınca **kırmızı olduğu ölçüldü** |
| 2 | **Kısmi yazma mümkündü ve checkpoint onu KALICILAŞTIRIYORDU.** N satırın k'ıncısında `UpsertAsync` hata verirse istisna `ExecuteAsync`'ten çıkar, iş retry edilir, `skipAlreadyScored: true` olur ve `ReadAlreadyScoredJudgesAsync` yalnız `Author`'a baktığı için **hayatta kalan tek satır** yargıcı atlatır: kalan metrikler bir daha hiç yazılmaz, iş `Completed` raporlanır ve hiçbir `JudgeFailure` görünmez. K-732'nin invariant'ı yalnız *doğrulama* yolunda tutuluyordu, *store hatası* yolunda tutulmuyordu | **DÜZELTİLDİ.** Yazma döngüsü `try/catch` içine alındı; hata hâlinde `CompensateAsync` o denemede yazılan satırları geri alır (upsert idempotent olduğu için retry temiz sayfadan yeniden yazar) ve `judge_failed` (retryable) raporlanır. Temizlik **iptal edilemez** (`CancellationToken.None`): iptal edilen bir temizlik tam da önlemek için var olduğu yarım kümeyi bırakırdı. Temizlik de başarısız olursa loglanır, fırlatılmaz. Test: `A_store_failure_partway_through_a_set_leaves_nothing_behind` — düzeltme geri alınınca **kırmızı olduğu ölçüldü** |

### 🟡 — kapatıldı

| # | Bulgu | Sonuç |
|---|---|---|
| 3 | Manuel kabul case'leri (EVAL-136/138/139), DoD satırı ve doğrulama komutu **var olmayan** `GET /api/runs/{id}/scores` ucuna curl atıyordu. Gerçek uç `GET /api/runs/{id}/feedback`'tir (Faz 31'den beri insan ve yargıç skorları tek listedir) — plandan miras alınan hata | **DÜZELTİLDİ**, dört yerde |
| 4 | `The_call_budget_reaches_the_evaluator` iddiasını **kanıtlamıyordu**: ön iptal edilmiş token yargıcın kendi `ThrowIfCancellationRequested`'ında patlıyor, `EvaluateAsync` hiç çağrılmıyordu — token'ı ileten satır silinse test yine yeşil kalırdı | **DÜZELTİLDİ.** `ScriptedEvaluator` artık aldığı token'ı kaydediyor; test **evaluator'ın gördüğü** token'a iddia ediyor. Ayrıca `A_token_cancelled_inside_the_evaluator_is_honoured` eklendi |
| 5 | Çok satırlı checkpoint ve store hatası davranışı test edilmemişti | **DÜZELTİLDİ.** İki yeni test (bulgu 2'nin repro'su ve `A_judge_that_wrote_several_rows_is_skipped_on_a_retry_and_reports_all_of_them`) |
| 6 | 🚨 Köprünün XML dokümanı ve faz dokümanı **"kiracının kendi sağlayıcı anahtarı uygulanır" (BYOK)** diyordu. Yanlış: `ResolveSetupCredentialAsync` **her yolda `null`** döndürür; yalnız egress policy'yi zorlar. BYOK `CreateChatClientAsync` yolundadır ve yerleşik yargıç da bu ayrımı taşır — yani bir kiracı/faturalama sınırı hakkında yanlış bir sevk edilen iddia | **DÜZELTİLDİ.** İkisi de "kiracının **egress policy**'si uygulanır" olarak düzeltildi ve setup yolunun anahtarı taşımadığı açıkça yazıldı |
| 7 | Durum `✅ Tamamlandı` iken 11 DoD kutusunun tamamı işaretsizdi; iki kanıt dokümanda yoktu. `dokuman-bakim.py` bunu **arşivlenir arşivlenmez** kırmızıya çevirirdi | **DÜZELTİLDİ.** Kutular işaretlendi; gerçek koşum kanıtı ve `secret` taraması sonucu belgeye yazıldı |

### 🟢 — biri düzeltildi, ikisi devredildi

| # | Bulgu | Sonuç |
|---|---|---|
| 8 | `IsInRange` tanımsız bir `RunScoreKind`'ı `Value == null` iken kabul ediyor; `RunScoreRules.Validate` de enum'a bakmıyor ⇒ `(RunScoreKind)99` ile satır yazılabilir | **Devredildi.** Enum guard'ı `RunScoreRules`'a girerse HTTP ucunu ve dört store'u birden etkiler; bu fazdan geniş |
| 9 | Checkpoint isabetinde `scores.Add(existing)` yargıcın N satırından **yalnız birini** çağırana döndürüyordu | **DÜZELTİLDİ** (ucuz ve gerçek bir tuzaktı): sözlük `Dictionary<string, List<RunScore>>` oldu, `scores.AddRange(existing)`. Test: `A_judge_that_wrote_several_rows_is_skipped_on_a_retry_and_reports_all_of_them` |
| 10 | Kaldırılmış `RunJudgment.Score`'a bakan bayat yorumlar | İki test dosyasında **düzeltildi**. 🚨 Üçüncüsü (`0048_run_score_name_and_shape.sql`) **bilerek düzeltilmedi**: uygulanmış bir migration'ın içeriği `scripts/applied-migrations.json`'da sha ile pinlidir; bir yorumu değiştirmek bütünlük kapısını kırar |

**Denetçinin temiz bulduğu başlıklar:** 3.5 (imza-gövde takibi eksiksiz) · 3.6
(plan dışı public API yok; `PublicAPI.Unshipped.txt` ve `public-surface-baseline.txt`
+1/+1 tam olarak `JudgeScore` ve `IEvalEvaluatorFactory`'yi karşılıyor) · 3.7
(dil, XML dokümanı, `TryAdd*`, MAF tipi sarmalanmamış, `secret` yok,
`ConfigureAwait(false)`) · **kiracı sınırı** (köprü hiçbir store'a dokunmuyor;
başka kiracının verisine erişilebilecek yol bulunamadı) · genişleme noktası
kapısı ve muafiyet listeleri (hiçbiri büyümedi).

## Sonraki Faza Devir Notu

Devralınan sözleşme birebir:

```csharp
public sealed record RunJudgment
{
    public const int MaxReasonLength = 4000;
    public IReadOnlyList<JudgeScore> Scores { get; init; } = [];   // BOS = karar yok, satir yazilmaz
}

public sealed record JudgeScore
{
    public required string Name { get; init; }   // [A-Za-z0-9._-]{1,64}, judgment icinde TEKIL
    public required RunScoreKind Kind { get; init; }
    public double? Value { get; init; }          // null = OLCUM YOK; Binary 0/1, Stars 1-5, Numeric 0-100
    public string? TextValue { get; init; }      // yalniz Kind == Categorical, <= 256 karakter
    public string? Comment { get; init; }        // <= MaxReasonLength, handler kirpar
}
```

### 🚨 Sonraki fazın bilmesi gerekenler

- **Manşet skor kuralı bir AD KARŞILAŞTIRMASIDIR** (K-730). `score.Name == judge.Name`
  ise skor `OnlineEvalSummaryService.RecordScoreAsync` ve
  `TraconMetrics.RecordJudgeScore`'a gider; değilse **gitmez**. Bu iki yol
  **0-100** tanımlıdır. Yeni bir yargıç ekliyorsan manşet skorunu bu ölçekte ver;
  başka bir ölçek kullanacaksan ona **başka bir ad** ver. Kural
  `OnlineEvalJobHandler.JudgeOneAsync` içindedir, tek yerde.
- **Doğrulama İLK YAZMADAN ÖNCE topludur** (`TryValidate`). Yeni bir invariant
  eklerken onu oraya ekle; `foreach` içine koymak yarım yazılmış küme üretir.
  🚨 Ad tekilliği ve kind-aralığı **`RunScoreRules`'ta DEĞİL** handler'dadır —
  `RunScoreRules.Validate` yalnız ad desenini ve kategorik değer şeklini bilir.
  Aralık kuralını `RunScoreRules`'a taşımak HTTP ucunu ve dört store'u etkiler;
  bu faz onu bilerek yapmadı.
- **Checkpoint `Author` üzerinden çalışır, `(author, name)` üzerinden DEĞİL**
  (K-638). Bir yargıç artık N satır yazdığı için bu **doğru** olan davranıştır:
  retry'da o yargıç atlanır. `ReadAlreadyScoredJudgesAsync` yorumunda yazılıdır.
- **Köprü metrik adını `{judge}.{metrik}` yapar ve ayırıcı `.`'dır** (K-729).
  `:` `RunScoreRules.IsValidName`'den geçmez. Yeni bir ad şeması düşünüyorsan
  önce o metodu oku.
- **`.Quality` sevk edilen hiçbir pakete girmez** (K-727). Katalogdan bir tip
  anmak isteyen her yerin (örnek, test, `<example>` bloğu) paketi **kendisi**
  referanslaması gerekir. `ExampleCompilationTests` sevk edilen her `<example>`
  bloğunu gerçekten derler ve referans kümesini
  `tests/Tracon.Generators.UnitTests`'ten alır.
- **`ShippedDocumentationSelfContainmentTests` bir cırcırdır.** XML dokümanına
  (`///`) 🚨 veya `K-NNN` yazma; uygulama yorumunda (`//`) meşrudur.
- **`IEvalEvaluatorFactory` suite'in check'lerini argüman alır.** Eval
  değerlendirmesini değiştiren bir faz onu buradan yakalar; `EvalJobHandler`
  artık `LocalEvaluator`'ı doğrudan `new`'lemiyor.

### Açık uçlar

- On bir kalibre evaluator'ın **bildirimsel** (JSON/HTTP) yüzeyi hâlâ yok —
  bağlama yalnız koddan yapılır. Bu faz bunu bilerek kapsam dışı bıraktı.
- `ToolCallAccuracyEvaluator` ölçüm üretemiyor: `RunJudgeContext` tool
  **adlarını** taşıyor, argüman ve sonuçlarını değil. Bağlamı genişletmek ayrı
  bir karardır (veri sızıntısı yüzeyi büyür) ve ölçülmedi.
- Evaluator paket sürümünü skor satırına damgalamak (Faz 153 taban çizgisinin
  sessizce kayması riski, plan risk tablosu) **yapılmadı**: `RunScore`'a alan
  eklemek migration ister ve bu fazın dar hedefinin dışındaydı. `docs/ADAYLAR.md`
  adayıdır.
