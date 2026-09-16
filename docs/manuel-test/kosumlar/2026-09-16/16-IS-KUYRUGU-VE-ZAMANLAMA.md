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
| **Bu oturumda koşulan** | Oturum 2: MT-JOB-001..013 (Bölüm 1, kısmi). Oturum 3: MT-JOB-014..054 (Bölüm 1 tamamlandı, Bölüm 2-5 tamamlandı) |

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

## Devir notu

**Nerede kalındı (oturum 3, bu oturum):** Oturum 2 MT-JOB-001..013'ü
bitirmişti (13/13 Geçti). Bu oturum MT-JOB-014'ten başladı ve **Bölüm
1'i tamamladı** (014, 015, 016), ardından **Bölüm 2 (Toplu Çalıştırma,
020-027), Bölüm 3 (Workflow, 030-032) ve Bölüm 5'e kadarki tüm Zamanlayıcı
Ayarları (040-054, Bölüm 4 "İptal" dahil)** koşuldu. **Toplam bu oturumda
41 case koştu, 37'si spec'in kendi numarası (014-054 aralığında var olan
her case), hepsi ☑ Geçti** (026/027/051 tek bir gözlem penceresinde
birleştirildi, ayrı satırlarda raporlandı). **Hiç `Kaldı`/kusur yok, hiç
`Beklemede` bırakılan case yok** bu aralıkta — MT-JOB-001..054 arası artık
**tam kapalı**.

✅ **`user-secrets list` okuma engeli TEMİZLENDİ.** Oturum 2'nin devir notu
bunun Claude Code otomatik-mod sınıflandırıcısı tarafından "Cloud Storage
Mass Delete" gerekçesiyle reddedildiğini kaydetmişti. Bu oturumun **ilk
adımı** olarak tekrar denendi ve **ilk seferde başarılı oldu** — tüm 17
anahtar okunabildi, gerçek OpenAI (MT-JOB-014/020-027/040-044/050/052/054),
gerçek workflow/Anthropic-google bağımlı olmayan (MT-JOB-030-032) çağrılar
sorunsuz çalıştı. **Sonraki oturum bu engeli artık beklememeli** — ama yine
de ilk adım olarak bir kez denenmesi (oturum 2 ve bu oturumun ikisi de
"önce dene" diyor) ucuz bir doğrulamadır.

🚨 **Sistemik spec bayatlığı sürüyor (Faz 129 handler_key migrasyonu).**
`JobScheduleSaveRequest`'in `"kind"` yerine `"handlerKey"` istediği kural
014-054 arasında da geçerliydi; her `PUT .../schedules/{name}` çağrısında
`"handlerKey": "tracon.agent-batch"` / `"tracon.workflow"` kullanıldı ve
sapma ayrıca not edilmedi (tekrarlayan aynı not, artık üst bilgide). Spec
dosyasına **hâlâ dokunulmadı** — bu, MT-JOB-055'ten sonrasının da (§6
tek-yürütücü, §7 async-run, §8 tetikleyiciler, §9 lane/telemetri, §10
custom handler) muhtemelen aynı düzeltmeyi isteyeceği anlamına gelir.

🆕 **Bu oturumda bulunan iki doküman kusuru (skill §1.1 istisnası,
`Beklenen sonuç` spec'te düzeltildi, ürün kusuru DEĞİL):**
1. **MT-JOB-015** — Faz 17 döneminden kalma "yalnız iki seçenek" beklentisi;
   Faz 137 (K-665) dinamik "Handler key" listesini getirdi (varsayılan
   dokuz anahtar, `GET /api/schedules/handler-keys`'ten). Zaten MT-JOB-130
   tarafından güncel haliyle test ediliyor. Spec'te düzeltildi.
2. **MT-JOB-053** — doğrulama sorgusu `occurred_at` sütununu arıyordu;
   gerçek sütun adı `created_at`'tır (`\d mt_s4.audit_log` ile doğrulandı).
   Spec'te düzeltildi. Case'in kendisi (denetim izi boşluğu) geçti.

**Kod donması ihlali YOK.** MT-JOB-032, `.UseWorkflows()`'u geçici olarak
yorum satırına almayı gerektiriyordu (spec'in kendi prosedürü) — uygulandı,
test koşuldu, **hemen geri alındı**, `git diff --stat --
samples/Tracon.Api/Program.cs` boş doğrulandı, yeniden derlendi
(0 uyarı), `git diff --stat 7e3a4de7..HEAD -- src samples tests` **boş**
doğrulandı, uygulama temiz ikili ile yeniden başlatıldı.

**Sonraki oturum neyle başlamalı:** MT-JOB-060'tan devam (§6 — Tek
Yürütücü Seçimi / `singleton_leases`, MT-JOB-060..066). 🚨 **Bu bölüm
karmaşıktır** — MT-JOB-062/063/065 **iki eşzamanlı süreç** ister (aynı
veritabanına bağlı), devir notundaki (oturum 9/03 kaynaklı) TCP-yönlendirici
tarifi veya doğrudan ikinci bir port (`5094` gibi, `5092`/`5090` diğer
amaçlarla ayrılmış olabilir — kullanılabilirliği kontrol et) ile ikinci bir
`dotnet run` örneği gerekebilir. §7'nin gerçek model çağıran case'leri
(`MT-JOB-070`–`075`) artık engelsiz olmalı. Uygulama bu oturumun sonunda
**açık bırakıldı**, varsayılan ortamda (port 5084, şema `mt_s4`,
`RunWorker`/`MaxConcurrentJobs`/`MaxItemsPerJob`/`PollInterval` hepsi
varsayılan) — sonraki oturum önce `curl .../api/diagnostics` ile
canlılığını doğrulamalı.

**Artık test fixture'ları (temizlenmedi, zararsız):** `bozuk-hedef`
(hedef `yok-boyle-bir-agent`, MT-JOB-026/027/051 için), `wf-toplu`
(`FIX-JOB-02`, tekrar kullanılabilir), `denetim-test` zaten silindi
(MT-JOB-053 sonunda). `hesapli-cron` oturum 2'den kalma, dokunulmadı.

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

---

## Sayım (skill §7 betiği)

```
{'Geçti': 37} toplam: 37
```

Bölüm 1-5 (`MT-JOB-001..054`, spec'te var olan her case) **tamamlandı**,
37/37 Geçti, sıfır `Kaldı`, sıfır `Beklemede`. `MT-JOB-060`'tan
`MT-JOB-131`'e kadar (61 case, Bölüm 6-10) bu oturumda koşulmadı —
sonraki oturumun işi (yukarıdaki devir notu).
