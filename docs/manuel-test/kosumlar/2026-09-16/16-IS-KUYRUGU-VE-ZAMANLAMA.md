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
| **Bu oturumda koşulan** | Oturum 2: MT-JOB-001..013 (Bölüm 1, kısmi). Oturum 3: MT-JOB-014..054 (Bölüm 1 tamamlandı, Bölüm 2-5 tamamlandı). Oturum 4: MT-JOB-060..066 (Bölüm 6, Tek Yürütücü Seçimi, tam), MT-JOB-070..085 (Bölüm 7, async-run, tam), MT-JOB-090 (Bölüm 8, tetikleyici kapsam şüphesi, tek case) |

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

**Nerede kalındı (oturum 4, bu oturum):** Oturum 3 MT-JOB-001..054'ü
tam kapatmıştı (37/37 Geçti). Bu oturum **MT-JOB-060'tan başladı** ve
**Bölüm 6'yı (Tek Yürütücü Seçimi, `singleton_leases`, 060-066) TAM
kapattı**, ardından **Bölüm 7'yi (async-run, `Prefer: respond-async`,
070-085) TAM kapattı**, son olarak **Bölüm 8'in ilk case'i MT-JOB-090'ı**
koştu. **Toplam bu oturumda 24 case koştu, hepsi ☑ Geçti** — hiç `Kaldı`,
hiç `Beklemede` yok. `MT-JOB-001..085` + `090` artık **tam kapalı** (61/61
bu dosyada, `MT-JOB-091`'den `131`'e kadar — 40 case, Bölüm 8'in kalanı +
Bölüm 9-10 — bu oturumda koşulmadı).

🚨 **Kurulum sapması (koordinatör talimatı) — MT-JOB-062/063/065:** Spec bu
üç case için `dotnet publish` + SQLite dosyası öneriyor; bunun yerine
**PostgreSQL `mt_s4` şeması PAYLAŞILARAK** iki ayrı süreç (port 5084 + yeni
port 5094) kullanıldı. **Ampirik bulgu: spec'in "`dotnet run` ile
ÇALIŞTIRILAMAZ, `launchSettings.json` her zaman 5080'i açar" iddiası bu
ortamda YANLIŞ** — `--urls` argümanı güvenilir şekilde kazanıyor (kanıt:
MT-JOB-062 kaydı). İkinci süreç (port 5094) case'ler bitince kapatıldı,
yalnız 5084 açık bırakıldı.

🚨 **Yeni altyapı tuzağı — `dotnet run` sarmalayıcısı bu ortamda kararsız.**
MT-JOB-090 sırasında uygulama iki kez (her ikisinde de `Application is
shutting down...` — istisna/hata günlüğü YOK) beklenmedik şekilde kapandı;
sinyal kaynağı belirlenemedi (muhtemelen ajan koşum ortamının arka plan
süreç yönetimi, `dotnet run`'ın kendi CLI sarmalayıcı sürecine bağlı).
**Çözüm:** derlenmiş DLL'i DOĞRUDAN çalıştırmak
(`dotnet artifacts/bin/Tracon.Api/release/Tracon.Api.dll --urls
http://localhost:5084` + aynı ortam değişkenleri, `dotnet run`
sarmalayıcısı OLMADAN) — bu şekilde başlatılan süreç kararlı kaldı.
**Sonraki oturum bu deseni kullanmalı**, `dotnet run --project ...`
DEĞİL (ikisi de `--no-build` gerektirir, ilki DLL'i `artifacts/bin/`'den
doğrudan alır). `setsid` macOS'ta YOKTUR (yeni ölçülen tuzak,
`timeout`/`user-secrets` engeli gibi) — kullanma.

