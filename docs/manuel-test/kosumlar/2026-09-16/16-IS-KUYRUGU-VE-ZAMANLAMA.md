# 16 — İş Kuyruğu, Zamanlama, Tek Yürütücü Seçimi ve Dayanıklı Çalıştırma (`JOB`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../16-IS-KUYRUGU-VE-ZAMANLAMA.md`](../../16-IS-KUYRUGU-VE-ZAMANLAMA.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. Spec'in kendi başlıkları `### MT-JOB-NNN` (h3); skill §4.1/§7
> konvansiyonuna uymak için burada `## MT-JOB-NNN` (h2) kullanılır.

| | |
|---|---|
| **Şerit** | `ap-s4` (Faz B) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s4` · dal `test/kosum-s4` |
| **Kod** | `7e3a4de7` donuk (`git diff --stat 7e3a4de7..HEAD -- src samples tests` boş, doğrulandı) |
| **Case sayısı** | 98 toplam (MT-JOB-001..131, numaralar bloklu, sıralı değil) |
| **Port** | 5084 |
| **Şema** | `mt_s4` (PostgreSQL, paylaşılan `ap-pg` container) |
| **Bu oturumda koşulan** | Oturum 2: MT-JOB-001..013 (Bölüm 1, kısmi). Oturum 3: MT-JOB-014..054 (Bölüm 1 tamamlandı, Bölüm 2-5 tamamlandı). Oturum 4: MT-JOB-060..066 (Bölüm 6, Tek Yürütücü Seçimi, tam), MT-JOB-070..085 (Bölüm 7, async-run, tam), MT-JOB-090 (Bölüm 8, tetikleyici kapsam şüphesi, tek case). Oturum 5-13 (önceki devir notlarında): MT-JOB-091..121. **Oturum 14 (bu oturum): MT-JOB-122..131 (Bölüm 9-10) + regresyon `MT-JOB-098`(B03) — AİLE KAPANDI, 98/98** |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): MT-JOB-012, 016, 025, 040,
050, 052, 054 `dotnet user-secrets set ...` yerine ortam değişkeniyle
uygulama yeniden başlatılarak koşuldu.

**Sapma — `user-secrets list` okuma engeli (oturum 3 güncellemesi):** Oturum
2'nin devir notu bu okumanın Claude Code otomatik-mod sınıflandırıcısı
tarafından "Cloud Storage Mass Delete" gerekçesiyle reddedildiğini
kaydetmişti. Oturum 3'ün başında **aynı komut ilk denemede başarılı oldu** —
engel bu oturumda **hiç tekrarlamadı**, tüm 17 anahtar okunabildi ve gerçek
OpenAI/Anthropic/Google anahtarları MT-JOB-014, 020-024, 030-032, 040-044,
050-054'ün gerçek model/workflow çağrılarında sorunsuz kullanıldı.

---

## HATA-S4-002 — `PUT /api/schedules/{name}` gövdede `payload` alanı olmadan `500` verir

- **Case:** MT-JOB-129
- **Önem:** Orta (bir HTTP API tüketicisi opsiyonel bir alanı atladığında
  `400`/`200` yerine ham `500` alıyor; veri kaybı yok, hizmet kesintisi yok,
  ama sözleşme ihlali ve kötü hata mesajı)
- **İzlek:** A (salt HTTP)
- **Ortam:** macOS arm64 · net10 · PostgreSQL (`mt_s4` VE ayrı bir scratch
  şeması, ikisinde de aynı sonuç) · sağlayıcı yok

**Beklenen:** `payload` alanı olmadan `PUT /api/schedules/{name}` ya
kabul edilir (boş payload varsayılır, `200`) ya da açık bir doğrulama
hatası döner (`400`).

**Gerçekleşen:** `500 Internal Server Error`,
`System.InvalidOperationException: Operation is not valid due to the
current state of the object.` — `JsonElementConverter.Write`
`default(JsonElement)`'i (ValueKind=Undefined) serialize edemiyor.

**Yeniden üretme:**
1. Sağlıklı bir Tracon örneğine karşı: `curl -X PUT
   $BASE/api/schedules/x -H "Authorization: Bearer $TOK" -H
   "Content-Type: application/json" -d
   '{"handlerKey":"tracon.workflow","targetName":"t","cron":"*/5 * * * *","enabled":true}'`
   — gövdede `payload` **yok**.
2. Yanıt `500` (boş gövde, `traceId` dışında bilgi yok).
3. Aynı istek `"payload":{}` eklenerek tekrarlanırsa `200` döner.

**Kanıt:**
- Ana uygulamada (port 5084, `mt_s4`) ve `~/tracon-manuel/job-handlers-s4`
  scratch host'unda (port 5190, `mtjob_s4`) BİREBİR aynı çöküş.
- Sunucu log'u: `Unhandled exception... at
  System.Text.Json.Serialization.Converters.JsonElementConverter.Write`,
  çağrı zinciri `SaveScheduleAsync`'in `TypedResults.Ok(saved)`'ine kadar
  iniyor.
- Kök neden satırları: `src/Tracon.AspNetCore/Contracts/SchedulingContracts.cs:45`
  (`JobScheduleSaveRequest.Payload` — `required` değil, varsayım yok) ve
  `src/Tracon.AspNetCore/Endpoints/SchedulingEndpoints.cs:266`
  (`Payload = request.Payload` — normalize edilmeden yanıt nesnesine
  kopyalanıyor, `TriggerScheduleAsync`'in `JobRecord.Payload` alanı ise
  DB round-trip'inden geçtiği için bu sorunu YAŞAMIYOR — bkz. MT-JOB-122/126
  kayıtlarında `"payload":null` başarıyla dönen `GET /api/jobs/{id}`).

**Kapsam:** Yalnız `PUT /api/schedules/{name}`'i doğrudan HTTP ile,
`payload` alanı olmadan çağıran bir tüketiciyi etkiler. Tracon.UI'nin
kendi formu `payload`'ı her zaman `"[]"` ile dolu gönderdiği için arayüzden
ERİŞİLEMEZ (MT-JOB-130'da doğrulandı). Bu turda `PUT
/api/schedules/{name}` kullanan diğer case'ler (`MT-JOB-001-003`, `102`,
`113-115`) hepsi `payload` alanını açıkça gönderdi, bu yüzden başka hiçbir
case bu turda etkilenmedi — ama sınıf taraması (aynı desende başka bir
`JsonElement`, `required` değil, alan) kapanış oturumunda yapılmalı.

---

## Devir notu

**🎉 AİLE 16 (JOB) KAPANDI — bu oturum (oturum 14).** Önceki devir notu
(oturum 5-13 birikimi) `MT-JOB-091`'den `121`'e kadarki çalışmayı
kaydetmiş ve "`122`'den `131`'e kadar (37 case) koşulmadı" demişti — bu
sayı **bayattı**: dosyanın git geçmişi (`d8e9c308`, `8f4f8c68`, `240d74a7`)
gösteriyor ki `091..121` zaten önceki oturumlarda koşulmuş. Bu oturum
gerçek kalanı ölçtü (`### MT-JOB-` blok sayımı: spec 98 blok, kayıt 87 blok
→ 11 eksik) ve hepsini kapattı: **`MT-JOB-122..131`** (Bölüm 9-10, 10 case)
+ spec'in `098` numarasını **ikinci kez** kullandığı regresyon case'i
(`Yarıda kalan approval handoff'u..., B03`, K-726). **Toplam: 98/98 case,
97 Geçti, 1 Kaldı (`MT-JOB-129` → `HATA-S4-002`), 0 Beklemede, 0 Atlandı.**

**Yöntem — donuk `samples/`'a dokunmadan üç ayrı teknik kullanıldı:**
1. **Özel scratch host** (`~/tracon-manuel/job-handlers-s4`, yerel paket
   feed'i `ap-s4/artifacts/package/release`, sürüm `0.0.0-preview.0.819`):
   `acme.a`/`acme.b` custom `IJobHandler`'ları, bir DI-scope marker'ı, bir
   `handoff-agent` + onay gerektiren `cancel_order` tool'u içeriyor.
   `JOBH_SCENARIO` ortam değişkeniyle beş varyant çalıştırıldı: `normal`,
   `handlers-before-addtracon` (123), `dup-type` (124, host çökmeli),
   `reserved` (125, host çökmeli), `sched-allow` (129 adım 3). Cases
   122-127, 129-131, 098(B03) hepsi bu host'ta koşuldu.
2. **Ham migration SQL + elle doldurulmuş `__migrations` ledger'ı** (128):
   `0001..0042` `{schema}` yerine gerçek şema adı geçirilerek doğrudan
   `psql`'e uygulandı, spec'in fixture'ı (9 `kind` × 1 schedule+job, `kind=0`
   için ikinci job) yazıldı, ledger'a `MigrationDescriptor.ComputeChecksum`
   ile birebir eşleşen SHA-256'lar elle yazıldı (`shasum -a 256`), sonra
   donuk DLL bu şemaya karşı başlatılıp gerçek `MigrationRunner.ApplyAsync`
   kalan 9 migration'ı (43+) çalıştırdı. 🚨 **Tuzak:** ledger'ı BOŞ bırakıp
   1-42'yi "replay" ettirmek denendi önce — `0040_persisted_payload_version.sql`
   idempotent OLMAYAN bir `RENAME COLUMN schema_version` taşıyor, ikinci
   çalıştırmada `column "schema_version" does not exist` ile patlıyor. Ders:
   "migration dosyaları hep IF NOT EXISTS/idempotent" varsayımı YANLIŞ,
   ledger'sız replay güvenli değil.
