# Faz 197 — Dayanıklı Workflow Yürütmesi: Bağlantısız Run ve Otomatik Sürdürme

> **Durum:** 📋 Planlandı (2026-10-12)
> **Plan onayı:** Bakımcı, 2026-10-13 (Açık Sorular'ın önerileri kabul edildi: 1-A, 2-A, 3-A, 4-A, 5-A, 6-A, 7-A)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-318**, **F-321**
> **Önkoşul:** F-344, F-341, F-364 2026-10-12'de kapandı (K-909, K-908, K-904). [Faz 196](196-UZUN-ADIM-SURE-VE-IPTAL.md) ile sıra serbest; ikisi de job token'ına dokunur
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Workflows`, `.AspNetCore`, `.Sql.Shared`, üç SQL sağlayıcısı, `.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** Yok (`0037_run_continuation.sql:18` indeksi, `job_items.run_id` var); Açık Soru 1-B gerektirir
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü: `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `concepts/{workflows,runs}.md`, `guides/{background-work,reliability}.md` · sevk edilen: XML, Workflows README, `CHANGELOG.md`
> **Manuel test alanı:** `docs/manuel-test/{15-WORKFLOWS,16-IS-KUYRUGU-VE-ZAMANLAMA,21-DAYANIKLILIK-VE-IPTAL}.md`

---

## Bu Faza Başlarken

1. Bu doküman (`faz-baslangic`)
2. `grep -n "K-127\|K-130\|K-162\|K-288\|K-304\|K-394\|K-498\|K-641\|K-845\|K-886\|K-898\|K-899" docs/KARARLAR.md`
3. `for f in 46-DAYANIKLI-CALISTIRMA 87-KESILEN-ISIN-DEVAMI 194-WORKFLOW-VE-KUYRUK-PARAMETRELERI; do awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/$f.md; done`
4. Hafıza: `workflows.md`, `altyapi-haritasi.md` (worker HTTP isteğini görmez), `test-yalitimi.md` (kuyrukta sahte saat)

---

## Amaç

Workflow run'ı okuyan HTTP akışıyla yaşar (K-845); kuyruktan başlayan run çökme veya deploy sonrası baştan koşar. Bu faz üç işlemi kuyruğa alır; job'un run'ı tekrar kiralamada son checkpoint'ten sürer.

- **F-318** — üç uca `Prefer: respond-async`, `Idempotency-Key`, kuyrukta yetki kapısı.
- **F-321** — opt-in kurtarma: son checkpoint, deneme sınırı, devam bağı.

**Tüketici:** podcast hattı `tracon.workflow` job'uyla başlar; deploy ortasında kalan üretim baştan koşmamalı. "`RequestPort` + makine yanıtı" deseni (render bitince host `respond` çağırır, slot boşalır) `respond-async` ister.

### Kapsam dışı

| Kalem | Neden |
|---|---|
| Reconciliation'ın sürdürmesi | Açık Soru 1 |
| Tek yanıt, kiracı geneli liste | F-319; ikinci yanıt dal kalır (K-130) |
| Konsol düğmesi, istek `lane`'i | HTTP önce; `LaneByHandlerKey` yeter |
| `jobs.payload` koruması | F-374 |

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `WorkflowEndpoints.cs:806-826` | Akış `RequestAborted` ile okunur; okuyucu düşerse run `Canceled` |
| `WorkflowEndpoints.cs:127-216` | Üç uç yalnız SSE; `Prefer`/`Idempotency` sayısı `0` |
| `AgentEndpoints.cs:270`, `:307`, `:983-1170` | Agent eşi: `WantsAsync`, filtre, `Queued` satır, `202` |
| `WorkflowJobHandler.cs:129-130` | İş yalnız yeni run açar; oturum, kullanıcı geçmez |
| `WorkflowJobHandler.cs:102-111` | Run kimliği sonda yazılır; tekrar kiralanan öğe baştan koşar |
| `RunAuthorizationTypes.cs:59-61` | Yetki kapısı bir kez, istekte sorulur; `Core`/`Workflows` çağırmaz |
| `IdempotencyResults.cs:41-54`, `IdempotencyFilter.cs:211` | 🚨 Yanıt belleğe tamponlanır; akış yalnız `stream: true` ile tanınır |
| `RunContinuationEligibility` (F-344, K-909) | Uzlaştırıcı ve işleyici yalnız kök agent run'ını sürdürür; workflow kökü `Failed` kalır ve ret olayı resume'u adlandırır. Bu faz workflow kökü için otomatik resume'u bu kuralın yanına ekler |
| `PostgresQueries.cs:244`, `RunContinuationJobHandler.cs:198`, `WorkflowRunner.cs:585-611` | UPSERT devam bağını düz yazar; bağsız ikinci başlangıç (runner) siler |
| `PostgresQueries.cs:1196-1200`, `JobItemRecord.cs:20` | `status = 0` öğeye `run_id` yazılabilir; XML "işlenmeden `null`" der |
| `WorkflowRunner.cs:454-473` | Son checkpoint run başınadır |
| `WorkflowRunner` resume hatası, `WorkflowAgentIdentity.cs:56-62` | 2026-10-12'de düzeldi (F-342): mesaj grafiğin yapısının değiştiğini söyler, yeniden başlatmayı suçlamaz (K-127) |
| `TraconCheckpointStore.cs:82-86`, `WorkflowRunner.cs:1370-1387` | Checkpoint MAF sürümü taşır; fark okumada görünür |
| `BatchItemInput.cs:42`, `WebhookEventPayload.cs:63` | Öğe `sessionId` taşımaz; webhook `SessionId` taşır |
| `SchedulingEndpoints.cs:490-501` | Her yerleşik anahtar HTTP ile zamanlanabilir |
| `RunEndpoints.cs:1029-1031` | `Queued` iptali iş kimliği = run kimliği eşliğine dayanır |
| `TraconAsyncRunOptions.cs:37`, `RunReconciliationOptions.cs:33`, `TraconSchedulingOptions.cs:31` | Kuyruk run'ı tek deneme; yetim eşiği ve kira 5 dk |

**Düşen kanıt:** adayların satır atıfları kaymıştı; iddialar geçerli.

> `99fe0b13`'te doğrulandı (2026-10-12). MAF 1.20.0 `ResumeStreamingAsync(Workflow, CheckpointInfo, CheckpointManager, CancellationToken)` ölçüldü (`maf-api-kesfi`).

---

## 197.1 — Kuyruk yolu (F-318)

- Kapılar `202`'den önce, senkron sırayla: atıf → yetki (`RunAccess.Start`; resume/respond kaynak run ile) → sahiplik → kota (Açık Soru 2) → parametre. Respond F-341 doğrulamasını koşar (**ölçülmeli:** akış dışında çağrılabilir mi).
- Run kimliği önce üretilir; `Queued` satır `Kind`, `WorkflowName`, `SessionId`, atıf taşır; iş kimliği = run kimliği (K-304).
- `202` + `Location` + `AcceptedRunResponse`; izleme `GET /api/runs/{id}/events`; `AsyncRun.Enabled = false` → `501`.
- `Idempotency-Key` filtresi eklenir. 🚨 `Prefer`'siz anahtar `400` (Açık Soru 5).
- İş `tracon.workflow-run` (Açık Soru 3). 🚨 Fabrika (`Registration.Storage.cs:189-192`) yeni bağımlılığı geçirmezse kurtarma sessizce kapalı kalır.
- Yük `operation`, `runId`, `sessionId`, `userId`, `labels` (F-364) ve işlem alanlarını taşır; worker yeniden doğrular. 🚨 Respond yanıtı `jobs.payload`'da düz durur (K-899).
- Zamanlama `tracon.workflow-run`'a `400` verir; elle yük yetki kapısını atlardı.

## 197.2 — Kurtarma (F-321)

- `TraconWorkflowOptions.ResumeInterruptedRuns` (`false`, K1), `MaxResumeAttempts` (`1`); checkpoint kapalıysa başlangıç hatası. `Core`/`AspNetCore` iç seam ile okur.
- **Sahip job'dur** (Açık Soru 1); bağlantıya bağlı run otomatik sürmez (K-845).
- Run'dan önce öğeye `Pending` + `RunId` işareti yazılır. Tekrar kiralanan işte işaretli `Pending` öğe kesintidir; çökme, zarif kapanış, Faz 196 süre aşımı ayırt edilmez.
- Son run `Completed`/`AwaitingInput` → benimse; `Running` → `Failed` kapat; devam `< MaxResumeAttempts` → son checkpoint'ten yeni run, değilse öğe `Failed`.
- Zincir `ContinuedFromRunId`, ileri yürüyüş `RunQuery.ContinuedFromRunId`. 🚨 Bağ UPSERT'te `COALESCE` olur.
- Checkpoint zincirde geriye aranır; yoksa run baştan koşar, resume/respond işlemi tekrarlar.
- Devam run'ı kaynağın oturumu, kayıtlı parametresi (K-898) ve yükteki atıfla koşar; kota ve yetki kapısı yok.
- Yarım düğüm tekrar koşar (K-498). Okunamayan checkpoint adlandırılmış hatayla düşer; resume mesajı F-342 ile düzeldi.
- Çocuk run (`Depth > 0`) agent devamına girmez (F-344 ile hazır; bu faz korur). Tek run'lı iş `1 + MaxResumeAttempts` deneme alır (Açık Soru 4).

## 197.3 — Batch öğesine `sessionId`

Kapsamda: öğe `sessionId` alır, handler `WorkflowRunRequest`'e geçirir. Host üretim kimliğini oturum yapar; `run.completed` ve devam run'ı aynı oturumu taşır. Agent öğesinde `sessionId` öğeyi düşürür. 🚨 Korelasyondur; tekilleştirme yalnız `Idempotency-Key`'dir.

---

## Planlanan Public API

> `RunQuery` alanı **bugün bedava / GA sonrası kırıcı**; seçenek ve sabit ikili uyumlu.

```csharp
public const string WorkflowRun = "tracon.workflow-run"; // JobHandlerKeys, BuiltIn
public Guid? ContinuedFromRunId { get; init; }           // RunQuery
public bool ResumeInterruptedRuns { get; set; }          // TraconWorkflowOptions
public int MaxResumeAttempts { get; set; } = 1;          // TraconWorkflowOptions
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `POST` | workflow `run`/`resume`/`respond` | Operator | `Prefer: respond-async` → `202` |
| `GET` | `/api/runs?continuedFromRunId=` | Reader | Devamları listeler |
| `PUT` | `/api/schedules/{name}` | Admin | `tracon.workflow-run` → `400` |

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/  JobHandlerKeys, BatchItemInput, JobItemRecord, IJobStore, RunQuery
src/Tracon.Core/          WorkflowJobHandler, WorkflowRecovery (yeni), AgentBatchJobHandler,
                          Registration.Storage, RunReconciliationService, InMemory depolar
src/Tracon.Workflows/     TraconWorkflowOptions, Validator, BuilderExtensions, WorkflowRunner
src/Tracon.AspNetCore/    Workflow/Run/SchedulingEndpoints, RouteBuilderExtensions
src/Tracon.{Sql.Shared,PostgreSql,SqlServer,Sqlite}/, Testing.Contracts.Xunit/
```

---

## Hata Modları ve Testler

> Kuyruklu testler gerçek saatle koşar.

| # | Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|---|
| 1 | Kapı atlanır, sırası değişir, red iş bırakır | Fonksiyonel | `WorkflowAsyncRunGateTests` |
| 2 | Reddeden `IRunAuthorizationHandler` kuyrukta `202` alır | Fonksiyonel | `WorkflowAsyncRunGateTests` |
| 3 | `Queued` satır eksik açılır; UPSERT atfı siler | Fonksiyonel + Sözleşme | `WorkflowAsyncRunTests`, `RunStoreContract` |
| 4 | **İptal:** kuyruktaki iş iptal edilemez | Fonksiyonel | `WorkflowAsyncRunCancelTests` |
| 5 | Geçersiz yanıt `202` alır | Fonksiyonel | `WorkflowAsyncRespondTests` |
| 6 | **Başka kiracı:** başkasının run'ı kuyruğa girer; süzgeç sızar | Fonksiyonel + `TenantIsolationContract` | `WorkflowAsyncTenantTests` |
| 7 | **Eşzamanlılık:** aynı anahtar iki iş açar | Fonksiyonel (paralel) | `WorkflowAsyncIdempotencyTests` |
| 8 | `Prefer`'siz anahtar SSE'yi tamponlar | Fonksiyonel | `WorkflowAsyncIdempotencyTests` |
| 9 | Yük atıf/oturum taşımaz | Fonksiyonel | `WorkflowAsyncAttributionTests` |
| 10 | Zamanlama `tracon.workflow-run` kabul eder | Fonksiyonel | zamanlama uç testleri |
| 11 | **Alt sistem hatası:** enqueue düşer, satır `Queued` kalır | Fonksiyonel | `WorkflowAsyncRunTests` |
| 12 | Bayrak kapalıyken davranış değişir | Fonksiyonel | `WorkflowRecoveryTests` |
| 13 | Tamamlanmış run yeniden koşar | Birim + Fonksiyonel | `WorkflowRecoveryTests` |
| 14 | Tamamlanmış düğüm tekrar çağrılır | Fonksiyonel (sayaçlı düğüm) | `WorkflowRecoveryTests` |
| 15 | Zincir sınırı aşılır; kaynak checkpoint'i kullanılmaz | Birim + Fonksiyonel | `WorkflowRecoveryTests` |
| 16 | UPSERT bağı siler; işaret sayacı bozar | Sözleşme (dört depo) | `RunStoreContract`, `JobStoreContract` |
| 17 | Gerçek çökme sonrası devam yok | Süreç (PostgreSQL, SIGKILL) | `TwoProcessWorkflowResumeTests` |
| 18 | Zarif kapanış sonrası devam yok | Fonksiyonel | `WorkflowRecoveryTests` |
| 19 | Farklı MAF damgası döngü yapar | Fonksiyonel | `WorkflowRecoveryTests` |
| 20 | Parametreli devam varsayılanla koşar | Fonksiyonel | `WorkflowRecoveryTests` |
| 21 | Workflow kökü/çocuğu agent devamına girer | Fonksiyonel | `RunContinuationTests` |
| 22 | **Boş/aşırı girdi:** boş mesaj, uzun `sessionId`, agent öğesinde `sessionId` | Birim + Fonksiyonel | `WorkflowJobHandlerTests` |
| 23 | İstemciler `202` gövdesini taşımaz | Paket | NSwag/TS kapıları |

Sınır: kira kaybında eski sahip yaşarsa iki kopya koşar (K-886).

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Worker açık | `run` + `Prefer` | `202`; `Queued` → `Completed` |
| 2 | Aynı | Aynı anahtar iki kez; `Prefer`'siz | Aynı `runId`; `400` |
| 3 | `AwaitingInput` run | `respond` + `Prefer`; yanlış `requestId` | `202`; `400` |
| 4 | Bayrak açık | Checkpoint sonrası süreci öldür, başlat | Devam `ContinuedFromRunId` taşır — 👤 |
| 5 | Aynı | Zarif durdur, başlat | Devam `Completed` — 👤 |
| 6 | Webhook | `sessionId`'li öğe | `run.completed` aynı `SessionId` |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Kesilen run'ı kim sürer? | A: job tekrarı · B: + reconciliation | **A** — B'de aynı run iki kez sürer; tek sahip migration ister (üç sağlayıcı) |
| 2 | Kuyruktaki resume/respond kota kapısı | A: yok · B: var | **A** — senkron yolla aynı (K-394, K-162) |
| 3 | İş anahtarı | A: tek anahtar + `operation` · B: üç anahtar | **A** — agent eşi |
| 4 | Tek run'lı işin deneme sınırı | A: `1 + MaxResumeAttempts` · B: `AsyncRun.MaxAttempts` | **A** — B'de varsayılan `1` kurtarmayı kapatır |
| 5 | `Prefer`'siz `Idempotency-Key` | A: `400` · B: yok say · C: akışsız JSON | **A** — B korumayı düşürür; C uzun run'ı bağlar |
| 6 | F-319'dan önce `respond-async` | A: şimdi · B: bekle | **A** — tekrarı `Idempotency-Key` kapatır |
| 7 | Çöken run'ın `Loop`'lu agent adımı tur düzeyinde sürsün mü (2026-10-13, podcast ölçümü: üç tur ≈ 14 dk ve ≈ $2,5; tüketicinin `--resume`'u tamamlanan turu korur) | A: kapsam dışı — adım baştan koşar, faz bunu belgeler · B: tur sonu checkpoint'i (harness loop durumu) | **A** — tur durumu MAF `LoopAgent`'ın içindedir; ölçülmeden checkpoint'e alınmaz. Belge maliyeti yazar; ihtiyaç ölçülürse aday |

---

## Bitiş Ölçütleri (DoD)

- [x] Üç uç `202` döner, aynı anahtar tek iş açar (test 1-8); kesilen iş checkpoint'ten sürer, bayrak kapalıyken davranış aynı (test 12-18) — `WorkflowAsync*Tests` (7 sınıf, 33 test), `WorkflowRecoveryTests` (9), `WorkflowRecoveryAttemptLimitTests` (5, son kiralama gerçek worker'dan), `TwoProcessWorkflowResumeTests` (PostgreSQL, `SIGKILL`)
- [ ] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 65d72b98` · sonuç § *Kapanış kapısı*'nda
- [x] `samples/Tracon.Api` ile gerçek `run` (async, süreç öldürme, devam) yapıldı, çıktı belgeye yazıldı — aşağıdaki iki tablo; ikincisi denetim düzeltmesinden sonra
- [x] `secret` taraması boş döndü — `kapi.py tarama`, kapanış zincirinin ilk adımı
- [x] Manuel kabul case'leri eklendi; otomatikleştirilebilenler koşuldu — `MT-WF-139`…`141` ✅, `MT-RES-095`/`096` ve `MT-JOB-145` 👤 (örnek uygulamada koşuldu)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 1 gerçek 🔴, düzeltildi (§ *Denetim Bulguları*)
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz · sonuç § *Kapanış kapısı*'nda

### Örnek uygulama koşumu — gerçek çıktı (2026-10-13, port 5197)

Derlenmiş DLL doğrudan koşuldu (`--contentRoot`), `ASPNETCORE_ENVIRONMENT=Production`
(`user-secrets` yüklenmedi, Echo sağlayıcı). Depo worktree içinde bir SQLite dosyası:
`Tracon__Sqlite__ConnectionString="Data Source=<worktree>/artifacts/f197-sample/tracon.db"`;
paylaşılan veritabanına yazılmadı. `Tracon__ContentProtection__Enabled=false` (SQLite
ile `RawKeys` gerekmesin diye), `Tracon__Workflows__ResumeInterruptedRuns=true`,
`Tracon__Scheduling__LeaseDuration=00:00:15`, `…__PollInterval=00:00:01`. Yavaş adım için
örnek uygulamaya `render` fonksiyonu eklendi (ayar `seconds`). İki tanım kaydedildi:
`episode-quick` (`word-count` → `render` 2 sn) ve `episode` (`word-count` → `render` 40 sn).

| Kontrol | Gerçek çıktı |
|---|---|
| `POST /api/workflows/episode-quick/run` + `Prefer: respond-async` + `Idempotency-Key: episode-7` | `202 Accepted`, `Location: /tracon/api/runs/01a1342e-cca9-7f99-82e8-917561e9f1dd`, `Preference-Applied: respond-async`; gövde `runId` = `jobId`, `eventsLocation` `…/events` |
| Aynı istek, aynı anahtar | `202`, aynı `runId`, `Idempotency-Replayed: true`; iş listesinde tek iş |
| `GET /api/runs/{id}` hemen / 5 sn sonra | `Queued Workflow episode-quick session=01a1342ecca97ca4bf71ea5c2afdc562` / `Completed` aynı oturum; iş `Completed`, `attempt 1 of 2` |
| `Idempotency-Key: episode-8`, `Prefer` yok | `400`, `title: "Idempotency-Key requires Prefer: respond-async"` |
| `episode` kuyrukta, ilk checkpoint yazıldıktan sonra `kill -9` (pid 64093), yeniden başlatma | kök `01a1342f-064d-…` `Failed`, `error.type: interrupted`, mesaj "…Run '01a1342f-43b9-…' continues it from its latest checkpoint." |
| `GET /api/runs?continuedFromRunId={kök}` | tek satır: `01a1342f-43b9-75f4-a868-987c3cfb9981 Completed continuedFromRunId=01a1342f-064d-… session=01a1342f064d7fd0a10fd591061bb0b6` (kökle aynı oturum); iş `Completed attempt 2 of 2` |
| İki run'ın olayları | kök: `ExecutorInvoked word-count` · `ExecutorCompleted word-count` · `ExecutorInvoked render` · `RunFailed`; devam: yalnız `ExecutorInvoked render` · `ExecutorCompleted render` · `RunCompleted` — `word-count` yeniden çağrılmadı |
| `episode` + `sessionId: episode-10`, checkpoint sonrası `kill -TERM` (zarif), yeniden başlatma | kök `Canceled`; iş `Completed attempt 2 of 2`; devam `01a13431-ecae-… Completed continuedFromRunId=01a13431-adbf-… session=episode-10` |
| `summarize-and-approve` kuyrukta → `AwaitingInput`; `respond` + `Prefer`, `requestId: no-such-request` | `400`, `title: "Response invalid"` |
| Aynı uç, `{"text":"yes"}` (port `bool` bekler) | `400` |
| Aynı uç, `{"approved":true}` | `202`; yeni run `Completed`; yanıtlanan run `AwaitingInput` kalır |
| Zamanlama `tracon.workflow`, öğe `{"message":"Episode eleven","sessionId":"production-11"}`, tetik | iş `Completed`; öğe `Completed`, `runId` = run; `GET /api/runs?sessionId=production-11` → run `Completed`, `workflowName: episode-quick` |
| `PUT /api/schedules/f197-run` `handlerKey: tracon.workflow-run` | `400` |
| `PUT /api/schedules/f197-agent` `tracon.agent-batch`, öğede `sessionId` | `200` — kayıt şekli kabul eder; öğe çalışırken düşer (`AgentBatchJobHandlerTests`). Bkz. Plandan Sapmalar 9 |
| Üç süreç günlüğü | `fail:`/istisna satırı yok |

Uygulama koşum sonunda `kill -TERM` ile kapatıldı; 5197 boş. Bu tablo denetim
düzeltmesinden ÖNCE ölçüldü: o kodda tek run'lı iş `1 + MaxResumeAttempts` deneme
alıyordu (`attempt … of 2`). Düzeltmeden sonra aynı iş `of 3` gösterir.

### Örnek uygulama koşumu — denetim düzeltmesinden sonra (2026-10-13, port 5197)

Aynı yapılandırma, yeni bir SQLite dosyası, `a9315535`'teki kod. `episode`
(`render` 40 sn) yeniden kaydedildi.

| Kontrol | Gerçek çıktı |
|---|---|
| `Tracon__Workflows__MaxResumeAttempts=0`; `episode` kuyrukta, ilk checkpoint yazıldıktan sonra `kill -9`, yeniden başlatma | iş `Failed attempt 2 of 2`, `errorMessage: tracon.job.workflow-run-not-continued (ref: 88016f5c)`; run `Failed`, `error.type: interrupted`, mesaj "…stopped before the run ended. It is not continued: its chain already has 0 continuation run(s), and 'Tracon:Workflows:MaxResumeAttempts' allows no more."; devam run'ı yok. Düzeltmeden önce bu kiralama handler'a ulaşmıyordu (iş `attempts-exhausted`, run `Running`) |
| Aynı süreç günlüğü | tek `fail:` satırı, beklenen ret: `WorkflowRunJobHandler` … `not-continued`; run kimliği `(null)` yazıyordu → `a9315535` run'ı adlandırır |
| `MaxResumeAttempts=1` (varsayılan), `episode` kuyrukta | iş `Pending maxAttempts 3`; run `Completed`; iş `Completed attempt 1 of 3` |

Uygulama `kill -TERM` ile kapatıldı; 5197 boş.

---

## Riskler

| Risk | Önlem |
|------|-------|
| Yarım düğüm yan etkiyi ikiler | K-498 kuralı XML ve sitede |
| Kuyruk eşzamanlılığı `MaxConcurrentRuns`'ı aşar, `RunTimeout` dolar (K-885) | Site yazar; F-322 |

---

<!-- AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama`. Başlıkları SİLME. -->

## Plandan Sapmalar

Uygulama oturumunda yazıldı (faz-uygulama Adım 1). Kapanış oturumu gözden geçirir.

1. **Kanıt satırı kısmen bayattı: `WorkflowJobHandler` kullanıcıyı geçiriyordu.** Plan
   "iş yalnız yeni run açar; oturum, kullanıcı geçmez" diyordu. Ölçüldü: F-364'ten beri
   handler `AmbientRunAttributionScope` açıyor, kullanıcı ve etiket geçiyor. Oturum
   geçmiyordu (doğru); 197.3 onu ekledi.
2. **`tracon.workflow-run` işinde öğe işareti yok; işaret run satırının kendisidir.**
   197.2 "run'dan önce öğeye `Pending` + `RunId` yazılır" diyor. Tek run'lı işin öğesi
   yoktur (ajan eşi gibi) ve iş kimliği = run kimliği (K-305). HTTP katmanının yazdığı
   `Queued` satır işarettir: tekrar kiralamada satır `Queued` ise iş hiç başlamamıştır,
   değilse kurtarma zinciri okunur. Öğe işareti yalnız `tracon.workflow` batch'inde.
3. **Zarif kapanış run'ı `Running` değil `Canceled` bırakır (K-845).** Plan "son run
   `Running` → `Failed` kapat" diyordu. `Canceled`/`Failed` son run da kesinti sayılır
   (çökme, deploy, zarif kapanış ayırt edilmez); kapatma yalnız `Running` satırda
   yazılır, ötekiler aldıkları hatayı korur. Kullanıcının iptal ettiği run handler'ın
   kendi sonucudur: iş `NonRetryable` düşer, kurtarmaya girmez
   (`WorkflowAsyncRunCancelTests`).
4. **`WorkflowSessionId` `Tracon.Workflows/Internal`'dan `Tracon.Abstractions`'a
   taşındı (internal).** Kural üç pakette gerekiyordu: motor, HTTP katmanı (kuyruklu
   run'ı `202`'den önce `400` ile reddetmek için) ve `BatchItemInput` (öğe tek başına
   düşsün). Kopyalamak yerine taşındı; davranış değişmedi, `Problem()`/`NewId()` eklendi.
5. **Kuyruklu `run` oturumu `202`'den önce üretir.** İstek `sessionId` taşımıyorsa
   kimlik HTTP katmanında üretilir; `Queued` satır ve devam run'ı aynı oturumu taşır.
6. **Kuyruklu `resume`/`respond` motorun akış içi retlerini `202`'den önce verir.**
   Plan respond için yalnız F-341 doğrulamasını sayıyordu. Ölçüldü: `ValidateResponseAsync`
   akış dışında çağrılabilir (zaten senkron uçta akıştan önce çağrılıyor), ama bekleyen
   olmayan `requestId`'yi ve `AwaitingInput` olmayan run'ı `null` ile geçirir. Kuyruklu
   yolda ikisi de `400`, bilinmeyen run `404`, checkpoint'siz kaynak `400` olur (`202`
   koşamayacak bir iş vaat etmesin diye). OpenAPI'ye iki uçta `404` eklendi.
7. **Bayrak kapalıyken `tracon.workflow-run` işi `AsyncRun.MaxAttempts` alır**
   (ajan eşi; varsayılan 1). Açıkken `1 + MaxResumeAttempts` (4-A) — denetimden sonra
   `2 + MaxResumeAttempts` oldu (Sapma 16). `IWorkflowRecoveryPolicy`
   iç seam'i (Core) iki değeri taşır; yalnız `UseWorkflows()` kaydeder.
8. **İş sonucu run sonucunu izler.** Run kendi başına `Failed`/`Canceled` biterse iş
   `NonRetryableJobException` (`tracon.job.workflow-run-failed`) ile hemen düşer; aksi
   hâlde kalan deneme ikinci kiralamada kesinti sanılırdı. Kodlar `internal const`
   (`QueuedTurn` deseni); public API büyümedi.
9. **Ajan batch öğesindeki `sessionId` kayıtta değil çalışmada düşer.** `BatchItemInput`
   iki handler için ortaktır ve `sessionId` artık bilinen üyedir; zamanlama kaydı şekli
   kabul eder, `AgentBatchJobHandler` öğeyi tek başına düşürür (plan: "öğeyi düşürür").
10. **Bellek içi iş deposu biten öğeyi eziyordu — ayrı `fix` commit'i.** SQL depoları
    yalnız `status = 0` öğeyi değiştirir; bellek içi depo her raporu yazıyordu. İşaret
    sözleşmesi (`A_report_for_a_finished_item_changes_nothing`) düşürdü, düzeltildi.
11. **Sözleşme case'leri sevk edilen pakette.** Plan "`tests/Shared/Contracts/`" diyen
    devri izliyordu; sözleşmeler `src/Tracon.Testing.Contracts.Xunit/`'tedir. Altı case
    eklendi (yayın provası gerekir).
12. **Örnek uygulamaya `render` fonksiyonu eklendi** (ayar `seconds`). Süreç öldürme
    koşumu için yavaş bir adım gerekiyordu; manuel case'ler de onu kullanır.
13. **Reconciliation ret metni güncellendi.** `RunContinuationEligibility`'nin workflow
    ret olayı artık job kurtarmasını da adlandırır (`ResumeInterruptedRuns`). Davranış
    aynı (1-A: reconciliation sürdürmez).
14. **Satır kayması:** plan `Registration.Storage.cs:189-192` diyordu; fabrika 196-200'deydi
    (kayma). Fabrika artık `CreateWorkflowRecovery` ile politikayı geçirir; geçirmeyen
    fabrika yedi kurtarma testini düşürdü (mutasyonla ölçüldü).
15. **Tahsis taban çizgisi `QueryRuns` 44832 → 45456 B (+624 B).** `kapi.py performans`
    kırmızı verdi. Taban commit'i (`65d72b98`) ayrı bir worktree'de tek başına ölçüldü:
    43,78 KB (= taban). Artış bu fazındır: `SelectRuns` her komutta yeni
    `continued_from_run_id` parametresini taşır (paylaşılan SQL metni sabit olduğu için
    parametre süzgeç boşken de eklenir). Yalnız bu satır güncellendi; `CacheHit`'in
    24 → 0 B düşüşü bu fazın yoluna dokunmuyor ve taban çizgisinde bırakıldı (uyarı).
    Denetim 🟡3 sonrası çözüldü: Sapma 19.

Denetim düzeltmelerinden doğan sapmalar (2026-10-13):

16. **4-A düzeltildi: iş `2 + MaxResumeAttempts` deneme alır; son deneme devam
    başlatmaz (denetim 🔴1, kullanıcı kararı, K-927).** Worker sınırı aşan
    kiralamayı handler'dan önce düşürür; `1 + MaxResumeAttempts` ile son devam
    `Running` kalıyordu. Ek kural `WorkflowRecovery.PlanAsync`'tedir: işin son
    denemesi (`job.Attempt >= job.MaxAttempts ?? TraconSchedulingOptions.MaxAttempts`)
    kesintiyi sürdürmez, zinciri kapatır. Sınıf taraması ölçtü: batch işinin
    varsayılan üç denemesi `MaxResumeAttempts ≥ 2` iken aynı kusuru üretiyordu
    (gerçek worker'lı test önce zaman aşımıyla düştü). Kural her iki handler'da
    tutar; batch işi öğe başına en çok `MaxAttempts − 2` devam alır. Sınırı
    yükseltmek tüketicinin açık `maxAttempts`'ını ezerdi.
17. **Açılıp başlamamış devam (`Queued`) kesinti sayılır.** Plan "aynı kimlikle
    başlat, yeniden sayma" diyordu (`ContinuationIsOpen`). Sayılmazsa her böyle
    deneme zinciri uzatmadan bir deneme yer ve son deneme yine handler'a
    ulaşmazdı. `ContinuationIsOpen` kalktı; kapanış (`CloseChainAsync`) zincirdeki
    her açık run'ı (`Running`/`Queued`) ardılının adıyla kapatır.
18. **Kurtarma adımı `store` hatasında işi değil denemeyi düşürür (🟡1).** Plan,
    kuyruk okuma hatasını ayırmıyordu; ilk kod onu `workflow-run-failed` ile
    tekrar denemesiz düşürüyordu. Kurtarma adımı artık motorun ret `catch`'inin
    DIŞINDA; devam satırı, onu adlandıran kapanıştan ÖNCE yazılır; kapatılamayan
    `Queued` satır (`CloseIfQueuedAsync` → `false`) denemeyi düşürür. Batch'te de
    aynı: işaret yazımı ve zincir okuması öğeyi değil denemeyi düşürür.
19. **`CacheHit` taban çizgisi 24 → 0 B (🟡3).** Taban `65d72b98` ayrı worktree'de
    0 B ölçtü: satır fazdan önce bayattı. `measuredAt` güncellendi; `kapi.py
    performans` uyarısız.
20. **Kuyruklu iş reddinin günlük satırı run'ı adlandırır.** `not-continued` ve
    `run-failed` reddi kapatılacak satır yokken `(null)` yazıyordu (örnek uygulama
    koşumunda görüldü); kapatma artık ayrı bir argümandır.
21. **Test koşumuna gerçek worker eklendi.** `QueuedWorkflowHarness.FinishWithWorkerAsync`
    son kiralamayı `JobWorkerBackgroundService`'ten geçirir; `RunAttemptAsync`
    deneme denetimini atlıyordu ve 🔴1'i gizledi.

## Bu Fazda Verilen Kararlar

Yedi karar `docs/KARARLAR.md`'ye girdi: **K-923** *(geri-dönüşü-pahalı)* kurtarmanın
tek sahibi job'dur, uzlaştırma workflow'u sürdürmez (Açık Soru 1-A, kullanıcı kararı)
· **K-924** *(public-api, güvenlik)* tek anahtar `tracon.workflow-run` + `operation`,
iş kimliği = run kimliği, HTTP ile zamanlanamaz, kuyruklu resume/respond kotaya
girmez, respond-async F-319'u beklemez (2-A, 3-A, 6-A, kullanıcı kararı) · **K-925**
*(public-api)* `Idempotency-Key` yalnız `Prefer: respond-async` ile (5-A, kullanıcı
kararı) · **K-926** *(public-api, kalıcı-veri)* bağın `COALESCE`'i, `RunQuery`
süzgeci ve öğe işareti sözleşmede · **K-927** *(public-api, geri-dönüşü-pahalı)*
`2 + MaxResumeAttempts` ve son deneme kuralı (4-A ve denetim 🔴1 düzeltmesi,
kullanıcı kararı) · **K-928** *(public-api)* batch öğesinin `sessionId`'si · **K-929**
*(güvenlik)* kuyruklu yanıt `jobs.payload`'da düz (K-899'un genişlemesi).

Yerel tercihler: Açık Soru 7-A — `Loop` adımının tur düzeyi checkpoint'i kapsam
dışı; adım baştan koşar, belge maliyeti yazar (kullanıcı kararı) · ret kodları
`internal const` (`QueuedTurn` deseni), public API büyümedi · `WorkflowSessionId`
`Tracon.Abstractions`'a taşındı (internal) · kuyruklu run'ın oturumu `202`'den
önce üretilir · `IWorkflowRecoveryPolicy` iç seam'dir, yalnız `UseWorkflows()`
kaydeder · son deneme kuralı sınırı worker'ın okuduğu yerden okur.

## Gerçekleşen Public API

```csharp
// Tracon.Abstractions
public const string JobHandlerKeys.WorkflowRun = "tracon.workflow-run";   // BuiltIn; zamanlanamaz
public Guid? RunQuery.ContinuedFromRunId { get; init; }
// Tracon.Workflows
public bool TraconWorkflowOptions.ResumeInterruptedRuns { get; set; }     // false; checkpoint ister
public int TraconWorkflowOptions.MaxResumeAttempts { get; set; } = 1;     // 0..100; iş 2 + bu kadar deneme alır
// Tracon.Testing.Contracts.Xunit (sevk edilen, yayın provası gerekir)
RunStoreContract.A_second_start_without_a_continuation_link_keeps_the_first_one()
RunStoreContract.A_queued_workflow_row_keeps_its_workflow_session_and_attribution_when_it_starts()
RunStoreContract.Query_filters_by_the_run_a_continuation_continues()
RunStoreContract.Query_by_continuation_does_not_return_another_tenants_run()
JobStoreContract.A_pending_report_marks_the_item_and_counts_nothing()
JobStoreContract.A_report_for_a_finished_item_changes_nothing()
```

HTTP: üç workflow POST ucu `Prefer: respond-async` ile `202` + `AcceptedRunResponse`
(`Location`, `Preference-Applied`); resume ve respond `404` de verir; `Prefer`'siz
`Idempotency-Key` `400`; `GET /api/runs?continuedFromRunId=`; `PUT /api/schedules/{name}`
`tracon.workflow-run`'a `400`. Davranış sözleşmesi XML'de: `IJobStore.ReportItemAsync`
yalnız bekleyen öğeyi değiştirir, `Pending` rapor işarettir. İş hata kodları
(`internal const`): `tracon.job.workflow-run-payload-invalid`, `…-run-failed`,
`…-run-not-continued`.

## Dosya Listesi (gerçekleşen)

```
src/Tracon.Abstractions/   JobHandlerKeys, RunSupportTypes (RunQuery), RunRecord, IJobStore, JobItemRecord,
                           TraconSchedulingOptions, BatchItemInput, Workflows/WorkflowSessionId (taşındı)
src/Tracon.Core/           Scheduling/{WorkflowRecovery*, WorkflowRunJobHandler*, WorkflowRunJobPayload*,
                           WorkflowJobHandler, AgentBatchJobHandler, InMemoryJobStore},
                           Workflows/IWorkflowRecoveryPolicy*, Storage/InMemoryRunStore.{Lifecycle,Queries},
                           Recording/RunContinuationEligibility, Registration.Storage
src/Tracon.Workflows/      TraconWorkflowOptions, Internal/{TraconWorkflowRecoveryPolicy*, …OptionsValidator},
                           TraconWorkflowsBuilderExtensions, README
src/Tracon.AspNetCore/     Endpoints/{Workflow,Run,Scheduling,Agent}Endpoints, Idempotency/{RespondAsyncPreference*,
                           IdempotencyFilter}, TraconEndpointRouteBuilderExtensions
src/Tracon.{Sql.Shared,PostgreSql,SqlServer,Sqlite}/  SqlRunStore, *Queries (COALESCE + süzgeç)
src/Tracon.Testing.Contracts.Xunit/  RunStoreContract, JobStoreContract
üretilen                   docs/openapi/tracon.json, Tracon.Client/Generated, packages/tracon-client/src/schema.ts,
                           llms*.txt, Tracon.AgentMap.md
samples/Tracon.Api/        Program.cs (render), RenderSettings*
bench/baseline.json        QueryRuns 45456 B, CacheHit 0 B
(* yeni)
```

**Testler — 104 yeni test metodu** (teori satırları tek sayıldı):

| Sınıf | Sayı | Neyi doğrular |
|---|---:|---|
| `WorkflowAsyncRunTests` · `…GateTests` · `…CancelTests` · `…RespondTests` · `…TenantTests` · `…IdempotencyTests` · `…AttributionTests` | 9 · 7 · 3 · 5 · 2 · 4 · 3 | Kuyruk yolu: kapı sırası, `Queued` satır, iptal, yanıt, kiracı, anahtar, atıf (hata modu 1-11) |
| `WorkflowRecoveryTests` | 9 | Kurtarma: tamamlanan düğüm tekrar koşmaz, parametre, işaret, sınır, zarif kapanış, okunamayan checkpoint, bayrak kapalı (12-20) |
| `WorkflowRecoveryAttemptLimitTests` | 5 | Son kiralama gerçek worker'dan: `MaxResumeAttempts` 1 ve 0, açılmış devam, batch son denemesi, `2 + Max` batch (🔴1) |
| `WorkflowRecoveryPlanTests` · `WorkflowRunJobHandlerTests` · `WorkflowJobHandlerTests` (+6) | 16 · 10 · 6 | Plan durumları, son deneme kuralı, zincir kapanışı; yük; `store` hatası denemeyi düşürür (🟡1) |
| `BatchItemInputTests` (+3) · `AgentBatchJobHandlerTests` (+1) · `RunContinuationTests` (+2) · `SchedulableHandlerKeyTests` (+2) | 8 | `sessionId`, ajan öğesi, uzlaştırma workflow'u sürdürmez, zamanlama reddi (21-22, 10) |
| `RunStoreContract` (+4) · `JobStoreContract` (+2) | 6 | Bağ, süzgeç, kiracı, işaret — dört depo (16) |
| `TwoProcessWorkflowResumeTests` | 1 | PostgreSQL, iki süreç, `SIGKILL` (17) |
| `WorkflowRecoveryOptionsValidationTests` | 5 | Seçenek doğrulaması, `JobAttempts` |
| `TraconApiClientQueuedWorkflowTests` · `queued-workflow.test.ts` | 2 · 3 | İki istemci `202` gövdesi (23) |

Mimari kapılar güncellendi: `JobHandlerOutcomeWriteSiteTests`, `JobAttributionScopeSiteTests`,
`ambient-write-baseline.txt`, `ServiceRegistrationSnapshotTests`, `SqlTextSnapshotTests` (üç dialekt).

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 1 — Açık Soru 4-A denetimden sonra `2 + MaxResumeAttempts` oldu (Sapma 16, kullanıcı kararı); öbür sapmalar ölçümün sonucudur |
| Düzeltme turu sayısı | 11 — çözümleyici (RS0016 · CA1826/CA1859 · MA0006/MA0002) · kurucu değişikliği sonrası test derlemesi · mimari kapılar (`///` içinde iç referans, Türkçe test metni, atıf ve sonuç sitesi listeleri, ambient tabanı) · `// delay:` etiketi · agent map tazeliği · manuel test bütçesi · kapanış 1: anlık görüntü testleri · kapanış 2: `QueryRuns` tahsisi · denetim düzeltmeleri (🔴1, 🟡1-4) · birleştirme sonrası kapanış (§ *Kapanış kapısı*) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 0 / 0 |
| Fazın ürettiği regresyon | 3 — 🔴1 (deneme sayısı son kiralamayı handler'dan önce düşürüyordu), 🟡1 (geçici `store` hatası işi tekrar denemesiz düşürüyordu), 🟡2 (site bayrak kapalı kuyruklu run'ı yanlış anlatıyordu). Üçünü de bağımsız denetim yakaladı, kapanıştan önce düzeltildi |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

Bağımsız denetçi (`faz-denetim`, taze bağlam, salt-okunur), aralık `65d72b98..72e04a13`.
Triyaj Adım 5.1: 🔴'nın sonucunu kullanıcı seçti (2026-10-13). Düzeltmeler `e795d74f`
(kod, test), `e355434d` (site, CHANGELOG, MT-RES-095), `6efa28b3` (taban çizgisi) ve
`a9315535` (günlük) commit'lerindedir.

| # | Sv. | Bulgu | Triyaj | Sonuç |
|---|---|---|---|---|
| 1 | 🔴 | Kuyruklu tek run'lı iş `1 + MaxResumeAttempts` deneme alır; worker son kiralamayı handler'dan önce `attempts-exhausted` ile düşürür (`JobWorkerBackgroundService.cs:322-345`): son devam `Running` kalır, `Exhausted` dalına ulaşılmaz, `MaxResumeAttempts=0` kesintiyi görmez, `OpenContinuationAsync` ile motor arasındaki çökme `Queued` satır bırakır. Testler kaçırdı: koşum deneme denetimini atlıyordu, test 15 yalnız batch'te koştu, seçenek testi tiyatroydu | **Gerçek** (kullanıcı; düzeltme yolu a) | **Düzeltildi:** `2 + MaxResumeAttempts` + son deneme kuralı + açılmış devam kesinti sayılır (Sapma 16, 17). **Önce düşen test görüldü:** `WorkflowRecoveryAttemptLimitTests` — `A_queued_run_interrupted_past_its_limit_is_closed_Failed_by_the_jobs_last_lease`, `With_no_continuation_allowed_…`, `A_continuation_whose_process_stopped_before_it_started_counts_as_an_interruption` eski kodda `tracon.job.attempts-exhausted (ref: …)` ile, `A_batch_jobs_last_attempt_closes_…` zaman aşımıyla düştü. Sınıf taraması: batch'in üç denemesi `Max ≥ 2` iken kusurluydu → aynı kural, `A_batch_job_with_two_more_attempts_than_continuations_gets_every_continuation`. Tiyatro testi `Zero_continuations_is_allowed_and_leaves_the_job_the_attempt_that_closes_the_interrupted_run` oldu (sayıyı ve nedenini iddia eder; uçtan uca kanıt fonksiyonel testtir). Plan birim testleri (+6), `WorkflowAsyncRunTests` teorisi (+`(true, 0, 2)`). XML, site, `CHANGELOG`, README, MT-RES-095. Örnek uygulama: `MaxResumeAttempts=0` + `SIGKILL` → `not-continued` |
| 2 | 🟡 | Kurtarma açıkken kurtarma okumasındaki geçici `store` hatası işi tekrar denemesiz düşürür; `CloseIfQueuedAsync` aynı hatada satırı `Queued` bırakır; `OpenContinuationAsync` düşerse kapanan run olmayan run'ı adlandırır; birim testleri hep `policy: null` | Gerçek | **Düzeltildi** (Sapma 18). Testler (kurtarma açık): `With_recovery_on_a_store_error_while_reading_the_chain_fails_the_attempt_not_the_work`, `…a_continuation_that_cannot_be_opened_leaves_the_interrupted_run_for_the_next_attempt`, `…a_refused_run_whose_row_cannot_be_closed_is_closed_by_the_next_attempt`, batch `With_recovery_on_a_store_error_while_writing_the_items_run_id_fails_the_attempt_not_the_item`. Önce düşen test görülmedi (testler düzeltmeyle yazıldı); yerine mutasyon: eski üç davranış geri konunca üç handler testi de düştü |
| 3 | 🟡 | `reliability.md:383` bayrak kapalı kuyruklu run'ı "baştan başlar" diye anlatır; kodda iş `AsyncRun.MaxAttempts` (1) alır ve kesintiden sonra `attempts-exhausted` ile düşer | Gerçek | **Düzeltildi** (`e355434d`): satır koda göre yazıldı |
| 4 | 🟡 | `bench/baseline.json` `CacheHit` 24 B, ölçüm 0 B; kapı uyarı verir, DoD "sıfır uyarı" der | Gerçek (taban fazdan önce bayattı) | **Düzeltildi** (Sapma 19): taban commit 0 B ölçtü; `kapi.py performans` ✅ üç satır |
| 5 | 🟡 | `Registration.Storage.cs:200` yorumu olmayan `WorkflowRecoveryRegistrationTests`'i adlandırır | Gerçek | **Düzeltildi:** yorum gerçek sınıfları adlandırır (`WorkflowRecoveryTests`, `WorkflowRecoveryAttemptLimitTests`) ve zamanlama seçeneğinin de geçirilmesi gerektiğini yazar |
| 6 | 🟢 | `queued-workflow.test.ts:29-67` sahte gövdeyi cast ile okur; `202` tipini şemadan kanıtlamaz | Gerçek | **Devredildi:** F-435 |

## Sonraki Faza Devir Notu

**Doğal sıradaki faz: [Faz 196](196-UZUN-ADIM-SURE-VE-IPTAL.md)** — job token'ına ve
deneme kuralına dokunur. Devralınan sözleşmeler orada, § *Faz 197'den devralınan
sözleşmeler* tablosundadır; 196'ya Açık Soru 13 ve hata modu 29 eklendi.

### Devralınan sözleşmeler (özet)

- **İş deneme sayısı:** kurtarma açıkken `2 + MaxResumeAttempts`; son deneme devam
  başlatmaz, zinciri kapatır (`WorkflowRecovery.PlanAsync`); batch işi öğe başına
  en çok `MaxAttempts − 2` devam alır; kapalıyken `AsyncRun.MaxAttempts` (K-927).
- **`Pending` işareti:** batch öğesine run'dan önce `Pending` + `RunId`; tek run'lı
  işte işaret `Queued` run satırıdır (K-926).
- **Token = kapanış varsayımı:** iki workflow handler'ı token iptalini "worker
  duruyor" sayar ve işi kiraya bırakır. 196.4 bunu değiştirir.
- 🚨 **Kurtarma süre aşımını kesintiden ayırt etmez** (§197.2): aşılan run
  `Canceled`/`Failed` kalır ve sonraki deneme onu sürdürür (196 Açık Soru 13).

### Açık uçlar

- **Kusurlar ADAYLAR'da:** F-431 (workflow uçlarında drain kapısı yok) · F-432 (ajan
  kuyruğunda enqueue telafisi yok) · F-433 (`KeepCheckpointsAfterCompletion=false` +
  zarif durdurma) · F-434 (ajan batch `sessionId`'si kayıtta kabul) · F-435 (TS testi).
- **Bilinen sınır:** son denemede hiç başlamamış baş run'ı (`NotStarted`) yine koşar;
  o da kesilirse onu yalnız run uzlaştırması kapatır (açıksa). Kira kaybında eski
  sahip yaşarsa iki kopya koşar (K-886).
- **7-A:** `Loop` adımı kesintiden sonra tamamladığı turları yeniden koşar; ihtiyaç
  ölçülürse aday.
- **Yayın provası koşulmadı** (koordinatör kararı); sevk edilen sözleşmeye altı case
  eklendi, ilk yayından önce `kapi.py yayin --kuru` gerekir.
- Site yayınlanmadı.
