# Faz 196 — Uzun Adımların Süre Sınırı ve İptali

> **Durum:** ✅ Tamamlandı (2026-10-13)
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

- [x] Aşım run'ı `Failed` + `workflow_function_timeout` ile bitirir; gövde token'ı iptal olur (test 1-4)
- [x] Çağıran iptali ve `RunTimeout` düğüm aşımı yazmaz; aralık dışı süre `400` (test 5, 9)
- [x] İptal handler token'ını bir `PollInterval` içinde düşürür; aşım `tracon.job.max-duration-exceeded` yazar (test 14-15)
- [x] İki alan dört depoda round-trip; harita bağlanır; `TimeoutSiteTests` güncel (test 12, 19-20, 22)
- [x] Agent düğümü süresi `ChildDeadline`'ı geçersiz kılar ve resume'u bozmaz; tanım `RunTimeout`'u tavanla sınırlıdır (test 24-28)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 9088e92e`, üç faz (196, 202, 203) `main`'e birleştirildikten sonra `c2592ccd` üzerinde tek koşum, çıkış 0; sevk edilen sözleşme değiştiği için `yayin --kuru --surum 1.0.0-preview.4` ilk koşumda çıkış 0 (§ *Kapanış kapısı ve yayın provası*)
- [x] `samples/Tracon.Api` ile gerçek `run` (case 1, 6, 7, 9, 11), çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri iki alan dosyasına eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz
- [x] `en.ts`/`tr.ts` eksiksiz; bundle payı yazıldı; `CHANGELOG.md` yeni aşırı yüklemeyi anar

### Kapanış kapısı ve yayın provası (`main`, 2026-10-13)

Faz 196, 202 ve 203 ayrı worktree dallarında paralel geliştirildi ve `main`'e birleştirildi (`bc3c7d11` → 203, `fac7fbd6` → 196, `c2592ccd` → 202). Uygulama dalları `kapanis` ve `yayin` koşmadı (orkestratör kararı, makine paylaşılıyordu); ikisi birleşik `main`'de bir kez koştu. Fazın tabanı `9088e92e`'dir.

| Kapı | Commit | Sonuç |
|---|---|---|
| `python3 scripts/kapi.py kapanis --taban 9088e92e` | `c2592ccd` | ✅ çıkış 0 — `tarama` · `--denetle` · script testleri · agent map · denetim paketi · `dotnet build` 89 sn · tam `dotnet test` 506 sn · `dotnet pack` · `dotnet format --verify-no-changes` 114 sn · `npm run check` (1078 sayfa, kırık bağlantı yok, SEO 0 hata). Performans kapısı tetiklenmedi: diff üç sıcak yola ve `bench/`'e dokunmaz |
| `python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.4` | `c2592ccd` | ✅ ilk koşumda çıkış 0 — kırıcı liste 3 tip, 0 TFM düşüşü, 3 paket (hepsi `Unreleased` notunda) · 20 paket · npm dry-run · altı paketlenmiş örnek (`FileRunStore` 106, `CustomModelProvider` 38 + 1 atlandı, `CustomRunJudge` 11, `CustomAgentSource` 15, `CustomTool` 18, `CustomJobHandler` 10) · Native AOT smoke · net8.0 tüketici smoke |

Önceki prova çıktısı (Faz 197/198, `dcc1a88f`) silinmedi: `artifacts/package/yayin/1.0.0-preview.4-onceki-dcc1a88f`.

### Doğrulama komutları

Örnek uygulama 2026-10-13'te koşuldu: `190beb9d` derlemesi, port 5081,
PostgreSQL şeması `faz196`, `Development`, `Tracon__Demo__SlowModel__Seconds=20`.
İstek yolları `/tracon/api/...` altındadır; gövdeler `MT-WF-142`…`147` ve
`MT-JOB-146`…`148` içindedir.