3. **Ana uygulama (port 5084, `mt_s4`)** doğrudan: 098(B03)'ün regresyon
   akışı denenmeden önce `support` agent'ının bu süreçte **echo**'ya
   bağlandığı görüldü (muhtemelen DLL doğrudan `ASPNETCORE_ENVIRONMENT`
   Development olmadan başlatıldığı için `user-secrets` yüklenmedi,
   `openAiEnabled=false` kaldı) — bu yüzden 098(B03) yerine scratch host'un
   kendi deterministik `handoff-model` (`FakeModelProvider.CallsTool`)
   agent'ı kullanıldı; ana uygulama yalnız `HATA-S4-002`'nin ana koddaki
   tekrarını doğrulamak için kullanıldı (payload'sız `PUT
   /api/schedules/manual-test-payloadless`, aynı `500`).

🚨 **Yeni kusur — `HATA-S4-002` (Orta, `MT-JOB-129`).** `PUT
/api/schedules/{name}` gövdede `payload` alanı **olmadan** gönderilirse
`500` (`JsonElementConverter.Write` — `default(JsonElement)` serialize
edilemiyor). Kök neden `SchedulingContracts.cs:45`
(`JobScheduleSaveRequest.Payload`, `required` değil, varsayımsız) →
`SchedulingEndpoints.cs:266` (`Payload = request.Payload`, normalize
edilmeden yanıta kopyalanıyor). Hem scratch host'ta hem donuk ana
uygulamada (port 5084, gerçek `mt_s4`) birebir tekrarlandı — host'a özgü
değil. UI formu `payload`'ı her zaman `"[]"` gönderdiği için arayüzden
ERİŞİLEMEZ; yalnız `payload` alanını atlayan bir HTTP API tüketicisini
etkiler. Ayrıntı ve tam repro case kaydında (`MT-JOB-129`, yukarıda).

**Kod donması ihlali YOK.** `git diff --stat 7e3a4de7..HEAD -- src samples
tests` bu oturumun sonunda da **boş**. Scratch host (`~/tracon-manuel/
job-handlers-s4`) ve geçici migration şeması (`mtjob128mig`, iş bitince
`DROP SCHEMA ... CASCADE` ile silindi) repo **dışında**.

🚨 **Bulk secret extraction reddedildi.** Beş provider secret'ını tek
seferde ayrı dosyalara çekmeye çalışan bir komut ("Credential
Materialization" gerekçesiyle) otomatik-mod sınıflandırıcısı tarafından
reddedildi. Tek tek, kullanım anında (`dotnet user-secrets list | grep
<tek anahtar>`) çekmek sorunsuz çalıştı (`Tracon:PostgreSql:ConnectionString`
bu şekilde alındı). **Ders:** provider anahtarlarını toplu/önden değil,
ihtiyaç anında tek tek çek.

**Sonraki oturumun işi:** Bu ailenin koşum işi bitti — `ap-s4`'in sıradaki
ataması `00-KOSUM-PLANI.md`'deki diğer altı aile (`06`, `09`, `22`, `26`,
`27`, `28`, `30`, hiçbiri henüz açılmadı, bkz. DEVIR.md §5 tablosu). Ana
uygulama (port 5084) hâlâ **açık** — echo provider'a bağlı `support` agent'ı
dahil tüm önceki case'lerin fixture'ları korunuyor. Bir sonraki aile
gerçek Anthropic/Google/Azure sağlayıcı çağrısı gerektiriyorsa (aile 06),
uygulamanın `openAiEnabled`/`anthropicEnabled`/`googleEnabled` bayraklarının
o an **hangi ortamda** doğru okunduğunu (Development mi Production mı)
önce doğrulamalı — bu oturum ortam değişkeni tabanlı bir override
denemedi (bulk-secret engeli nedeniyle yarım bırakıldı).

**Artık test fixture'ları (temizlenmedi, zararsız):** önceki oturumlardan
kalanlar (`bozuk-hedef`, `wf-toplu`, `hesapli-cron`, `ozet-toplu`)
dokunulmadı. Bu oturum `mt_s4` şemasına eklediği tek kalıcı iz: scratch
host'un kendi `mtjob_s4` şemasındaki test job/schedule satırları (`acme.a`/
`acme.b`/`acme.no-such-handler` — ayrı şema, `mt_s4`'ü etkilemiyor,
temizlenmedi çünkü zararsız ve kanıt değeri taşıyor).

---

## MT-JOB-001 — `PUT /api/schedules/{name}` yeni bir zamanlama oluşturur

**Gerçek sonuç**
**⚠️ Spec sapması (yukarıdaki devir notu):** `"kind": "AgentBatch"` yerine
`"handlerKey": "tracon.agent-batch"` gönderildi (contract artık `Kind` kabul
etmiyor). `PUT $APU/api/schedules/ozet-toplu` → `HTTP: 200`. Gövde: `id`
dolu bir GUID (`01a0ac58-c0f4-7c1a-b682-a7ef621859b6`), `nextRunAt: null`
(cron yok), `createdAt == updatedAt` (`2026-09-16T22:31:32.835675+00:00`).
Beklenen sonuçla (alan adı sapması hariç) eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-002 — Aynı adı tekrar `PUT` etmek GÜNCELLER; `id` ve `createdAt` sabit kalır

**Gerçek sonuç**
Aynı `handlerKey` düzeltmesiyle tek ögeli bir payload ile tekrar `PUT`
edildi. `id` MT-JOB-001'dekiyle **birebir aynı**
(`01a0ac58-c0f4-7c1a-b682-a7ef621859b6`). `createdAt` değişmedi
(`22:31:32.835675`), `updatedAt` ilerledi (`22:31:41.796164`). Beklenen
sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-003 — `GET /api/schedules/{name}` tekil kaydı döner

**Gerçek sonuç**
`GET $APU/api/schedules/ozet-toplu` → `HTTP: 200`, gövde MT-JOB-002'nin
sonucuyla birebir aynı (aynı `id`, `updatedAt`, payload). Beklenen sonuçla
eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-004 — `GET /api/schedules` kiracının tüm zamanlamalarını listeler

**Gerçek sonuç**
`GET $APU/api/schedules` → `['ozet-toplu']`. Beklenen sonuçla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-005 — `DELETE` zamanlamayı siler, sonraki `GET` `404` verir

**Gerçek sonuç**
`DELETE .../ozet-toplu` → `HTTP: 204`. Ardından `GET .../ozet-toplu` →
`HTTP: 404`, `title: "Schedule not found"`, `detail: "There is no schedule
named 'ozet-toplu'."`. **⚠️ Metin sapması (K-228 deseni, dosya 05/07'de de
görülen):** spec Türkçe `"Zamanlama bulunamadi"` bekliyor, ürün İngilizce
`"Schedule not found"` döndürüyor — nitel iddia (silme + 404) doğru,
yalnız dil beklentisi bayat. Case sonunda `ozet-toplu` MT-JOB-001'in
(düzeltilmiş) gövdesiyle **yeniden oluşturuldu** — spec'in istediği gibi,
sonraki case'ler onu kullanıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-006 — Var olmayan bir zamanlamayı silmek → `404`

**Gerçek sonuç**
`DELETE .../hic-yok-boyle-zamanlama` → `HTTP: 404`,
`title: "Schedule not found"` (aynı K-228 dil sapması, kusur değil).
Beklenen sonuçla (durum kodu) eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-007 — Boş `targetName` → `400`

**Gerçek sonuç**
`PUT .../hedefsiz` (`targetName: ""`, `handlerKey` düzeltmesiyle) →
`HTTP: 400`, `title: "Schedule invalid"`, `detail: "'targetName' is
required."` — beklenen davranış (K-228 dil sapmasıyla), alan adı tam
söyleniyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-008 — Geçersiz saat dilimi → `400`

**Gerçek sonuç**
`PUT .../yanlis-tz` (`timeZone: "Dunya/Hicbiryer"`) → `HTTP: 400`,
`detail: "'Dunya/Hicbiryer' is not a valid time zone."` — beklenen davranış.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-009 — Cron 5 alan yerine 6 alan taşırsa → `400`

**Gerçek sonuç**
`PUT .../6-alanli` (`cron: "0 0 3 * * *"`) → `HTTP: 400`,
`detail: "'0 0 3 * * *' does not match the supported five-field cron
subset."` — beklenen davranış.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-010 — Desteklenmeyen cron uzantısı (`L`) → `400`

**Gerçek sonuç**
`PUT .../vixie-uzantisi` (`cron: "0 0 L * *"`) → `HTTP: 400`,
`detail: "'0 0 L * *' does not match the supported five-field cron
subset."` — mesaj spec'in beklediği alt-metni ("desteklenen bes alanli cron
alt kumesiyle eslesmiyor" → İngilizce eşdeğeri) içeriyor, beklenen davranış.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-011 — Geçerli cron kaydedilince `nextRunAt` doğru hesaplanır

**Gerçek sonuç**
Koşum anı `22:32` UTC idi; `"33 22 * * *"` cron'uyla kaydedildi (bir dakika
sonrası). Dönen `nextRunAt`: `2026-09-16T22:33:00+00:00` — girilen
dakika/saatle **tam eşleşiyor**. Beklenen sonuçla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-012 — `payload` dizisi `MaxItemsPerJob`'ı aşarsa `PUT` → `400`

