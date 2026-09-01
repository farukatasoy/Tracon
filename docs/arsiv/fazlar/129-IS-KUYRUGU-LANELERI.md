# Faz 129 — İş Kuyruğu `lane`'leri

> **Durum:** ✅ Tamamlandı (2026-09-01)
> **Kaynak:** [kesif/2026-09-01-tuketici-feature-talepleri.md](../../kesif/2026-09-01-tuketici-feature-talepleri.md) — **F-172**
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.AspNetCore`, `.UI`, `.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** gerekli — üç set (PostgreSQL · SqlServer · Sqlite); numara uygulama anında alınır
> **Public API:** büyüyor **ve bir imza kırıyor** (`IJobStore.LeaseAsync`). `wc -l src/*/PublicAPI.Shipped.txt` → her dosya 1 satır; shipped giriş **sıfır**, yani bugün kırmak bedava, Faz 7'den sonra bir sürüm kararı
> **Tüketici yüzeyi:** `docs-site/`: `guides/background-work.md`, `guides/write-your-own-store.md`, `reference/configuration.md`, `ui.md` (+ jobs ekran görüntüsü); `http-api/` ve `api/` **üretilir** — oradaki iş XML dokümanı ve `.Produces` üstverisidir · sevk edilen: `IJobStore`/`JobRecord` XML dokümanı, `capabilities.md` satırı
> **Manuel test alanı:** [`docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md`](../../manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 1633088:docs/arsiv/fazlar/129-IS-KUYRUGU-LANELERI.md
> ```
>
> Damıtıldı 2026-09-01 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism'in iş kuyruğu bugün **tek havuzdur**. Dokuz `JobKind` aynı sırayı ve aynı `MaxConcurrentJobs` bütçesini paylaşır. Kaynak profili çok farklı işler birbirini bekletir: uzun süren bir toplu koşu, saniyeler süren bir `ApprovalResume`'u slot boşalana kadar tutar. Bu bir throughput sorunu değil, iş türleri arasında **head-of-line blocking**'dir.

## Bitiş Ölçütleri (DoD)

- [x] Hiçbir ayar yapılmayan kurulumda davranış Faz 128 ile **birebir** aynıdır (case 1) — `JobStoreContract.Lease_with_null_lanes_applies_no_filter`, `AsyncRunTests.Queued_run_without_a_lane_defaults_to_default`
- [x] `Lanes: ["media"]` olan worker `default` `lane`'inden `lease` **alamaz** (case 2) — `JobWorkerBackgroundServiceTests.A_worker_scoped_to_one_lane_never_leases_another_lane`
- [x] `media` doluyken `default` `lane` ilerler (case 3) — `JobWorkerBackgroundServiceTests.A_full_lane_does_not_block_the_default_lanes_job`
- [x] Geçersiz `lane` adı `enqueue` anında reddedilir; HTTP `400` döner (case 4) — gerçek uç `POST /api/agents/{name}/run` + `Prefer: respond-async` (plan taslağındaki `.../batch` **yok**, bkz. Plandan Sapmalar); `AsyncRunTests.Invalid_lane_is_rejected_with_400`, `SchedulingEndpointTests.Invalid_lane_is_rejected`, `samples/AgentPrism.Api` üzerinde elle doğrulandı (aşağıda)
- [x] `retry` `lane`'i korur (case 5) — `JobStoreContract.Retry_preserves_the_jobs_lane`
- [x] `JobStoreContract` dört koşumun dördünde de yeşil (bellek içi + PostgreSQL + SqlServer + Sqlite) — 2216/2216 (Core), 685/685 (PostgreSQL), 615/615 (SqlServer), 629/629 (Sqlite)
- [x] `EXPLAIN ANALYZE` çıktısı `jobs_claim_idx` kullanımını gösterir ve belgeye yazıldı (aşağıda) — bu doğrulama sırasında `jobs_claim_idx`'in ORİJİNAL kısmi koşulunun (`status IN (0,1)`) `LeaseJob`'ın OR'lu `WHERE`'ini hiç KARŞILAYAMADIĞI bulundu; bkz. Plandan Sapmalar
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 91f345b`; K-612 gereği `scripts/applied-migrations.json`'a bu fazın üç yeni migration'ı iki-commit deseniyle pin'lendi (bkz. Plandan Sapmalar)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı (aşağıda)
- [x] `secret` taraması boş döndü — `kapi.py tarama` içinde koştu
- [x] Manuel kabul case'leri `docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md` içine eklendi (MT-JOB-110..116); otomatikleştirilebilenler (110, 111, 113, 114, 115) fonksiyonel/birim testlerle zaten kapsandı
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. Denetim Bulguları
- [x] `docs-site/` güncellendi (`guides/background-work.md`, `guides/write-your-own-store.md`, `reference/configuration.md`, `ui.md` + ekran görüntüsü); `npm run check` (içerik+derleme+bağlantı+ağırlık) temiz
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — `176.2 KB` gzip (bütçe 250 KB), embedded `151.0 KB` brotli (önceki: 145.3 KB · `148 807 B`)

### Doğrulama komutları (gerçekleşen)

```bash
# Lane filtreli liste — gerçek run
curl -s "http://localhost:5080/agentprism/api/jobs?lane=media" -H "$APB"
# → 1 kayıt, lane: "media"

# Geçersiz lane adı reddi — gerçek uç (plandaki .../batch YOK, düzeltme aşağıda)
curl -s -o /dev/null -w '%{http_code}\n' -X PUT \
  http://localhost:5080/agentprism/api/schedules/invalid-lane-demo \
  -H 'content-type: application/json' \
  -d '{"kind":"AgentBatch","targetName":"summarizer","lane":"Media","timeZone":"UTC","payload":[]}'
# → 400, detail: "'Media' is not a valid lane name..."

# Index kullanımı (PostgreSQL, 10 010 satır, corrected status IN (0,1,2))
docker exec ap-pg psql -U postgres -d agentprism -c "
EXPLAIN (ANALYZE, BUFFERS)
SELECT id FROM agentprism.jobs
 WHERE ((status = 0 AND scheduled_for <= now())
    OR (status IN (1, 2) AND lease_until < now()))
   AND lane = ANY(ARRAY['other-lane-9']::text[])
 ORDER BY scheduled_for FOR UPDATE SKIP LOCKED LIMIT 1;"
# → BitmapOr üzerinde İKİ "Bitmap Index Scan using jobs_claim_idx" (biri her OR
#   dalı için), toplam 13 buffer hit, Execution Time: 0.092 ms — Seq Scan YOK.
```

**Gerçek çalıştırma (samples/AgentPrism.Api, PostgreSQL):**
```
PUT /api/schedules/lane-demo {"kind":"AgentBatch","targetName":"summarizer","lane":"media",...} → 200, lane: "media"
PUT /api/schedules/invalid-lane-demo {...,"lane":"Media",...}                                    → 400
POST /api/schedules/lane-demo/trigger {}                                                          → 200, job.lane: "media"
GET  /api/jobs/{id}  (birkaç saniye sonra)                                                        → status: "Completed", lane: "media"
GET  /api/jobs?lane=media                                                                         → 1 kayıt
```

---

## Plandan Sapmalar

1. **🚨 Plan iki yerde var olmayan bir uca (`POST /api/agents/{name}/batch`) referans veriyordu — böyle bir uç yok ve hiç olmadı.** `grep -rni batch src/AgentPrism.AspNetCore/` sıfır sonuç döndü; `AgentBatch` job'ları yalnız `PUT /api/schedules/{name}` + `POST .../trigger` üzerinden yaratılabiliyor. Kuyruklu TEK çalıştırma gerçek ucu `POST /api/agents/{name}/run` + `Prefer: respond-async`'tir (`AgentEndpoints.RunQueuedAsync`). "Görünürlük" tablosu, "Planlanan Public API"nin HTTP `endpoint`'leri bölümü, manuel kabul case 4 ve DoD doğrulama komutu bu gerçek uca göre uygulandı; manuel test dosyasına (MT-JOB-110..116) bu düzeltme açıkça not edildi. Bu, `faz-uygulama`'nın "planın yapısal iddiasını grep'le ölç" kuralının tam bir örneğidir (K-320 sınıfı) — plan muhtemelen keşif raporunun (`kesif/2026-09-01-tuketici-feature-talepleri.md`) kavramsal "toplu iş" dilinden bu somut yolu doğrulamadan türetmişti.
2. **Aynı sınıftan ikinci ölçüm hatası: `Hata Modları` tablosundaki `SchedulingEndpointsTests` ve E2E `jobs.spec.ts` gerçek dosya adları değil.** Gerçek dosyalar `tests/AgentPrism.AspNetCore.FunctionalTests/SchedulingEndpointTests.cs` (tekil "Endpoint") ve `tests/AgentPrism.Ui.E2ETests/UiTests.cs` (E2E testleri TypeScript `.spec.ts` değil, C#/Playwright'tır — repoda hiç `.spec.ts` yok). Case'ler bu dosyalara eklendi.
3. **`LaneByKind` çözümü on çağrı yerine değil, `IJobStore.EnqueueAsync`'in TEK noktasına merkezileşti.** Plan dosya listesi on üretim çağrı yerinin (`EvalEndpoints`, `RetentionEndpoints`, `ApprovalEndpoints`, `InboundTriggerDispatcher` ×2, `RunReconciliationService`, `RunSampler`, `WebhookPublisher`, `JobWorkerBackgroundService`, `AgentEndpoints`, `SchedulingEndpoints`) tek tek gözden geçirilmesini ima ediyordu. Ölçüldü: sekiz çağrı yeri `Lane` alanını hiç ayarlamıyor (varsayılan `JobLanes.Default`'a düşüyor) — `JobLanes.Resolve(job.Lane, job.Kind, laneByKind)` her `EnqueueAsync` uygulamasının (bellek içi + SQL) İÇİNDE çağrılır ve `job.Lane == Default` olduğunda `LaneByKind`'ı okur. Sonuç aynı garantiyi verir (hiçbir çağıran unutulmaz, çünkü hiçbiri elle değiştirilmedi) ve gerçek dokunulan yer sayısını 10'dan 2'ye indirir. Yalnız açıkça bir `lane` kabul eden iki uç (`AgentEndpoints.RunQueuedAsync`, `SchedulingEndpoints.SaveScheduleAsync`) HTTP gövdesinden `lane` okuyup doğrular.
4. **`Lanes: null` + `MaxConcurrentJobsPerLane` dolu kombinasyonu planda tanımsızdı; `IJobStore.LeaseAsync(lanes)` yalnız bir İÇERME listesi (plan böyle tanımlıyor).** `MaxConcurrentJobsPerLane`'de adı geçen bir `lane`'i paylaşılan havuzdan HARİÇ tutmak bir DIŞLAMA listesi gerektirir ve arayüz bunu desteklemiyor. Çözüm: paylaşılan (shared) geçiş, `Lanes` `null` VE `MaxConcurrentJobsPerLane` doluyken kapsamını `{"default"} ∪ adlandırılmış lane'ler`'e daraltır, sonra adlandırılmışları çıkarır — geriye yalnız `["default"]` kalır (sonlu, ifade edilebilir bir liste). `Lanes` boşken ve `MaxConcurrentJobsPerLane` de boşken davranış TAMAMEN değişmeden kalır (K1). Etkisi: bir üçüncü, adlandırılmamış `lane` (`"default"` da değil, `MaxConcurrentJobsPerLane`'de de yok) `Lanes` `null`'ken artık YALNIZCA `MaxConcurrentJobsPerLane` boşsa işlenir — dolu olduğunda o `lane`'i işlemek için `Lanes`'in açıkça listelenmesi gerekir. Bu, planın kendi "bilinmeyen `lane`'in görünürlükle çözüldüğü" mitigasyon deseniyle (129.1) uyumludur ve `AgentPrismSchedulingOptions.MaxConcurrentJobsPerLane`'in XML dokümanında ve `guides/background-work.md`'de açıkça yazılıdır.
5. **`JobStoreConcurrencyTests`'in yeni `lane`-kapsamlı yarış case'i yalnız PostgreSQL'de eklendi, dört koşumda değil.** Bu sınıf zaten (lane'lerden ÖNCE de) yalnız PostgreSQL'de vardı — kendi XML dokümanı "gerçek kanıt yalnız gerçek PostgreSQL'den gelebilir" der (bellek içi tek `lock`, tek süreçte tartışmasızdır). Aynı emsal korundu.
6. **EXPLAIN ANALYZE doğrulaması sırasında, `jobs_claim_idx`'in kısmi koşulunda (`WHERE status IN (0, 1)`) FAZDAN BAĞIMSIZ, Faz 17'den kalma bir kusur bulundu ve düzeltildi.** `LeaseJob`'ın `WHERE` yan tümcesi `(status = 0 AND ...) OR (status IN (1, 2) AND lease_until < now())` — ikinci dal `status = 2` (Running, çöken bir worker'ın çalıştırması) içeriyor, ama index'in kısmi koşulu yalnız `status IN (0, 1)`'i kapsıyordu. Ölçüldü: PostgreSQL, index'in KENDİ koşulunun OR'un ikinci dalını kapsamadığını kanıtlayamadığı için index'i HİÇ kullanmıyor, tablo büyüklüğünden bağımsız her zaman Seq Scan yapıyordu (`enable_seqscan = off` ile bile). Bu, lane'lerden TAMAMEN bağımsız, ÖNCEDEN VAR olan bir sorun — yalnız bu fazın DoD'si "EXPLAIN ANALYZE index kullanımını göstersin" dediği için ortaya çıktı. Kullanıcının "konuyla alakasız bug'ları da çöz" talimatı gereği, üç migration'da da (`0041`/`0028`/`0028`) kısmi koşul `status IN (0, 1, 2)`'ye genişletildi — bu SADECE bir SÜPERSET (daha fazla satır indexlenir, davranış değişmez); ölçülmüş kanıt: `BitmapOr` + iki `Bitmap Index Scan using jobs_claim_idx`, 10 010 satırda 13 buffer hit, 0.092 ms (önceki: Seq Scan, 300+ buffer hit).
7. **Site senkronu: üç kural (`http-api`, `cekirdek-kavram`, `kalicilik`) tetiklendi ama içerik güncellemesi gerekmiyordu; `--site-gerekce-yazildi` ile geçildi.** Gerekçe, her biri somut ölçümle doğrulandı:
   - `http-api.md`'nin "162 operations across 125 paths" satırı DEĞİŞMEDİ — bu faz mevcut gövdelere/sorgu parametrelerine alan ekledi, yeni bir yol veya operasyon eklemedi (`docs/openapi/agentprism.json` üzerinde doğrulandı: 125 yol, 162 operasyon, fazdan önce ve sonra aynı).
   - `concepts/`'te "lane" için doğal bir yer yok — kavram sayfaları temel zihinsel modeller içindir (run, session, agent); lane bir arka-plan-işi rota/izolasyon aracıdır ve zaten doğru irtifada (`guides/background-work.md`, yeni "## Lanes" bölümü) belgelendi. Yeni bir `concepts/` sayfası açmak DoD'nin istemediği bir kapsam genişlemesi olurdu.
   - `getting-started/persistence.md` migration MEKANİZMASINI (otomatik/ayrı uygulama, kilitleme, şema izolasyonu) anlatır, migration SAYISINI değil — sayfada hiçbir migration sayısı iddiası yok, bu yüzden yeni bir migration eklemek sayfayı BAYATLATMAZ.

