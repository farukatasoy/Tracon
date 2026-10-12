# Faz 197 — Dayanıklı Workflow Yürütmesi: Bağlantısız Run ve Otomatik Sürdürme

> **Durum:** 📋 Planlandı (2026-10-12)
> **Plan onayı:** onaylanmadı — plan 2026-10-12'de bakımcı talebiyle yazıldı; engelleyici kararlar § Açık Sorular'da öneriyle bekler, uygulama onaydan önce başlamaz
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

- [ ] Üç uç `202` döner, aynı anahtar tek iş açar (test 1-8); kesilen iş checkpoint'ten sürer, bayrak kapalıyken davranış aynı (test 12-18)
- [ ] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [ ] `samples/Tracon.Api` ile gerçek `run` (async, süreç öldürme, devam) yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz

---

## Riskler

| Risk | Önlem |
|------|-------|
| Yarım düğüm yan etkiyi ikiler | K-498 kuralı XML ve sitede |
| Kuyruk eşzamanlılığı `MaxConcurrentRuns`'ı aşar, `RunTimeout` dolar (K-885) | Site yazar; F-322 |

---

<!-- AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama`. Başlıkları SİLME. -->

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Adaylar: kurtarma sahibi, kuyruk kuralı, öğe işareti ve `sessionId` (K-899), bağ `COALESCE`'i.

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

> Kapanışta doldurulur. Bulgu yoksa "🔴 ve 🟡 yok" yazılır.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
