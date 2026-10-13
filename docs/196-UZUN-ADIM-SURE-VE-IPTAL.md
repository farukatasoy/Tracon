# Faz 196 — Uzun Adımların Süre Sınırı ve İptali

> **Durum:** 📋 Planlandı (2026-10-12)
> **Plan onayı:** Bakımcı, 2026-10-13 (Açık Sorular'ın önerileri kabul edildi: 1-B, 2-A, 3-A, 4-A, 5-A, 6-A, 7-A, 8-A, 9-A, 10-B, 11-B, 12-B, 13-A). Faz 203 ile paralel geliştirilir
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-303**, **F-323**, **F-405**
> **Önkoşul:** [Faz 195](arsiv/fazlar/195-WORKFLOW-DUGUM-DUZENLEME-VE-AYAR.md) — aynı kayıt yüzeyi ve editör satırı; 2026-10-12'de kapandı (`55272673`)
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Workflows`, `.Sql.Shared`, SQL sağlayıcıları, `.AspNetCore`, `.Testing.Contracts.Xunit`, `.UI` · üretilen istemciler
> **Yeni paket:** Yok · **Migration:** gerekli (Açık Soru 5) — `jobs` süre sütunu, üç sağlayıcı; numara uygulamada alınır
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-12): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, her biri yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `concepts/workflows.md`, `guides/write-your-own-job-handler.md`, `guides/background-work.md`, `reference/configuration.md`, `ui.md`, `capabilities.md` · sevk edilen: `IJobHandler`/`RunTimeout` XML'i, `<example>`, `Tracon.AgentMap.md`, `Tracon.Workflows/README.md`, `CHANGELOG.md`
> **Manuel test alanı:** `docs/manuel-test/15-WORKFLOWS.md`, `16-IS-KUYRUGU-VE-ZAMANLAMA.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula; yalnız işaret edilen bölümleri oku.

1. Bu doküman
2. Kararlar:
   ```bash
   grep -n "K-422\|K-497\|K-725\|K-805\|K-844\|K-886\|K-887\|K-900\|K-901\|K-902" docs/KARARLAR.md
   ```
   **K-497** (`TimeoutAIFunction` deseni) · **K-805** · **K-844**
   (`RunTimeout` → `Canceled`) · **K-887** (adım aşımı → `Failed`) ·
   **K-886** · **K-900/K-902** (ayar `TSettings`; kayıt ayarı seçenek
   nesnesinde) · **K-901** · **K-422** · **K-725**
3. Devir notları (`awk '/## Sonraki Faza Devir Notu/,0'`): `arsiv/fazlar/{195,71}-*.md`
4. Hafıza: `workflows.md` · `cekirdek-calistirma.md` ·
   `secenek-baglama-ve-gizlilik.md` · `altyapi-haritasi.md` · `sql-migration.md`
5. Faz 204 devri: `DefaultRunErrorClassifier.StableIdentities`'e `output_truncated`
   satırı eklendi (`:54`); `:41-51` atıfları geçerli. `RunErrorClass` 16'ya kadar
   dolu; yeni sınıf gerekirse 17'den, `RunErrorClassContractTests` ve iki UI
   sözlüğüyle (`dashboard.errorClass.*`) birlikte.
6. **Faz 197 devri** (kuyruklu workflow ve kurtarma; aşağıdaki § *Faz 197'den
   devralınan sözleşmeler* tek başına yeter): `grep -n "K-923\|K-924\|K-926\|K-927" docs/KARARLAR.md`
   · `awk '/## Sonraki Faza Devir Notu/,0' docs/197-DAYANIKLI-WORKFLOW-YURUTMESI.md`
   (kapanınca `arsiv/fazlar/197-*.md`) · hafıza `workflows.md` (son dört madde) ve
   `test-altyapisi.md` § *Handler'ı worker'ın deneme denetimi olmadan çağıran koşum*.

### Faz 197'den devralınan sözleşmeler

Bu faz job token'ına ve deneme kuralına dokunur; Faz 197'nin iki workflow
handler'ı ikisine de dayanır. Satırlar Faz 197 kapanışında koddan ölçüldü.

| Sözleşme | Nerede | 196 için anlamı |
|---|---|---|
| Kurtarma açıkken kuyruklu workflow işi `2 + MaxResumeAttempts` deneme alır; **son deneme devam başlatmaz**, zinciri kapatır; batch işi kendi sınırıyla öğe başına en çok `MaxAttempts − 2` devam alır (K-927) | `WorkflowRecoveryPolicy.JobAttempts` · `WorkflowRecovery.PlanAsync` (`job.MaxAttempts ?? TraconSchedulingOptions.MaxAttempts`, worker'ın okuduğu değer) | Süre aşımı denemesi (Açık Soru 7-A) aynı bütçeden düşer. Son deneme kuralı `job.Attempt >= limit` okur; deneme sayımını değiştiren her yol bu iki yeri birlikte izler |
| Batch öğesine run'dan ÖNCE `Pending` + `RunId` işareti yazılır; `ReportItemAsync` yalnız bekleyen öğeyi değiştirir, `Pending` rapor sayaç artırmaz (K-926, `JobStoreContract`) | `WorkflowJobHandler.PrepareItemAsync` | İptal veya aşım öğeyi kapatan bir yol eklerse işaretli öğe "başlamamış" değildir: run'ı vardır ve kapanmalıdır |
| Token iptal edilince iki handler da işi **kiraya bırakır**: run `Canceled` kapanır, öğe raporlanmaz, `ThrowIfCancellationRequested` (yorum: "the worker is stopping") | `WorkflowRunJobHandler.ExecuteAsync` · `WorkflowJobHandler.ExecuteAsync` | 🚨 Bugün token yalnız kapanışta düşer; 196.4 iptal ve süre aşımını da aynı token'a bağlar. Handler'lar sebebi ayırmazsa aşılan iş kiraya bırakılır ve sonraki deneme onu sürdürür |
| 🚨 **Kurtarma süre aşımını kesintiden ayırt etmez** (§197.2): son run `Running`, `Canceled` ya da `Failed` ise sonraki deneme onu son checkpoint'ten sürdürür | `WorkflowRecovery.PlanAsync` | Kurtarma açıkken job süresi deneme başına olur ve zincir `MaxResumeAttempts`'e kadar sürer: süre işi durdurmaz. Açık Soru 13 |
| İptal edilen kuyruklu iş: handler başlamadan `IsCancelledAsync` okur; koşan run'ı iptal ucu `Canceled` kapatır, iş `tracon.job.workflow-run-failed` ile düşer | `WorkflowRunJobHandler` · `WorkflowAsyncRunCancelTests` | 196.4'ün yoklaması bu yolu hızlandırır; davranış değişmemeli |
| Kurtarma adımındaki `store` hatası işi değil denemeyi düşürür (`NonRetryable` değil) | `WorkflowRunJobHandler` · `WorkflowJobHandler` | Aşım sebebini okuyan hata yolu bu istisnaları "aşım" saymamalı |

🚨 **Test tuzağı:** son kiralamayla ilgili her case gerçek worker'dan geçer
(`QueuedWorkflowHarness.FinishWithWorkerAsync`); handler'ı doğrudan çağıran koşum
worker'ın deneme denetimini atlar ve Faz 197'nin 🔴 kusurunu dokuz yeşil testle
gizledi. Süre aşımı da worker'dadır: aynı kural geçerlidir.

---

## Amaç

Bugün fonksiyon düğümünü yalnız kurulum geneli `RunTimeout` keser; job'un
süre sınırı yoktur; iptal çalışan koda token olarak ulaşmaz. Faz ortak
altyapıyı kurar: adım süresi, token'a ulaşan iptal, ayırt edilebilir hata.

- **F-303** — fonksiyon düğümüne süre (kayıtta varsayılan, düğümde geçersiz
  kılma); aşımda düğüm iptal edilir, run kararlı bir tiple düşer.
- **F-323** — job'a süre; iptal ve aşım handler token'ını iptal eder.
- **F-405** — agent düğümüne süre (`ChildDeadline`'ı düğümde geçersiz
  kılar) ve workflow tanımına `RunTimeout` (kurulum değerini tanım başına
  geçersiz kılar, host tavanıyla sınırlı).

**Tüketici bağlamı.** Bir SaaS'ın podcast workflow'u host'un sandbox
runner'ını fonksiyon düğümüyle HTTP'den çağırır; takılan çağrı run'ı 10 dk
tutar. Video hattının `produce` job'u iptalin ≤ 30 sn'de koda ulaşmasını
ister. Podcast pilotunda (2026-10-13 ölçümü) writer agent çağrısı 1,2–5,8 dk,
üç turlu `Loop` 14 dk sürdü; `ChildDeadline` 2 dk ve `RunTimeout` 10 dk bu
adımı her koşumda düşürür ve bugün tek çare kurulum geneli değeri
yükseltmektir. Plan geneldir.

### Kapsam dışı

| Kalem | Neden |
|---|---|
| `RunTimeout`'un `Canceled` davranışı | K-844 değişmez |
| Dinlemeyen kodu zorla durdurma | .NET güvenli thread iptali vermez |
| Süreç içi anlık job iptal sinyali | Yoklama yeter (Açık Soru 6) |
| `JobSchedule`'a süre alanı | `MaxAttempts` emsali; harita kullanılır |
| `agentNames` biçimli workflow'da adım başına süre | Açık Soru 10; tanım düzeyi `RunTimeout` ve kurulum `ChildDeadline` yeter |

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `TraconWorkflowOptions.cs:45-68` | Tek süre: kurulum geneli `RunTimeout` (10 dk); XML adımı handler'a bırakır (`:59-61`) |
| `TraconWorkflowFunctionExtensions.cs:94-129` · `:260-314` · `:331-347` | Kayıtta süre yok; işleyiciyi yalnız retry sarar; iki parametreli kayıt seçenek nesnesi almaz |
| `WorkflowFunctionOptions.cs:13-25` · `WorkflowNodeReference.cs:27-73` | Seçenek nesnesinde (K-902) ve düğümde süre yok |
| `WorkflowAgentCache.cs:56-60` · `:126-133` | Agent adımı kurulum geneli `ChildDeadline`'ı (2 dk) alır |
| `WorkflowAgentCache.cs:103-105` · `:167` · `:148-152` | Önbellek anahtarı `(tenant, workflow, agent)`; kimlik `WorkflowAgentIdentity` ile çiftten türer, örnekten değil (resume uyumu) |
| `ChildAgentInvoker.cs:90-100` | Süre (`childDeadline`, `waitTimeout`) kurucuda sabitlenir |
| `WorkflowDefinitionCompiler.cs:350-355` | `Wrap` düğümü bilmez; yalnız ad ve açıklama geçer |
| `WorkflowRunner.cs:550-554` | `RunTimeout` CTS'i `_options.Value`'dan; tanım okunmaz |
| `WorkflowDefinition.cs:24-158` · `WorkflowNodeReference.cs:35-38` | Tanımda ve düğümde süre alanı yok |
| `SubAgentSettings.cs:52` | Emsal: delegasyonda agent başına `ChildDeadline` geçersiz kılması |
| `WorkflowRunner.cs:507` · `:746` · `:1813-1820` | `RunTimeout` aşımı → `Canceled` + `TimeoutException`; sebep `timeout` CTS'inden |
| `ChildAgentInvoker.cs:634-643` · `DefaultRunErrorClassifier.cs:41-51` | Agent adımı aşımı → `Failed` + `child_run_timeout`, sınıf `Timeout` (`:50`) |
| `WorkflowNodeRetry.cs:21-27` · `:93-96` | `Timeout` sınıfı yeniden denenir |
| `TimeoutAIFunction.cs:101-145` | K-497 deseni: bağlı CTS, `WhenAny`, iptal, geç tamamlanma gözlemi |
| `WorkflowDefinitionCompiler.cs:65` · `:194-196` · `WorkflowEndpoints.cs:424` | Aynı doğrulayıcı kayıtta ve derlemede koşar |
| `TimeoutSiteTests.cs:29-48` | Yeni `CancelAfter(` sitesi kapıyı kırar (F-280) |
| `IJobHandler.cs:40` · `:58-67` | Token yalnız kapanışta düşer; süre sınırı yok |
| `JobWorkerBackgroundService.cs:473-477` · `:495` · `:955-1028` | İptal yalnız `IsCancelledAsync` ile görünür; handler `stoppingToken` alır; kira yenilemesi (100 sn) iptali okumaz |
| `SchedulingEndpoints.cs:136-144` · `:469-490` | Cancel ucu yalnız depoya yazar |
| `IJobDispatcher.cs:99-103` · `SqlQueriesBase.cs:992-996` · `SqlJobStore.cs:302-325` | Emsal `MaxAttempts` → `jobs.max_attempts`; süre sütunu yok |
| `JobErrorCodes.cs:12-66` | Süre aşımı kodu yok |
| `write-your-own-job-handler.md:148-155` · `background-work.md:573-575,636-640` | Site "süre sınırı yok" der; `:556` iptalde token'ın düştüğünü ima eder |

> Kanıtlar 2026-10-12'de `99fe0b13` üzerinde doğrulandı. F-405 satırları
> 2026-10-13'te `55272673` üzerinde doğrulandı. Faz 197'nin kaydırdığı atıflar
> (`WorkflowEndpoints`, `SchedulingEndpoints`, `background-work.md`) Faz 197
> kapanışında yeniden ölçüldü.

---

## 196.1 — Fonksiyon düğümü süresi

- **Kayıt:** `WorkflowFunctionOptions.Timeout` (`TimeSpan?`, `null` = süre
  yok, K1). İki parametreli kayıt `Action<WorkflowFunctionOptions>` alan
  aşırı yükleme kazanır (Açık Soru 4); mevcut imza kalır.
- **Düğüm:** `WorkflowNodeReference.Timeout`, yalnız fonksiyon düğümünde
  (Açık Soru 1); `Settings`'e girmez (K-900). Öncelik: düğüm > kayıt > yok.
- **Doğrulama (F-280):** kayıtta `TimerPeriod.IsValid` dışı değer
  `ArgumentOutOfRangeException`; aralık dışı değer
  `WorkflowDefinitionValidator`'da `400`. Agent düğümünde de geçerlidir
  (§ 196.5). `RunTimeout`'a göre doğrulanmaz.
- Süre executor kimliğine girmez; resume'u bozmamalı — ölçülür.
- Katalog kayıtlı süreyi gösterir. Konsol: saniye alanı, boşken kayıt
  varsayılanı yer tutucudur; `chainBody` alanı taşır (K-725).

## 196.2 — Düğüm aşımı

- **Sarmalayıcı** (`Internal/WorkflowNodeTimeout.cs`): çağrı başına bağlı
  CTS, `Task.Delay(süre, TimeProvider)` ile `WhenAny`. Aşımda CTS iptal
  edilir (K-805), `TraconWorkflowFunctionTimeoutException` atılır. Dinlemeyen
  gövdede bekleme yine kesilir; geç tamamlanma loglanır, run'a yazılmaz.
- **Sonuç:** run `Failed`, tip `workflow_function_timeout`, sınıf `Timeout`
  (Açık Soru 2); kimlik `StableIdentities`'e girer.
- 🚨 **Sebep yalnız düğümün kendi CTS'inden okunur** (K-844 kuralı):
  çağıran iptali veya `RunTimeout` sırasında run `Canceled` kalır.
- **Retry:** deneme başına, `retry(timeout(handler))` (Açık Soru 3); en kötü
  süre XML'de. `CancelAfter` seçilirse site `TimeoutSiteTests`'e girer.
- **Ölçülmeli (Adım 0):** MAF `FunctionExecutor` işleyicisine run'ın `linked`
  token'ını geçiriyor mu? Testler ayırmaz (`WorkflowRunTimeoutTests.cs:68-82`).
  Hayırsa run iptali çalışan fonksiyona ulaşmaz; `TraconWorkflowOptions.cs:48-55`
  ve `concepts/workflows.md:353-358` yanlıştır → `kusur-giderme`.

## 196.3 — Job süresi

- **Alan:** `JobRequest.MaxDuration` → `JobRecord.MaxDuration`; sütun
  `jobs.max_duration_ms bigint NULL` (49,7 gün `int` ms'yi aşar), üç
  sağlayıcı (Açık Soru 5). `JobColumns` sonuna; sabit indeks okuyucu
  (`SqlJobStore.cs:323-324`) yeniden numaralanmaz.
- **Varsayılan:** `TraconSchedulingOptions.MaxDurationByHandlerKey`, worker'da
  çözülür; zamanlama ve üçüncü taraf depo job'u da kapsanır. Öncelik: job >
  harita > yok (K1). Süre deneme başınadır.
- **Doğrulama:** `JobDispatcher` → `ArgumentException`; harita →
  `TraconSchedulingOptionsValidator`. Harita `BindSchedulingCollections`'a
  elle eklenir: dört koleksiyon F-385 ile (2026-10-13) orada bağlandı;
  `TraconSchedulingOptionsBindingTests` bir satır alır.
- **Aşım:** token iptal edilir; handler dönünce deneme başarısızdır,
  `ErrorMessage = tracon.job.max-duration-exceeded (ref: …)`; deneme kalırsa
  yeniden kiralanır (Açık Soru 7). Dinlemeyen handler beklenir (Açık Soru 8);
  süreden sonra başarıyla dönen handler `Completed` yazar.
- İçteki workflow/agent run'ı `Canceled` kapanır. `JobStoreContract` alanı
  sınar; alanı taşımayan depo süreyi düşürür — rehber yazar.

## 196.4 — İptalin token'a ulaşması

- Handler token'ı üç ayrı CTS'e bağlıdır: `stoppingToken`, `cancelled`,
  `deadline`; hata yolu sebebi kaynaktan okur.
- **Yoklama:** çalışan her job için `IJobStore.GetAsync` her `PollInterval`'da
  (10 sn; Açık Soru 6); `Cancelled` veya kayıt yoksa `cancelled` iptal
  edilir. Kira döngüsü (100 sn) yavaştır. Hata tick başına yakalanır (F-326).
- `FinalizeFailedAsync`'in `Cancelled` dalı korunur (`:615`); `IJobStore`
  değişmez. `IJobHandler` XML'i ve cancel ucu açıklaması
  (`SchedulingEndpoints.cs:144`, istemci tabanı) yeniden yazılır.

---

## 196.5 — Agent düğümü süresi ve workflow başına `RunTimeout` (F-405)

- **Düğüm:** `WorkflowNodeReference.Timeout` agent düğümünde de geçerlidir;
  `ChildDeadline`'ı o düğüm için geçersiz kılar. `waitTimeout` aynı kuralla
  türer (süre + 30 sn, `TraconOptions.cs:211-214`). Öncelik: düğüm > kurulum
  `AgentGraph.ChildDeadline`. Aşım bugünkü davranıştır: run `Failed`,
  `child_run_timeout` (K-887); yeni tip gerekmez.
- **Süre kurucuya gider, kimlik değişmez.** `ChildAgentInvoker` süreyi
  kurucuda alır (`:90-100`); önbellek anahtarı süreyi de taşır
  (`AgentKey(tenant, workflow, agent, timeout)`). Kimlik
  `WorkflowAgentIdentity` ile `(workflow, agent)` çiftinden türer
  (`WorkflowAgentCache.cs:31-38`), yani yeni örnek aynı executor kimliğini
  alır. 🚨 **Ölçülmeli (Adım 0):** süre değişikliği bekleyen run'ın resume'unu
  bozmaz (test 10 deseni, agent düğümüyle).
- **Workflow:** `WorkflowDefinition.RunTimeout` (`TimeSpan?`, `null` =
  kurulum değeri, K1). Runner CTS'i tanımdan kurar (`WorkflowRunner.cs:554`);
  🚨 **ölçülmeli:** tanım bu noktada çözülmüş mü — değilse CTS tanım
  çözümünden sonra kurulur ve slot beklemesi kurulum değerine sayılır.
- **Tavan:** `TraconWorkflowOptions.MaxRunTimeout` (`TimeSpan?`, varsayılan
  `null`). `null` iken tanım yalnız **kısaltabilir** (≤ `RunTimeout`); host
  tavanı açınca tanım o değere kadar uzatabilir (Açık Soru 11). Gerekçe: süre
  paylaşılan `MaxConcurrentRuns` slotunu tutar (F-322); tavanı host seçer.
- **Doğrulama:** `TimerPeriod.IsValid` (F-280) ve tavan, kayıtta `400`.
  Düğüm süresi `RunTimeout`'a göre doğrulanmaz (Açık Soru 12).
- `agentNames` biçimi (GroupChat, Handoff, Magentic, Concurrent) düğüm
  taşımaz (K-901); orada tanım `RunTimeout`'u ve kurulum `ChildDeadline`'ı
  geçerlidir (Açık Soru 10).
- Konsol: tanım düzeyinde `RunTimeout` alanı (saniye); düğüm satırındaki
  süre alanı (§ 196.1) agent düğümünde de görünür.

---

## Planlanan Public API

> Taslak. **Bugün eklemek bedava / `1.0.0` GA sonrası record alanı ve aşırı
> yükleme kırıcı.** CS0121 (`null` argüman) ve RS0026/RS0027 ölçülür.

```csharp
// Tracon.Abstractions
WorkflowNodeReference, WorkflowFunctionDescriptor { TimeSpan? Timeout { get; init; } } // agent düğümünde de (F-405)
WorkflowDefinition { TimeSpan? RunTimeout { get; init; } }                            // F-405
sealed class TraconWorkflowFunctionTimeoutException : TraconException // ad taslak
    { FunctionName; Timeout; ErrorType => "workflow_function_timeout" }
JobRequest, JobRecord { TimeSpan? MaxDuration { get; init; } }
JobErrorCodes.MaxDurationExceeded = "tracon.job.max-duration-exceeded"
TraconSchedulingOptions { IDictionary<string, TimeSpan> MaxDurationByHandlerKey { get; } }
// Tracon.Workflows
WorkflowFunctionOptions { TimeSpan? Timeout { get; set; } }
TraconWorkflowOptions { TimeSpan? MaxRunTimeout { get; set; } }                     // F-405, varsayılan null
AddWorkflowFunction<TInput, TOutput>(builder, name, factory, Action<WorkflowFunctionOptions> configure)
WorkflowFunctionResponse { TimeSpan? Timeout } // Tracon.AspNetCore
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Değişiklik |
|---|---|---|---|
| `PUT` | `/api/workflows/{name}` | Admin | `timeout` (fonksiyon ve agent düğümü) ve `runTimeout` doğrulanır |
| `GET` | `/api/workflows/functions` | Reader | `timeout` görünür |
| `GET` | `/api/jobs/{id}` | Reader | `maxDuration` görünür |
| `POST` | `/api/jobs/{id}/cancel` | Operator | Token bir `PollInterval`'da düşer |

Roller değişmez (`WorkflowEndpoints.cs:64,105`, `SchedulingEndpoints.cs:124,137`).

### Arayüz payı

Düğüm satırına süre alanı (agent düğümü dahil) ve tanım düzeyinde `RunTimeout` alanı; pay kapanışta gzip KB olarak yazılır.

---

## Planlanan Dosya Listesi

```
Abstractions TraconException · Workflows/{WorkflowNodeReference,WorkflowFunctionDescriptor,WorkflowDefinition}
             Scheduling/{IJobDispatcher,JobRecord,JobErrorCodes,IJobHandler,TraconSchedulingOptions}
Core         Runs/DefaultRunErrorClassifier · Workflows/WorkflowDefinitionValidator · …Binding.Operations
             Scheduling/{JobWorkerBackgroundService,JobDispatcher,InMemoryJobStore,…OptionsValidator,
                       WorkflowRunJobHandler,WorkflowJobHandler (token sebebi, Faz 197)}
Workflows    WorkflowFunctionOptions · TraconWorkflowFunctionExtensions · TraconWorkflowOptions
             Internal/{WorkflowNodeTimeout*,WorkflowFunctionRegistration,…Registry,WorkflowDefinitionCompiler,
                       WorkflowAgentCache,WorkflowRunner}
Sql.Shared   SqlQueriesBase · SqlJobStore ; {PostgreSql,SqlServer,Sqlite}/Migrations
AspNetCore   WorkflowContracts · SchedulingEndpoints ; Testing.Contracts.Xunit JobStoreContract
UI           lib/workflow-steps.ts · screens/workflow-editor/steps.tsx · locales/{en,tr}/workflows.ts
diğer        TimeoutSiteTests · samples/Tracon.Api/Program.cs (süreli fonksiyon, yavaş job)
(* yeni; kök src/Tracon.<paket>/)
```

---

## Hata Modları ve Testler

> Seviyeyi plan seçer. Workflow satırları `WorkflowTestHost` +
> `TriggerableTimeProvider`, job satırları `ManualTimeProvider` kullanır.

| # | Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|---|
| 1 | Aşım run'ı düşürmez, yanlış tiple düşürür ya da sonraki düğüm koşar | Fonksiyonel | `WorkflowFunctionTimeoutTests` |
| 2 | Düğüm alanı kaydı ezmez; boş alan varsayılana dönmez | Fonksiyonel | `WorkflowFunctionTimeoutTests` |
| 3 | **İptal:** aşımdan sonra dinleyen gövde koşar (K-805) | Fonksiyonel (token gözlemi) | `WorkflowFunctionTimeoutTests` |
| 4 | Dinlemeyen gövde run'ı tutar, kapalı run'a yazar, gözlenmeyen istisna bırakır | Fonksiyonel | `WorkflowFunctionTimeoutLateCompletionTests` |
| 5 | Çağıran iptali veya `RunTimeout` düğüm aşımı yazılır | Fonksiyonel | `WorkflowFunctionTimeoutReasonTests` |
| 6 | Retry aşılan denemeyi denemez veya çağıran iptalini dener | Birim + Fonksiyonel | `WorkflowFunctionTimeoutRetryTests` |
| 7 | **Eşzamanlılık:** paylaşılan kapanışta bir run'ın aşımı diğerini keser | Fonksiyonel (paralel iki run) | `WorkflowFunctionTimeoutConcurrencyTests` |
| 8 | **Başka kiracı:** aynı ad, farklı süre karışır | Fonksiyonel | `WorkflowFunctionTimeoutTenantTests` |
| 9 | **Aşırı girdi:** sıfır, negatif, 49,7 gün üstü veya agent düğümünde süre geçer | Birim + Fonksiyonel (`PUT` → `400`) | `WorkflowFunctionTimeoutValidationTests` |
| 10 | Süre değişikliği bekleyen run'ın resume'unu bozar | Fonksiyonel | `WorkflowFunctionTimeoutResumeTests` |
| 11 | Yeni kimlik `Unknown` sınıflanır | Birim | `DefaultRunErrorClassifierTests` |
| 12 | Düğüm alanı depoda düşer (`TimeSpan`, kaynak üreteci) | Sözleşme (dört depo) | `WorkflowDefinitionStoreContract` |
| 13 | Konsol kaydı süreyi siler | Vitest | `full-replace-editors.test.tsx`, `workflow-steps.test.ts` |
| 14 | İptal handler'a ulaşmaz ya da bir `PollInterval`'dan geç ulaşır | Birim + Fonksiyonel | `JobWorkerCancellationTests`, `JobCancelEndpointTests` |
| 15 | Aşım kod taşımaz; kapanış veya iptal aşım sayılır; deneme kuralı bozulur | Birim | `JobWorkerDeadlineTests` |
| 16 | **Başka kiracı:** başkasının iptali job'a ulaşır | Fonksiyonel | `JobCancelEndpointTests` |
| 17 | **Alt sistem:** yoklamada `store` hatası döngüyü durdurur veya handler'ı keser | Birim (hatalı depo) | `JobWorkerCancellationTests` |
| 18 | **Eşzamanlılık:** bir job'un iptali diğerini keser; yoklama sızar | Birim | `JobWorkerCancellationTests` |
| 19 | `MaxDuration` depoda düşer | Sözleşme (dört depo) | `JobStoreContract` |
| 20 | Harita bağlanmaz; aralık dışı değer açılışta düşmez | Birim (DI, `ValidateOnStart`) | `TimeoutOptionValidationTests` + bağlama testi |
| 21 | İçteki run job iptalinde açık kalır | Fonksiyonel (`tracon.workflow`) | `WorkflowJobCancellationTests` |
| 22 | Yeni süre sitesi kapıya girmez; istemciler alanı taşımaz | Mimari + Paket | `TimeoutSiteTests`, NSwag/TS kapıları |
| 23 | Konsoldan verilen süre uygulanmaz | E2E | mevcut workflow E2E |
| 24 | Agent düğümü süresi uygulanmaz; kurulum `ChildDeadline`'ı kazanır ya da `waitTimeout` türemez | Fonksiyonel (`HangingModelProvider`) | `WorkflowAgentNodeTimeoutTests` |
| 25 | Süre değişikliği agent adımının kimliğini değiştirir, bekleyen run resume'da düşer | Fonksiyonel | `WorkflowAgentNodeTimeoutResumeTests` |
| 26 | Tanımın `RunTimeout`'u uygulanmaz; tavan yokken uzatma kabul edilir; tavan aşımı `400` değil | Fonksiyonel (`PUT` → `400`, `TriggerableTimeProvider`) | `WorkflowDefinitionRunTimeoutTests` |
| 27 | **Başka kiracı:** aynı adlı workflow'un süresi diğer kiracıya geçer (önbellek anahtarı) | Fonksiyonel | `WorkflowAgentNodeTimeoutTests` |
| 28 | `RunTimeout`/`MaxRunTimeout` depoda ya da üç istemcide düşer | Sözleşme (dört depo) + Paket | `WorkflowDefinitionStoreContract`, NSwag/TS kapıları |
| 29 | Kurtarma açıkken (Faz 197) süre aşımı workflow işini durdurmaz: sonraki deneme aşılan run'ı sürdürür; son deneme kuralı (K-927) aşım denemesini saymaz | Fonksiyonel, son kiralama gerçek worker'dan (`QueuedWorkflowHarness.FinishWithWorkerAsync`) | `WorkflowJobDeadlineRecoveryTests` |

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | `slow`: kayıt 2 sn, gövde 10 sn | Koş | ~2 sn'de `Failed`, `workflow_function_timeout` |
| 2 | Aynı | Düğüm `"timeout":"00:00:05"` → `PUT` → koş | ~5 sn'de düşer |
| 3 | — | `"00:00:00"` veya agent düğümünde `timeout` | `400` |
| 4 | Konsol | Süre gir → kaydet → aç; boşalt | Değer durur; boşken varsayılan — 👤 görsel |
| 5 | Token'ı dinlemeyen fonksiyon | Koş | Sürede düşer; geç tamamlanma logu |
| 6 | Yavaş workflow'a `tracon.workflow` zamanlaması | `trigger` → `POST …/cancel` | Job `Cancelled`, run `Canceled`, ≤ 10 sn |
| 7 | Haritada `tracon.workflow: 00:00:05` | `trigger` | Job `Failed`, `tracon.job.max-duration-exceeded` |
| 8 | Haritada `00:00:00` | Host'u başlat | Açılış seçeneği adlandırarak düşer |
| 9 | Yavaş model, agent düğümü `"timeout":"00:00:05"` | Koş | ~5 sn'de `Failed`, `child_run_timeout`; kurulum `ChildDeadline` 2 dk |
| 10 | `MaxRunTimeout` yok | `"runTimeout":"00:30:00"` ile `PUT` | `400`; tavanı açıklar |
| 11 | `Tracon__Workflows__MaxRunTimeout=01:00:00` | Aynı `PUT`, sonra 15 dk'lık zinciri koş | `200`; run 10 dk'da düşmez |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Düğüm başına geçersiz kılma | A: yalnız kayıt · B: + `WorkflowNodeReference.Timeout` · C: `TSettings` | **B** — süre canlı script'e bağlı; C K-900'ü açar |
| 2 | Aşımda run durumu | A: `Failed` (K-887) · B: `Canceled` (K-844) | **A** — adım cevap vermedi |
| 3 | Süre ve retry | A: deneme başına · B: düğüm toplamı | **A** — retry `Timeout`'u geçici sayar |
| 4 | İki parametreli kayda süre | A: `Action<…>` aşırı yüklemesi · B: opsiyonel parametre · C: yalnız ayarlı kayıt | **A** — B K-422/K-902'ye aykırı |
| 5 | Job süresinin yeri | A: alan + sütun + harita · B: yalnız harita · C: payload | **A** — `MaxAttempts` emsali; payload handler'ındır (F-374) |
| 6 | İptal yoklama aralığı | A: `PollInterval` · B: yeni seçenek · C: + süreç içi sinyal | **A** — 10 sn yeter |
| 7 | Aşım denemesi | A: başarısız deneme, yeniden kiralanabilir · B: hep terminal | **A** — K-886 ile tutarlı |
| 8 | Dinlemeyen job handler'ı | A: beklenir, slot tutulur · B: sınırdan sonra terk | **A** — B çift yürütme getirir (K-886); düğümde bekleme kesilir |
| 9 | Agent düğümü + workflow başına `RunTimeout` | A: bu faz · B: yeni aday (F-405) | **A — karar verildi** (bakımcı, 2026-10-13): podcast ölçümü varsayılanların altında; § 196.5 |
| 10 | `agentNames` biçiminde adım süresi | A: tanım düzeyi `AgentStepTimeout` · B: yok, `RunTimeout` + kurulum `ChildDeadline` | **B** — K-901 alanı yalnız `nodes`'a koydu; ihtiyaç ölçülünce aday |
| 11 | Tanım `RunTimeout`'u kurulum değerini aşabilir mi | A: hiç · B: `MaxRunTimeout` tavanına kadar (varsayılan `null` = yalnız kısaltır) · C: sınırsız | **B** — K1; slot paylaşımı host'un kararıdır |
| 12 | Düğüm süresi tanımın `RunTimeout`'unu aşarsa | A: kayıtta `400` · B: kabul, `RunTimeout` zaten keser | **B** — iki süre ayrı soruyu cevaplar; XML ve site yazar |
| 13 | Kurtarma açıkken süre aşımıyla duran workflow işi (Faz 197 devri) | A: aşım ayrı sebeptir — handler aşılan run'ı ayırt edilebilir tiple kapatır, kurtarma onu sürdürmez · B: aşım kesintidir — sonraki deneme sürdürür, süre deneme başınadır | **A** — B'de süre workflow işini durdurmaz; Faz 197 ayırmadı (§197.2) |

---

## Bitiş Ölçütleri (DoD)

- [ ] Aşım run'ı `Failed` + `workflow_function_timeout` ile bitirir; gövde token'ı iptal olur (test 1-4)
- [ ] Çağıran iptali ve `RunTimeout` düğüm aşımı yazmaz; aralık dışı süre `400` (test 5, 9)
- [ ] İptal handler token'ını bir `PollInterval` içinde düşürür; aşım `tracon.job.max-duration-exceeded` yazar (test 14-15)
- [ ] İki alan dört depoda round-trip; harita bağlanır; `TimeoutSiteTests` güncel (test 12, 19-20, 22)
- [ ] Agent düğümü süresi `ChildDeadline`'ı geçersiz kılar ve resume'u bozmaz; tanım `RunTimeout`'u tavanla sınırlıdır (test 24-28)
- [ ] Dört doğrulama kapısı sıfır uyarı — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [ ] `samples/Tracon.Api` ile gerçek `run` (case 1, 6, 7, 9, 11), çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri iki alan dosyasına eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz
- [ ] `en.ts`/`tr.ts` eksiksiz; bundle payı yazıldı; `CHANGELOG.md` yeni aşırı yüklemeyi anar

### Doğrulama komutları

Komutlar case 1, 6, 7, 9 ve 11'den kapanışta gerçek çıktıyla yazılır.

---

## Riskler

| Risk | Önlem |
|------|-------|
| Dinlemeyen gövde arka planda yan etki üretir | XML ve site yazar; geç bitiş loglanır |
| Yoklama depo yükü | Çalışan job × 1 okuma / `PollInterval` |
| Faz 199 (F-299) düğüm maliyeti eklerse geç gövdenin maliyeti kaybolur (K-806 sınıfı) | Faz 199 devir notu |

---

<!-- AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME. -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez**.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Beklenen: düğüm süresi + hata kimliği (`public-api`),
> job süresi sütunu + iptal yoklaması (`kalıcı-veri`, `public-api`), tanım
> `RunTimeout`'u ve `MaxRunTimeout` tavanı (`public-api`).

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | |

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Bulgu yoksa "🔴 ve 🟡 yok"
> yazılır.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: sözleşmeler, tuzaklar (🚨), yarım işler, sıradaki faz.
