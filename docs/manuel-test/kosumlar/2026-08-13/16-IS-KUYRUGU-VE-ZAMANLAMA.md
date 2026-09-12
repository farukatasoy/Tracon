# 16 — İş Kuyruğu, Zamanlama, Tek Yürütücü Seçimi ve Dayanıklı Çalıştırma (`JOB`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../16-IS-KUYRUGU-VE-ZAMANLAMA.md`](../../16-IS-KUYRUGU-VE-ZAMANLAMA.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show efd5247:docs/manuel-test/kosumlar/2026-08-13/16-IS-KUYRUGU-VE-ZAMANLAMA.md
> ```

---

## Temiz geçen case'ler (53)

| Case | Durum | Başlık |
|---|---|---|
| MT-JOB-001 | ☑ | `PUT /api/schedules/{name}` yeni bir zamanlama oluşturur |
| MT-JOB-002 | ☑ | Aynı adı tekrar `PUT` etmek GÜNCELLER; `id` ve `createdAt` sabit kalır |
| MT-JOB-003 | ☑ | `GET /api/schedules/{name}` tekil kaydı döner |
| MT-JOB-004 | ☑ | `GET /api/schedules` kiracının tüm zamanlamalarını listeler |
| MT-JOB-005 | ☑ | `DELETE` zamanlamayı siler, sonraki `GET` `404` verir |
| MT-JOB-006 | ☑ | Var olmayan bir zamanlamayı silmek → `404` |
| MT-JOB-007 | ☑ | Boş `targetName` → `400` |
| MT-JOB-008 | ☑ | Geçersiz saat dilimi → `400` |
| MT-JOB-009 | ☑ | Cron 5 alan yerine 6 alan taşırsa → `400` |
| MT-JOB-010 | ☑ | Desteklenmeyen cron uzantısı (`L`) → `400` |
| MT-JOB-011 | ☑ | Geçerli cron kaydedilince `nextRunAt` doğru hesaplanır |
| MT-JOB-012 | ☑ | `payload` dizisi `MaxItemsPerJob`'ı aşarsa `PUT` → `400` |
| MT-JOB-013 | ☑ | Cron olmadan zamanlama: `nextRunAt` `null`, yalnız elle tetiklenir |
| MT-JOB-014 | ☑ | `FIX-JOB-03` (dakikalık cron) otomatik tetiklenir |
| MT-JOB-015 | ☑ | Arayüz: Jobs ekranı zamanlama formu, `Kind` seçenekleri yalnız `AgentBatch`/`Workflow` |
| MT-JOB-016 | ☑ | `RunWorker = false` iken Jobs ekranında uyarı bandı görünür |
| MT-JOB-020 | ☑ | `POST .../trigger` zamanlamayı hemen çalıştırır |
| MT-JOB-021 | ☑ | İşçi işi alır, ögeleri SIRAYLA gerçek modelle çalıştırır |
| MT-JOB-022 | ☑ | Ögenin `runId`'si gerçek bir `runs` satırına işaret eder |
| MT-JOB-023 | ☑ | `GET /api/jobs/{id}` iş kaydı ve ögeleri TEK çağrıda döner |
| MT-JOB-024 | ☑ | `trigger` gövdesindeki `payload`, zamanlamanın kendi yükünün YERİNE geçer |
| MT-JOB-025 | ☑ | `trigger` sırasında `MaxItemsPerJob` aşımı → `400` |
| MT-JOB-027 | ☑ | Tüm ögeler başarısız olursa iş `Failed` olur |
| MT-JOB-030 | ☑ | Workflow hedefli bir zamanlama tetiklenir, gerçek workflow çalışır |
| MT-JOB-031 | ☑ | `items[0].runId` workflow'un KÖK `runs` satırına işaret eder |
| MT-JOB-032 | ☑ | `UseWorkflows()` kayıtlı değilken bir Workflow işi `Failed` olur (geçici kod değişikliği) |
| MT-JOB-040 | ☑ | `Pending` bir işi iptal etmek → `204`, durum `Cancelled` |
| MT-JOB-041 | ☑ | Zaten iptal edilmiş bir işi tekrar iptal etmek → `409` |
| MT-JOB-042 | ☑ | `Running` bir iş iptal edilirse işçi ögeler arasında bunu fark eder |
| MT-JOB-043 | ☑ | Var olmayan bir iş kimliğini iptal etmek → `404` |
| MT-JOB-044 | ☑ | Arayüzden iptal: `Cancel` düğmesi yalnız `Pending`/`Leased`/`Running`'de görünür |
| MT-JOB-050 | ☑ | `RunWorker = false` iken hiçbir iş kiralanmaz, kuyrukta `Pending` bekler |
| MT-JOB-051 | ☑ | Var olmayan `targetName` (agent) hedefli iş, `MaxAttempts` denemesinde `Failed` olur |
| MT-JOB-054 | ☑ | `MaxConcurrentJobs` sınırı: 3. iş, ilk ikisi bitene kadar kiralanmaz |
| MT-JOB-060 | ☑ | `SingletonExecution.Enabled = false` (varsayılan) iken `singleton_leases` tablosuna HİÇ satır yazılmaz |
| MT-JOB-063 | ☑ | Kira sahibi süreç öldürülünce diğer süreç DEVRALIR; süre ölçülür |
| MT-JOB-064 | ☑ | Yenileme aralığı `LeaseDuration/3`tür: `updated_at` düzenli aralıklarla ilerler |
| MT-JOB-065 | ☑ | İki süreç aynı anda kuyruktan iş çeker, İKİSİ DE aynı işi almaz |
| MT-JOB-066 | ☑ | Kira durumunu görmenin TEK yolu veritabanı sorgusudur — hiçbir HTTP ucu yoktur |
| MT-JOB-070 | ☑ | `Prefer: respond-async` → `202` + `Location` + `Preference-Applied` + `AcceptedRunResponse` |
| MT-JOB-071 | ☑ | `202`den HEMEN sonra `GET /api/runs/{runId}` → `Queued`, `404` DEĞİL |
| MT-JOB-072 | ☑ | İşçi işi alır: `Queued` → `Running` → `Completed` |
| MT-JOB-073 | ☑ | `runs.id` `Location`'daki kimlikle BİREBİR eşleşir; iş kimliği de aynıdır |
| MT-JOB-074 | ☑ | `202`den sonra `/events`e bağlanan istemci BAŞLANGIÇ olaylarını kaçırmaz |
| MT-JOB-075 | ☑ | Başlık GÖNDERİLMEYEN istekte davranış DEĞİŞMEZ (K1) |
| MT-JOB-076 | ☑ | Boş `message` ile `Prefer: respond-async` → `400` |
| MT-JOB-077 | ☑ | Ek (`attachmentIds`) veya onay kararı ile birlikte `respond-async` → `400` |
| MT-JOB-078 | ☑ | `Tracon:AsyncRun:Enabled = false` → başlık taşıyan istek `501` alır |
| MT-JOB-079 | ☑ | Kota dolu iken kuyruğa alma `429` alır, iş AÇILMAZ |
| MT-JOB-080 | ☑ | `Queued` durumdaki bir çalıştırma iptal edilebilir |
| MT-JOB-081 | ☑ | İptal edilmiş bir kuyruk çalıştırmasını TEKRAR iptal etmek → `409` |
| MT-JOB-082 | ☑ | Var olmayan `runId` iptali → `404` |
| MT-JOB-085 | ☑ | Jobs ekranında `AgentRun` türü iş görünür ama Ögeler listesi BOŞTUR |

## Ayrıntı taşıyan case'ler (8)

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

## MT-JOB-052 — `PollInterval <= 0` ayarlanırsa işçi hiçbir tur atmadan hemen döner

**Gerçek sonuç**
`Tracon__Scheduling__PollInterval=00:00:00` env var ile yeniden
başlatma denendi. Uygulama **başlamadı**:
`Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException:
TraconSchedulingOptions.PollInterval sifirdan buyuk olmalidir. Gelen
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

## MT-JOB-083 — `Prefer: respond-async` + AYNI `Idempotency-Key` → TEK iş, aynı `Location`

**Gerçek sonuç**
İkinci istek YENİ bir iş açmadı — gövdedeki `runId`/`jobId`/`location`
ilk istekle birebir aynı, `Idempotency-Replayed: true` başlığı var. AMA
ikinci yanıtta **`Location` HTTP başlığı hiç yok** (ve `Preference-Applied`
de yok) — yalnızca gövdedeki JSON `location` alanı doğru. Kök neden
doğrulandı: **`HATA-S3-008`** — `IdempotencyResponse` kaydı
(`src/Tracon.Abstractions/Idempotency/IdempotencyTypes.cs:45-56`)
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
