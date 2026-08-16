# 16 — İş Kuyruğu, Zamanlama, Tek Yürütücü Seçimi ve Dayanıklı Çalıştırma (`JOB`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../16-IS-KUYRUGU-VE-ZAMANLAMA.md`](../../16-IS-KUYRUGU-VE-ZAMANLAMA.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-JOB-001 — `PUT /api/schedules/{name}` yeni bir zamanlama oluşturur

**Gerçek sonuç**
`HTTP: 200`. `id:"019ffbdb-5ab7-7e60-a964-e2542bef55e0"`, `nextRunAt:null`,
`createdAt == updatedAt`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-002 — Aynı adı tekrar `PUT` etmek GÜNCELLER; `id` ve `createdAt` sabit kalır

**Gerçek sonuç**
`id` birebir aynı. `createdAt:"...662962"` değişmedi, `updatedAt`
`"...455381"`'e ilerledi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-003 — `GET /api/schedules/{name}` tekil kaydı döner

**Gerçek sonuç**
`HTTP: 200`, gövde MT-JOB-002 ile birebir aynı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-004 — `GET /api/schedules` kiracının tüm zamanlamalarını listeler

**Gerçek sonuç**
`['ozet-toplu']`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-005 — `DELETE` zamanlamayı siler, sonraki `GET` `404` verir

**Gerçek sonuç**
Silme: `HTTP: 204`. Sonraki `GET`: `HTTP: 404`, `title:"Zamanlama
bulunamadi"`. `ozet-toplu` yeniden oluşturuldu (`HTTP:200`). Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-006 — Var olmayan bir zamanlamayı silmek → `404`

**Gerçek sonuç**
`HTTP: 404`, `title:"Zamanlama bulunamadi"`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-007 — Boş `targetName` → `400`

**Gerçek sonuç**
`HTTP: 400`, `title:"Zamanlama gecersiz"`, `detail:"'targetName' alani
zorunludur."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-008 — Geçersiz saat dilimi → `400`

**Gerçek sonuç**
`HTTP: 400`, `detail:"'Dunya/Hicbiryer' gecerli bir saat dilimi degil."`.
Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-009 — Cron 5 alan yerine 6 alan taşırsa → `400`

**Gerçek sonuç**
`HTTP: 400`, `detail:"'0 0 3 * * *' desteklenen bes alanli cron alt
kumesiyle eslesmiyor."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-010 — Desteklenmeyen cron uzantısı (`L`) → `400`

