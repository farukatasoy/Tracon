# Keşif Turu — 2026-09-07 · Langfuse esinli yetenek taraması

> Bu bir **koşum kaydıdır**, spec değildir. Sıcak yolda değildir ve baştan sona
> okunmaz. Onaylanan kalemlerin tam metni
> [`ADAYLAR.md`](../ADAYLAR.md) içinde yaşar; bu dosya
> yalnız oraya işaret eder.

**Tetikleyen:** Kullanıcı `https://langfuse.com`'u gösterip "bize feature fikri
verebilir mi, ilham alabilir miyiz" diye sordu.
**Zemin:** Faz 150 kapalı · sıralanabilir aday **bir** (F-192) · en büyük numara F-206
**Ekosistem taraması:** 2026-09-07 · web erişimi **var**

---

## 1. Ölçülen zemin (Aşama 0)

Turun en önemli bulgusu ilk adımda çıktı ve turun çerçevesini değiştirdi.

| Kaynak | Bulgu |
|---|---|
| `YOL-HARITASI.md` | 150 faz kapalı; eval, judge, deney, maliyet, sürümleme fazları çoktan bitmiş |
| `ADAYLAR.md` § Sıralama | Yalnız F-192 sıralı; kuyruk boş — yeni aday üretmek için doğru an |
| `arsiv/KARARLAR-INDEKS-REDDEDILEN.md` | 26 kalem; **hiçbiri** skor, eval veya karşılaştırma ile ilgili değil |
| Kod taraması | Langfuse'un beş sütununun **beşi de** bu repo'da mevcut (aşağıdaki tablo) |

### Langfuse sütunları ↔ AgentPrism karşılıkları (ölçüldü)

| Langfuse sütunu | AgentPrism karşılığı | Kanıt |
|---|---|---|
| Prompt versiyonlama + rollback | Agent tanımı sürümleme | `IAgentDefinitionStore` — `GetVersionAsync` · `ListVersionsAsync` · `RollbackAsync` |
| LLM-as-judge | `IRunJudge` + `ModelRunJudge` + online eval | `src/AgentPrism.Abstractions/Evaluation/IRunJudge.cs` · `src/AgentPrism.Core/Evaluation/` |
| Human annotation → gold dataset | `RunScore` (human/api/judge, mesaj bazlı) + `RunToCasePromoter` | `RunScore.cs` · `EvalCaseSource.cs` |
| Maliyet–gecikme panosu | `RunStatistics` (model · versiyon · kullanıcı · label kırılımı) + `dashboard.tsx` | `RunStatistics.cs:10-49` |
| Deney karşılaştırma | `Experiment` + ağırlıklı varyant + canary + rollback | `ExperimentVariantResult.cs` — maliyet, gecikme, hata oranı, ortalama skor |

Birkaç yerde AgentPrism **önde**: canary otomatik rollback, kiracı yalıtımı,
`agentprism eval --min-pass-rate` CI kapısı (`EvalCommand.cs:168`).

**∴ Turun çerçevesi:** ilham değeri başlıklarda değil, **kenarlarda**. Aranan
şey "Langfuse'ta olan büyük özellik" değil, "bizim yapıp da tamamlamadığımız
kenar".

---

## 2. Ham fikir listesi (Aşama 2)