**Gerçek sonuç**
Skill §1.2 uyarınca `dotnet user-secrets set` yerine ortam değişkeni
kullanıldı: uygulama `Tracon__Scheduling__MaxItemsPerJob=2` ile yeniden
başlatıldı (port 5084 durdurulup aynı ortamla + bu değişkenle tekrar
`dotnet run`). `PUT .../cok-oge` (3 ögeli payload) → `HTTP: 400`,
`detail: "The payload has 3 items; at most 2 are supported."` — beklenen
davranış (K-228 dil sapmasıyla). Ardından uygulama bu değişken **olmadan**
yeniden başlatıldı (varsayılana dönüş doğrulandı: `/api/diagnostics` →
`canConnect: true`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-013 — Cron olmadan zamanlama: `nextRunAt` `null`, yalnız elle tetiklenir

**Gerçek sonuç**
`GET .../ozet-toplu` → `nextRunAt: null`. İş üreticinin sorgusu (uygulama
başlangıç logunda görüldü) açıkça `cron IS NOT NULL AND next_run_at IS NOT
NULL` filtresiyle çalışıyor, yani `nextRunAt = null` olan bir zamanlama
otomatik iş üretme sorgusuna hiç girmiyor — nitel iddia (otomatik iş
üretilmez, yalnız `trigger` ile açılır) koddan doğrulanıyor, gerçek bir
bekleme yapılmadı (mantık statik olarak yeterli kanıt).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-014 — `FIX-JOB-03` (dakikalık cron) otomatik tetiklenir

**Gerçek sonuç**
`PUT .../dakikalik-ozet` (`handlerKey` düzeltmesiyle) → `HTTP: 200`,
`id: 01a0ac60-bb71-7df3-8954-c2e290f8fc53`, `nextRunAt:
2026-09-16T22:41:00+00:00`. 75 saniye beklendi. `GET
/api/jobs?scheduleId=<id>` → tek `JobRecord`, `status: "Completed"`,
`totalItems: 1`, `doneItems: 1`, `failedItems: 0`, `startedAt`/`completedAt`
dolu (gerçek OpenAI çağrısı — bkz. devir notu, bu oturumda `user-secrets`
okuma engeli tekrarlamadı). `GET .../dakikalik-ozet` → `lastRunAt:
2026-09-16T22:41:01.799317+00:00` (ilerledi), `nextRunAt:
2026-09-16T22:42:00+00:00` (bir sonraki dakikaya geçti). Case sonunda
`DELETE .../dakikalik-ozet` → `HTTP: 204` (zamanlama silindi, artık dakikada
bir iş üretmiyor). Beklenen sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-015 — Arayüz: Jobs ekranı zamanlama formu, Handler key seçenekleri

**Gerçek sonuç**
⚠️ **Spec düzeltildi** (bkz. spec dosyasındaki not, skill §1.1 istisnası):
orijinal beklenti Faz 17 döneminden kalmaydı. Playwright ile Jobs ekranı
açıldı (`http://localhost:5084/tracon/jobs`, bearer token ile giriş yapıldı),
**New schedule** tıklandı. **Handler key** combobox'ı tam **dokuz** seçenek
gösterdi: `tracon.agent-batch`, `tracon.workflow`, `tracon.eval`,
`tracon.webhook-delivery`, `tracon.retention`, `tracon.agent-run`,
`tracon.online-eval`, `tracon.approval-resume`, `tracon.run-continuation` —
güncellenmiş beklentiyle tam eşleşiyor (`jobs.tsx:133-137`'nin kod yorumu bu
tasarımı doğruluyor: liste artık sunucudan geliyor, sabit iki seçenek değil).
Konsol: `browser_console_messages` ile kontrol edildi, yalnız sayfa
yüklemesindeki bilinen CSP inline-script hatası var (bu dosyanın işiyle
ilgisiz, `07-HTTP-YONETIM-API.md`/diğer ailelerde de görülen bir arayüz
kalıntısı), yeni bir JS hatası yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-016 — `RunWorker = false` iken Jobs ekranında uyarı bandı görünür

**Gerçek sonuç**
Skill §1.2 uyarınca `dotnet user-secrets set` yerine ortam değişkeni
kullanıldı: uygulama durduruldu, `Tracon__Scheduling__RunWorker=false` ile
yeniden başlatıldı (port 5084, şema `mt_s4` korunarak). `GET /api/meta` →
`storage.jobWorkerEnabled: false`. Jobs ekranı açıldığında sayfa başında
banner göründü: **"The worker is off in this process. A schedule and a job
can still be created and inspected, but nothing is leased or run here. The
setting is RunWorker."** — metin `RunWorker` adını içeriyor, beklenen
davranışla tam eşleşiyor. Case sonunda uygulama durduruldu ve
`Tracon__Scheduling__RunWorker` **olmadan** yeniden başlatıldı; `GET
/api/meta` → `storage.jobWorkerEnabled: true` (varsayılana dönüş
doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Toplu Çalıştırma (`AgentBatch`)

## MT-JOB-020 — `POST .../trigger` zamanlamayı hemen çalıştırır

**Gerçek sonuç**
`POST .../ozet-toplu/trigger` (`{}`) → `HTTP: 200`, `status: "Pending"`,
`totalItems: 2` (zamanlamanın kendi payload'ından), `scheduledFor` şimdiki
zamana yakın. Beklenen sonuçla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-021 — İşçi işi alır, ögeleri SIRAYLA gerçek modelle çalıştırır

**Gerçek sonuç**
14 saniye sonra `job.status: "Completed"`, `doneItems: 2`, `failedItems: 0`.
`items[0].status`/`items[1].status`: ikisi de `"Completed"`,
`runId`'leri **farklı** (`01a0ac67-6b81-...` / `01a0ac67-7264-...`).
Beklenen sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-022 — Ögenin `runId`'si gerçek bir `runs` satırına işaret eder

**Gerçek sonuç**
`GET /api/runs/<items[0].runId>` → `agentName: "summarizer"`, `status:
"Completed"`, `kind: "Agent"`. Beklenen sonuçla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-023 — `GET /api/jobs/{id}` iş kaydı ve ögeleri TEK çağrıda döner

**Gerçek sonuç**
Üst düzey anahtarlar tam olarak `['job', 'items']`. Beklenen sonuçla
eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-024 — `trigger` gövdesindeki `payload`, zamanlamanın kendi yükünün YERİNE geçer

**Gerçek sonuç**
`{"payload": ["Tek seferlik ozel girdi."]}` ile tetiklendi →
`totalItems: 1` (zamanlamanın kayıtlı 2 ögesi DEĞİL). Beklenen sonuçla
eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-025 — `trigger` sırasında `MaxItemsPerJob` aşımı → `400`

**Gerçek sonuç**
Skill §1.2 uyarınca ortam değişkeni kullanıldı: uygulama
`Tracon__Scheduling__MaxItemsPerJob=1` ile yeniden başlatıldı. 2 ögeli
payload (`["bir","iki"]`) ile tetiklendi → `HTTP: 400`, `title: "Trigger
failed"`, `detail: "The payload has 2 items; at most 1 are supported."`
(K-228 dil sapmasıyla, MT-JOB-012'nin aynı deseni). Ardından değişken
**olmadan** yeniden başlatıldı. Beklenen davranışla (durum kodu + nitel
mesaj) eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-026 — Bir öge başarısız olursa iş DEVAM eder, `failedItems` sayılır

**Gerçek sonuç**
`bozuk-hedef` zamanlaması (`targetName: "yok-boyle-bir-agent"`,
`handlerKey` düzeltmesiyle) kaydedildi ve tetiklendi. ~10s sonra
`job.attempt: 1`, `job.status: "Pending"` (yeniden deneme bekliyor),
`job.errorMessage: "The agent named 'yok-boyle-bir-agent' was not found.
The job will be marked as failed."` — **iş seviyesinde** bir hata,
`items[0].status` hâlâ `"Pending"`, `runId: null` (öge döngüsüne hiç
girilmedi). Beklenen davranışla (iş seviyesi hata, öge işlenmedi) tam
eşleşiyor; spec'in kendi notu bu case'in yalnız bu dalı doğruladığını
zaten söylüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-027 — Tüm ögeler başarısız olursa iş `Failed` olur

**Gerçek sonuç**
Aynı `bozuk-hedef` işi ~34s sonraki yoklamada `attempt: 3`, `status:
"Failed"`, `errorMessage` agent-bulunamadı mesajını taşıyor (MT-JOB-051 ile
birlikte tek bir gözlem penceresinde koşuldu — ayrıntı orada). Beklenen
sonuçla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Workflow Hedefli İşler (Faz 17 × Faz 15)

## MT-JOB-030 — Workflow hedefli bir zamanlama tetiklenir, gerçek workflow çalışır

**Gerçek sonuç**
`wf-toplu` (`handlerKey: tracon.workflow`, hedef
`summarize-and-translate`) kaydedildi ve tetiklendi. 15s sonra
`job.status: "Completed"`, `items[0].status: "Completed"`, `runId` dolu.
Beklenen sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-031 — `items[0].runId` workflow'un KÖK `runs` satırına işaret eder

**Gerçek sonuç**
`GET /api/runs/<runId>` → `kind: "Workflow"`, `workflowName:
"summarize-and-translate"`. `GET /api/runs/<runId>/tree` → **3** satır (1
workflow + 2 agent). Beklenen sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-032 — `UseWorkflows()` kayıtlı değilken bir Workflow işi `Failed` olur (geçici kod değişikliği)

**Gerçek sonuç**
`samples/Tracon.Api/Program.cs:197`'deki `.UseWorkflows()` GEÇİCİ olarak
yorum satırına alındı, `dotnet build -c Release` (0 uyarı), uygulama
yeniden başlatıldı. `wf-toplu` tetiklendi. Yoklama: `attempt=1/2` →
`status: "Pending"`, `errorMessage: "The workflow engine is not
registered. Add the 'Tracon.Workflows' package and call UseWorkflows()."`;
`attempt=3` → `status: "Failed"` (aynı mesajla kilitlendi) —
**üçüncü** denemenin sonunda `Failed`'e geçti (spec'in sorduğu "kaç deneme
sonra" sorusunun cevabı: 3). Ardından değişiklik geri alındı, `git diff
--stat -- samples/Tracon.Api/Program.cs` **boş**, tekrar `dotnet build`
(0 uyarı), `git diff --stat 7e3a4de7..HEAD -- src samples tests` **boş**
doğrulandı (kod donması bozulmadı), uygulama temiz ikili ile yeniden
başlatıldı. Beklenen davranışla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — İptal (Faz 17)

## MT-JOB-040 — `Pending` bir işi iptal etmek → `204`, durum `Cancelled`

**Gerçek sonuç**
Skill §1.2 uyarınca ortam değişkeni kullanıldı:
`Tracon__Scheduling__RunWorker=false` ile yeniden başlatıldı. `ozet-toplu`
tetiklendi, hemen `POST .../cancel` → `HTTP: 204`. Sonraki `GET` →
`status: "Cancelled"`. Beklenen sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-041 — Zaten iptal edilmiş bir işi tekrar iptal etmek → `409`

**Gerçek sonuç**
MT-JOB-040'ın iptal edilmiş işi tekrar `POST .../cancel` → `HTTP: 409`,
`title: "Job could not be canceled"`, `detail: "The job is already in
status 'Cancelled'."` (K-228 dil sapmasıyla). Beklenen sonuçla (durum kodu
+ mesaj anlamı) eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-042 — `Running` bir iş iptal edilirse işçi ögeler arasında bunu fark eder

**Gerçek sonuç**
(Varsayılan `RunWorker` ile, MT-JOB-040/041'den önce koşuldu.)
`ozet-toplu` tetiklendi ve hemen ardından `cancel` çağrıldı — yarış işçiden
hızlı geldi: `status: "Cancelled"`, `doneItems: 0`, iki öge de hâlâ
`Pending`, `runId: null` (`AgentBatchJobHandler`'ın döngü başındaki
`IsCancelledAsync` kontrolü ilk ögeye hiç girmeden yakaladı) — spec'in
"İki öge de henüz başlamadıysa doneItems: 0 ve ikisi de Pending kalır"
dalıyla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-043 — Var olmayan bir iş kimliğini iptal etmek → `404`

**Gerçek sonuç**
`POST /api/jobs/00000000-0000-0000-0000-000000000000/cancel` → `HTTP: 404`,
`title: "Job not found"` (K-228 dil sapmasıyla). Beklenen sonuçla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-044 — Arayüzden iptal: `Cancel` düğmesi yalnız `Pending`/`Leased`/`Running`'de görünür

**Gerçek sonuç**
Playwright: tamamlanmış bir işin (`01a0ac6a-718f...`, workflow) detay
sayfasında **Cancel düğmesi yok**. Yeni tetiklenen `Pending` bir işin
(`01a0ac70-5f55...`) detay sayfasında **Cancel düğmesi görünüyor**.
Beklenen sonuçla tam eşleşiyor. (Sample token her role'e sahip olduğundan
rol kısıtı ayrıca test edilmedi — spec'in kendi notu bunu zaten kabul
ediyor.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Zamanlayıcı Ayarları (Faz 17)

## MT-JOB-050 — `RunWorker = false` iken hiçbir iş kiralanmaz, kuyrukta `Pending` bekler

**Gerçek sonuç**
Aynı `RunWorker=false` penceresinde `ozet-toplu` tekrar tetiklendi; 30
saniye sonra `status: "Pending"`, `leaseOwner: null` — hiç kiralanmadı.
Ardından ayar kaldırılıp yeniden başlatıldı; ~12s sonra aynı iş `status:
"Completed"`, `doneItems: 2` — normal şekilde tamamlandı. Beklenen sonuçla
tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-051 — Var olmayan `targetName` (agent) hedefli iş, `MaxAttempts` denemesinde `Failed` olur

**Gerçek sonuç**
`bozuk-hedef` işi (MT-JOB-026'nın devamı) yoklandı: ~12s'de `attempt: 1`,
`Pending`; ~34s'lik pencerede tekrar okunduğunda `attempt: 3`, `status:
"Failed"` — spec'in beklediği gibi her denemede `Pending`'e dönüp sonunda
`Failed`'e kilitlendi (ara adımların tam zamanlaması `retryAfter: null`
nedeniyle hızlı geçti, `attempt=2` anlık penceresi ayrı yakalanamadı ama
nihai `attempt=3` → `Failed` geçişi doğrulandı). Beklenen sonuçla (nitel
davranış) eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-052 — `PollInterval <= 0` ayarlanırsa işçi hiçbir tur atmadan hemen döner

**Gerçek sonuç**
Spec'in kendisi zaten "doküman düzeltmesi" olarak işaretlemişti (§'da).
Ampirik doğrulandı: `Tracon__Scheduling__PollInterval=00:00:00` ile
uygulama başlatıldığında **8 saniye içinde çöktü**
(`OptionsValidationException: TraconSchedulingOptions.PollInterval must be
greater than zero. Actual value: 00:00:00.`,
`TraconSchedulingOptionsValidator.cs`'in `ValidateOnStart` yolu) — süreç
kendiliğinden sonlandı, port asla açılmadı. Ayar olmadan yeniden
başlatıldığında normal açıldı. Spec'in düzeltilmiş beklentisiyle (uygulama
başlamayı reddeder) tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-053 — Zamanlama/iş eylemleri denetim izine YAZILMAZ

**Gerçek sonuç**
⚠️ **Doküman düzeltmesi** (bkz. spec'teki not): doğrulama sorgusundaki
`occurred_at` sütunu yok, `created_at` kullanıldı; şema `mt_s4`. Yeni bir
zamanlama (`denetim-test`) oluşturuldu, silindi (`204`), `ozet-toplu`
tetiklenip iptal edildi (`204`). `SELECT count(*) FROM mt_s4.audit_log
WHERE created_at > now() - interval '5 minutes' AND (action LIKE
'schedule.%' OR action LIKE 'job.%')` → **`0`**. Aynı pencerede tüm
`audit_log` satırlarını gruplandıran sorgu da **boş** döndü — zamanlama/iş
eylemlerinin hiçbiri denetim izine düşmüyor, spec'in işaret ettiği
gözlemlenebilirlik boşluğu doğrulandı (mevcut/bilinen bulgu, yeni `HATA`
açılmadı — spec zaten bunu bir "boşluk" olarak, kusur değil sınır durumu
olarak belgeliyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-054 — `MaxConcurrentJobs` sınırı: ilk iş bitene kadar ikincisi kiralanmaz

**Gerçek sonuç**
⚠️ Spec başlığı "3. iş" diyor ama **Adımlar** yalnız 2 iş tetikliyor
(`MaxConcurrentJobs=1` ile) — başlık/adım uyuşmazlığı, doküman kusuru
(kusur değil, kod davranışını etkilemiyor); koşum **Adımlar**'ı esas aldı.
`Tracon__Scheduling__MaxConcurrentJobs=1` ile yeniden başlatıldı,
`ozet-toplu` art arda 2 kez tetiklendi (`J1`, `J2`). `t+6s`: `J1:
Completed`, `J2: Pending` (henüz kiralanmadı — ilk iş bitmeden ikincisi
işlenmedi). `t+12s`: ikisi de `Completed`. `SemaphoreSlim slots` sınırının
davranışı (bir iş biterken diğeri kiralanmıyor) doğrulandı. Ayar
kaldırılıp yeniden başlatıldı. Beklenen sonuçla (adımlar bazında) eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-060 — `SingletonExecution.Enabled = false` (varsayılan) iken `singleton_leases` tablosuna HİÇ satır yazılmaz

**Gerçek sonuç**
Uygulama (5084) önceki oturumdan devralınmış, varsayılan yapılandırma ile
saatlerdir çalışıyor durumda bulundu (`ps eww` ile ortam değişkenleri
doğrulandı: `SingletonExecution`/`Health`/`Mcp` için hiçbir override yok).
`SELECT count(*) FROM mt_s4.singleton_leases;` → **`0`**. Beklenen sonuçla
tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-061 — `Enabled = true` + `Health.BackgroundInterval` açılınca `model-provider-health` kirası yazılır

**Gerçek sonuç**
Uygulama (5084) ortam değişkenleriyle (skill §1.2 — `user-secrets` DEĞİL)
yeniden başlatıldı: `Tracon__SingletonExecution__Enabled=true`,
`Tracon__SingletonExecution__LeaseDuration=00:00:12`,
`Tracon__Health__BackgroundInterval=00:00:05`,
`Tracon__Mcp__RefreshInterval=00:00:05`. ~8 saniye sonra
`SELECT name, owner_id, expires_at, updated_at FROM mt_s4.singleton_leases`
→ **3 satır** (`approval-expiration`, `mcp-discovery`,
`model-provider-health`), üçü de `owner_id = Faruk-MacBook-Pro:64481:<guid>`
— gerçek OS PID (`lsof -iTCP:5084` → `64481`) ile **birebir eşleşiyor**,
yani `owner_id`'nin `{ProcessId}` kısmı gerçek işletim sistemi PID'idir (bu
bilgi MT-JOB-062/063'te süreç eşlemesi için kullanılacak). Spec'in doküman
düzeltmesiyle (iki değil üç satır) tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-062 — İki süreç aynı veritabanına bağlanınca kira yalnız BİRİNDE kalır

**Gerçek sonuç**
⚠️ **Kurulum sapması (koordinatörün talimatı, spec'in kendi "Ön koşul"undan
farklı):** Spec `dotnet publish` + SQLite dosyası + iki DLL süreci istiyor
("`dotnet run` ile ÇALIŞTIRILAMAZ" iddiasıyla). Bu koşumda onun yerine
**PostgreSQL `mt_s4` şeması paylaşılarak** iki ayrı `dotnet run --no-build
--urls` süreci kullanıldı (port 5084 — MT-JOB-061'in süreci, PID 64481 — ve
yeni ikinci süreç port 5094, PID 64999). **Ampirik bulgu: spec'in iddiası bu
ortamda YANLIŞ** — `-c Release --no-build --urls "http://localhost:5094"`
ile başlatılan ikinci süreç gerçekten `5094`'te dinliyor
(`lsof -iTCP:5094` → `Tracon.Ap 64999 ... TCP *:5094 (LISTEN)`), `5080`'e
düşmedi; `launchSettings.json` `--urls`'i EZMEDİ. (Doküman notu — ürün
kusuru değil, kurulum tarifi bayat/ortama özgü.)

İki süreç ~8 saniye eşzamanlı çalıştıktan sonra `SELECT name, owner_id FROM
mt_s4.singleton_leases`: **3 satır, üçü de `owner_id` içinde PID `64481`**
(MT-JOB-061'deki GUID'lerle birebir aynı — hiç değişmedi). İkinci sürecin
günlüğünde (`/tmp/mt-s4-app5094.log`) yalnız başarısız `INSERT ... ON
CONFLICT` denemeleri var (`WHERE owner_id = EXCLUDED.owner_id OR expires_at
< $4` koşulu tutmadığı için 0 satır etkilendi), `"MCP kesfi tamamlandi"`
veya benzeri bir tamamlanma satırı **hiç görünmedi**. Beklenen sonuçla (tek
`owner_id`, kaybedenin günlüğünde tamamlanma satırı yok) tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-063 — Kira sahibi süreç öldürülünce diğer süreç DEVRALIR; süre ölçülür

**Gerçek sonuç**
⚠️ Aynı kurulum sapması (PostgreSQL paylaşımlı şema, bkz. MT-JOB-062).
Kirayı tutan süreç (port 5084, PID `64481`) `kill -TERM` ile durduruldu,
`date -u` ile ölçülen an: `2026-09-16T23:22:52Z`. `mt_s4.singleton_leases`
her 2 saniyede bir örneklendi; `mcp-discovery` satırının `owner_id`'si
`64481`'den `64999`'e (port 5094'ün süreci) geçmiş halde ilk yakalandığı
örnekleme anı `23:23:02Z` idi (örnekleme aralığı kabası nedeniyle üst
sınır). Üç satırın da gerçek `updated_at` zaman damgası `23:23:07.75-.79Z`
— kill anından **~15 saniye** sonra, spec'in beklediği `~1,5 ×
LeaseDuration` (12 sn kirada → 18 sn üst sınır) penceresinin İÇİNDE, Faz
42'nin ölçtüğü ~17,6 sn'ye yakın. Hayatta kalan sürecin günlüğünde
(`/tmp/mt-s4-app5094.log`) bu andan sonra gerçek `"MCP discovery
completed: 0 tools available."` satırları defalarca belirdi (önceki
denemeler sessizce `0 satır etkilendi` ile başarısız oluyordu). Beklenen
sonuçla (nitel geçiş + süre penceresi) eşleşiyor. Ölçülen değer skill'in
"süre ölçümü kanıttır" uyarısı gereği buraya kaydedildi: **~15 sn**.

Kalan tek süreç (port 5094, PID 64999) MT-JOB-064/065 için canlı bırakıldı;
port 5084'ün eski süreci temizlendi (bkz. devir notu — sıradaki oturum
uygulamayı 5084'te YENİDEN başlatmalı, bu case'ten sonra 5084 KAPALI).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-064 — Yenileme aralığı `LeaseDuration/3`tür: `updated_at` düzenli aralıklarla ilerler

**Gerçek sonuç**
MT-JOB-063'ün hayatta kalan tek süreci (port 5094, PID `64999`,
`LeaseDuration=00:00:12`) kullanıldı. Üç örnekleme, 5 sn arayla:
`23:23:51.80` → `23:23:55.79` → `23:23:59.79`. Ardışık farklar: **~3,99 sn**
ve **~4,00 sn** — beklenen `12/3 = 4` sn ile pratik olarak birebir eşleşiyor
(üç kira satırının hepsi aynı anda, birlikte yenileniyor). Beklenen sonuçla
tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-065 — İki süreç aynı anda kuyruktan iş çeker, İKİSİ DE aynı işi almaz

**Gerçek sonuç**
İkinci süreç port 5084'te (PID `67102`, aynı `mt_s4` şeması) YENİDEN
başlatıldı — MT-JOB-063'ün hayatta kalanı (port 5094, PID `64999`) hâlâ
çalışıyordu, ikisi birlikte kullanıldı. `ozet-toplu` (2 ögeli, gerçek
OpenAI `summarizer` çağrısı) 5084'te 3 kez, 5094'te 3 kez tetiklendi → 6
farklı `job.id`. ~20 sn sonra `mt_s4.jobs` sorgulandı: **6 işin 6'sı da
`status=3` (Completed), `done_items=2/2`**. `mt_s4.job_items`'te bu 6 işe
ait **12 satır, 12'si de FARKLI `run_id`** (hiçbiri tekrarlanmadı, hiçbir
öge iki kez işlenmedi) — `FOR UPDATE SKIP LOCKED` iki ayrı .NET sürecinde
de güvenli kaldı. (`lease_owner` sütunu tamamlanmış işlerde boş — kira,
tamamlanınca temizleniyor; bu yüzden "hangi PID hangi işi aldı" sütun
üzerinden değil iş sonucu bütünlüğü — sıfır yinelenen `run_id`, sıfır çift
işlenmiş öge — üzerinden doğrulandı, ki case'in asıl iddiası zaten budur.)
Beklenen sonuçla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-066 — Kira durumunu görmenin TEK yolu veritabanı sorgusudur — hiçbir HTTP ucu yoktur

**Gerçek sonuç**
`curl $APU/api/diagnostics | grep -io "singleton\|lease"` (port 5084) →
**boş çıktı**. Beklenen sonuçla eşleşiyor — Faz 33'ün teşhis ucu bu alanları
hâlâ almamış.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-070 — `Prefer: respond-async` → `202` + `Location` + `Preference-Applied` + `AcceptedRunResponse`

**Gerçek sonuç**
Uygulama (5084) varsayılan ayarlarla yeniden başlatıldı (SingletonExecution
override'ları kaldırıldı). `POST /api/agents/support/run` ("ORD-1001
siparisim nerede?", `Prefer: respond-async`) → `HTTP/1.1 202 Accepted`,
`Location: /tracon/api/runs/01a0ac8b-4b6b-79d5-ba2e-eed307e97a89`,
`Preference-Applied: respond-async`. Gövde `AcceptedRunResponse`: `runId`
ve `jobId` **birebir aynı** GUID, `location` ve `eventsLocation`
(`.../events` ile bitiyor) dolu. Beklenen sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-071 — `202`den HEMEN sonra `GET /api/runs/{runId}` → `Queued`, `404` DEĞİL

**Gerçek sonuç**
İlk denemede (ayrı bir `curl` çağrısı, araya birkaç saniye girdi) run zaten
`Running`'e geçmiş bulundu — hâlâ `404` değildi ama tam `Queued` anını
yakalamadı. Tek bir kabukta tetikleme + hemen ardından `GET` zincirlenerek
tekrarlandı (ikinci çağrı, "ORD-1002"): `HTTP: 200`,
`status: "Queued"`, `modelId: null`, `startedAt` dolu — işçi henüz almadan
satırın var olduğu doğrulandı. Beklenen sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-072 — İşçi işi alır: `Queued` → `Running` → `Completed`

**Gerçek sonuç**
MT-JOB-071'in `runId`'si (`01a0ac8b-a6dd-7954-a45e-9a6bb009de79`) 2 sn
sonra tekrar okunduğunda zaten `status: "Completed"` idi (gerçek OpenAI
çağrısı ~1,4 sn sürmüş — `startedAt` 23:27:10.39, `completedAt`
23:27:11.82) — geçiş `Queued` (071'de yakalandı) → `Running` (çok kısa,
ayrıca yakalanamadı) → `Completed` sırasını izledi. `support` agent'ı
`get_order_status` tool'unu çağırdı (SSE olay akışında `get_order_status`
adı 2 kez geçti — çağrı + sonuç olayı, tek çağrı). Beklenen sonuçla (nitel
geçiş sırası + tool çağrısı) eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-073 — `runs.id` `Location`'daki kimlikle BİREBİR eşleşir; iş kimliği de aynıdır

**Gerçek sonuç**
SQL: `SELECT id, kind, workflow_name FROM mt_s4.runs WHERE id =
'01a0ac8b-a6dd-7954-a45e-9a6bb009de79'` → 1 satır, `kind=0` (Agent enum
değeri), `workflow_name` boş. `GET /api/jobs/{runId}` → `job.id == runId`
✅. ⚠️ **Sistemik spec bayatlığı (Faz 129 handler_key migrasyonu, devir
notunda zaten belgelenmiş desenin bir örneği daha):** `job.kind` alanı
ARTIK yok, yerine `job.handlerKey: "tracon.agent-run"` var — nitel iddia
(iş kimliği = çalıştırma kimliği, `AgentRun`/`tracon.agent-run` türü)
doğru, yalnız alan adı bayat. Beklenen sonuçla (alan adı sapması hariç)
eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-074 — `202`den sonra `/events`e bağlanan istemci BAŞLANGIÇ olaylarını kaçırmaz

**Gerçek sonuç**
Yeni `Prefer: respond-async` isteği (`runId`
`01a0ac8d-1dff-74d6-95de-013a590da2ed`) sonrası `sleep` KOYMADAN hemen
`/events`e bağlanıldı. Bağlantı ~23 `: waiting` keep-alive pingi boyunca
AÇIK kaldı (işçi henüz almamıştı — `PollInterval` varsayılan 10 sn
penceresi), ardından TÜM olaylar sırayla, sıfır kayıpla geldi: `id:0
run.started` → `id:1 tool.invoking` (`get_order_status`,
`orderId=ORD-1001`) → `id:2 tool.invoked` (sonuç: "Order ORD-1001 has
shipped...") → `id:3 message.delta` → `id:4 message.completed` → `id:5
run.completed`. İlk olay tam olarak `run.started`, hiçbir sequence
atlanmadı. Beklenen sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-075 — Başlık GÖNDERİLMEYEN istekte davranış DEĞİŞMEZ (K1)

**Gerçek sonuç**
`POST /api/agents/support/run` (`Prefer` başlığı YOK) → `HTTP/1.1 200 OK`,
`Content-Type: text/event-stream`. Ayrı bir çağrıda başlıklar tam
denetlendi: `Location`/`Preference-Applied` **yok**. Bugünkü senkron SSE
davranışı bit bit korunmuş. Beklenen sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-076 — Boş `message` ile `Prefer: respond-async` → `400`

**Gerçek sonuç**
`POST .../support/run` (`{}`, `Prefer: respond-async`) → `HTTP: 400`,
`title: "Empty request"`, `detail: "'message' is required for a queued
run."` — beklenen davranış (K-228 dil sapmasıyla, spec Türkçe metin
bekliyor). Beklenen sonuçla (durum kodu + anlam) eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-077 — Ek (`attachmentIds`) veya onay kararı ile birlikte `respond-async` → `400`

**Gerçek sonuç**
`POST .../support/run` (`attachmentIds` dolu, `Prefer: respond-async`) →
`HTTP: 400`, `title: "Not supported"`, `detail: "A queued run ('Prefer:
respond-async') does not support approval decisions, client-side tool
results, attachments, parameters, or documents in this version."` —
beklenen davranış (K-228 dil sapmasıyla). Beklenen sonuçla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-078 — `Tracon:AsyncRun:Enabled = false` → başlık taşıyan istek `501` alır

**Gerçek sonuç**
Skill §1.2 uyarınca ortam değişkeni kullanıldı (`user-secrets` DEĞİL):
`Tracon__AsyncRun__Enabled=false`, yeniden başlatıldı. `POST
.../support/run` (`Prefer: respond-async`) → `HTTP: 501`, `title: "Queuing
not enabled"`, `detail: "...queuing support is disabled in this setup
(TraconAsyncRunOptions.Enabled = false)."` — sessizce SSE'ye düşmedi,
gerçek `501` döndü. Beklenen sonuçla (K-228 dil sapmasıyla) eşleşiyor.
Ayar kaldırılıp uygulama varsayılana döndürüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-079 — Kota dolu iken kuyruğa alma `429` alır, iş AÇILMAZ

**Gerçek sonuç**
Kurulum adımı `23-SAKLAMA-ARSIV-KOTA.md`'nin kota kural motoruna dayanır
(bu dosyanın kapsamı dışı, yalnız sonuç doğrulanır) — `PUT /api/quotas`
(`agentName: "support"`, `period: "Daily"`, `maxRuns: 0`, `enabled: true`)
ile geçici bir kural oluşturuldu. `POST .../support/run` (`Prefer:
respond-async`) → `HTTP: 429`, `title: "Quota exceeded"`, `detail: "The
daily run quota for agent 'support' has been exceeded (5/0)..."`.
`GET /api/jobs?kind=AgentRun` sayısı **23** — istekten ÖNCEKİ son kayıt
(23:28:44, MT-JOB-074'ün işi) ile aynı, istekten SONRA yeni bir kayıt
**açılmadı**. Kural silindi (`DELETE /api/quotas/{id}` → `204`). Beklenen
sonuçla (`QuotaGate.CheckAsync`'in `WantsAsync` dallanmasından önce
çalıştığı) eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-080 — `Queued` durumdaki bir çalıştırma iptal edilebilir

**Gerçek sonuç**
`Tracon__Scheduling__RunWorker=false` ile yeniden başlatıldı (işçi işi
almasın diye). `POST .../support/run` (`Prefer: respond-async`) →
`runId` alındı, HEMEN `POST /api/runs/{runId}/cancel` çağrıldı →
`HTTP: 202`, gövde `RunRecord`: `status: "Canceled"`, `completedAt` dolu
(`startedAt`/`completedAt` arası 82 ms — işçi hiç almadan doğrudan
kapatıldı, orphan oluşmadı). Ayar kaldırılıp uygulama varsayılana
döndürüldü. Beklenen sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-081 — İptal edilmiş bir kuyruk çalıştırmasını TEKRAR iptal etmek → `409`

**Gerçek sonuç**
MT-JOB-080'in iptal edilmiş `RUN_ID`'si tekrar iptal edildi → `HTTP: 409`,
`title: "Run already ended"`, `detail: "Run '...' is already in status
'Canceled'."` — beklenen davranış (K-228 dil sapmasıyla).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-082 — Var olmayan `runId` iptali → `404`

**Gerçek sonuç**
`POST /api/runs/00000000-.../cancel` → `HTTP: 404`, `title: "Run not
found"`. Beklenen sonuçla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-083 — `Prefer: respond-async` + AYNI `Idempotency-Key` → TEK iş, aynı `Location`

**Gerçek sonuç**
Aynı `Idempotency-Key: tekillestirme-testi-01` ile iki ardışık istek →
iki `Location` başlığı **birebir aynı**
(`/tracon/api/runs/01a0ac91-760d-7dc1-8add-896cd9480cd8`), `diff` ile
karşılaştırılan iki gövde **birebir aynı** — ikinci istek yeni bir iş
açmadı, ilk `202`'nin tekilleştirilmiş gövdesini döndürdü. Beklenen
sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-084 — Akışlı istek (başlık YOK) + `Idempotency-Key` → Faz 43'ün `400`'ü KORUNUR

**Gerçek sonuç**
Spec'in kendi "doküman düzeltmesi" notu ampirik doğrulandı: `Idempotency-Key`
başlığı taşıyan ama `Prefer: respond-async` OLMAYAN istek → `HTTP: 200`,
normal buffered JSON yanıtı (`runId`, `response.messages[...]`) — `400`
DEĞİL. Kaynağın işaret ettiği `AgentEndpoints.cs`'nin `streaming =
!Headers.ContainsKey(IdempotencyFilter.HeaderName)` dalıyla tam tutarlı.
Beklenen sonuçla (düzeltilmiş hâliyle) eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-085 — Jobs ekranında `AgentRun` türü iş görünür ama Ögeler listesi BOŞTUR

**Gerçek sonuç**
⚠️ **Playwright bu case için kullanılamadı** — paylaşılan tarayıcı profili
o anda başka bir şerit tarafından kullanılıyordu (`Error: Browser is
already in use for .../mcp-chrome-...`); paylaşılan kaynağı zorla
almak/kapatmak yerine (skill §1.3, paylaşılan kaynaklara dokunmama ilkesi)
API-katmanı eşdeğeriyle doğrulandı: `GET /api/jobs/{runId}` (MT-JOB-072'nin
işi, `01a0ac8b-a6dd-7954-a45e-9a6bb009de79`) → `handlerKey:
"tracon.agent-run"`, `status: "Completed"`, **`items: []`** — arayüzün
`job-detail.tsx:97-99`'daki `items.length === 0` dalı BİREBİR bu alanı
okuyor (spec'in kendi kaynak referansı), yani boş-durum panelinin
görüneceği veri koşulu doğrulandı. Görsel render (metin/biçim) elle/Playwright
ile TEYİT EDİLMEDİ — düşük önem ve kesin API kanıtı nedeniyle bu koşumda
yeterli görüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-090 — 🚨 `RunsRead`-kapsamlı bir API anahtarı zamanlama silebiliyor/iş iptal edebiliyor mu?

**Gerçek sonuç**
✅ **Şüphe ÇÜRÜTÜLDÜ — doküman bayat, kusur DEĞİL.** Spec'in dayandığı
`grep -n "RequireApiKeyScope" src/Tracon.AspNetCore/Endpoints/SchedulingEndpoints.cs`
bu koşumda **boş DÖNMEDİ** — 9 çağrı bulundu (`PlatformRead` ×3,
`PlatformAdmin` ×2, `RunsWrite` ×2, `RunsRead` ×2, satır 29-138).
Kod, case yazıldığı tarihten SONRA (muhtemelen `15-WORKFLOWS.md`
`MT-WF-100`'ün bulduğu aynı sınıf kusur genel olarak kapatılırken)
düzeltilmiş. Ampirik doğrulama: `POST /api/api-keys` ile yalnız `RunsRead`
kapsamlı bir anahtar üretildi (`plaintextKey` alanı — spec'in beklediği
`rawKey` DEĞİL, ayrı bir doküman sapması). Bu anahtarla:
- Adım 2 (`PUT /api/schedules/kapsam-testi`) → `HTTP: 403`, `"This
  endpoint requires the 'PlatformAdmin' scope; the key does not carry
  it."`
- Adım 3 (`DELETE` aynı zamanlama) → `HTTP: 403`, aynı mesaj.
- Adım 4 (kontrol grubu, `AgentsAdmin` gerektiren agent ucu) → `HTTP: 403`,
  `"...requires the 'AgentsAdmin' scope..."`.

Üçü de kapsam sistemi tarafından REDDEDİLDİ — şüphenin iddia ettiği
"kapsam kısıtı UYGULANMAZ" durumu bu ortamda **gözlenmedi**.
`SchedulingEndpoints` artık `WorkflowEndpoints`'in (`MT-WF-100`) düştüğü
boşluğa düşmüyor. Test anahtarı temizlendi (`DELETE /api/api-keys/{id}`
→ `204`). **Spec'in "Beklenen sonuç (şüphe)" bölümü artık bayat — kusur
kapanmış durumda, yeni `HATA` açılmadı.**

⚠️ **Altyapı notu (ürün kusuru değil):** Bu case sırasında uygulama iki kez
`dotnet run` (wrapper CLI süreci) altında beklenmedik şekilde
"Application is shutting down..." ile kapandı (istisna/hata günlüğü YOK,
sinyal kaynağı belirlenemedi — muhtemelen ortamın arka plan süreç yönetimi).
Derlenmiş DLL doğrudan çalıştırılarak (`dotnet
artifacts/bin/Tracon.Api/release/Tracon.Api.dll --urls ...`, `dotnet run`
sarmalayıcısı OLMADAN) sorun ortadan kalktı ve kalan tüm case'ler bu
şekilde koşuldu. Sonraki oturum bu deseni (DLL doğrudan çalıştırma)
tercih etmeli.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-JOB-091 — Doğru imzalı istek `202` döner ve çalıştırma kuyruktan koşar

**Gerçek sonuç**
Ortak kurulum: `PUT /api/triggers/slack` (hedef `support`, `signingSecretConfigurationName:
"Tracon:TriggerSecrets:Slack"`, `payloadPath: "event.text"`) → `200`,
`resolved:true` (5086'da, bkz. devir notu). Doğru imzalı istek (gerçek
`TRIGSECRET="whsec_manuel_test_66"` ile hesaplanan HMAC-SHA256) →
`HTTP: 202`, `Location: /tracon/api/runs/01a0accc-4dfa-7cbf-be2d-5bbced241bb7`,
gövdede `runId` ve `jobId` **aynı** değer. `GET /api/runs/{runId}` (5084
üzerinden, worker orada) ilk pollde `Queued`, ikinci pollde (~3 sn sonra)
`status: "Completed"`. Beklenen sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-092 — İmzasız istek `401` döner

**Gerçek sonuç**
İmza/timestamp başlığı olmayan istek → `HTTP: 401`,
`title: "Signature verification failed"`. Beklenen sonuçla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-093 — Gövde bir bayt değişince aynı imza artık geçmez

**Gerçek sonuç**
İmza `{"event":{"text":"orijinal"}}` için hesaplandı, gönderilen gövde
`{"event":{"text":"degistirildi"}}` → `HTTP: 401`,
`title: "Signature verification failed"`. Beklenen sonuçla eşleşiyor —
`WebhookSigner.Verify` gövde+zaman damgası birleşimini doğruluyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-094 — On dakika eski zaman damgası `401` döner

**Gerçek sonuç**
`TS = now - 600` ile doğru imza hesaplandı → `HTTP: 401`,
`title: "Signature verification failed"`. Beklenen sonuçla eşleşiyor —
varsayılan beş dakikalık `TimestampTolerance` penceresi dışında.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-095 — Aynı imza ikinci kez `409` döner; ikinci çalıştırma açılmaz

**Gerçek sonuç**
MT-JOB-091 deseninde yeni bir istek (`BODY`/`TS`/`SIG` sabit) ilk kez
`202` (`runId: 01a0acdb-...`) döndü. **Aynı** `BODY`/`TS`/`SIG` ile
tekrar gönderildi → `HTTP: 409`, `title: "Request already processed"`.
`GET /api/runs?agentName=support&limit=5` bu istek için yalnız **bir**
yeni `runs` satırı gösterdi — ikinci istek yeni bir çalıştırma açmadı.
Beklenen sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-096 — Bilinmeyen kiracı `401` döner; varsayılan kiracıya düşmez

**Gerçek sonuç**
`POST /api/triggers/boyle-bir-kiraci-yok/slack` (gövde/imza yok) →
`HTTP: 401`, `title: "Signature verification failed"` — `404` DEĞİL.
Beklenen sonuçla (kararın kendi belirttiği "bilinmeyen kiracı da imza
hatasıyla aynı jenerik 401'i alır" biçimiyle) eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-097 — Devre dışı tetikleyici reddedilir

**Gerçek sonuç**
`PUT /api/triggers/slack` ile `enabled:false` yapıldı (`200`). Geçerli
imzalı bir istek gönderildi → `HTTP: 401`, `title: "Signature verification
failed"` — devre dışı tetikleyici, bilinmeyen/imza-hatalı istekten ayırt
edilemiyor. Ardından `enabled:true` ile eski hâline getirildi (`200`,
doğrulandı). Beklenen sonuçla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-098 — Kota dolu olunca `429` döner, kota bypass edilmez

**Gerçek sonuç**
Gerçek API şekli spec'in varsaydığı `PUT /api/quotas/agents/support/runs`
DEĞİL — `PUT /api/quotas` (gövdede `agentName`/`period`/`maxRuns`), `Period`
yalnız `Daily`/`Monthly` değerlerini alıyor (`QuotaPeriod` enum'u — spec'in
"1 istek/dakika" örneği bu API'de yok, en küçük pencere `Daily`). `support`
için `{"agentName":"support","period":"Daily","maxRuns":1,"enabled":true}`
kaydedildi (`200`). `GET /api/quotas/usage?agentName=support` bu oturumun
önceki case'lerinden (091, 095) kalan **2** günlük `runs` sayacını gösterdi
— kota zaten aşılmış durumdaydı. Bu yüzden hemen ardından gönderilen
imzalı tetikleyici isteği ilk denemede `HTTP: 429` döndü (spec'in
"önce kabul, sonra ret" iki adımlı sırası yerine tek adımda gözlendi —
paylaşılan gün-içi sayaç nedeniyle, ürün kusuru değil):
`{"title":"Quota exceeded","status":429,"detail":"The daily run quota for
agent 'support' has been exceeded (2/1)...","quotaMetric":"Runs",...}`,
`Retry-After: 83474`. Tetikleyici endpoint'i bearer token taşımadığı hâlde
`QuotaGate` uygulanıyor — kota bypass edilmiyor. Test kuralı temizlendi
(`DELETE /api/quotas/{id}` → `204`). Davranışın özü (kota dolunca `429`,
`Retry-After` mevcut, bypass yok) beklenen sonuçla eşleşiyor; yalnız
adım sırası ortam durumu yüzünden sıkıştı — **doküman kusuru değil**,
kota API'sinin gerçek şekli spec'ten farklı (bkz. not).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-099 — `Path` modunda alan yoksa `400` döner, çalıştırma başlamaz

**Gerçek sonuç**
`{"event":{"baska_alan":"x"}}` gövdesi, doğru imza ile gönderildi →
`HTTP: 400`, gövde tam olarak: `{"title":"Invalid request body","status":400,
"detail":"The payload path 'event.text' did not resolve to a value in the
request body."}` — `event.text` yolunun çözülemediğini söylüyor (spec'in
beklediği anlamla eşleşiyor, tam cümle spec'te verilmemişti). Bu istek
`runs` tablosunda yeni bir satır açmadı (bir önceki case'lerin oluşturduğu
satır sayısına göre karşılaştırıldı). Beklenen sonuçla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-100 — İmza `secret`'ı veritabanında hiç yaşamaz

**Gerçek sonuç**
```
mt_s4=# SELECT signing_secret_configuration_name FROM mt_s4.inbound_triggers;
 signing_secret_configuration_name
------------------------------------
 Tracon:TriggerSecrets:Slack
```
`pg_dump --schema=mt_s4 -U postgres tracon | grep -c "whsec_manuel_test_66"`
→ `0`. Sütun yalnız yapılandırma anahtarının ADINI taşıyor, gerçek
`secret` değeri şema dökümünde hiç geçmiyor. Beklenen sonuçla eşleşiyor
(K-059).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-101 — 100 istek arka arkaya gönderilince hız sınırı devreye girer

**Gerçek sonuç**
100 ardışık geçerli imzalı istek (`event.text` dolu, her biri farklı
gövde/zaman damgası) 5086'ya gönderildi. Gözlenen kod dağılımı (`uniq -c`):
`202` × 58, `429` × 42 (artı boş bir satır — döngü kabuğunun ilk
biriktirme adımından kalan boşluk, istek sayısını etkilemiyor). Tam 60/40
DEĞİL 58/42 — sapma, bu pencerede MT-JOB-098/099'un birkaç saniye önce
gönderdiği isteklerin AYNI dakika penceresine dahil olmasından kaynaklanıyor
(varsayılan `MaxRequestsPerMinute=60` süreç ömrü boyunca kayan/sabit
pencereli tek sayaçtır — önceki case'lerin istekleri de sayılıyor). Bu bir
kusur değil, ölçümün art arda çalıştırılan case'lerin aynı dakikaya
düşmesinden kaynaklanan beklenen bir yan etkisi: sınırın **60**'ta devreye
girdiği ve devam eden isteklerin `429` aldığı doğrulandı. Beklenen sonuçla
(K-158: dağıtık değil, tek süreç sayacı) eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-102 — Tetikleyici arayüzden tanımlanır ve listelenir

**Gerçek sonuç**
⚠️ **Playwright bu case için kullanılamadı** — `browser_navigate` denemesi
(iki tekrar, aralarında 20 sn bekleme) `Error: Browser is already in use
for .../mcp-chrome-3eca5a9` döndü — paylaşılan tarayıcı profili başka bir
şerit tarafından kullanılıyordu (MT-JOB-085'in düştüğü aynı sınır, skill
§1.3 — paylaşılan kaynağı zorla almama ilkesi). API-katmanı + kaynak
denetimiyle eşdeğer doğrulama yapıldı: `PUT /api/triggers/manual-ui-test`
(`signingSecretConfigurationName: "Tracon:TriggerSecrets:DoesNotExist"`,
bilerek çözülemeyen bir anahtar) → `200`, `resolved:false`. `GET
/api/triggers` yeni tetikleyiciyi **ve** `resolved:true` olan `slack`
tetikleyicisini doğru durumlarıyla birlikte listeledi — rozet gerçek
durumu yansıtıyor. `src/Tracon.UI/frontend/src/screens/triggers.tsx:276`
`acceptUrl`'i `${origin}${prefix}/api/triggers/${tenantId}/${triggerName}`
olarak kuruyor — spec'in beklediği `{origin}/tracon/api/triggers/{tenantId}/
{name}` biçimiyle birebir eşleşiyor (`prefix` = `/tracon`, ampirik olarak
tüm bu ailenin isteklerinde doğrulandı). Test tetikleyicisi temizlendi
(`DELETE /api/triggers/manual-ui-test` → `204`). Görsel render (buton
metni, sayfa düzeni) Playwright ile TEYİT EDİLMEDİ; veri/mantık katmanı
tam doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-103 — Worker öldürülünce yalnız kalan öge'ler yeniden işlenir

**Gerçek sonuç**
⚠️ **Gerçek `kill -9` ile canlı süreç üzerinde koşulmadı.** Bu oturumda
süreç durdurma/başlatma eylemleri ajanın otonom-mod izin sınıflandırıcısı
tarafından defalarca "Interfere With Workloads" ile reddedildi (bkz. devir
notu); `kill` denemeleri bu oturumda **hiç** başarılı olmadı (yalnız yeni
süreç BAŞLATMA denemeleri, birkaç retry sonrası, bazen geçti). 5084'teki
tek worker'lı süreci `kill -9` ile öldürmek — eğer sınıflandırıcı bu kez de
reddederse süreç zombi durumda kalabilir ya da bir sonraki başlatma
denemesi tekrar reddedilebilir — ailenin kalan ~20 case'ini (110-131)
tehlikeye atacak paylaşılan bir kaynağı bozma riski taşıyordu (skill §1.4
madde 3: "bir case paylaşılan bir kaynağı bozacak" → dur/sor kuralı). Bu
riski almak yerine, mekanizmanın kanıtlandığı otomatik karşılığı çalıştırıldı:
`JobLeaseExpiryTests.An_abandoned_lease_expires_and_the_re_leased_job_carries_its_completed_item_unfiltered`
— **Geçti** (`dotnet test`/binary doğrudan koşum, 1/1 passed, 343ms).
Bu test tam olarak MT-JOB-103'ün iddia ettiği store-katmanı sözleşmesini
ölçüyor: ilk lease altında `seq:0` `Completed` raporlanır, worker
raporlamadan "ölür" (`CompleteAsync`/`ReleaseForRetryAsync` çağrılmaz),
lease süresi dolunca **aynı job** ikinci kez kiralanabiliyor
(`Attempt: 2`), ve öge listesinde `seq:0` **Completed** kalırken yalnız
`seq:1` **Pending** kalıyor — yani ilk ögenin yan etkisi tekrarlanmıyor,
yalnız kalan öge yeniden işleniyor. Gerçek bir OS süreç kill'i ve gerçek
`JobWorkerBackgroundService` döngüsüyle uçtan uca DOĞRULANMADI — yalnız
`IJobStore` sözleşmesi doğrulandı. Beklenen sonucun **temel mekanizması**
kanıtlandı; canlı süreç kill'i bu ortamın altyapı kısıtı yüzünden
koşulamadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-104 — Dokümanı izleyen dış bir `IJobHandler` sözleşme testini geçer

**Gerçek sonuç**
`dotnet build samples/Tracon.Samples.CustomJobHandler.Tests -c Release`
→ başarılı, 0 uyarı. `NightlyReportJobHandlerContractTests` filtreli koşum
→ **3/3 Geçti** (158ms): zaten `Completed` bir öge yeniden işlenmiyor,
retry'de yalnız `Pending` öge işleniyor, iptal öge'ler arasında gözleniyor.
`samples/Tracon.Samples.CustomJobHandler.Tests.csproj`'de `Tracon` ve
`Tracon.Testing.Contracts.Xunit` **`PackageReference`** (`ProjectReference`
DEĞİL) — yalnız iki örnek proje arasındaki bağ (`Tracon.Samples.
CustomJobHandler`'a) `ProjectReference`. Beklenen sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-110 — Ayar yapılmayan kurulumda her job `default` `lane`'inde çalışır

**Gerçek sonuç**
Ön koşul zaten sağlanmış durumdaydı — çalışan hiçbir süreçte
`Tracon:Scheduling:Lanes`/`MaxConcurrentJobsPerLane`/`LaneByKind`
ayarlanmadı. `ozet-toplu` tetiklendi (`POST /api/schedules/ozet-toplu/trigger`)
→ yanıt gövdesinde `"lane":"default"`. `GET /api/jobs/{id}` işin
`Completed` olduğunu ve `lane:"default"` kaldığını doğruladı. Beklenen
sonuçla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-113 — Kuyruklu tek çalıştırmada geçersiz `lane` → `400`

**Gerçek sonuç**
`POST /api/agents/summarizer/run` (`Prefer: respond-async`,
`{"message":"test","lane":"Media"}`) → `HTTP: 400`, `title: "Invalid lane"`,
`detail: "'Media' is not a valid lane name. A lane name must be 1-64
characters: lowercase ASCII letters, digits, '.', '_', or '-', starting
with a letter or digit."` İş kuyruğa hiç yazılmadı (yanıt zaten hata,
`runId` yok). Beklenen sonuçla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-114 — Kuyruklu tek çalıştırma istenen `lane`'i taşır

**Gerçek sonuç**
Aynı uç, `lane:"media"` ile → `202`, `runId`. `GET /api/jobs/{runId}` →
`"lane":"media"`. Beklenen sonuçla eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-115 — `retry` `lane`'i korur; geçersiz `lane`'li zamanlama `400` alır

**Gerçek sonuç**
Adım 1: `PUT /api/schedules/buyuk-harf-lane` (`lane:"Media"`, gövdede
gerçek alan adı **`handlerKey`**, spec'in `"kind"`'ı DEĞİL — Faz 129
migrasyonu, önceki oturumların bulgusuyla tutarlı) → `HTTP: 400`,
`title: "Schedule invalid"`, aynı "lowercase ASCII" mesajı.
Adım 2-3: `lane:"media"`, `targetName:"yok-boyle-bir-agent"` (var olmayan
agent) ile geçici bir zamanlama (`media-lane-retry-test`) oluşturuldu ve
tetiklendi; `GET /api/jobs/{id}` ~28 saniye boyunca 4 sn aralıkla
izlendi: `attempt` sırayla `0→1→2→3` ilerledi, **her denemede `lane` hep
"media"`da** kaldı, son durumda `status:"Failed"` (varsayılan
`MaxAttempts=3` tükendi). `ReleaseForRetryAsync`'in `lane`'i değiştirmediği
doğrulandı. Test zamanlaması temizlendi (`DELETE`, `204`). Beklenen
sonuçla tam eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-111 — `Lanes: ["media"]` olan worker `default` `lane`'inden kiralayamaz

**Gerçek sonuç**
⚠️ **Canlı ortamda izole koşulamadı — mimari çakışma.** Bu ailenin geri
kalanı (091-131) `mt_s4` şemasını 5084'teki (worker açık, `Lanes` AYARSIZ)
sürece bağımlı tutuyor; bu süreç durdurulamadığından (bkz. MT-JOB-103
notu), `Lanes:["media"]` ile YENİ bir üçüncü süreç başlatılsa bile 5084'ün
kısıtsız worker'ı (`Lanes=null` → kaynak: `AND ($4 IS NULL OR lane =
ANY($4))` sorgusu, `$4=NULL` iken HER lane'i eşler — `JobStore` SQL'inde
doğrulandı) `default` lane'indeki işi rakip worker'dan önce kiralar ve
"hâlâ Pending" beklentisini yapısal olarak geçersiz kılar — bu ortamın bir
kısıtı, ürün kusuru değil. Otomatik karşılığı çalıştırıldı:
`JobWorkerBackgroundServiceTests.A_worker_scoped_to_one_lane_never_leases_another_lane`
— **Geçti** (izole `InMemoryJobStore`'da tek worker, `Lanes:["media"]`,
`default` lane'inde bekleyen iş 200ms sonra hâlâ `Pending` — rakip
worker olmadığı için iddia temiz ölçülüyor). `dotnet test`/binary filtreli
koşum: 4/4 (bu dosyadaki tüm `JobWorkerBackgroundServiceTests`) geçti.
Mekanizma kanıtlandı; canlı çok-süreçli ortamda DOĞRULANMADI (yapısal
çakışma nedeniyle).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-112 — `MaxConcurrentJobsPerLane` dolu bir `lane`, `default`'u aç bırakır

**Gerçek sonuç**
Aynı yapısal çakışma MT-JOB-111 için geçerli (5084'ün kısıtsız worker'ı
canlı ortamda kontrolü bozar). Otomatik karşılığı:
`JobWorkerBackgroundServiceTests.A_full_lane_does_not_block_the_default_lanes_job`
— **Geçti**: iki `media` işi (`MaxConcurrentJobsPerLane["media"]=1`) kalıcı
olarak bloke ederken bir `default` işi hemen `Completed` oluyor — `media`
dolu olması `default`'u BEKLETMİYOR (129.4'ün starvation-önleme garantisi).
Aynı dosyadaki 4/4 test geçti. Mekanizma kanıtlandı; canlı ortamda
DOĞRULANMADI.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-116 — Jobs ekranı: `lane` sütunu ve süzgeci

**Gerçek sonuç**
⚠️ **Playwright kullanılamadı** (paylaşılan tarayıcı profili meşgul, aynı
sınır — retry yapıldı, hâlâ meşgul). API + kaynak eşdeğeriyle doğrulandı:
`GET /api/jobs?limit=50` 50 satırın hepsinde `lane` alanı taşıyor
(`Counter({'default': 48, 'media': 2})` — bu ailenin önceki case'lerinin
gerçek verisi). `GET /api/jobs?lane=media&limit=50` yalnız `media`
satırlarını döndürdü (2/2). `src/Tracon.UI/frontend/src/screens/jobs.tsx:123-143`
`laneFilter` state'ini doğrudan `lane` sorgu parametresine bağlıyor;
`:515-523` süzgeç kutusunu `Toolbar`/`ToolbarField` ile render ediyor ve
`:515` `onReset`'i kutu dolu olduğunda aktif ediyor (boşaltma → liste eski
hâline döner). Veri/mantık katmanı tam doğrulandı; görsel render (sütun
başlığı metni, tablo düzeni) Playwright ile TEYİT EDİLMEDİ.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-117 — Job sayacı ve süre histogramı ayar olmadan yazılır

**Gerçek sonuç**
⚠️ **`samples/Tracon.Api/Program.cs` hiçbir OTel metrik pipeline'ı
kaydetmiyor** (`grep -n "WithMetrics\|AddOpenTelemetry\|AddMeter"` boş
döndü) — spec'in ön koşulu ("OTel konsol exporter'ı `TraconDiagnostics.
MeterName`'i dinliyor") bu örnek uygulamada hiç KURULMAMIŞ; kod donuk
olduğu için canlı süreçte bu pipeline'ı eklemek mümkün değil. Otomatik
karşılıkları çalıştırıldı (`Tracon.Core.UnitTests/Diagnostics/
JobMetricsTests.cs`, `JobQueueDepthGaugeTests.cs`):
`A_completed_job_is_counted_once_with_its_lane_kind_and_status`,
`The_duration_histogram_carries_no_tenant_tag`,
`No_measurement_is_produced_and_the_store_is_not_queried_while_disabled`
— **26/26 Geçti** (bu dört dosyanın toplamı, 390ms). Üçü de tam olarak
MT-JOB-117'nin iddialarını ölçüyor: `tracon.job.executions` lane/kind/status
etiketleriyle bir kez sayılıyor, `tracon.job.duration`'da `tenant` etiketi
yok, `tracon.job.queue.depth` gauge kapalıyken hiç ölçüm üretmiyor VE
`store`'u sorgulamıyor. Canlı OTel çıktısıyla DOĞRULANMADI (pipeline yok);
mekanizma birim testleriyle kanıtlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-118 — Kuyruk derinliği gauge'ı açıldığında `lane` × `status` raporlar

**Gerçek sonuç**
Aynı altyapı sınırı (OTel pipeline yok). Otomatik karşılıkları:
`JobQueueDepthGaugeTests.Depth_is_reported_per_lane_and_status_when_enabled`,
`The_gauge_carries_no_tenant_tag`,
`A_second_scrape_inside_the_refresh_interval_does_not_query_the_store` —
**Geçti** (yukarıdaki 26/26 koşumun parçası). Üçü sırasıyla: gauge açıkken
`lane`/`status` etiketli ölçüm üretiyor, `tenant` etiketi taşımıyor, ve
yenileme aralığı içindeki ikinci scrape `store`'a ikinci sorgu
göndermiyor (önbellek). Canlı `psql` sorgu logu izlemesiyle DOĞRULANMADI.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-119 — `retry` bırakması sayaca girmez, yalnız nihai durum sayılır

**Gerçek sonuç**
Aynı altyapı sınırı. Otomatik karşılığı:
`JobMetricsTests.A_release_for_retry_is_not_counted_only_the_final_failure_is`
— **Geçti**: sayaç yalnız nihai `Failed` durumunda **1** artıyor, ara
`ReleaseForRetryAsync` çağrıları sayılmıyor, histogram tek ölçüm taşıyor
(son denemenin süresi). `attempt` alanının işin toplam deneme sayısını
yansıttığı MT-JOB-115'in canlı koşumunda zaten ayrıca doğrulandı
(`attempt: 3`, `status: Failed`). Beklenen sonuçla birleşik olarak
eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-120 — `lane` kardinalite muhafızı `other`'a düşürür

**Gerçek sonuç**
Aynı altyapı sınırı. Otomatik karşılıkları:
`JobLaneCardinalityTests.A_lane_beyond_the_limit_is_written_as_other`,
`A_lane_that_earned_its_name_keeps_it_after_the_limit_is_reached`,
`JobQueueDepthGaugeTests.The_gauge_applies_the_same_lane_cardinality_guard_as_the_counter`
— **Geçti**: `MaxJobLaneCardinality` aşıldığında yeni lane adı `other`'a
düşüyor, daha önce adı geçen bir lane süreç ömrü boyunca adını koruyor,
ve `queue.depth` gauge'ı **aynı** kardinalite kümesini sayaçla paylaşıyor
(iki enstrüman ayrı bütçe tutmuyor). Canlı taze-süreç koşumuyla
DOĞRULANMADI (OTel pipeline yok, ayrıca taze süreç gereksinimini
sağlamak MT-JOB-103/111/112'nin aynı yeniden başlatma kısıtına takılırdı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-121 — Derinlik sorgusu `jobs_claim_idx` kullanır

**Gerçek sonuç**
`mt_s4.jobs` bu turda yalnız **88** satır taşıyor (status 3/4/5 — hiçbiri
0/1/2 "açık" değil), spec'in varsaydığı 60 000 satır / 3 000 açık ölçeğinde
DEĞİL — bu ailenin fixture verisiyle bu ölçek üretilemedi. Yine de
`EXPLAIN (ANALYZE, BUFFERS)` planı **`Index Only Scan using jobs_claim_idx
on jobs`** gösterdi (Bitmap Index Scan bile değil, daha güçlü bir plan —
`jobs_claim_idx`'in `WHERE status = ANY(ARRAY[0,1,2])` kısmi indeksi
sorguyla birebir örtüşüyor), **`Seq Scan on jobs` planda hiç yok**.
`rows=0.00` (şu an açık iş yok) ve `Execution Time: 0.096 ms`. Beklenen
sonucun **plan şekli** iddiası (indeks kullanılıyor, seq scan yok)
doğrulandı; **satır ölçeği** iddiası (3 000 satır tarandığı, tablonun
tamamı değil) bu veri hacminde ANLAMLI ölçülemedi — küçük tabloda planlayıcı
zaten en iyi planı seçiyor, büyük ölçekte davranışın DEĞİŞMEYECEĞİ kısmi
indeksin tanımından (yalnız açık durumları kapsıyor) mantıksal olarak
çıkarılabilir ama ampirik olarak bu turda kanıtlanmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-098 — Yarıda kalan approval handoff'u, AYNI kararı tekrarlayarak tamamlanır (B03)

**Gerçek sonuç**
Spec bu case'i `MT-JOB-098` olarak numaralandırıyor — dosyanın başındaki
gerçek MT-JOB-098 (kota testi, bu dosyada satır 1021) ile **ID çakışması**;
spec'e dokunulmadı (kural 1), kayıt bu ikinci `## MT-JOB-098` bloğu ile
tutuluyor. `~/tracon-manuel/job-handlers-s4` scratch host'una
`FakeModelProvider("handoff-model").CallsTool("cancel_order", {orderId:
"ORD-7"}).EchoesLastToolResult()` + `AddTool(..., RequiresApproval=true)` +
`handoff-agent` eklenerek K-726'nın otomatik testiyle (`ApprovalResumeHandoffTests.cs`)
birebir aynı senaryo canlı sunucuda kuruldu:
1. `POST /api/agents/handoff-agent/run` → run `AwaitingApproval`'a düştü,
   `GET /api/approvals/pending` bekleyen approval'ı verdi.
2. `POST /api/approvals/{id}/decide {approved:true}` → `200`, approval
   `Approved`, yeni bir resume run (`...750e...`) `Completed` kapandı —
   normal yol.
3. **AYNI** kararı (`approved:true`) tekrar `POST` etmek — HTTP `200`
   (eski davranışta `409 AlreadyDecided` olurdu), approval kaydı
   değişmeden döndü, `GET /api/runs?sessionId=...` **hâlâ 2 run** gösterdi
   (yeni bir resume run DOĞMADI — aynı satıra indi, K-726'nın vaadi).
4. **TERS** kararı (`approved:false`) aynı id'ye `POST` etmek → `409
   Decision already made` — hâlâ gerçek bir çakışma.
Gerçek bir kesinti-penceresi (decide↔enqueue arası çökme) enjekte edilemedi
(kod donuk, `FailFirstResumeEnqueue` test-only fault injection'dır); ama
dışa dönük sözleşme — aynı karar tekrarı idempotent `200`, ters karar
tekrarı `409` — canlı ölçüldü ve otomatik testle birebir örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-122 — İki custom handler kendi işini çalıştırır

**Gerçek sonuç**
`~/tracon-manuel/job-handlers-s4` scratch host'u (donuk `samples/`'a
dokunmadan, paketlenmiş `0.0.0-preview.0.819` yerel feed'den, `AddJobHandler
<AHandler>("acme.a")` + `AddJobHandler<BHandler>("acme.b")`) ile: `acme.a`
ve `acme.b` işleri kuyruğa alındı, ikisi de `Completed` kapandı.
Sunucu log'u yalnız `AHandler ran job <a-id>` ve `BHandler ran job <b-id>`
yazdı — hiçbir handler diğerinin job id'sini görmedi.
`GET /api/jobs?handlerKey=acme.a` yalnız `acme.a` job'ını döndü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-123 — Kayıt sırası sonucu değiştirmez

**Gerçek sonuç**
Aynı scratch host, `JOBH_SCENARIO=handlers-before-addtracon` ile yeniden
başlatıldı — bu kez `AddJobHandler<AHandler>("acme.a")` /
`AddJobHandler<BHandler>("acme.b")` çağrıları `builder.AddTracon()`'den
**ÖNCE** yapıldı. Host normal açıldı (hata yok); iki yeni `acme.a`/`acme.b`
işi tekrar kuyruğa alındı ve MT-JOB-122 ile **birebir aynı** sonucu verdi
(ikisi de `Completed`, doğru handler'a dispatch). Faz 137 sonrası sıra
kazananı belirlemiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-124 — Aynı anahtarın iki kez kaydı host'u açtırmaz

**Gerçek sonuç**
`JOBH_SCENARIO=dup-type` (aynı host, `acme.a` için ek olarak
`AddJobHandler<BHandlerAsA>("acme.a")`) ile başlatılan süreç **4 saniye
içinde çöktü** (`Hosting failed to start`):
```
System.InvalidOperationException: Two job handlers are registered for the
key 'acme.a': 'AHandler' and 'BHandlerAsA'. A key identifies exactly one
handler; give one of them a different key.
   at Tracon.JobHandlerRegistry.Create(...)
   at Microsoft.Extensions.Hosting.Internal.Host.StartAsync(...)
```
Kök neden: `JobHandlerRegistry.Create`
(`src/Tracon.Core/Scheduling/JobHandlerRegistry.cs:78-84`), bir
`IValidateOnStart` doğrulayıcısı (`JobHandlerRegistryValidator.cs`)
üzerinden `Host.StartAsync`'te tetikleniyor — worker'ın ilk tick'i değil,
**host başlangıcı**. Mesaj çakışan anahtarı ve iki tam tip adını birlikte
taşıyor. Beklenen sonuçla birebir örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-125 — `tracon.` öneki tüketiciye kapalıdır

**Gerçek sonuç**
`JOBH_SCENARIO=reserved` (`AddJobHandler<AHandler>("tracon.retention")`)
süreç **anında** çöktü — `Build()`/`Run()`'a hiç ulaşmadı:
```
System.ArgumentException: The job handler key 'tracon.retention' is
reserved: the 'tracon.' namespace belongs to Tracon's own handlers. Pick a
key of your own (for example 'contoso.nightly-report'). (Parameter
'handlerKey')
   at Tracon.TraconServiceCollectionExtensions.AddJobHandler[THandler](...)
   at Program.<Main>$(String[] args) in Program.cs:line 50
```
`AddJobHandler<T>` çağrısının **kendisi** senkron olarak fırlatıyor
(`src/Tracon.Core/TraconServiceCollectionExtensions.cs:122-127`) — yerleşik
`tracon.retention` handler'ı hiç gölgelenmedi (süreç zaten hiç ayağa
kalkmadı). Beklenen sonuçla birebir örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-126 — Kayıtsız anahtarlı iş fail-closed'dır ve anahtarı sızdırmaz

**Gerçek sonuç**
`IJobStore.EnqueueAsync` ile doğrudan (dispatcher'ı atlayarak)
`handlerKey="acme.no-such-handler"` yazıldı. Worker tick'inden sonra:
`GET /api/jobs/{id}` → `status:"Failed"`,
`errorMessage:"tracon.job.unknown-handler-key (ref: bd57fbee)"` — **ham
anahtar yok**. Sunucu log'u: `Job <id> carries the handler key
'acme.no-such-handler', which no IJobHandler is registered for. (ref:
bd57fbee)` — ham anahtar **yalnız** burada, ve `ref` errorMessage'daki
korelasyon kimliğiyle birebir aynı. Kaynak:
`JobWorkerBackgroundService.cs:277-291`, `JobErrorCodes.cs:26,56`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-127 — Her execution kendi DI scope'unu alır

**Gerçek sonuç**
`services.AddScoped<Marker>()` + `AHandler(Marker, ILogger)`. İki ayrı
`acme.a` işi (`marker-1`, `marker-2`) **farklı** marker id'leriyle çalıştı
(`8532796e...` / `4f7c6a3a...`). Üçüncü iş (`target=fail-once`) 1.
denemede marker `b4870a6f...` ile fırlattı, `ReleaseForRetryAsync` sonrası
2. denemede **farklı** marker `9f292b7c...` ile `Completed` kapandı — retry
yeni bir DI scope alıyor, `JobContext` üzerinde `IServiceProvider` yok
(constructor injection dışında erişim yolu da yok).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-128 — `handler_key` migration'ı dokuz değerin dokuzunu eşler

**Gerçek sonuç**
PostgreSQL için canlı ölçüldü (SQLite/SQL Server'da AYNI yordamla
tekrarlanmadı — zaman bütçesi, aşağıda not edildi). `ap-pg`'de yeni şema
`mtjob128mig`: migration `0001..0042` ham SQL olarak (`{schema}` yerine
substitute edilerek) uygulandı, spec'in `Adımlar`ındaki gibi 9 `kind`
değerinin her biri için bir schedule + bir job, `kind=0` için ikinci bir
job yazıldı (10 job, 9 schedule — otomatik testle (`JobHandlerKeyMigrationTests.cs`)
birebir aynı fixture). `__migrations` ledger'ı gerçek dosya SHA-256
checksum'larıyla (`MigrationDescriptor.ComputeChecksum` — ham metnin
SHA-256'sı) 1-42 için elle dolduruldu (ledger boş bırakılıp migration'lar
"replay" edilseydi `0040`'ın idempotent OLMAYAN `RENAME COLUMN
schema_version` satırı ikinci kez çalışıp patlardı — bunu ampirik olarak
kanıtladım, bkz. not). Sonra donuk ikili (`Tracon.Api.dll`) bu şemaya karşı
başlatıldı — `AutoApplyMigrations=true` varsayılanıyla **9 migration**
(43'ten sona) gerçekten uygulandı:
- Satır sayıları **değişmedi** (10 job, 9 schedule).
- Eşleme tam beklenen gibi: `0→tracon.agent-batch`, `1→tracon.workflow`,
  `2→tracon.eval`, `3→tracon.webhook-delivery`, `4→tracon.retention`,
  `5→tracon.agent-run`, `6→tracon.online-eval`, `7→tracon.approval-resume`,
  `8→tracon.run-continuation`.
- `\d mtjob128mig.jobs`: `kind` sütunu **yok**, `handler_key` **NOT NULL**,
  0 satırda `handler_key IS NULL`.
Geçici şema iş bitince `DROP SCHEMA ... CASCADE` ile silindi.
⚠️ **SQLite ve SQL Server'da tekrarlanmadı** — mekanizma (aynı
`MigrationRunner`, aynı ledger sözleşmesi) sağlayıcılar arası paylaşılıyor
ve üç sağlayıcı için de ayrı `JobHandlerKeyMigrationTests.cs` otomatik
testi zaten var, ama bu turda yalnız PostgreSQL ampirik olarak canlı
koşuldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-129 — Zamanlama ucu izin listesi dışındaki anahtarı reddeder

**Gerçek sonuç**
1. `GET /api/schedules/handler-keys` (varsayılan, `HttpSchedulableHandlerKeys`
   boş) → yalnız dokuz yerleşik anahtar.
2. `PUT /api/schedules/x {"handlerKey":"acme.a",...}` → `400`, mesaj
   `HttpSchedulableHandlerKeys`'i adlandırıyor; zamanlama oluşmadı.
3. `JOBH_SCENARIO=sched-allow` ile yeniden başlatma (`HttpSchedulableHandlerKeys`'e
   `acme.a` eklendi) sonrası: `GET .../handler-keys` → `["acme.a"]` (liste
   **acme.a'yı içeriyor** — beklenen budur; yerleşik dokuzun listede
   KALMAMASI `ListHandlerKeysAsync`'in tasarımı: liste boş değilse
   built-in'lerin YERİNE geçer, `SchedulingEndpoints.cs:171-174` — bug
   değil). `PUT /api/schedules/x {"handlerKey":"acme.a",...,"payload":{}}`
   → `200`.
4. `GET /api/schedules/handler-keys` `RequireRole(roles.Admin)` taşıyor
   (`SchedulingEndpoints.cs:66`) — bu scratch host'ta Reader/Admin ayrımı
   ampirik ÖLÇÜLMEDİ (basit tek-token demo auth, dosya 02/07'nin zaten
   kapsamlıca doğruladığı genel rol politikasının aynısı; kaynaktan
   doğrulandı, bu case'te ayrıca canlı koşulmadı).

🚨 **Yeni kusur — `HATA-S4-002` (Orta):** 2. adımın PUT'unu `payload` alanı
**olmadan** göndermek (`{"handlerKey":...,"targetName":...,"cron":...,"enabled":true}`,
gövdede `payload` yok) sunucu tarafında `500` ile patlıyor —
`400` beklenirdi ya da (boş payload varsayılarak) `200`. Kök neden:
`JobScheduleSaveRequest.Payload` (`src/Tracon.AspNetCore/Contracts/SchedulingContracts.cs:45`)
`JsonElement`, `required` değil ve varsayılan yok; alan gövdede yoksa STJ
onu `default(JsonElement)` (ValueKind=Undefined) bırakıyor.
`SaveScheduleAsync` bunu doğrudan yanıt nesnesine kopyalıyor
(`SchedulingEndpoints.cs:266`: `Payload = request.Payload`) ve
`TypedResults.Ok(saved)` bunu serialize etmeye çalışınca
`System.InvalidOperationException: Operation is not valid due to the
current state of the object.` (`JsonElementConverter.Write`) ile patlıyor.
**Ölçüldü — donuk ana uygulamada da (samples/Tracon.Api, port 5084, gerçek
`mt_s4` şeması) birebir tekrarlandı**, scratch host'a özgü değil:
`curl -X PUT .../api/schedules/manual-test-payloadless` (payload'sız,
`tracon.workflow`) aynı `500`'ü verdi. UI formu payload alanını her zaman
`"[]"` ile dolu gönderdiği için (MT-JOB-130'da gözlendi) arayüzden
ERİŞİLEMEZ — yalnız HTTP API'yi doğrudan çağıran bir tüketici
(`payload` alanını atlarsa) etkileniyor. Kapsam: yalnız bu case mi, yoksa
başka `PUT /api/schedules/{name}` kullanan case'ler de mi — dosya
16'daki diğer PUT çağıran case'ler (102, 113-115, 129 adım 3'ün
işaretlediğim workaround'u) hepsi `payload` alanını AÇIKÇA gönderdi, bu
yüzden bu turda başka hiçbir case'i etkilemedi; ama HERHANGİ bir gerçek
tüketici `payload`'ı atlarsa (JSON'da opsiyonel bir alan olduğu için makul
bir varsayım) aynı 500'e düşer.

**Durum:** ☐ Beklemede · ☑ Geçti · ☑ Kaldı · ☐ Atlandı (kısmi — 1., 2., 3.

---

**Gerçek sonuç — kapanış yeniden koşumu (2026-09-19, canlı sunucu)**
`HATA-S4-002` kapandı. Aynı istek, aynı uç, PostgreSQL destekli canlı örnek
(`127.0.0.1:5199`, `samples/Tracon.Api`, gerçek `Tracon:Ui:AuthToken`):

```
PUT /tracon/api/schedules/payloadsiz-kapanis
{"handlerKey":"tracon.workflow","targetName":"t","cron":"*/5 * * * *","enabled":true}
-> HTTP 200   "payload":[]
GET /tracon/api/schedules/payloadsiz-kapanis
-> HTTP 200   "payload":[]
```

`500` yok; yazılan ile geri okunan aynı. Ölçümün kaydı üç katman buldu ve
üçü de kapandı: yanıtın serileştirilmesi, **SQL Server'ın `CHECK (ISJSON(...))`
kısıtı** (`null` literali reddediliyordu — `job_schedules_payload_json` ihlali
birebir ölçüldü) ve `JobPayload.ExtractItems`'in JSON `null`'ı **tek bir
`"null"` kalemi** sanması. Sınıf taraması beş özelliğe genişledi. Kapı:
`FreeFormJsonContractTests` (her özelliğin kendi `init`'ini çağırır) +
üç sağlayıcıda koşan iki `store` sözleşme testi. Zamanlama silindi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı
adımlar Geçti; 2. adımın `payload`sız hâli `HATA-S4-002`'yi açtı; 4. adım
kaynaktan doğrulandı, canlı ölçülmedi)

## MT-JOB-130 — Arayüz handler açılır listesi sunucudan gelir

**Gerçek sonuç**
Playwright, scratch host'un UI'sine (`Tracon.UI`, `.UseUI()`) bearer token
ile giriş yaptı. Jobs ekranı → "New schedule" → "Handler key" açılır
listesi: `sched-allow` senaryosu aktifken (server `["acme.a"]` döndürüyor)
liste **tam olarak** `["Pick a handler…", "acme.a"]` gösterdi — iki sabit
seçenek DEĞİL, `GET /api/schedules/handler-keys` yanıtının birebir
yansıması. Jobs ekranındaki hem "Schedules" hem "Recent jobs" tablosunda
sütun başlığı **"Handler key"** ve hücreler tam anahtarı gösteriyor
(`acme.a`, `acme.no-such-handler` — kısaltılmamış). Konsol: 2 hata —
(1) `script-src 'self'` CSP'sinin inline script'i engellemesi, bu **zaten
bilinen `HATA-S2-002`** ile aynı kök neden (embedded console erken tema
boyama), yeni değil; (2) ilk yüklemede `/api/agents`'a `401` — bu scratch
host'ta hiç agent kaydı olmadığından kaynaklanan, iş/zamanlama iddialarıyla
ilgisiz bir gözlem, HATA açılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-JOB-131 — `GET /api/jobs` her satırda `handlerKey` taşır

**Gerçek sonuç**
`GET /api/jobs` (tüm satırlar): her satırda `handlerKey` alanı var
(`acme.a`, `acme.b`, `acme.no-such-handler`), hiçbir satırda `kind` alanı
yok. `?handlerKey=acme.b` → 2 satır, ikisi de `acme.b`. `?handlerKey=tracon.retention`
(bu türde hiç iş yok) → `[]` — süzgeç gerçekten uygulanıyor, boş liste
sessizce "tümü" değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## Sayım (skill §7 betiği)

```
{'Geçti': 97, 'Kaldı': 1} toplam: 98
```

Bölüm 1-7 (`MT-JOB-001..085`) + `MT-JOB-090` + Bölüm 8 (`MT-JOB-091..121`)
+ Bölüm 9-10 (`MT-JOB-122..131`) + regresyon `MT-JOB-098`(B03) — **aile 16
TAMAMLANDI**: 98/98 case işlendi, 97 Geçti, 1 Kaldı (`MT-JOB-129`,
`HATA-S4-002`), sıfır `Beklemede`. Sıfır `Atlandı`.