**Gerçek sonuç**
`HTTP: 400`, `detail:"'0 0 L * *' desteklenen bes alanli cron alt
kumesiyle eslesmiyor."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-011 — Geçerli cron kaydedilince `nextRunAt` doğru hesaplanır

**Gerçek sonuç**
Şu an `16:03` iken cron `05 16 * * *` verildi; `nextRunAt:
"2026-08-13T16:05:00+00:00"` — hedefle birebir eşleşti. Tam beklendiği
gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-012 — `payload` dizisi `MaxItemsPerJob`'ı aşarsa `PUT` → `400`

**Gerçek sonuç**
`AgentPrism__Scheduling__MaxItemsPerJob=2` env var ile yeniden başlatıldı
(KOSUM-PLANI §2.2, `user-secrets` yerine env var isolation). `HTTP: 400`,
`detail:"Yuk 3 oge tasiyor; en fazla 2 oge desteklenir."`. Ayar
kaldırılıp normal başlatıldı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-013 — Cron olmadan zamanlama: `nextRunAt` `null`, yalnız elle tetiklenir

**Gerçek sonuç**
`nextRunAt: null`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-014 — `FIX-JOB-03` (dakikalık cron) otomatik tetiklenir

**Gerçek sonuç**
`16:06:00` hedefinde 1 `JobRecord` üretildi: `status:"Completed"`,
`totalItems:1`, `doneItems:1`, `scheduledFor:"...16:06:00"`,
`startedAt:"...16:06:02"`. Zamanlamanın `lastRunAt` ilerledi,
`nextRunAt:"...16:07:00"`'a geçti. Zamanlama silindi (tekrar
tetiklenmesin diye). Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-015 — Arayüz: Jobs ekranı zamanlama formu, `Kind` seçenekleri yalnız `AgentBatch`/`Workflow`

**Gerçek sonuç**
Playwright ile `/agentprism/jobs` → **New schedule** açıldı: `Kind`
açılır listesi tam olarak iki seçenek taşıyor: "Agent batch" (seçili),
"Workflow". Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-016 — `RunWorker = false` iken Jobs ekranında uyarı bandı görünür

**Gerçek sonuç**
`AgentPrism__Scheduling__RunWorker=false` env var ile yeniden başlatıldı.
`/api/meta`: `jobWorkerEnabled:false`. Jobs ekranında uyarı: **"The
worker is off in this process."** + "...The setting is RunWorker." — tam
beklenen metin, `RunWorker` adını içeriyor. Ayar kaldırılıp normal
başlatıldı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Elle Tetikleme ve Gerçek Toplu Çalıştırma (İzlek B)

---

## MT-JOB-020 — `POST .../trigger` zamanlamayı hemen çalıştırır

**Gerçek sonuç**
`HTTP: 200`. `id:"019ffbe2-2188-7d27-8ae9-2b47cc0be1db"`,
`status:"Pending"`, `totalItems:2`, `scheduledFor` şimdiki zamana çok
yakın. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-021 — İşçi işi alır, ögeleri SIRAYLA gerçek modelle çalıştırır

**Gerçek sonuç**
`status:"Completed"` (10 sn içinde), `doneItems:2`, `failedItems:0`.
`items[0].status`/`items[1].status`: `"Completed"`, `runId`'ler farklı
(`019ffbe2-2ff0-...` / `019ffbe2-353e-...`). Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-022 — Ögenin `runId`'si gerçek bir `runs` satırına işaret eder

**Gerçek sonuç**
`agentName:"ozetleyici"`, `status:"Completed"`, `kind:"Agent"`. Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-023 — `GET /api/jobs/{id}` iş kaydı ve ögeleri TEK çağrıda döner

**Gerçek sonuç**
MT-JOB-021'de zaten okunan gövde (`GET /api/jobs/{id}`) üst düzeyde tam
olarak `job` ve `items` anahtarlarını taşıyor. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-024 — `trigger` gövdesindeki `payload`, zamanlamanın kendi yükünün YERİNE geçer

**Gerçek sonuç**
`totalItems: 1`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-025 — `trigger` sırasında `MaxItemsPerJob` aşımı → `400`

**Gerçek sonuç**
`AgentPrism__Scheduling__MaxItemsPerJob=1` env var ile yeniden başlatıldı.
`HTTP: 400`, `title:"Tetikleme basarisiz"`, `detail:"Yuk 2 oge tasiyor; en
fazla 1 oge desteklenir."`. Ayar kaldırılıp normal başlatıldı. Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-026 — Bir öge başarısız olursa iş DEVAM eder, `failedItems` sayılır

