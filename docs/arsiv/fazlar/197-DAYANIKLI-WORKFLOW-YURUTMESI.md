# Faz 197 — Dayanıklı Workflow Yürütmesi: Bağlantısız Run ve Otomatik Sürdürme

> **Durum:** ✅ Tamamlandı (2026-10-13)
> **Plan onayı:** Bakımcı, 2026-10-13 (Açık Sorular'ın önerileri kabul edildi: 1-A, 2-A, 3-A, 4-A, 5-A, 6-A, 7-A)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-318**, **F-321**
> **Önkoşul:** F-344, F-341, F-364 2026-10-12'de kapandı (K-909, K-908, K-904). [Faz 196](../../196-UZUN-ADIM-SURE-VE-IPTAL.md) ile sıra serbest; ikisi de job token'ına dokunur
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Workflows`, `.AspNetCore`, `.Sql.Shared`, üç SQL sağlayıcısı, `.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** Yok (`0037_run_continuation.sql:18` indeksi, `job_items.run_id` var); Açık Soru 1-B gerektirir
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü: `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `concepts/{workflows,runs}.md`, `guides/{background-work,reliability}.md` · sevk edilen: XML, Workflows README, `CHANGELOG.md`
> **Manuel test alanı:** `docs/manuel-test/{15-WORKFLOWS,16-IS-KUYRUGU-VE-ZAMANLAMA,21-DAYANIKLILIK-VE-IPTAL}.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7d1effe6:docs/arsiv/fazlar/197-DAYANIKLI-WORKFLOW-YURUTMESI.md
> ```
>
> Damıtıldı 2026-10-13 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Workflow run'ı okuyan HTTP akışıyla yaşar (K-845); kuyruktan başlayan run çökme veya deploy sonrası baştan koşar. Bu faz üç işlemi kuyruğa alır; job'un run'ı tekrar kiralamada son checkpoint'ten sürer. - **F-318** — üç uca `Prefer: respond-async`, `Idempotency-Key`, kuyrukta yetki kapısı. - **F-321** — opt-in kurtarma: son checkpoint, deneme sınırı, devam bağı.

## Bitiş Ölçütleri (DoD)

- [x] Üç uç `202` döner, aynı anahtar tek iş açar (test 1-8); kesilen iş checkpoint'ten sürer, bayrak kapalıyken davranış aynı (test 12-18) — `WorkflowAsync*Tests` (7 sınıf, 33 test), `WorkflowRecoveryTests` (9), `WorkflowRecoveryAttemptLimitTests` (5, son kiralama gerçek worker'dan), `TwoProcessWorkflowResumeTests` (PostgreSQL, `SIGKILL`)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 65d72b98`, `main` birleştirilmiş dalda tek koşum, çıkış 0 (§ *Kapanış kapısı*); sevk edilen sözleşme değiştiği için `yayin --kuru --surum 1.0.0-preview.4` `main`'de üçüncü koşumda çıkış 0 (§ *Yayın provası*)
- [x] `samples/Tracon.Api` ile gerçek `run` (async, süreç öldürme, devam) yapıldı, çıktı belgeye yazıldı — aşağıdaki iki tablo; ikincisi denetim düzeltmesinden sonra
- [x] `secret` taraması boş döndü — `kapi.py tarama`, kapanış zincirinin ilk adımı
- [x] Manuel kabul case'leri eklendi; otomatikleştirilebilenler koşuldu — `MT-WF-139`…`141` ✅, `MT-RES-095`/`096` ve `MT-JOB-145` 👤 (örnek uygulamada koşuldu)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 1 gerçek 🔴, düzeltildi (§ *Denetim Bulguları*)
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz — kapanış zincirinin `npm run check` adımı (1063 sayfa, kırık bağlantı yok, ağırlık tavanı altında)

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

### Kapanış kapısı

`python3 scripts/kapi.py kapanis --taban 65d72b98`, `main` (`f9c3c2a8`, Faz 198)
birleştirilmiş dalda, `3b93b79d` üzerinde, baştan sona **tek koşum: çıkış 0, 1219 sn**
(2026-10-13).

| Adım | Süre |
|---|---:|
| `kapi.py tarama` (`secret`, sync kopyası) | 6,40 sn |
| `dokuman-bakim.py --denetle` | 1,91 sn |
| `scripts` birim testleri | 8,41 sn |
| `build-agent-map.mjs --check` | 0,10 sn |
| `denetim-paketi.py --taban 65d72b98` | 0,42 sn |
| `dotnet build Tracon.slnx -c Release` | 128,23 sn |
| `dotnet test Tracon.slnx` (tam, `-maxcpucount:2`) | 684,04 sn |
| `dotnet pack` | 9,76 sn |
| `dotnet format --verify-no-changes` | 160,74 sn |
| `kapi.py performans` — `CacheHit` 0 B · `AppendEvent` 112 B · `QueryRuns` 45456 B, uyarı yok | 178,38 sn |
| `npm run check` (docs-site) | 39,76 sn |

Bu turun önceki iki koşumu: (1) ilki 59 sn'de, derleme adımında **elle durduruldu**
(`--site-denetle` mağaza yazarı rehberinin eksiğini gösterdi; sayfa yazıldı,
`b5f5d58f`). (2) İkincisi on adımı yeşil geçti, `npm run check` düştü:
`guides/coding-agents.md` `llms-full.txt`'i "about 825 KB" diyordu, iki fazın birleşik
metni 978,4 KB'tır (%15 bandı dışında). Sayı düzeltildi (`3b93b79d`). Kırılgan test
görülmedi (F-356, F-430 dahil).

Sonra `python3 scripts/dokuman-bakim.py --site-denetle --taban 65d72b98
--site-gerekce-yazildi` → çıkış 0 (gerekçeli iki kural aşağıda; bayraksız koşum bu
ikisi için çıkış 1 verir). Bu bölümü yazan commit yalnız bu dokümanı değiştirir;
üstünde `tarama`, `--denetle` ve `--site-denetle` yeniden koşuldu.

### Yayın provası (`main`, 2026-10-13)

`python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.4`, birleştirmeden sonra
`main`'de (Faz 197 + Faz 198). Faz sevk edilen `Tracon.Testing.Contracts.Xunit`'e altı
case eklediği için zorunluydu (`faz-tamamlama` Adım 1). Uygulama dalı provayı
koşmadı (orkestratör kararı); iki kusuru yalnız prova gördü:

| Koşum | Commit | Sonuç | Düzeltme |
|---|---|---|---|
| 1 | `378a140f` | ❌ kırıcı liste notla eşleşmedi: `Tracon.Client: TraconApiClient` | `TraconListRunsAsync` `continuedFromRunId`'yi `skip`/`take`'ten önce, `TraconVoiceListAsync` (Faz 198) `provider`'ı token'dan önce aldı; konumsal çağıran derlenmez. `CHANGELOG` `[Unreleased] → Changed` iki imzayı adıyla anar (`61283c9a`) |
| 2 | `61283c9a` | ❌ `Tracon.Samples.FileRunStore.Tests` 103/106: `A_second_start_without_a_continuation_link_keeps_the_first_one`, `Query_filters_by_the_run_a_continuation_continues`, `Query_by_continuation_does_not_return_another_tenants_run` | Örnek depo bağı `COALESCE` eder ve `RunQuery.ContinuedFromRunId` ile süzer (`dcc1a88f`) — BL-053 sınıfı: örnekler çözümde değildir, kapanış kapısı onları koşmaz |
| 3 | `dcc1a88f` | ✅ çıkış 0 | kırıcı liste 3 tip, 3 paket (hepsi `Unreleased`'da) · 20 paket · npm dry-run · altı paketlenmiş örnek (`FileRunStore` 106/106, `CustomModelProvider` 38 + 1 atlandı, `CustomRunJudge` 11, `CustomAgentSource` 15, `CustomTool` 18, `CustomJobHandler` 10) · Native AOT smoke · net8.0 tüketici smoke |

Önceki prova çıktıları silinmedi: `artifacts/package/yayin/1.0.0-preview.4-onceki-4800b280`
(Faz 201/204) ve `-onceki-61283c9a`. İki düzeltme yalnız `CHANGELOG.md` ve
`samples/` dosyasına dokunur; `samples/` hiçbir çözümde değildir ve `CHANGELOG`
derlenmez, bu yüzden kapanış kapısı (`3b93b79d`) yeniden koşulmadı; prova üstünde
`tarama` ve `--denetle` yeşil.

### Site senkronu (`--site-denetle --taban 65d72b98`)

Aralık Faz 198'in birleştirilen commit'lerini de kapsar. Sekiz kuralın altısı
sayfa değişikliğiyle karşılandı; ikisi gerekçeyle geçer (`--site-gerekce-yazildi`):

- **`kalicilik` → `getting-started/persistence.md` değişmedi.** Tetikleyen SQL
  metnidir (`*Queries.cs`): ikinci başlatma devam bağını `COALESCE` ile korur,
  `SelectRuns` yeni süzgeci alır. Şema, migration, kurulum ve sağlayıcı seçimi
  değişmedi; sayfanın anlattığı hiçbir şey değişmedi. Depo yazarının yeni iki kuralı
  `guides/write-your-own-store.md`'ye yazıldı.
- **`paket-readme` → `packages.md` değişmedi.** `src/Tracon.Workflows/README.md`
  kurtarma bölümü ve iki sınır satırı aldı; paketin `packages.md`'deki tanımı
  değişmedi. `Tracon.AspNetCore`/`Tracon.Voice` README'lerinin gerekçesi Faz 198
  dokümanındadır (madde 13).

---

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

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 1 — Açık Soru 4-A denetimden sonra `2 + MaxResumeAttempts` oldu (Sapma 16, kullanıcı kararı); öbür sapmalar ölçümün sonucudur |
| Düzeltme turu sayısı | 12 — çözümleyici (RS0016 · CA1826/CA1859 · MA0006/MA0002) · kurucu değişikliği sonrası test derlemesi · mimari kapılar (`///` içinde iç referans, Türkçe test metni, atıf ve sonuç sitesi listeleri, ambient tabanı) · `// delay:` etiketi · agent map tazeliği · manuel test bütçesi · kapanış 1: anlık görüntü testleri · kapanış 2: `QueryRuns` tahsisi · denetim düzeltmeleri (🔴1, 🟡1-4) · birleştirme sonrası kapanış: mağaza yazarı rehberi ve `llms-full` boyut iddiası (§ *Kapanış kapısı*) · `main`'de yayın provası: sürüm notu kırıcı istemci imzasını anmıyordu ve paketlenmiş `FileRunStore` örneği üç yeni sözleşme case'inde düştü (§ *Yayın provası*) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 0 / 0 |
| Fazın ürettiği regresyon | 3 — 🔴1 (deneme sayısı son kiralamayı handler'dan önce düşürüyordu), 🟡1 (geçici `store` hatası işi tekrar denemesiz düşürüyordu), 🟡2 (site bayrak kapalı kuyruklu run'ı yanlış anlatıyordu). Üçünü de bağımsız denetim yakaladı, kapanıştan önce düzeltildi. Ayrıca sevk edilen sözleşmenin yeni üç case'i paketlenmiş `FileRunStore` örneğini kırdı; yalnız yayın provası gördü (`dcc1a88f`) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı; sonraki `kusur-giderme` koşumu doldurur) |

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

**Doğal sıradaki faz: [Faz 196](../../196-UZUN-ADIM-SURE-VE-IPTAL.md)** — job token'ına ve
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
- **Yayın provası `main`'de yeşil** (`dcc1a88f`, § *Yayın provası*); iki kusuru
  yalnız o gördü. 🚨 Sevk edilen sözleşmeye case ekleyen faz provayı kapanıştan
  önce koşmalıdır — paketlenmiş örnek depo onu ilk uygulayan tüketicidir.
- Site yayınlanmadı (`site-deploy.sh` bakımcı onayı bekler).