```bash
dotnet artifacts/bin/Tracon.Api/release/Tracon.Api.dll --urls http://127.0.0.1:5081   # + case'e göre --Tracon:... argümanları
curl -sX PUT  $APU/api/workflows/bounded -H 'Content-Type: application/json' -d '{...}'
curl -sX POST $APU/api/workflows/bounded/run -H 'Content-Type: application/json' -d '{"message":"hello"}'
curl -s       $APU/api/runs/{runId}
curl -sX POST $APU/api/schedules/episode-nightly/trigger        # gövdesiz; boş JSON gövde + JSON content-type 400 döner (eski davranış)
curl -sX POST $APU/api/jobs/{jobId}/cancel
```

| Case | Gerçek çıktı |
|---|---|
| 1 | `HTTP 200 in 2.3 s` · run `Failed` · `workflow_function_timeout` / `Timeout` · "Function node 'slow' did not return within 2 s and was cancelled…" · katalog: `slow`, `deaf` → `"00:00:02"` |
| 2 | Düğüm `00:00:05`: `5.1 s`, `Failed`, "…within 5 s…" |
| 3 | Fonksiyon düğümünde `00:00:00` → `400` "Node 'slow' in workflow 'bad' has a 'timeout' of 00:00:00. A function node's timeout must be from 1 to 4294967294 milliseconds…" · agent düğümünde `00:00:05` → `200` |
| 5 | `deaf`: `2.2 s`, `Failed`; ~4 sn sonra log: "Function node deaf of workflow run 01a13615-fea6-… returned 00:00:06.0025155 after it started, past its timeout of 00:00:02; its result is discarded" |
| 6 | İptal `16:29:49.25` → `204`; iş hemen `Cancelled`; run `01a13619-408c…` `Canceled`, `completedAt 16:29:57.03` (iptalden 7,8 sn sonra; render 60 sn sürerdi); öğe `Pending` |
| 7 | Harita `tracon.workflow=00:00:05`: iş `Failed`, `attempt 1`, "tracon.job.max-duration-exceeded (ref: 7262b7b0)"; run 5,0 sn sonra `Canceled`, `job_max_duration_exceeded`; öğe `Failed`. Düzeltmeden önce: `attempt 2`, "All of the job's items failed." (kusur, `b93ed7da`) |
| 8 | Harita `00:00:00`: süreç dinlemeden çıktı (134); "TraconSchedulingOptions.MaxDurationByHandlerKey['tracon.workflow'] must be between 1 and 4294967294 milliseconds. Actual value: 00:00:00." |
| 9 | `podcast` (agent düğümü `00:00:05`, model 20 sn): `5.3 s`, `Failed`, `child_run_timeout` / `Timeout`, "Agent 'slow-writer' did not respond in time (limit: 00:00:05, set by the 'timeout' of node 'slow-writer' in workflow 'podcast')…" · `podcast-plain`: `20.2 s`, `Completed` |
| 10 | `runTimeout 00:30:00`, tavan yok → `400` "…above the installation's run timeout (00:10:00). A definition may only shorten 'Tracon:Workflows:RunTimeout' unless the host sets 'Tracon:Workflows:MaxRunTimeout'…" |
| 11 | Ölçekli (`RunTimeout=00:00:10`, `MaxRunTimeout=00:01:00`, 15 sn'lik zincir): `00:02:00` → `400` "…above the host's ceiling 'Tracon:Workflows:MaxRunTimeout' (00:01:00)." · `00:00:30` → `200`, run `15.2 s` `Completed` · kontrol (süresiz): `10.1 s` `Canceled`, `TimeoutException`, "…Raise the 'Tracon:Workflows:RunTimeout' value…" · tanım `00:00:05`: `5.0 s` `Canceled`, "…after 00:00:05, the run timeout its definition sets…" |

Case 4 👤 görseldir; koşulmadı (Vitest karşılığı: `workflow-editor.test.tsx`
"timeouts"; Aşama 2'de E2E `WorkflowTests` konsoldan süre girip run'ı ölçer).
**Arayüz payı:** gzip bundle 205,5 KB, taban 204,1 KB → **+1,4 KB**.

Aşama 2 yeniden koşumu (`135c7fde` derlemesi, aynı ortam): case 7 — iş `Failed`,
`attempt 1`, "tracon.job.max-duration-exceeded (ref: a3fe3931)", run 5,0 sn sonra
`Canceled` + `job_max_duration_exceeded`, öğe `Failed`; case 6 — iptal
`18:25:54.84`, run `Canceled` `18:26:02.76` (7,9 sn), iş `Cancelled`, öğe `Pending`.

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

> Plan ile gerçek arasındaki fark **gizlenmez**. Aşama 1 (kod + örnek uygulama)
> sonunda yazıldı; Aşama 2'de bakımcı kararları ve denetim bulgularıyla
> güncellendi. "Karar N" bakımcının 2026-10-13 kararlarıdır.

| # | Plan | Gerçek | Gerekçe |
|---|---|---|---|
| 1 | §196.2 Adım 0: MAF `FunctionExecutor` run token'ını geçiriyor mu? | **Geçiriyor.** Prob testi: `RunTimeout`'ta gövdenin token'ı 2 sn içinde iptal oldu. `kusur-giderme` gerekmedi; prob silindi | Ölçüm |
| 2 | §196.5: tanım slot beklemesinden önce çözülmemişse CTS sonra kurulur, slot beklemesi kurulum değerine sayılır | Tanım çözümü ve derleme **slot beklemesinden önceye** alındı; tanımın `RunTimeout`'u slot beklemesini de sayar; derlenemeyen tanım slot almadan düşer. **Onaylandı (karar 2, K-960)** | Geri dönüş yolu tanımın süresini kuyrukta yok sayardı |
| 3 | §196.5 + Açık Soru 10-B: `agentNames` biçimi süre taşımaz; konsolda süre alanı agent düğümünde de görünür | Aşama 1: süreli agent-only zincir `nodes` yazılıyordu ve sapma K-901'i yanlış alıntıladı (K-901: "fonksiyon varsa `nodes`, yoksa `agentNames`"). **Aşama 2 (karar 1, denetim 🟡4, K-959):** alan yalnız fonksiyon adımlı zincirde görünür; süre biçimi değiştirmez; `agentNames` biçimine düşen süreli adım (son fonksiyon kaldırıldı ya da HTTP ile yazılmış `nodes`) uyarıyla kaydı tutar, `chainBody` istek kurmaz | Biçim değişimi bekleyen run'ı "graph's structure differs" ile resume edilemez yapar ve agent adımının canlı akışını keser. Süre isteyen operatör fonksiyon adımı ekler ya da `nodes`'u HTTP ile yazar |
| 4 | Açık Soru 7-A: aşım denemesi başarısızdır, deneme kaldıkça yeniden kiralanır | Genel handler'da öyle. Workflow handler'larında 13-A önce gelir (**karar 4, K-963**): kuyruklu run işi hemen düşer; batch durdurulan öğeyi `Failed` yazar, deneme başarısız olur; bekleyen öğe kalmadıysa iş kodla hemen düşer. Aşama 2: aynı anda tamamlanan son öğede iş tamamlanır (🟡2); run'ı başlamamış öğe bekler kalır (🟡1) | Örnek uygulama case 7: tek öğeli batch kodsuz "All of the job's items failed." ile bitiyordu (`b93ed7da`) |
| 5 | §196.3: depoda okunan geçersiz `MaxDuration` için kural yok | Aşama 1: işçi loglar, işi sınırsız koşar. **Aşama 2 (🟡3):** loglar ve anahtarın haritadaki sınırına düşer; harita yoksa sınırsız | Bozuk satır operatörün sınırını kapatmamalı |
| 6 | §196.3: runner job sınırını nasıl öğrenir — yazılmadı | İç `JobAttemptSignals` + ambient `AmbientJobAttempt` (işçinin `ExecuteWithHandlerAsync` gövdesinde açılır); `AmbientWriteSiteTests` yeni işaretçi aldı; `JobContext.Signals` iç özelliktir | Run hata tipini yalnız sebebi bilen yazabilir; public yüzey büyümez |
| 7 | §196.5: kayıtta tavan `400` | `Tracon.Core` içinde iç `IWorkflowRunTimeoutPolicy`; `UseWorkflows` kaydeder; `PUT` onu opsiyonel `[FromServices]` ile alır | `Tracon.AspNetCore` `Tracon.Workflows`'a bağımlı değil; motor kayıtlı değilken yalnız `TimerPeriod` denetlenir |
| 8 | §196.5: tavan yalnız kayıtta | `MaxRunTimeout` açılışta `Enabled` kapalıyken de doğrulanır (`TimerPeriod`, `≥ RunTimeout`); tavan sonradan düşerse kayıtlı daha uzun değer tavana kesilir, uyarı loglanır. **Onaylandı (karar 3, K-960)** | Ayar tavanı emsali |
| 9 | Açık Soru 4: CS0121 ölçülür | `builder.AddWorkflowFunction<TIn,TOut>(name, factory, null)` **CS0121** verir (konumsal `null` hem `string? description`'a hem `Action<WorkflowFunctionOptions>`'a uyar); adlı argüman çalışır; RS0026/RS0027 tetiklenmedi. `CHANGELOG` "Source break" yazar | `1.0.0` öncesi kabul (K-958) |
| 10 | §196.1: konsolda saniye alanı | Saniye (`90`, `2.5`) veya `hh:mm:ss`; dokunulmayan değer sunucudaki yazımıyla döner (`storedTimeout`, K-918); okunamayan metin kaydı tutar | Tam değiştirme kaydı (K-725) değeri sessiz silmemeli |
| 11 | Test 9: "agent düğümünde süre" kötü girdi | F-405 ile geçerli girdi; test ve case 3 agent düğümünü `200` + üst sınırı (`MaxAgentNodeTimeout` = `TimerPeriod` − 30 sn) ile sınar | Plan satırı F-405'ten önceydi |
| 12 | Test 24: `waitTimeout` türetmesi | Sert kesim (süre + 30 sn) invoker alanından yansımayla okunur | 30 sn beklenmez |
| 13 | §196.5: aşım bugünkü davranış | `ChildAgentInvoker` opsiyonel `deadlineSetting` alır; ret metni ayarı adlandırır ("set by the 'timeout' of node …") | Operatör hangi ayarı değiştireceğini bilmeli |
| 14 | Planlanan dosya listesi | Örnek uygulamaya `SlowModelProvider` (`Tracon:Demo:SlowModel:Seconds`), `slow` ve `deaf` fonksiyonları | Case 9 gerçek model olmadan ölçülebilsin |
| 15 | Manuel case 11: 10 dk / 1 sa / 15 dk | Ölçekli: 10 sn / 1 dk / 15 sn | Aynı kural, kısa koşum |
| 16 | Migration numarası uygulamada | PostgreSQL `0057`, SQL Server `0045`, SQLite `0044` (`job_max_duration`) | — |
| 17 | Test 23 (E2E) | Aşama 1'de koşulmadı. Aşama 2: `WorkflowTests.A_step_timeout_typed_in_the_console_is_saved_on_its_node_and_stops_the_run` yazıldı; tam koşumda yeşil | — |
| 18 | Job satırları sahte saatle (`ManualTimeProvider`) | Core'un `ManualTimeProvider`'ı zamanlayıcı sürmez; Aşama 1 job testleri gerçek saat + kısa süre kullandı. **Aşama 2 (🟡6):** due-time `ManualClock` `tests/Shared/Time/`'a taşındı (Core + Workflows, K-411 bağlantı), periyodik zamanlayıcı aldı; iptalin tam bir `PollInterval`'da ve sürenin tam sınırda düştüğü işçinin kendi saatinde ölçülür. Kalan: diğer job satırları gerçek saatle, sınırları hata sınırıdır | İşçinin iş arama döngüsü `TimeProvider` almaz (`new PeriodicTimer(PollInterval)`): tamamen sahte saatle işçi iş kiralamaz |
| 19 | §196.2: geç bitiş logu | Çağıran iptali veya run süresiyle beklenmeyen gövdenin geç bitişi artık "past its timeout" demez (🟢4) | Yanıltıcı log |
| 20 | — | Aşama 1'in iki `<example>` bloğu örnek kapısında derlenmedi (JSON parçası `language="json"` taşımıyordu; örnek tanımsız `IAudioRenderer`'a dayanıyordu). Aşama 1 `ic-dongu` yalnız derlemişti; Aşama 2'nin tam koşumu yakaladı (`f3ba7ced`) | Fazın ürettiği regresyon |
| 21 | `capabilities.md` iki satır | Birleşik dalda ajan haritası bütçeyi 38 B aştı (11 302 / 11 264); iki satır tek "Time limits" satırı oldu (11 213 B) | `build-agent-map.mjs` bütçesi |
| 22 | — | Yeni public istisna `TraconWorkflowFunctionTimeoutException` yüzey envanterinde `kanıtsız` çıktı; site sayfası sabitini adlandırır (`concepts/workflows.md`) | `public-yuzey-envanteri.py` |

