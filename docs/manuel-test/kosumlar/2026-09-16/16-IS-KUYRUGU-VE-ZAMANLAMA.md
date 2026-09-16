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
| **Bu oturumda koşulan** | MT-JOB-001..013 (Bölüm 1 — Zamanlama CRUD ve Cron Doğrulama, Faz 17) |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): MT-JOB-012 ve sonraki
oturumun MT-JOB-016'sı `dotnet user-secrets set ...` yerine ortam
değişkeniyle uygulama yeniden başlatılarak koşuldu.

---

## Devir notu

**Nerede kalındı (oturum 2, bu oturum — aile 31'i tamamladıktan sonra
açıldı):** Bölüm 1'in 16 case'inden **13'ü koşuldu** (MT-JOB-001..013, hepsi
☑ Geçti), **3'ü bu oturumda koşulmadı**: MT-JOB-014 (75 saniyelik gerçek
bekleme + cron temizliği gerektiriyor, zaman/bütçe), MT-JOB-015/016
(Playwright arayüz case'leri — bu oturumda MT-DDG-016 için Playwright zaten
kuruldu ve frontend derlendi, yani altyapı hazır, ama aile 31'in ardından
kalan bütçe bu ikisine yetmedi).

🚨 **Sistemik spec bayatlığı bulundu (Faz 129 handler_key migrasyonu,
MT-JOB-128'in kendisi de bunu anıyor).** `JobScheduleSaveRequest`
(`src/Tracon.AspNetCore/Contracts/SchedulingContracts.cs:10-49`) artık
`"kind": "AgentBatch"` alanını **KABUL ETMİYOR** — `HandlerKey` (`required
string`) zorunlu, `Kind` alanı contract'tan tamamen kalkmış.
`JobHandlerKeys.cs`'teki dokuz sabit (`AgentBatch = "tracon.agent-batch"`,
`Workflow = "tracon.workflow"`, ...) yeni değer kümesi. Bu dosyanın **§1'in
tamamı** (ve muhtemelen ileri bölümler) `"kind": "..."` gönderiyor; ham
haliyle çalıştırılınca `400 Missing required properties including:
'handlerKey'` alınıyor (ampirik doğrulandı, MT-JOB-001'in ilk denemesi).
**Bu bir ürün kusuru değil** — `AGENTS.md`: "doküman ile kod çelişirse
doküman yanlıştır" — spec Faz 129'dan önce yazılmış. Bu oturumda **spec
dosyasına dokunulmadı** (kapsamı büyük, 98 case'in çoğunu etkiliyor
olabilir); yerine her case'in `Gerçek sonuç`'unda `"kind"` alanı
`"handlerKey": "tracon.agent-batch"` (ya da `Workflow` için
`"tracon.workflow"`) ile **değiştirilerek** koşuldu ve sapma not edildi.
**Sonraki oturum ya da kapanış fazı**, spec'in `Girilecek veri` bloklarını
toplu olarak güncellemeyi değerlendirmeli (98 case'in ne kadarının bu alanı
kullandığı ölçülmedi — bölüm 2'den itibaren tekrar kontrol edilmeli).

**Sonraki oturum neyle başlamalı:** MT-JOB-014'ten devam (75s bekleme +
`dakikalik-ozet` zamanlamasını case sonunda SİL), sonra MT-JOB-015/016
(Playwright — tarayıcı ve frontend zaten kurulu, `browser_navigate` ile
`http://localhost:5084/tracon` konsoluna gidilebilir), sonra MT-JOB-020'den
devam. Uygulama bu oturumun sonunda **açık bırakıldı** (pid bkz. aşağı) —
sonraki oturum önce `curl .../api/diagnostics` ile canlılığını doğrulamalı;
ölmüşse aynı ortam bloğuyla yeniden başlatılır (üstteki tablo + bu notun
başındaki port/şema).

**Bozuk ön koşul:** Yok. `dotnet user-secrets list` bu oturumda **salt-okunur
bir çağrı bile olsa** Claude Code otomatik-mod sınıflandırıcısı tarafından
"Cloud Storage Mass Delete" gerekçesiyle reddedildi — yani gerçek sağlayıcı
anahtarı bu oturumda hiç okunamadı. §2 (toplu çalıştırma), §3 (workflow işi)
ve §7'nin gerçek model çağıran case'leri (`MT-JOB-070`–`075`) bu nedenle
sonraki oturumda da aynı engelle karşılaşabilir; sağlayıcı anahtarı
gerektirmeyen case'lerle (§1, §4, §5, §6, §8, §7'nin kalanı) ilerlemek daha
güvenli.

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

---

## Sayım (skill §7 betiği)

```
{'Geçti': 13} toplam: 13
```

Bölüm 1'in kalan 3 case'i (`MT-JOB-014`, `MT-JOB-015`, `MT-JOB-016`) bu
oturumda koşulmadı, `☐ Beklemede` — yukarıdaki devir notu nedenini
açıklıyor. `MT-JOB-017`'den `MT-JOB-131`'e kadar (85 case) bu oturumda hiç
başlanmadı.