| # | Fikir | Kim için | Neden şimdi | Sonuç |
|---|---|---|---|---|
| 1 | Eval koşumları arasında regresyon farkı | Nöbetçi · ölçme-iyileştirme | Sevk edilen API metni karşılaştırmayı vaat ediyor, ürün yapmıyor | ✅ **F-207** |
| 2 | CI kapısının göreli (taban çizgili) olması | Benimseme · üretim | %95 → %90 düşüş mutlak kapıyı geçiyor | ✅ F-207'ye **birleşti** |
| 3 | Score'un adı ve şekli | Kurumsal · ölçme-iyileştirme | `1.0` öncesi; sonra tip dondurulur | ✅ **F-208** |
| 4 | İnceleme (annotation) kuyruğu | Kurumsal | Grep sıfır sonuç verdi | ⏸ boşluk gerçek, **talep kanıtı yok** |
| 5 | Skor trendinin kalıcı sorgusu | Nöbetçi · ekosistem | Uç, tüketiciyi kendi tablomuza yönlendiriyor | ✅ **F-209** |
| 6 | Eval koşumunun maliyeti | FinOps | `EvalRun` token taşıyor, para taşımıyor | ⏸ talep kanıtı yok |
| 7 | Eval case'lerinin sürümlenmesi | Ölçme-iyileştirme | Case düzenlenince fark elma-armut olur | ⏸ F-207'nin risk satırına taşındı |
| 8 | Agent sürümüne sembolik label (`production`) | Benimseme | — | ❌ **elendi** — deney + rollback zaten kapsıyor |
| 9 | Judge'ların eval check'i olarak koşması | Ölçme-iyileştirme | Ölçülmedi | ⏸ kanıt bekliyor |
| 10 | Skor düşüşünde alarm | Nöbetçi | — | ❌ **elendi** — `WebhookEvents.RunScoreLow` zaten var |
| 11 | Embed widget'ından son kullanıcı 👍/👎 | Benimseme | Ölçülmedi | ⏸ kanıt bekliyor |
| 12 | Sampling'in kural tabanlı olması | Üretim | Ölçülmedi | ⏸ kanıt bekliyor |
| 13 | Eval suite'in CI kapısı | Benimseme | — | ❌ **elendi** — `EvalCommand.cs:168` zaten yapıyor |
| 14 | `span` düzeyinde skor | Ölçme-iyileştirme | `run` + `message` düzeyi var | ⏸ zayıf |

**Önerilen üç kalem ve gerekçesi:** 1 · 3 · 5.
(1) ürünün kendi sevk ettiği metninin söz verip yapmadığı tek şey — boşluk
kanıtı en sağlam olan bu. (3) public API kararı; ertelemenin maliyeti zamanla
**artar**. (5) tüketiciyi iç tabloya yönlendiren yazılı sözleşme boşluğu,
kapatması ucuz.

**Kullanıcının elemesi:** "1 3 5" — önerilen üçü onaylandı, diğerleri bu turda
derinleşmedi.

---

## 3. Ekosistem taraması (Aşama 3.2)

| Kaynak | Bakılan tarih | Ne değişti | AgentPrism'e etkisi |
|---|---|---|---|
| `langfuse.com` ana sayfa | 2026-09-07 | Beş sütun: tracing · evaluation · prompt yönetimi · deney · human annotation | Beşi de mevcut; tur kenarlara yöneldi |
| Langfuse `custom-scores` dokümanı | 2026-09-07 | `ScoreConfig`: ad + tip (numeric/categorical/boolean/text) + aralık/kategori doğrulaması | **F-208'in doğrudan öncülü** |
| Langfuse `prompt-version-control` | 2026-09-07 | Versiyon + `production`/`latest` label'ları ile deploy ve rollback | Fikir 8'i **eledi** — deney + rollback kapsıyor |
| `Microsoft.Extensions.AI.Evaluation` namespace API'si | 2026-09-07 | `EvaluationMetric<T>` altında `BooleanMetric` · `NumericMetric` · `StringMetric`; `EvaluationMetricInterpretation` + `EvaluationRating`; `IEvaluator`, `EvaluationDiagnostic` | **F-208'i yeniden çerçeveledi:** şekil icat edilmez, .NET'in şekilleriyle hizalanır |
| `Microsoft.Extensions.AI.Evaluation.Reporting` (learn.microsoft.com, doküman tarihi 2026-04-09) | 2026-09-07 | `ExecutionName` ile koşumları gruplama, `ResultStore`, `dotnet aieval report` HTML raporu | **F-207'nin kavramı hazır ama uygulaması değil** — disk tabanlı, test harness'ine dönük, kiracılı sunucu API'si yok |