## Bu Fazda Verilen Kararlar

Karar defterine girenler (hepsi kullanıcı kararı; ayrıntı satırlarında):

| K | Özet | Kategori |
|---|---|---|
| K-958 | Fonksiyon düğümü süresi: kayıt + düğüm, deneme başına, `workflow_function_timeout`; `CS0121` kabul | `public-api` |
| K-959 | Agent düğümü süresi yalnız `nodes` zincirinde; konsol biçimi çevirmez (karar 1) | `public-api` |
| K-960 | Tanım `RunTimeout`'u + `MaxRunTimeout` tavanı; slot beklemesi sayılır (karar 2, 3) | `public-api`, `geri-dönüşü-pahalı` |
| K-961 | Job süresi alanı, sütunu, haritası; deneme başına; bozuk değer haritaya düşer | `public-api`, `kalıcı-veri` |
| K-962 | İptal token'a bir `PollInterval` içinde ulaşır; sebep kaynaktan | `public-api` |
| K-963 | Workflow işinde süre aşımı işin sonudur; 13-A > 7-A; hazırlık penceresi (karar 4) | `geri-dönüşü-pahalı`, `kalıcı-veri` |

Kategori dışı (K açılmadı):

- İç seam'ler public değil: `JobAttemptSignals`, `AmbientJobAttempt`, `JobContext.Signals`, `IWorkflowRunTimeoutPolicy` (Sapma 6, 7).
- `ManualClock` paylaşılan test aparatıdır, iki test projesine bağlanır (K-411 deseni).
- Örnek uygulamanın `SlowModelProvider`'ı yalnız `Tracon:Demo:SlowModel:Seconds` verilince kaydolur.
- Batch handler'da sınır yolu `ItemRun.Started`'a bağlıdır (K-963'ün uygulaması).

## Gerçekleşen Public API

```csharp
// Tracon.Abstractions
WorkflowNodeReference      { TimeSpan? Timeout { get; init; } }
WorkflowFunctionDescriptor { TimeSpan? Timeout { get; init; } }
WorkflowDefinition         { TimeSpan? RunTimeout { get; init; } }
JobRequest, JobRecord      { TimeSpan? MaxDuration { get; init; } }
TraconSchedulingOptions    { IDictionary<string, TimeSpan> MaxDurationByHandlerKey { get; } }
JobErrorCodes.MaxDurationExceeded = "tracon.job.max-duration-exceeded"
public class TraconWorkflowFunctionTimeoutException : TraconException
{
    public const string WorkflowFunctionTimeoutErrorType = "workflow_function_timeout";
    public override string ErrorType { get; }
    public string? FunctionName { get; init; }
    public TimeSpan? Timeout { get; init; }
    // + () · (string) · (string, Exception) kurucuları
}
// Tracon.Workflows
WorkflowFunctionOptions { TimeSpan? Timeout { get; set; } }
TraconWorkflowOptions   { TimeSpan? MaxRunTimeout { get; set; } }
static ITraconBuilder AddWorkflowFunction<TInput, TOutput>(this ITraconBuilder, string name,
    Func<IServiceProvider, Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>>> factory,
    Action<WorkflowFunctionOptions> configure)
// Tracon.AspNetCore
WorkflowFunctionResponse { TimeSpan? Timeout { get; init; } }
WorkflowSaveRequest      { TimeSpan? RunTimeout { get; init; } }
// Tracon.Testing.Contracts.Xunit
JobStoreContract.A_jobs_own_time_limit_survives_the_round_trip_and_the_lease()
JobStoreContract.A_job_without_a_time_limit_reads_back_none()
WorkflowDefinitionStoreContract.Node_timeouts_and_the_run_timeout_survive_the_round_trip()
WorkflowDefinitionStoreContract.Saving_again_without_timeouts_clears_them()
```

HTTP: `PUT /api/workflows/{name}` `timeout` (fonksiyon ve agent düğümü) ve
`runTimeout` alır, aralık ve tavan dışını `400` ile reddeder;
`GET /api/workflows/functions` `timeout`, `GET /api/jobs/{id}` `maxDuration`
gösterir; `POST /api/jobs/{id}/cancel` açıklaması token'ın bir `PollInterval`'da
düştüğünü yazar. OpenAPI belgesi ve iki istemci yeniden üretildi. Yüzey tabanı:
`Tracon.Abstractions` 414 → 415 tip (birleşik dalda 421).

## Dosya Listesi (gerçekleşen)

Yeni: `Abstractions/Scheduling/JobAttemptSignals.cs` · `Core/Workflows/IWorkflowRunTimeoutPolicy.cs` ·
`Workflows/Internal/{WorkflowNodeTimeout,TraconWorkflowRunTimeoutPolicy}.cs` · üç migration
(`PostgreSql/0057`, `SqlServer/0045`, `Sqlite/0044` `_job_max_duration.sql`) ·
`samples/Tracon.Api/SlowModelProvider.cs` · `tests/Shared/Time/ManualClock.cs` (Workflows
testlerinden taşındı).

Değişen kaynak: `TraconException.cs`, `Workflows/{WorkflowNodeReference,WorkflowFunctionDescriptor,WorkflowDefinition}.cs`,
`Scheduling/{JobRecord,IJobDispatcher,JobErrorCodes,TraconSchedulingOptions,IJobHandler}.cs` ·
Core: `DefaultRunErrorClassifier`, `WorkflowDefinitionValidator`, `ChildAgentInvoker`,
`TraconOptions` (`WaitTimeoutPad` iç), `JobDispatcher`, `TraconSchedulingOptionsValidator`,
bağlama, `JobWorkerBackgroundService`, `WorkflowRecovery`, `WorkflowRunJobHandler`,
`WorkflowJobHandler` · Workflows: `WorkflowFunctionOptions`, `TraconWorkflowOptions`,
`TraconWorkflowOptionsValidator`, `TraconWorkflowFunctionExtensions`, `WorkflowFunctionRegistration`,
`WorkflowFunctionRegistry`, `WorkflowDefinitionCompiler`, `WorkflowAgentCache`, `WorkflowRunner`,
`TraconWorkflowsBuilderExtensions`, `README.md` · Sql: `SqlQueriesBase`, `SqlJobStore`,
`WorkflowDefinitionPayload`, `applied-migrations.json` · AspNetCore: `WorkflowContracts`,
`WorkflowEndpoints`, `SchedulingEndpoints` · üretilen: `docs/openapi/tracon.json`,
`TraconApiClient.g.cs`, `schema.ts` · UI: `lib/workflow-steps.ts`, `screens/workflow-editor.tsx`,
`screens/workflow-editor/steps.tsx`, `locales/{en,tr}/workflows.ts` · `PublicAPI.Unshipped.txt` (4 paket).

### Gerçekleşen testler

| Sınıf | Test | Ne kanıtlar |
|---|---:|---|
| `WorkflowFunctionTimeoutTests` | 6 | Aşım `Failed` + tip; düğüm > kayıt; gövde token'ı iptal |
| `WorkflowFunctionTimeoutLateCompletionTests` | 3 | Dinlemeyen gövde run'ı tutmaz; geç bitiş/arıza loglanır; çağıran durunca "past its timeout" yazılmaz |
| `WorkflowFunctionTimeoutReasonTests` | 4 | Çağıran iptali ve `RunTimeout` düğüm aşımı yazmaz (K-844) |
| `WorkflowFunctionTimeoutRetryTests` | 4 | Süre deneme başına; çağıran iptali denenmez |
| `WorkflowFunctionTimeoutConcurrencyTests` · `…TenantTests` | 1 · 1 | Paralel run ve kiracı ayrımı |
| `WorkflowFunctionTimeoutValidationTests` | 12 | Aralık dışı süre kayıtta ve `PUT`'ta reddedilir |
| `WorkflowFunctionTimeoutResumeTests` · `WorkflowAgentNodeTimeoutResumeTests` | 2 · 3 | Süre değişikliği resume'u bozmaz |
| `WorkflowDefinitionRunTimeoutTests` | 4 | Tanım süresi, tavan, kesme ve uyarı |
| `JobWorkerCancellationTests` | 6 | İptal token'a tam bir `PollInterval`'da ulaşır (işçinin saati); tick hatası döngüyü bitirmez |
| `JobWorkerDeadlineTests` | 9 | Kod, deneme kuralı, sebep önceliği, harita, sınırda düşme (işçinin saati), bozuk değer haritaya düşer |
| `JobDispatcherMaxDurationTests` | 3 | Dispatcher aralık denetimi |
| `WorkflowJobHandlerTests` (+3) | 14 | Hazırlıkta düşen sınır öğeyi bekletir (iki yol); aynı anda tamamlanan son öğe işi tamamlar |
| `WorkflowTimeoutSaveTests` · `WorkflowAgentNodeTimeoutTests` | 8 · 3 | HTTP kayıt, katalog, agent düğümü süresi, kiracı önbellek anahtarı |
| `JobCancelEndpointTests` · `WorkflowJobCancellationTests` | 3 · 2 | İptal ucu, başka kiracı, iç run kapanır |
| `WorkflowJobDeadlineRecoveryTests` | 6 | 13-A: iş/öğe sonu; `Stopped` planı gerçek işçiden (kuyruklu run ve batch) |
| Sözleşme (`JobStoreContract`, `WorkflowDefinitionStoreContract`) | 2 + 2 | Dört depoda round-trip |
| Vitest (`workflow-steps`, `workflow-editor`, `full-replace-editors`) | 77 | Saniye/`hh:mm:ss`, dokunulmayan değer, biçim kuralı, kayıt tutma |
| E2E `WorkflowTests` (+1) | 1 | Konsoldan süre → düğüm → run `workflow_function_timeout` |

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 2 — plan aşamasında F-405 kapsamı eklendi, Faz 197 devri Açık Soru 13'ü ekledi; uygulamada bir karar değişti (K-959, karar 1) |
| Düzeltme turu sayısı | 1 — Aşama 2: denetimin 6 🟡'ı ve 🟢4 |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 1 — iki `<example>` bloğu örnek kapısında derlenmedi (Sapma 20, `f3ba7ced`) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi — faz henüz kapanmadı |

## Denetim Bulguları

`faz-denetim` (bağımsız denetçi, 2026-10-13): 🔴 yok, 6 🟡, 4 🟢. Hepsi gerçek bulgu.

| # | Bulgu | Triyaj | Sonuç |
|---|---|---|---|
| 🟡1 | Hazırlık penceresinde sınır (K-926): devam satırı sonsuza dek `Queued`; başlamamış öğe run kimliksiz `Failed` | gerçek | Düzeltildi `135c7fde`: sınır yolu `ItemRun.Started`'a bağlı; iki düşen test, mutasyonla kırmızı |
| 🟡2 | Son öğe sınırla aynı anda tamamlanınca iş yine kodla `Failed` | gerçek | Düzeltildi `135c7fde`; düşen test |
| 🟡3 | Depodaki aralık dışı `MaxDuration` haritaya düşmüyor | gerçek | Düzeltildi `135c7fde`; `JobRecord` XML'i; düşen test |
| 🟡4 | Sapma 3'ün K-901 alıntısı yanlış; biçim değişimi resume'u ve akışı bozar | gerçek | Karar 1 ile değişti `dedbc039` (K-959); Vitest, E2E, MT-WF-144 |
| 🟡5 | `Stopped` plan adımının handler dalları testsiz | gerçek | İki fonksiyonel test, son kiralama gerçek işçiden `135c7fde`; mutasyonla kırmızı |
| 🟡6 | DoD "bir `PollInterval` içinde" ölçülmüyor | gerçek | `ManualClock` testleri `135c7fde`; kalan sapma Sapma 18 |
| 🟢1 | `TimeoutSiteTests` `Task.Delay(TimeSpan, TimeProvider, …)` sitelerini görmez | gerçek | Devredildi: F-449 |
| 🟢2 | `WorkflowAgentCache` süre başına girdi açar, tahliye yok | gerçek | Devredildi: F-450 |
| 🟢3 | `JobStoreContract` `int` ms sınırını aşan süreyi sınamıyor | gerçek | Devredildi: F-451 |
| 🟢4 | Çağıran iptalinde geç bitiş "past its timeout" diye loglanıyor | gerçek | Düzeltildi `f3ba7ced`; düşen test |

## Sonraki Faza Devir Notu

**Sözleşmeler.** Fonksiyon düğümü `retry(timeout(handler))` sarmalayıcısında koşar
(`Workflows/Internal/WorkflowNodeTimeout.cs`, `TraconWorkflowFunctionExtensions.NodeWrapper`).
Hata kimlikleri: `workflow_function_timeout`
(`TraconWorkflowFunctionTimeoutException.WorkflowFunctionTimeoutErrorType`),
`job_max_duration_exceeded` (`JobAttemptSignals.MaxDurationExceededRunErrorType`, iç),
`tracon.job.max-duration-exceeded` (`JobErrorCodes.MaxDurationExceeded`). Job token'ının
sebebi `JobContext.Signals.Reason`'dan okunur (iç).

**Tuzaklar.**
- 🚨 Workflow job handler'ına yeni bir hazırlık adımı eklenirse `ItemRun.Started`'ın ÖNÜNDE kalır; sınır yalnız başlamış run'ı bitirir (K-963, K-926).
- 🚨 Run'ı `job_max_duration_exceeded` dışında bir tiple kapatan değişiklik kurtarmayı süreyi yeniden deneme başına yapar (`WorkflowRecovery.PlanAsync` `Stopped`).
- 🚨 `TimeoutSiteTests` ortaktır (Faz 202 dahil): yeni `CancelAfter`/argümanlı CTS sitesi bir satır ister; `Task.Delay(süre, saat, …)` siteleri kapsam dışıdır (F-449).
- 🚨 Ajan haritası 11 213 / 11 264 B: yeni yetenek satırı başka bir satırı kısaltmayı gerektirir.
- 🚨 `docs/manuel-test/*.md` bütçesi birleşik dalda 2 550 487 / 2 556 000 B (~5,5 KB boş): sonraki fazın case'i için önce tarihli koşum notları `arsiv/manuel-test-kosum-2026-09/`'a taşınır (F-441 deseni).
- Zamanı soran testte `tests/Shared/Time/ManualClock.cs` kullan; iş işçisinin iş arama döngüsü gerçek saattir.

**Devirler.** Faz 199 (aynı sarmalama noktası; geç gövdenin maliyeti) ve Faz 200
(`RunTimeout`/`Timeout` tanımın parçası; süre şekil değişikliği değildir) "Bu Faza
Başlarken"e birer madde aldı. Faz 202 başka bir oturumda uygulanıyor; ortak dosya
`TimeoutSiteTests` ve `tests/Shared/Time/ManualClock.cs` bu notta.

**Yarım kalan.** `kapi.py kapanis` ve yayın provası birleşik `main`'de yeşil koştu (§ *Kapanış kapısı ve yayın provası*). `site-deploy.sh` koşulmadı: yayın bakımcı onayını bekler (üç fazın site değişikliği birlikte yayınlanır).