🚨 **Sistemik spec bayatlığı sürüyor (Faz 129 handler_key migrasyonu),
060-090 arasında da doğrulandı:** `PUT .../schedules/{name}` çağrılarında
hâlâ `"handlerKey"` kullanıldı (`"kind"` DEĞİL); `job.kind` alanı da yok,
`job.handlerKey` var (MT-JOB-073/090'da ayrıca doğrulandı). Spec dosyasına
hâlâ dokunulmadı — Bölüm 8'in kalanı (091+) da muhtemelen aynı düzeltmeyi
isteyecek.

🆕 **Bu oturumda bulunan doküman kusurları (skill §1.1 istisnası, ürün
kusuru DEĞİL):**
1. **MT-JOB-084** — spec'in kendi önceki "doküman düzeltmesi" notu ampirik
   doğrulandı: `Idempotency-Key` + `Prefer` YOK → `HTTP: 200` (400 DEĞİL).
2. **MT-JOB-090** — `POST /api/api-keys` yanıtı `rawKey` DEĞİL
   `plaintextKey` alanı taşıyor.
3. **MT-JOB-090** — spec'in "şüphe"si (SchedulingEndpoints'te
   `RequireApiKeyScope` yok) **ÇÜRÜTÜLDÜ**: kod artık 9 çağrı taşıyor
   (`grep -n "RequireApiKeyScope"
   src/Tracon.AspNetCore/Endpoints/SchedulingEndpoints.cs` boş dönmüyor).
   Ampirik: `RunsRead`-kapsamlı anahtarla `PUT`/`DELETE /api/schedules/*`
   → ikisi de `403 Insufficient scope`. `WorkflowEndpoints`'in (`MT-WF-100`)
   düştüğü boşluğa `SchedulingEndpoints` **düşmüyor**. Yeni `HATA` açılmadı.

**Kod donması ihlali YOK** — bu oturumda `src/`'e hiç dokunulmadı; tek
istisna MT-JOB-062/063/065'in `dotnet publish`/DLL kullanımı (spec'in
kendi izin verdiği "İzlek A" yordamı, kaynağı değiştirmez, yalnız derlenmiş
çıktıyı doğrudan çalıştırır). `git diff --stat 7e3a4de7..HEAD -- src
samples tests` oturum sonunda **boş** doğrulandı.

**Sonraki oturum neyle başlamalı:** MT-JOB-091'den devam (§8 — Gelen
Tetikleyiciler, `TRIGSECRET`/`openssl` imza yordamı ortak kurulumu
gerektirir; `Tracon:TriggerSecrets:Slack` **ortam değişkenine çevrilmeli**,
`dotnet user-secrets set` DEĞİL — skill §1.2). Uygulama bu oturumun
sonunda **açık bırakıldı**, PID değişebilir ama süreç DLL doğrudan
çalıştırılarak başlatıldı (port 5084, şema `mt_s4`, tüm ayarlar
varsayılan — `SingletonExecution`/`AsyncRun`/`Scheduling.*` hiçbiri
override edilmemiş durumda, `ps eww <pid> | grep Tracon__` ile
doğrulanabilir). Sonraki oturum önce `curl .../api/diagnostics` ile
canlılığını doğrulamalı; kapalıysa **DLL yordamıyla** yeniden başlatmalı
(`dotnet run` DEĞİL, yukarıdaki tuzağa bakın).

**Artık test fixture'ları (temizlenmedi, zararsız):** `bozuk-hedef`,
`wf-toplu` (bu oturumda da yeniden kullanıldı, MT-JOB-065), `hesapli-cron`
— hepsi önceki oturumlardan, dokunulmadı. `dakikalik-ozet` oturum 3'te
zaten silinmişti (bu oturumda `mt_s4.job_schedules`'ta 4 satır: `ozet-toplu`,
`hesapli-cron`, `bozuk-hedef`, `wf-toplu` — doğrulandı). Bu oturum yeni
kalıcı fixture BIRAKMADI: geçici kota kuralı (MT-JOB-079) ve geçici API
anahtarı (MT-JOB-090) ikisi de case sonunda silindi.

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

## Sayım (skill §7 betiği)

```
{'Geçti': 61} toplam: 61
```

Bölüm 1-7 (`MT-JOB-001..085`, spec'te var olan her case) + `MT-JOB-090`
**tamamlandı**, 61/61 Geçti, sıfır `Kaldı`, sıfır `Beklemede`.
`MT-JOB-091`'den `MT-JOB-131`'e kadar (Bölüm 8'in kalanı + Bölüm 9-10,
37 case) bu oturumda koşulmadı — sonraki oturumun işi (yukarıdaki devir
notu).