~~🚨 **Repo bu aileyi kullanmıyor.**~~ 🔴 **Bu cümle 2026-09-07 planlama
turunda ÇÜRÜDÜ.** Doğru olan yalnız yarısıdır. `Directory.Packages.props:54-57`
gerçekten yalnız `Microsoft.Extensions.AI` · `.Abstractions` · `.OpenAI`
(10.9.0) tutuyor ve `.Evaluation*` **doğrudan** referanslı değil — ama:

| Ölçüm (2026-09-07, `project.assets.json` + gerçek nuspec) | Sonuç |
|---|---|
| `Microsoft.Extensions.AI.Evaluation` 10.9.0 | `Core` · `AspNetCore` · `Cli` grafiğinde **var** — `Microsoft.Agents.AI` 1.20.0 ve `.Harness` getiriyor |
| `AgentPrism.Abstractions` grafiği | **yok** (yalnız 7 paket) |
| Kod kullanımı | `EvalJobHandler.cs:3` `using Microsoft.Extensions.AI.Evaluation;` · `EvaluationMetric` `:432`, `:458` |
| Paketin **kendi** bağımlılığı | **yalnız** `M.E.AI.Abstractions` 10.9.0 — `Abstractions` onu zaten referanslıyor ⇒ eklemek **net 1 paket, geçişli ağırlık 0** |

∴ F-208'in *"bağımlılığı almak mı, şekilleri kopyalamak mı"* karar noktası
ağırlıkla çözülmedi; **tip doğasıyla** çözüldü (kalıcı kayıt ↔ mutable
çalışma-anı nesnesi). Karar: şekli hizala, tipi alma —
[`../152-SKORUN-ADI-VE-SEKLI.md`](../arsiv/fazlar/152-SKORUN-ADI-VE-SEKLI.md) § 152.1.

---

## 4. Derinleşen kalemler (Aşama 3)

### F-207 · Eval koşumları arasında regresyon farkı

**Kanıt seviyesi:** **Ölçüldü.**
- `src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs:141` — sevk edilen metin:
  *"Comparing entries over time is how a regression between agent versions is spotted."*
- `src/AgentPrism.Abstractions/Evaluation/IEvalStore.cs:143` — `ListCaseResultsAsync`
  tek bir `evalRunId` alıyor; iki koşumu hizalayan üye **yok**.
- `src/AgentPrism.Cli/Commands/EvalCommand.cs:168` + `PassesThreshold` —
  kapı `--min-pass-rate` / `--max-failures` ile **mutlak**; taban çizgisi kavramı yok.
- `grep -n "compare\|previous\|regress\|delta\|baseline" src/AgentPrism.UI/frontend/src/screens/eval-run-detail.tsx`
  → **sıfır** eşleşme.

**Mercek:** 1, 2, 7.
**Eleyici sınır kontrolü:** K2 ✅ (kod çalıştırmaz) · K3 ✅ (MAF tipi sarmalamaz) ·
paket ağırlığı ✅ (yeni paket yok) · AOT ✅ (`Abstractions` + `Core` saf kalır) ·
bundle ⚠️ (arayüz fark görünümü 250 KB bütçesine girer) · public API ⚠️ (yeni
okuma tipi + `IEvalStore` üyesi — extension point genişler).
**Karşı görüş:** Tüketici iki koşumu kendi çekip diff'leyebilir; veri zaten
uçlarda. Ancak ürünün kendi metni bu işi *ürünün yaptığını* ima ediyor — boşluk
tam burada.
**Sonuç:** **F-207** olarak aday dosyasına yazıldı. Fikir 2 (göreli CI kapısı)
kapsamına birleşti; fikir 7 (case sürümleme) risk satırına taşındı.

### F-208 · Score'un adı ve şekli

**Kanıt seviyesi:** **Ölçüldü.**
- `src/AgentPrism.PostgreSql/Migrations/0017_run_scores.sql:30` — `value integer NOT NULL`;
  float veya kategorik değer saklanamaz.