## Bu Fazda Verilen Kararlar

> Yeni bir `K-NNN` kaydı açılmadı. Yukarıdaki 7 sapmanın hiçbiri
> public API/uyumluluk sözleşmesi, güvenlik/kiracı sınırı veya kalıcı
> veri/migration biçimi kararı DEĞİL — hepsi yerel implementasyon tercihi
> (AGENTS.md: "Yerel implementation tercihi faz dokümanında veya kod
> yorumunda kalır"). `IJobStore.LeaseAsync`'in imza kırılması planın
> kendisinde zaten "bilinçli" olarak işaretliydi (bkz. Planlanan Public API);
> burada yalnız GERÇEKLEŞEN imza doğrulandı, yeni bir karar alınmadı.

## Denetim Bulguları

Taze bağlamlı bir denetçi (Agent, `general-purpose`) 2026-09-01'de koştu;
tam rapor aşağıda özetlenir.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `kapi.py tarama` üç yeni migration için "git tabanı dosyayı içermiyor" hatası veriyordu — DoD'nin "dört kapı sıfır uyarı" iddiası, manifest pin'lenmeden önce yazılmıştı | **Düzeltildi.** İki-commit deseni tamamlandı: bu fazın commit'i sonrası `scripts/applied-migrations.json`'a üç `sourceCommits` girdisi eklendi (ayrı bir "docs: pin phase 129's migrations" commit'i, Faz 126'nın emsaliyle aynı desen), `kapi.py kapanis` yeniden koşuldu — temiz |
| 2 | 🟡 | Plan "Başka kiracının job'u lane filtresiyle sızar → TenantIsolationContract, dört koşumda birden" vaat ediyordu ama `Lane`+kiracı kombinasyonunu sınayan özel bir test yoktu | **Düzeltildi.** `JobStoreContract.QueryAsync_lane_filter_does_not_leak_another_tenants_job` eklendi; dört koşumun (bellek içi + PostgreSQL + SqlServer + Sqlite) dördünde de yeşil |

**🔴 ve 🟡 (yukarıdakiler dışında) yok.** Denetçinin "temiz" bulduğu
başlıklar: 3.2 (test tiyatrosu yok), 3.3 (test seviyeleri doğru), 3.5
(imza-gövde kayması yok — sekiz sessiz `new JobRecord` çağrı yeri
`JobLanes.Resolve` üzerinden doğru kapsanıyor, `grep`'le doğrulandı), 3.6
(plan dışı public API yok), 3.7 (repo kuralları — İngilizce, XML doküman,
`ConfigureAwait`, `TryAdd*`, K-059, K3 — ihlal yok), 3.8 (ürün yüzeyi —
`en.ts`/`tr.ts` eksiksiz, docs-site kendine yeterli, `.WithTags`/`.Produces`
üstverisi mevcut).

## Sonraki Faza Devir Notu

- **`lane` başına metrik (F-178) artık hazır önkoşula sahip.** Bu faz `lane`
  kimliğini sevk etti; `AgentPrismMetrics`'e job/kuyruk sayaçları eklerken
  etiket olarak kullanılabilir.
- **Kuyruk derinliği görünürlüğü hâlâ yalnız `/api/jobs?lane=` ve jobs
  ekranı üzerinden.** Kimsenin dinlemediği bir `lane`'de biriken iş
  otomatik ALARM üretmiyor — metrik fazı bunu kapatabilir.
- **`AgentPrismSchedulingOptions.MaxConcurrentJobsPerLane` ve `Lanes`'in
  birlikte etkileşimi (Sapma 4) davranışsal bir inceliktir**, sonraki bir
  okuyucunun `ComputeSharedLanes`'i değiştirirken gözden kaçırabileceği
  türden — XML dokümanı ve `guides/background-work.md` bunu açıkça
  anlatıyor, ama kod tarafında yalnız `JobWorkerBackgroundServiceTests`
  bunu iki uçtan (starvation + lane-scoping) sınıyor. Bir üçüncü test
  ("Lanes null + MaxConcurrentJobsPerLane dolu + gerçekten üçüncü,
  adlandırılmamış bir lane") bu fazda YAZILMADI — davranış koddan ve
  XML'den doğru okunabiliyor ama ayrı bir regresyon testi yok.
- **`jobs_claim_idx`'in kısmi koşulundaki düzeltme (Sapma 6) yalnız bu üç
  migration'da yapıldı.** Aynı sınıf bir sorun (bir OR dalının index'in
  kısmi koşulu dışında kalması) başka bir index'te de yaşıyor olabilir —
  taranmadı, bu fazın kapsamı dışında.