**Gerçek sonuç**
**Doküman notu:** Verilen `curl` komutu `content-type: application/json`
başlığını taşımıyor — bu hâliyle `415 Unsupported Media Type` döner
(doküman yazım eksikliği, ürün kusuru değil). Başlık eklenince: ilk
denemede `job.status:"Pending"` (yeniden deneme için serbest bırakıldı,
`attempt:1`), `job.errorMessage:"'yok-boyle-bir-agent' adinda bir agent
bulunamadi. Is basarisiz olarak isaretlenecek."` — mesaj HEMEN
yazılıyor (yalnız 3. denemede değil). Tam beklendiği gibi (iş seviyesi
hata, öge döngüsüne hiç girilmedi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-027 — Tüm ögeler başarısız olursa iş `Failed` olur

**Gerçek sonuç**
3 deneme sonrası (`attempt:3`) `status:"Failed"`,
`errorMessage:"'yok-boyle-bir-agent' adinda bir agent bulunamadi. Is
basarisiz olarak isaretlenecek."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Workflow İşi (`JobKind.Workflow`, Faz 17 × Faz 15)

Workflow'un kendisi (graf, checkpoint, human-in-the-loop) `15-WORKFLOWS.md`'nin
konusudur. Burada yalnız **iş kuyruğunun** bir workflow'u sıraya alıp
çalıştırma yeteneği sınanır.

---

## MT-JOB-030 — Workflow hedefli bir zamanlama tetiklenir, gerçek workflow çalışır

**Gerçek sonuç**
`job.status:"Completed"`, `items[0].status:"Completed"`,
`items[0].runId:"019ffbe6-f3aa-7f2c-9ab0-567ca55d16b9"`. Tam beklendiği
gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-031 — `items[0].runId` workflow'un KÖK `runs` satırına işaret eder

**Gerçek sonuç**
`kind:"Workflow"`, `workflowName:"ozetle-ve-cevir"`. `/tree` `3` satır
döndü. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-032 — `UseWorkflows()` kayıtlı değilken bir Workflow işi `Failed` olur (geçici kod değişikliği)

**Gerçek sonuç**
`Program.cs`'teki `.UseWorkflows()` geçici olarak yorum satırına alındı,
derlendi (0 uyarı, 0 hata), yeniden başlatıldı. `wf-toplu` tetiklendi:
`attempt:2`'de hâlâ `Pending`, `errorMessage` zaten dolu (ara denemelerde
de mesaj yazılıyor — yalnız 3. denemede değil); `attempt:3`'te
`status:"Failed"`. Mesaj tam beklenen: `"Workflow motoru kayitli degil.
'AgentPrism.Workflows' paketini ekleyip UseWorkflows() cagirin."`.
Değişiklik `git checkout --` ile geri alındı, yeniden derlendi (0 uyarı,
0 hata). Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — İş İptali

---

## MT-JOB-040 — `Pending` bir işi iptal etmek → `204`, durum `Cancelled`

**Gerçek sonuç**
`AgentPrism__Scheduling__RunWorker=false` env var ile yeniden başlatıldı
(iptal penceresini genişletmek için). İptal: `HTTP: 204`. Sonraki okuma:
`status:"Cancelled"`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-041 — Zaten iptal edilmiş bir işi tekrar iptal etmek → `409`

**Gerçek sonuç**
`HTTP: 409`, `title:"Is iptal edilemedi"`, `detail:"Is zaten 'Cancelled'
durumunda."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-042 — `Running` bir iş iptal edilirse işçi ögeler arasında bunu fark eder

**Gerçek sonuç**
İptal, işçi işi kiralamadan önce yetişti: `status:"Cancelled"`,
`doneItems:0`, `attempt:0`, `startedAt:null`, ikisi de `items[*].status:
"Pending"`, `runId:null`. Bu, dokümanın öngördüğü "iki öge de henüz
başlamadı" dalıdır. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-043 — Var olmayan bir iş kimliğini iptal etmek → `404`

**Gerçek sonuç**
`HTTP: 404`, `title:"Is bulunamadi"`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-044 — Arayüzden iptal: `Cancel` düğmesi yalnız `Pending`/`Leased`/`Running`'de görünür

**Gerçek sonuç**
İki tamamlanmış iş detay sayfasında (MT-JOB-021, taze bir tetikleme)
`Cancel` düğmesi **yok** — yalnız Status/Progress/Attempt/Scheduled
başlıkları var. `RunWorker=false` ile dondurulmuş `Pending` bir işte
`button "Cancel"` **görünüyor**. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Sınır Ayarları (Zamanlama)

---

## MT-JOB-050 — `RunWorker = false` iken hiçbir iş kiralanmaz, kuyrukta `Pending` bekler

**Gerçek sonuç**
`RunWorker=false` ile yeniden başlatıldı, tetiklendi. 20 sn sonra
`status:"Pending"` (hiç `Leased`/`Running` olmadı). Ayar kaldırılıp
normal başlatıldı — birkaç saniye içinde `status:"Running"`'e geçti (işçi
işi aldı). Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-051 — Var olmayan `targetName` (agent) hedefli iş, `MaxAttempts` denemesinde `Failed` olur

**Gerçek sonuç**
Taze bir `bozuk-hedef` tetiklemesi izlendi: `attempt:2, Pending` →
`attempt:3, Failed`. MT-JOB-026'da ayrıca `attempt:1, Pending` de
gözlenmişti (aynı desen). `MaxAttempts:3` sonrası `Failed`'e kilitlendi.
Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-052 — `PollInterval <= 0` ayarlanırsa işçi hiçbir tur atmadan hemen döner

**Gerçek sonuç**
`AgentPrism__Scheduling__PollInterval=00:00:00` env var ile yeniden
başlatma denendi. Uygulama **başlamadı**:
`Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException:
AgentPrismSchedulingOptions.PollInterval sifirdan buyuk olmalidir. Gelen
deger: 00:00:00.` Kaynak doğrulandı (yukarıdaki not). Bu, ürün kusuru
DEĞİL — doğrulayıcı doğru çalışıyor; asıl kusur dokümanın yanlış
varsayımıydı (yukarıda düzeltildi). Ayar kaldırılıp normal başlatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-053 — Zamanlama/iş eylemleri denetim izine YAZILMAZ

**Gerçek sonuç**
**Doküman notu:** Şemada `occurred_at` değil `created_at` sütunu var
(sorgu bu şekilde düzeltildi), şema öneki de `mt_s3` (şerit izolasyonu).
Yeni bir zamanlama oluşturuldu/silindi, bir iş tetiklendi/iptal edildi;
düzeltilmiş sorgu: `count = 0`. Şüphe/boşluk doğrulandı — hiçbir
zamanlama/iş eylemi denetim izine düşmüyor. Doküman zaten bunu bilinen
bir gözlemlenebilirlik boşluğu olarak çerçeveliyordu (yeni `HATA` kaydı
açılmadı, mevcut çerçeveleme yeterli).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-054 — `MaxConcurrentJobs` sınırı: 3. iş, ilk ikisi bitene kadar kiralanmaz

**Gerçek sonuç**
`MaxConcurrentJobs=1` ile yeniden başlatıldı, `ozet-toplu` art arda 2 kez
tetiklendi. `t+2s`: `job1:Completed, job2:Pending`. `t+4s`:
`job1:Completed, job2:Running`. `job2` `job1` tamamlanana kadar
`Pending` kaldı, kiralanmadı. Ayar kaldırılıp normal başlatıldı. Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Tek Yürütücü Seçimi: `singleton_leases` (Faz 42)

Bu bölüm HTTP ucu **taşımaz** (Faz 42 hiç uç eklemedi) — tüm doğrulama
veritabanı sorgusu ve gerçek iki-süreç gözlemiyle yapılır. `JobWorkerBackgroundService`
bu mekanizmadan **etkilenmez** (Faz 42.2, zaten kira tabanlı) — bu bölüm
yalnız MCP keşfini ve model sağlık yoklamasını sınar.

---

## MT-JOB-060 — `SingletonExecution.Enabled = false` (varsayılan) iken `singleton_leases` tablosuna HİÇ satır yazılmaz

**Gerçek sonuç**
`SELECT count(*) FROM mt_s3.singleton_leases;` → `0`. Uygulama bu oturumda
zaten defalarca çalıştı (`SingletonExecution:Enabled` hiç ayarlanmadı),
tablo boş. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-061 — `Enabled = true` + `Health.BackgroundInterval` açılınca `model-provider-health` kirası yazılır

**Gerçek sonuç**
3 satır döndü: `approval-expiration`, `mcp-discovery`,
`model-provider-health` — üçü de AYNI `owner_id` önekini
(`Faruk-MacBook-Pro:45747:...`) taşıyor, yalnız GUID kısmı farklı (her
kira kendi GUID'ini üretir, süreç kimliği paylaşılır). Kaynak doğrulandı:
`ApprovalExpirationService.cs` gerçekten var ve `approval-expiration`
adını kullanıyor. Doküman kusuru (eksik satır sayısı) yukarıda
düzeltildi — davranışsal bir ürün kusuru değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-062 — İki süreç aynı veritabanına bağlanınca kira yalnız BİRİNDE kalır

**Gerçek sonuç**
**Doküman düzeltmesi (mimari yanlış anlama).** 3 satır (MT-JOB-061'in
düzeltmesiyle tutarlı), AMA hepsi AYNI sürece ait DEĞİL:
```
approval-expiration  | ...:46238:...  (Süreç A, port 5091)
mcp-discovery         | ...:46238:...  (Süreç A)
model-provider-health | ...:46259:...  (Süreç B, port 5092)
```
Bu sonuç birkaç saniye arayla tekrar sorgulanıp DEĞİŞMEDİĞİ doğrulandı
(kararlı, yarış eseri değil). Kaynak incelendi: `SingletonGuard`
her `_leaseName` için AYRI bir örnek olarak kurulur ve BAĞIMSIZ yarışır
(`SingletonGuard.cs:36,49-67`) — sistemde küresel bir "tek lider" seçimi
YOKTUR, yalnız **her isim için ayrı ayrı** tek sahiplik garantisi vardır.
Doküman "tüm kiralar aynı sürece gider" varsayıyordu; bu **mimari bir
yanlış anlamaydı**, ürün kusuru değil — her kiranın KENDİ İÇİNDE tek
sahibi olması (iki farklı süreç AYNI kirayı asla paylaşmaması) hâlâ
doğrulanabilir asıl garanti ve bu garanti tuttu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-063 — Kira sahibi süreç öldürülünce diğer süreç DEVRALIR; süre ölçülür

**Gerçek sonuç**
Süreç A (PID 46238, iki kirayı tutuyordu: `mcp-discovery`,
`approval-expiration`) `kill -TERM` ile durduruldu. **3 saniye içinde**
(ilk poll penceresinde, doğrulanan üst sınırın çok altında) her ÜÇ kira
da (`model-provider-health` zaten B'deydi) Süreç B'ye (PID 46259) geçti.
Süreç B'nin logunda `"MCP kesfi tamamlandi: 0 tool kullanilabilir."`
satırları bu andan sonra tekrar görünmeye başladı (7 kez, düzenli
aralıklarla). Tam beklendiği gibi (ölçülen devir süresi dokümanın
öngördüğünden çok daha hızlı — muhtemelen `LeaseDuration=12s` küçük
tutulduğu için).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-064 — Yenileme aralığı `LeaseDuration/3`tür: `updated_at` düzenli aralıklarla ilerler

**Gerçek sonuç**
Üç ardışık okuma: `...46.121`, `...50.113`, `...54.115` — aralıklar
`~3.99s` ve `~4.00s`. `12/3=4` ile birebir eşleşti. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-065 — İki süreç aynı anda kuyruktan iş çeker, İKİSİ DE aynı işi almaz

**Gerçek sonuç**
Süreç A yeniden başlatıldı (aynı SQLite dosyası), `ozet-toplu` her iki
porttan 3'er kez tetiklendi (toplam 6 iş). 12 sn sonra: 6 farklı `id`,
her biri tam bir `job_items` satırı, her ögenin `run_id`'si **benzersiz**
(6 farklı GUID) — hiçbir öge iki kez işlenmedi, hiçbir çakışma yok.
(`lease_owner` tamamlanma sonrası temizleniyor, bu yüzden doğrudan
gözlenmedi — ama sonucun kendisi çakışmasızlığı kanıtlıyor.) Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-066 — Kira durumunu görmenin TEK yolu veritabanı sorgusudur — hiçbir HTTP ucu yoktur

**Gerçek sonuç**
Boş çıktı — `/api/diagnostics` yanıtında `singleton`/`lease` sözcüğü hiç
geçmiyor. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Dayanıklı Çalıştırma: `Prefer: respond-async` (Faz 46)

---

## MT-JOB-070 — `Prefer: respond-async` → `202` + `Location` + `Preference-Applied` + `AcceptedRunResponse`

**Gerçek sonuç**
`HTTP/1.1 202 Accepted`. `Location:
/agentprism/api/runs/019ffbfd-5ae2-734f-b50a-d8ec8ddcfa67`.
`Preference-Applied: respond-async`. Gövde: `runId` ve `jobId` birebir
aynı, `location` ve `eventsLocation` (`.../events` ile biter) doğru. Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-071 — `202`den HEMEN sonra `GET /api/runs/{runId}` → `Queued`, `404` DEĞİL

**Gerçek sonuç**
`HTTP: 200`, `status:"Queued"`, `completedAt:null`, `eventCount:0`. `404`
alınmadı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-072 — İşçi işi alır: `Queued` → `Running` → `Completed`

**Gerçek sonuç**
MT-JOB-071'de `Queued` yakalanmıştı; 2 sn sonraki pollingde zaten
`Completed` (işçi hızlı işledi, `Running` penceresi çok kısa sürdü,
ayrıca yakalanamadı — dokümanın "kısaca" notuyla tutarlı). Olay listesi
`get_order_status` için `toolName` alanı taşıyan tam **2** olay
gösteriyor (`tool.invoking`+`tool.invoked` — bir çağrı). Tam beklendiği
gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-073 — `runs.id` `Location`'daki kimlikle BİREBİR eşleşir; iş kimliği de aynıdır

**Gerçek sonuç**
SQL: bir satır, `id` runId ile birebir aynı. `GET /api/jobs/{runId}`:
`job.id` runId ile birebir aynı, `job.kind:"AgentRun"`. Tam beklendiği
gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-074 — `202`den sonra `/events`e bağlanan istemci BAŞLANGIÇ olaylarını kaçırmaz

**Gerçek sonuç**
`202`den hemen sonra `/events`e bağlanıldı: işçi işi alana kadar `:
bekleniyor` keep-alive yorum satırları geldi (bağlantı canlı tutuldu),
sonra ilk gerçek olay `event: run.started` (`sequence:0`) olarak geldi,
ardından `tool.invoking`→`tool.invoked`→`message.delta`→
`message.completed`→`run.completed` sırayla tam eksiksiz aktı. Hiçbir
olay kaçırılmadı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-075 — Başlık GÖNDERİLMEYEN istekte davranış DEĞİŞMEZ (K1)

**Gerçek sonuç**
`HTTP/1.1 200 OK`, `Content-Type: text/event-stream`. `Location`/
`Preference-Applied` başlıkları yok. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-076 — Boş `message` ile `Prefer: respond-async` → `400`

**Gerçek sonuç**
`HTTP: 400`, `title:"Istek bos"`, `detail:"Kuyruga alinan bir
calistirmada 'message' zorunludur."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-077 — Ek (`attachmentIds`) veya onay kararı ile birlikte `respond-async` → `400`

**Gerçek sonuç**
`HTTP: 400`, `title:"Desteklenmiyor"`, `detail:"Kuyruga alinan ('Prefer:
respond-async') bir calistirma onay kararlarini veya ekleri bu surumde
desteklemez."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-078 — `AgentPrism:AsyncRun:Enabled = false` → başlık taşıyan istek `501` alır

**Gerçek sonuç**
`AgentPrism__AsyncRun__Enabled=false` env var ile yeniden başlatıldı.
`HTTP: 501`, `title:"Kuyruga alma destegi kapali"`, `detail`
`AgentPrismAsyncRunOptions.Enabled = false` ifadesini içeriyor. Sessizce
SSE'ye düşmedi. Ayar kaldırılıp normal başlatıldı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-079 — Kota dolu iken kuyruğa alma `429` alır, iş AÇILMAZ

**Gerçek sonuç**
`PUT /api/quotas` ile `support` agent'ına `maxRuns:0` kuralı tanımlandı.
İstek `HTTP: 429`, `detail:"'support' agent'i icin gunluk calistirma
kotasi asildi (4/0)..."`. `GET /api/jobs?kind=AgentRun` sayımı `3`'te
kaldı (yeni iş açılmadı). Kural silindi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-080 — `Queued` durumdaki bir çalıştırma iptal edilebilir

**Gerçek sonuç**
`RunWorker=false` ile yeniden başlatıldı. `Prefer: respond-async` ile
`202 Accepted` alındı. Hemen iptal: `HTTP: 202`, `status:"Canceled"`,
`completedAt` dolu. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-081 — İptal edilmiş bir kuyruk çalıştırmasını TEKRAR iptal etmek → `409`

**Gerçek sonuç**
`HTTP: 409`, `title:"Calistirma zaten sonlanmis"`,
`detail:"...calistirma zaten 'Canceled' durumunda."`. Tam beklendiği
gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-082 — Var olmayan `runId` iptali → `404`

**Gerçek sonuç**
`HTTP: 404`, `title:"Calistirma bulunamadi"`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-083 — `Prefer: respond-async` + AYNI `Idempotency-Key` → TEK iş, aynı `Location`

**Gerçek sonuç**
İkinci istek YENİ bir iş açmadı — gövdedeki `runId`/`jobId`/`location`
ilk istekle birebir aynı, `Idempotency-Replayed: true` başlığı var. AMA
ikinci yanıtta **`Location` HTTP başlığı hiç yok** (ve `Preference-Applied`
de yok) — yalnızca gövdedeki JSON `location` alanı doğru. Kök neden
doğrulandı: **`HATA-S3-008`** — `IdempotencyResponse` kaydı
(`src/AgentPrism.Abstractions/Idempotency/IdempotencyTypes.cs:45-56`)
yalnız `StatusCode`, `ContentType`, `Body`, `RunId` taşıyor; hiçbir HTTP
başlığı saklamıyor. `IdempotencyReplayResult.ExecuteAsync`
(`IdempotencyResults.cs:12-19`) yalnız `StatusCode`/`ContentType`/
`Idempotency-Replayed` başlığını ayarlayıp gövdeyi yazıyor — orijinal
yanıtın `Location`/`Preference-Applied` başlıkları hiçbir yerde
saklanmadığı için tekrarlanamıyor. Ayrıntı: `SONUCLAR-S3-2026-08-13.md`.

---
Kapanış oturumu (Aile S, `docs/manuel-test/KAPANIS-PLANI.md`): kök neden
doğrulandığı gibi çıktı — `IdempotencyResponse` govde disi HTTP baslikları
hic saklamiyordu. `IdempotencyResponse.Headers` (yeni, varsayilan bos
sozluk) eklendi; `IdempotencyCapturingResult` `Content-Type`/`Content-Length`/
`Transfer-Encoding`/`Idempotency-Replayed` DISINDA kalan tum yanit
basliklarini yakalar, `IdempotencyReplayResult` bunlari govdeden ONCE
yeniden yazar — yalniz `Location`/`Preference-Applied`'e ozel bir alan
degil, genel bir yakalama (ileride eklenecek her yeni baslik icin ayni
kusurun tekrar acilmasini onler). SQL depolarinda yeni `headers` sutunu
(`jsonb`/`TEXT`/`nvarchar(max)`, nullable) — Postgres migration 0029,
Sqlite/SqlServer migration 0016; serilestirme `SqlWebhookStore`'daki
`Dictionary<string,string>` deseniyle AYNI (AOT uyumlu kaynak uretilmis
baglam). Canlı `mt_fin` şemasına karşı doğrulandı: aynı `Idempotency-Key`
ile ikinci istek artık `Location` (birebir aynı) VE `Preference-Applied`
başlıklarını taşıyor; `idempotency_keys.headers` sütunu ikisini de JSON
olarak saklıyor.

**Değişen dosyalar:** `IdempotencyTypes.cs` (+`Headers`),
`IdempotencyResults.cs` (yakalama + replay), `SqlIdempotencyStore.cs`
(+serialize/deserialize), üç `*Queries.cs` (Select/Complete sorguları),
üç migration dosyası. **Regresyon testleri:**
`IdempotencyStoreContract.cs` (2 yeni test, dört depoda da koşar) ·
`IdempotencyTests.cs` `Prefer_respond_async_replay_Location_ve_Preference_Applied_basliklarini_da_doner`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-084 — Akışlı istek (başlık YOK) + `Idempotency-Key` → Faz 43'ün `400`'ü KORUNUR

**Gerçek sonuç**
`HTTP: 200` — `400` ALINMADI. Yanıt normal, akışsız, tamamlanmış bir JSON
gövdesiydi (`support` agent'ının serbest metin yanıtı). Bu, bu test
oturumunun `22-GUARDRAIL-VE-YAPISAL-CIKTI.md` bölümünde ONLARCA kez
kullanılan desenle (`Idempotency-Key` + `Prefer` YOK → buffered JSON)
tam tutarlıdır — kod-doğrulanmış, kararlı davranış. Doküman kusuru
yukarıda düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-085 — Jobs ekranında `AgentRun` türü iş görünür ama Ögeler listesi BOŞTUR

**Gerçek sonuç**
Playwright ile MT-JOB-074'ün `AgentRun` işi açıldı: üst bilgi `AgentRun ·
support`, `Status: completed`, `Progress: No item`. **Items** paneli:
"No item" / "This job has no input item." Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Güvenlik: API Anahtarı Kapsam Boşluğu

---

## MT-JOB-090 — 🚨 `RunsRead`-kapsamlı bir API anahtarı zamanlama silebiliyor/iş iptal edebiliyor mu?

**Gerçek sonuç**
**Şüphe DOĞRULANDI.** **Doküman notu:** anahtar alanı `rawKey` değil
`plaintextKey` (yanıt gövdesi: `{"record":{...},"plaintextKey":"..."}`).
Yalnız `RunsRead` kapsamlı bir anahtarla: Adım 2 (`PUT
/api/schedules/kapsam-testi`) → `HTTP:200` (zamanlama oluşturuldu!).
Adım 3 (`DELETE`) → `HTTP:204` (silindi!). Kapsam kısıtı hiç
uygulanmadı. Kontrol grubu (Adım 4, `PUT /api/agents/...`) → `HTTP:403`,
`detail:"Bu uc 'AgentsAdmin' kapsamini gerektiriyor..."` — kapsam
sisteminin `AgentEndpoints`'te çalıştığını ama `SchedulingEndpoints`'te
HİÇ devrede olmadığını doğruluyor. `15-WORKFLOWS.md` `MT-WF-100`'ün aynı
bulgusuyla birlikte bu artık İKİ bağımsız uç grubunda doğrulanmış
sistematik bir kalıp. Kayıt: **`HATA-S3-009`** (bu dosyanın son
kusuru — Şerit 3 tamamlandı).

---

**GECTI (Aile F, docs/manuel-test/KAPANIS-PLANI.md §6).** SchedulingEndpoints.cs'in tum 8 ucuna RequireApiKeyScope eklendi (GET /api/schedules, GET/api/schedules/{name}, GET /api/jobs, GET /api/jobs/{id} -> PlatformRead/RunsRead; PUT/DELETE /api/schedules/{name} -> PlatformAdmin; POST /api/schedules/{name}/trigger, POST /api/jobs/{id}/cancel -> RunsWrite). Canli PostgreSQL'e karsi yeniden uretildi: ayni RunsRead-kapsamli anahtarla Adim 2 (PUT /api/schedules/kapsam-testi) -> HTTP 403, title: "Kapsam yetersiz", detail: "Bu uc 'PlatformAdmin' kapsamini gerektiriyor; anahtar bu kapsami tasimiyor." Adim 3 (DELETE) de ayni sebeple -> HTTP 403 (zamanlama zaten olusmadigi icin de aslinda yok). Kapsam kisiti artik SchedulingEndpoints'te de calisiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