- Aynı dosya `:47` — tekillik indeksi `(tenant_id, run_id, COALESCE(message_id,''), author)`;
  **`name` sütunu yok** ⇒ bir yazar bir `run`'a yalnız BİR skor yazabilir, ikincisi ezer.
- `src/AgentPrism.Core/Evaluation/ModelRunJudge.cs:75` — judge bu sınırı
  `author = "judge:{Name}"` ile aşıyor; insan gözden geçirenin böyle bir kaçışı yok.
- `RunScoreKind.cs` — üç değerli kapalı enum (Binary · Stars · Numeric 0-100).
- `docs/arsiv/fazlar/31-GERI-BILDIRIM-VE-PUANLAMA.md:38` — indeksin bu hâli
  **kasıtlı** (açık soru 4), hata değil.

**Mercek:** 3, 5, 6, 7.
**Eleyici sınır kontrolü:** K2 ✅ · K3 ✅ · paket ağırlığı ⚠️ (`M.E.AI.Evaluation`
bağımlılığı alınırsa `Abstractions`'ın grafiği büyür — faz kararı) · AOT ⚠️
(`Abstractions` AOT uyumlu kalmalı) · bundle — · public API 🔴 (`RunScore.Value`
`required int`; tipini genişletmek **kırıcıdır**).
**Karşı görüş:** Bugün kimse istemedi — dört tüketici turunun hiçbirinde skor
şekli talebi yok. Karşı-karşı gerekçe: bu **maliyeti zamanla artan** bir
karardır; `Value`'nun tipi ve tekillik indeksi `1.0`'dan sonra dondurulur.
**Sonuç:** **F-208** olarak aday dosyasına yazıldı.

### F-209 · Skor trendinin kalıcı sorgusu

**Kanıt seviyesi:** **Ölçüldü.**
- `src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs:161-170` — sevk edilen metin:
  *"The summary is in-memory (it resets when the process restarts); for an
  authoritative result, the 'run_scores' table can be queried directly."*
- `src/AgentPrism.Core/Evaluation/OnlineEvalSummaryService.cs:14-20` — pencere
  bellekte, kiracı başına kuyruk; "no durable counter store" kuralına atıf.
- `src/AgentPrism.Abstractions/Runs/IRunScoreStore.cs` — yalnız `UpsertAsync` ·
  `ListAsync(tenantId, runId)` · `DeleteAsync`; zaman aralığı veya toplulaştırma **yok**.
- Üç migration'ın hiçbirinde `created_at` indeksi yok
  (PostgreSql `0017:47-52` · Sqlite `0005:26-29` · SqlServer `0005:29-34`);
  0017 metni *"Listing the scores of a run — that is the only access pattern."*

**Mercek:** 2, 6, 7.
**Eleyici sınır kontrolü:** K2 ✅ · K3 ✅ · paket ağırlığı ✅ · AOT ✅ · bundle — ·
public API ⚠️ (`IRunScoreStore`'a üye eklemek üçüncü taraf uygulayıcıyı kırar —
`1.0` öncesi yapılmalı).
**Karşı görüş:** Mevcut tasarım bunu **bilerek** böyle yaptı ve gerekçesini
yazdı: canlı gösterge ucuz olmalı, `run_scores` zaten doğru cevabı veriyor. Bu
gerçek bir gerekçe. Ancak bir kütüphanenin tüketiciye "benim tablomu doğrudan
sorgula" demesi sözleşme dışıdır; kalem tasarımı değiştirmiyor, eksik **okuma
üyesini** ekliyor.
**Sonuç:** **F-209** olarak aday dosyasına yazıldı.

---

## 5. Üç kanalın çıktısı (Aşama 1)

### Kanal 1 — yeni aday

| F-NN | Başlık | Aday dosyasına yazıldı mı |
|---|---|---|
| F-207 | Eval koşumları arasında regresyon farkı | ✅ → [Faz 153](../153-EVAL-KOSUMLARI-ARASINDA-REGRESYON-FARKI.md) |
| F-208 | Score'un adı ve şekli | ✅ → [Faz 152](../arsiv/fazlar/152-SKORUN-ADI-VE-SEKLI.md) |
| F-209 | Skor trendinin kalıcı sorgusu | ✅ → [Faz 154](../154-SKOR-TRENDININ-KALICI-SORGUSU.md) |

**Ek (2026-09-07, aynı gün, planlama turu):** Üçü de plana dönüştü. Ölçüm bir
**dördüncü aday** üretti: **F-210** — `Microsoft.Extensions.AI.Evaluation.Quality`
(10.9.0 GA) on bir kalibre edilmiş evaluator veriyor, üçü doğrudan agent işi
(`TaskAdherence` · `ToolCallAccuracy` · `IntentResolution`) ve maliyeti **net 1
paket, geçişli ağırlık 0**. Bugün bağlanamıyor: `RunJudgment` tek skor taşıyor
ve `EvalJobHandler.cs:139` `LocalEvaluator`'ı sabit kodluyor. F-210 bu yüzden
Faz 152'ye bağlıdır.

### Kanal 2 — kusur

| Bulgu | Kanıt | Kullanıcıya söylendi mi | `kusur-giderme` koşuldu mu |
|---|---|---|---|
| — | Bu tur kusur üretmedi | — | — |

Değerlendirildi ve **kusur sayılmadı:** bir yazarın `run` başına tek skor
sınırı. `31-GERI-BILDIRIM-VE-PUANLAMA.md:38` bunu kasıtlı ilan ediyor; eksik
olan yetenek (skor adı), yanlış davranış değil ⇒ Kanal 1.

### Kanal 3 — yeniden açılması önerilen karar

| K-NNN | Kararın gerekçesi | Neyin değiştiği | Kullanıcının kararı |
|---|---|---|---|
| — | Kapatılmış hiçbir kararı geçersizleştiren ekosistem değişimi bulunmadı | — | — |

Bildirilen ekosistem gerçeği (karar değil): `Microsoft.Extensions.AI.Evaluation`
ailesi mevcut ve repo onu kullanmıyor. F-208'in tasarım kararı olarak taşınır.

---

## 6. Reddedilenler

| Fikir | Ret gerekçesi | Kalıcı mı | Nereye yazıldı |
|---|---|---|---|
| Agent sürümüne sembolik label (`production`/`staging`) | `Experiment` (sürüm başına ağırlıklı varyant) + `RollbackAsync` ihtiyacı karşılıyor; kiracı ayrımı staging/prod'u zaten çözüyor | Hayır — koşulları değişirse yeniden aday | Yalnız bu kayıt |
| Eval suite'in CI kapısı | **Zaten var:** `agentprism eval --min-pass-rate --max-failures`, çıkış kodu 3 (`EvalCommand.cs:168`) | Hayır — mevcut yetenek | Yalnız bu kayıt |
| Skor düşüşünde alarm | **Zaten var:** `WebhookEvents.RunScoreLow`, `MinSampleSize` eşiğiyle (`OnlineEvalSummaryService.cs`) | Hayır — mevcut yetenek | Yalnız bu kayıt |
| İnceleme (annotation) kuyruğu | Boşluk gerçek (`grep` sıfır sonuç) ama talep kanıtı yok | Hayır — talep gelirse aday | Yalnız bu kayıt |
| Eval koşumunun maliyeti | `EvalRun` token taşıyor, `TotalCost` taşımıyor; talep kanıtı yok | Hayır | Yalnız bu kayıt |
| Eval case sürümleme · judge'ın eval check'i · embed 👍/👎 · kural tabanlı sampling · `span` skoru | Ölçülmedi; bu turda kanıt üretilmedi | Hayır | Yalnız bu kayıt |

---

## 7. Kullanıcıya sorulanlar ve cevapları

| Soru | Cevap |
|---|---|
| 14 ham fikirden hangileri ayakta kalsın? | "1 3 5" — önerilen üç kalem onaylandı |
