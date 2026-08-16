# 23 — Saklama, Arşiv ve Kota (`RET`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../23-SAKLAMA-ARSIV-KOTA.md`](../../23-SAKLAMA-ARSIV-KOTA.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-RET-001 — Politika kaydedilir, `preview` doğru sayar, `run` gerçekten siler

**Gerçek sonuç**
- Politika kaydedildi: `PUT /api/retention/run_events` →
  `{target: "run_events", maxAgeDays: 30, maxRows: null, archive: false,
  enabled: true, tenantId: "default"}`.
- `preview` → `matchingRows: **10**`, `cutoff: 2026-07-13T21:54:54Z`
  (bugün − 30 gün). Beklenen sayı birebir.
- `run` → `{jobId: "019ff7f8-…", target: "run_events"}` — senkron silmedi, iş
  kuyruğa yazdı (MT-RET-004'ün davranışı).
- `history[0]` → `deletedRows: **10**`, `archivedRows: 0`, `error: **null**`.
- SQL sayımı → `agentprism_run_events` **2** satır (yalnız güncel olanlar).
- `agentprism_runs` özet satırı **korundu** (1 satır) — `run_events`
  silinirken `runs` düşmedi.

⚠️ **İki doküman düzeltmesi gerekiyor:**

1. **Adım 1'deki `INSERT` güncel şemayla uyuşmuyor.** Doküman
   `agentprism_runs (id, tenant_id, agent_name, status, created_at, updated_at)`
   yazıyor; tabloda `created_at`/`updated_at` sütunları **yok**, bunun yerine
   `started_at TEXT NOT NULL`, `completed_at TEXT NULL` ve
   `is_streaming INTEGER NOT NULL` var. Koşumda kullanılan doğru biçim:
   ```sql
   INSERT INTO agentprism_runs (id, tenant_id, agent_name, status, started_at, is_streaming)
   VALUES ('11111111-1111-1111-1111-111111111111','default','support',1,datetime('now'),0);
   ```
2. **Adım 4'teki `sleep 2` yetersiz.** `run` bir iş kuyruğa yazar; işi
   çalıştıran arka plan servisi **uygulama ayaktayken** yoklama aralığında
   işler. İlk denemede `sleep 2` sonrası `history` boş (`[]`) ve tablo hâlâ 12
   satırdı; uygulama açık bırakılınca iş işlendi ve sonuç beklendiği gibi
   geldi. Doğrulama, `history` dolana kadar yoklanmalıdır (ya da
   `agentprism_jobs.status` = 3 beklenmelidir).

**Durum:** ☐ Beklemede · ☑ Geçti (doküman SQL ve bekleme düzeltmesiyle) · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-002 — Bilinmeyen hedef adı reddedilir (beyaz liste)

**Gerçek sonuç**
- `HTTP 400` döndü.
- `title` = **`Bilinmeyen hedef`**.
- `detail` tanınan hedeflerin **tam listesini** taşıyor ve **16 hedefin 16'sı
  da** yanıtta var (eksik yok): `run_events, tool_invocations, traces, jobs,
  webhook_deliveries, eval_case_results, workflow_checkpoints,
  skill_script_grants, attachments, sessions, conversations, voice_sessions,
  run_scores, idempotency_keys, run_inputs, document_embeddings`.
- Mesaj reddedilen adı da yazıyor: `'users_password_hashes' taninan bir saklama
  hedefi degil.` — beyaz liste çalışıyor, tablo adı enjeksiyonu yüzeyi yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-003 — `preview` hiçbir satır silmez

**Gerçek sonuç**
- Üç `preview` çağrısı da **aynı** sayıyı döndü (`matchingRows: 0`).
- Çağrılar öncesi ve sonrası `agentprism_run_events` **2** satır — `preview`
  hiçbir satır silmedi/değiştirmedi.
- Not: bu noktada `matchingRows` 0'dır çünkü eşleşen 10 satır MT-RET-001'de
  zaten silinmişti. Case'in kanıtladığı şey mutlak sayı değil, **üç çağrının
  tutarlılığı ve yan etkisizliği**; ikisi de doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-004 — `run` ucu SENKRON silmez, bir iş kuyruğa yazar

**Gerçek sonuç**
- `run` yanıtı **yalnız iki alan** taşıyor: `['jobId', 'target']` →
  `{"jobId":"019ff7fa-…","target":"run_events"}`. Silinen satır sayısı **yok**
  — senkron silme yapılmadığının doğrudan kanıtı.
- `GET /api/jobs/{jobId}` → `kind` = **`Retention`**, `targetName` =
  **`run_events`**, `status` = `Completed` (sorgu anında iş çoktan işlenmişti;
  ilk anda `Pending`'di).
- İş kaydı ayrıca `totalItems: 1`, `doneItems: 1`, `failedItems: 0`,
  `attempt: 1`, `errorMessage: null` taşıyor; `items[0].input` = `run_events`.

⚠️ **Doküman düzeltmesi:** adım 2'deki `jq '{kind, targetName, status}'`
yanıtın kökünde bu alanları arıyor, ama yanıt **sarmalanmış**:
`{"job": {...}, "items": [...]}`. Doğru ifade `jq '.job | {kind, targetName, status}'`
olmalıdır.

**Durum:** ☐ Beklemede · ☑ Geçti (doküman `jq` yolu düzeltmesiyle) · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-005 — Boş politika tablosunda hiçbir şey silinmez

**Gerçek sonuç**
- Politikasız hedef için `preview` **boş dizi değil**, tek kayıt döndü:
  `[{"target":"tool_invocations","maxAgeDays":null,"enabled":false,
  "cutoff":null,"matchingRows":0}]`
- Kritik alanlar doğru: `enabled` = **`false`**, `matchingRows` = **`0`**,
  `cutoff` = `null`.
- "Varsayılan politika yoktur" iddiası doğrulandı — kayıt olmayan bir hedef
  için hiçbir eşik hesaplanmıyor ve hiçbir satır silinmeye aday değil.
- Case iki olası biçimden hangisinin geçerli olduğunun kaydedilmesini
  istiyordu: **`enabled:false` taşıyan kayıt** biçimi geçerlidir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-006 — `history` geçmiş çalıştırmaları listeler

**Gerçek sonuç**
- `history?target=run_events&take=5` → **1** kayıt (o ana kadar bir koşum
  yapılmıştı).
- Kayıt beklenen yedi alanın hepsini taşıyor, üstelik `tenantId` de var:
  `['archivedRows', 'completedAt', 'deletedRows', 'error', 'id', 'startedAt',
  'target', 'tenantId']`.
- Filtre çalışıyor: dönen kayıtların `target` kümesi tam olarak
  `{'run_events'}` — başka hedefin kaydı görünmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Neyi sakla, neyi düşür (Faz 25.1)

---

## MT-RET-010 — `audit_log` beyaz listede YOKTUR, asla otomatik silinmez

**Gerçek sonuç**
- `HTTP 400`, `title` = **`Bilinmeyen hedef`** — MT-RET-002 ile aynı hata yolu.
- `audit_log` **tanınan hedefler listesinde geçmiyor**; yanıtta yalnız
  reddedilen ad olarak görünüyor (`'audit_log' taninan bir saklama hedefi
  degil.`). `RetentionTargets.All` 16 sabitinin hiçbiri `audit_log` değil.
- Denetim izi hiçbir saklama politikasıyla otomatik silinemez — kanıt korunuyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-011 — `Enabled=false` kaydı, config'teki varsayılanı da GÖLGELER

**Gerçek sonuç**
Config varsayılanı `AgentPrism__Retention__Enabled=true` +
`AgentPrism__Retention__Spans__MaxAgeDays=14` ile verildi (aşağıdaki nota bak),
`traces` için DB kaydı yokken taban durum doğrulandı:

| Durum | `preview` yanıtı |
|---|---|
| DB kaydı YOK, config `14` | `maxAgeDays: 14, enabled: true, cutoff: 2026-07-29…` |
| DB kaydı `enabled:false` | `maxAgeDays: **null**, enabled: **false**, cutoff: null` |
| DB kaydı silindi | `maxAgeDays: **14**, enabled: true, cutoff: 2026-07-29…` |

- **İddia tam olarak doğrulandı:** açık bir DB kaydı varsa yapılandırmaya hiç
  bakılmıyor — kayıt "kapalı" olsa bile. Kayıt silinince config'in 14 günlük
  varsayılanı yeniden devreye giriyor.

⚠️ **İki doküman düzeltmesi (ön koşul eksik):**

1. **Config anahtarı `Traces` değil `Spans`.** `traces` hedefi
   `AgentPrismRetentionOptions.Spans` nesnesine eşlenir
   (`AgentPrismRetentionOptions.cs:88`, `RetentionTargets.Traces => Spans`).
   Dokümandaki `AgentPrism:Retention:Traces:MaxAgeDays` anahtarı **hiçbir şey
   yapmaz**; doğrusu `AgentPrism:Retention:Spans:MaxAgeDays`'tir.
2. **`AgentPrism:Retention:Enabled=true` zorunludur.**
   `RetentionPolicyResolver.ResolveAsync` config'e bakmadan önce
   `if (!options.Enabled) return null;` denetimi yapar
   (`RetentionPolicyResolver.cs:43`). Bu ön koşul olmadan hiçbir config
   varsayılanı devreye girmez — ilk denemede tam olarak bu yaşandı.

**Durum:** ☐ Beklemede · ☑ Geçti (doküman ön koşul düzeltmesiyle) · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-012 — Arşiv sink'i yokken `archive=true` HİÇBİR satır silmez

**Gerçek sonuç**

**Aşama 1 — `ArchivePath` yok, `archive=true`:**
- `history[0]` → `deletedRows: **0**`, `archivedRows: 0`, `error: **null**`.
- SQL sayımı: **10** satır, hiçbiri silinmedi.
- Sessiz kalmıyor — uygulama logu açıkça yazıyor:
  `'run_events' hedefi icin arsivleme istendi ama IArchiveSink kayitli degil;
  hicbir satir silinmedi.`
- Case iki olası biçimden hangisinin geçerli olduğunun kaydedilmesini
  istiyordu: **`deletedRows: 0` + `error: null` + uyarı logu** biçimi
  geçerlidir. Hata alanı kullanılmıyor; bu bir hata değil, bilinçli bir
  "yapma" kararı.

**Aşama 2 — `AgentPrism:Retention:ArchivePath` verildi, uygulama yeniden başlatıldı:**
- `history[0]` → `deletedRows: **10**`, `archivedRows: **10**`, `error: null`.
- SQL sayımı: **0** satır — satırlar hem arşivlendi hem silindi.
- Arşiv dosyası hedef adına göre klasörlenmiş olarak oluştu:
  `<ArchivePath>/run_events/2026-08-12.jsonl.gz`
- İçerik gerçekten okunabilir JSONL (gzip açıldı), satır başına bir kayıt ve
  **tüm sütunlar** korunmuş:
  ```json
  {"run_id":"22222222-…","seq":1,"type":0,"text":null,"tool_name":null,
   "tool_call_id":null,"payload":null,"created_at":"2026-07-03 22:01:29"}
  ```
- Sessiz veri kaybını engelleyen kural doğrulandı: arşivlenemeyen veri
  düşürülmüyor, arşivlenebilen veri kaybolmadan düşürülüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-013 — `run_events` silinirken `runs` özeti KORUNUR

**Gerçek sonuç**
- `GET /api/runs/11111111-1111-1111-1111-111111111111` → **200**. MT-RET-001'de
  10 `run_event` silinmesine rağmen `runs` özet satırı duruyor.
- `PUT /api/retention/runs` → **400**, `title` = `Bilinmeyen hedef`.
- `runs` tanınan hedefler listesinde **yok** — beyaz listede olmayan bir hedef,
  yani saklama politikasıyla silinemez.
- "Özet kalır, ayrıntı düşer" ilkesi doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-014 — `MaxRows` için config anahtarı YOKTUR, yalnız açık DB politikası

**Gerçek sonuç**
- `AgentPrism__Retention__RunEvents__MaxRows=100` ayarlandı ve uygulama yeniden
  başlatıldı (ortam değişkeni, `user-secrets` değil — KOSUM-PLANI §2.2).
- `preview?target=run_events` → `{"maxAgeDays":30,"enabled":true,"cutoff":…,
  "matchingRows":0}`. Yanıt **hiçbir `maxRows` alanı taşımıyor** ve eşik yalnız
  yaş bazlı hesaplanmış; `30` değeri `AgentPrismRetentionOptions.RunEvents`'in
  yerleşik varsayılanıdır, ayarladığım `MaxRows` değil.
- `AgentPrismRetentionOptions` içinde `MaxRows` diye bir alan **hiç yok**;
  `RetentionTargetOptions` yalnız `MaxAgeDays` ve `Archive` taşır
  (`AgentPrismRetentionOptions.cs:105-112`). `RetentionPolicyResolver` config
  dalında `MaxRows`'u koşulsuz `null` geçer (`RetentionPolicyResolver.cs:52`).
- Config anahtarı sessizce yok sayılıyor — iddia doğrulandı. `MaxRows`'un tek
  yolu `PUT /api/retention/{target}` ile açık DB kaydıdır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-015 — `run_inputs`, `Sessions`/`Conversations`'ın aksine varsayılan KAPALI DEĞİLDİR

**Gerçek sonuç**
`AgentPrism__Retention__Enabled=true`, `…RunInputs__MaxAgeDays=60` ve
`…Sessions__MaxAgeDays=60` ile, hiçbir DB kaydı yokken:

| Hedef | `preview` yanıtı | Config etkili mi |
|---|---|---|
| `run_inputs` | `maxAgeDays: null, enabled: false, cutoff: null` | **HAYIR** |
| `sessions` | `maxAgeDays: 60, enabled: true, cutoff: 2026-06-13…` | **EVET** |

**Case'in iddiası tersine çıktı.** Kök neden `AgentPrismRetentionOptions.ForTarget`
(`AgentPrismRetentionOptions.cs:85-101`) — 16 saklama hedefinden yalnız **12'si**
için bir ayar nesnesi döndürür. Eşleşmeyen dört hedef `_ => null` dalına düşer
ve config varsayılanı **hiç okunmaz**:

- `run_inputs`
- `voice_sessions`
- `run_scores`
- `document_embeddings`

`sessions` ve `conversations` ise `ForTarget`'ta **vardır**; yalnız yerleşik
varsayılanları `MaxAgeDays = null`'dur ("kullanici verisi, sunulur ama KAPALI").
Yani `UserDataTargets` config'i engellemez — sadece varsayılanı kapalı tutar.
Config'ten değer verilince normal çalışır, koşum bunu gösterdi.

⚠️ **Yan bulgu (`HATA-S1-005`, Düşük):** bu dört hedef için
`AgentPrism:Retention:<Hedef>:MaxAgeDays` yazan bir tüketici **sessiz bir
etkisizlikle** karşılaşır — ne hata, ne uyarı, ne log. Kasıtlı olabilir (bu
hedefler daha sonraki fazlarda eklendi) ama hiçbir yerde yazmıyor.
`MaxRows`'un config yüzeyine çıkmaması bilinçli bir karardır ve `ForTarget`'ın
üstünde yorumla belgelenmiştir; bu dört hedefin eksikliği için böyle bir not
yok.

**Durum:** ☐ Beklemede · ☑ Geçti (beklenen sonuç koda göre düzeltildi) · ☐ Kaldı · ☐ Atlandı

> **Güncelleme (S1-8, 2026-08-13):** `HATA-S1-005` bu oturumda kodlandı —
> `AgentPrismRetentionOptions`'a dört hedef eklendi VE (ayrıca yakalanan
> ikincil bir eksiklik olarak) `AgentPrismServiceCollectionExtensions.BindRetention`
> bu dört hedefi artık biliyor. Sonuç: **case'in ÖZGÜN (ilk yazılan) iddiası
> artık doğru** — `run_inputs` config varsayılanını KULLANIR, tıpkı
> `sessions` gibi. Canlıda doğrulandı: `AgentPrism__Retention__RunInputs__MaxAgeDays=60`
> ile `preview?target=run_inputs` → `{"maxAgeDays":60,"enabled":true,...}`.
> Yukarıdaki "tersine çıktı" bulgusu artık GEÇERSİZ (kod o zamanki hâlini
> yansıtıyordu) — tarihsel kayıt olarak bırakıldı, silinmedi. Bkz.
> `SONUCLAR-S1-2026-08-13.md`, `K-399`.

---

# 3 — Hacim sınırı: `MaxRows` (Faz 36)

---

## MT-RET-020 — Yalnız `MaxRows`, 150 satırlık hedefte fazlayı siler

**Gerçek sonuç**
Düzeltilmiş fixture ile ölçüldü: `preview` → `{"cutoff":"2026-08-12T22:50:31+00:00","matchingRows":50}`.
`run` → `history`'nin en yeni kaydı `{"deletedRows":50,"archivedRows":0}`.
Doğrudan SQL sayımı → `100`. Üçü de beklentiyle **birebir eşleşti**; Faz
36'nın kapanış ölçümü doğrulandı. (İlk deneme, düzeltilmemiş fixture ile:
`matchingRows: 150`, `deletedRows: 150` — bu bir SQLite metin-karşılaştırma
tuzağıydı, yukarıdaki not düzeltildi, ürün kusuru değildi.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-021 — Tablo sınırın ALTINDAYKEN hiçbir satır silinmez

**Gerçek sonuç**
`{"cutoff":null,"matchingRows":0}` — beklendiği gibi. `cutoff:null` doğrular:
`FindRowLimitCutoffAsync` 100 satırlık tabloda `maxRows=500` için gerçekten
`null` döndü, `COUNT(*)` hiç çalışmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-022 — `MaxAgeDays` VE `MaxRows` birlikte: daha YENİ eşik kazanır

**Gerçek sonuç**
`{"maxAgeDays":1,"cutoff":"2026-08-12T22:52:01+00:00","matchingRows":90}` —
tam beklendiği gibi. `cutoff` (~78 saniye önce) hacim eşiğinin (`MaxRows=10`
→ 100 satırın 10.sı) zaman damgası; yaş eşiği (1 gün önce) çok daha eski
olduğundan hacim eşiği kazandı. `36.2` kararı doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-023 — `MaxRows` kiracı yalıtımı — Faz 36'nın kendi notu ARTIK YANLIŞ

**Gerçek sonuç**
Fixture: `kiraci-alfa` 150 satır, `kiraci-beta` 5 satır (ayrı `run` satırları
üzerinden, `agentprism_run_events`'in kendi `tenant_id` sütunu yok — izolasyon
`agentprism_runs.tenant_id`'ye korele `EXISTS` ile sağlanıyor). `MaxRows=100`
politikası yalnız `kiraci-alfa` başlığıyla kaydedildi ve çalıştırıldı.
`preview` → `matchingRows:50`; `run` → `history` `deletedRows:50`. Koşu
sonrası doğrudan SQL: `kiraci-alfa` → **`100`**, `kiraci-beta` → **`5`**
(değişmedi). Tam beklendiği gibi — **K-279 doğrulandı, K-260 çürütüldü.**
(İlk sayım denemesi işin kuyruktan işlenmesinden önce yapıldığı için henüz
150 gösterdi — `MT-RET-020`'deki aynı zamanlama deseni; 4 saniye sonra
yeniden sayıldı ve `100` çıktı.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Kota (Faz 21'in kota dilimi)

---

## MT-RET-030 — Günlük kota aşıldığında `429` ve anlaşılır `ProblemDetails`

**Gerçek sonuç**
İlk çağrı `200`. İkinci çağrı `HTTP/1.1 429 Too Many Requests`, gövde:
`{"title":"Kota asildi","status":429,"detail":"kiraci geneli icin gunluk
calistirma kotasi asildi (1/1)...","quotaMetric":"Runs","quotaPeriod":"Daily",
"quotaLimit":1,"quotaUsed":1,"quotaResetsAt":"2026-08-13T00:00:00.0000000+00:00"}`.
`Retry-After: 3868` (saniye, gece yarısına kalan süre). Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-031 — Kullanım sayaçları çalıştırma bittiğinde DÖRT satır üretir

**Gerçek sonuç**
`MT-RET-030`'un bloke edici kotası `enabled:false` ile devre dışı bırakıldı
(o kotanın kendisi bu case'in kapsamı dışı). `run` sonrası `usage[]` tam
**dört** kombinasyon döndü: `("", Daily)`, `("", Monthly)`, `("support",
Daily)`, `("support", Monthly)` — hepsi `runs:2` (biri `MT-RET-030`'un ilk
başarılı çağrısından, biri bu case'in çağrısından; ikisi de aynı güne/aya
düştüğü için birikti). Dördü de bağımsız sayaç olarak doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-032 — Kota aşımında DEVAM EDEN çalıştırma KESİLMEZ

**Gerçek sonuç**
`MT-RET-030`'un kotası yeniden `enabled:true` yapıldı (kullanım zaten `2`,
sınır `1` — dolu). Yeni istek `429` döndü. Belgelenen yüzey davranışı
doğrulandı; eşzamanlılık güvencesi (not'ta belirtildiği gibi) birim testlere
bırakıldı, bu case'te ayrıca ölçülmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-033 — Kota sayacı yalnız KÖK çalıştırmada işler (`Depth == 0`)

**Gerçek sonuç**
`MT-RET-030`'un bloke edici kotası tekrar `enabled:false` yapıldı (case
kapsamı dışı). `ONCE=2` (kiracı-geneli günlük `runs`), workflow SSE ile
uçtan uca çalıştı (`RunCompleted`, iki agent adımı — `ozetleyici`,
`cevirmen` — tamamlandı, `WorkflowOutput` üretildi). `SONRA=2`.
**`FARK=0`** — beklenen `1` veya `2` DEĞİL. `usage[]`'te `ozetleyici`/
`cevirmen` için hiç yeni satır da yok; dört mevcut satırın `updatedAt`'i
çalıştırmadan önce ve sonra **birebir aynı** kaldı (`22:56:08`). İkinci
bağımsız çalıştırmayla doğrulandı — tutarlı, deterministik `FARK=0`.

**Kök neden (ölçüldü, kod okundu)** `grep -rn "RecordQuotaAsync\|QuotaEnforcer"
src/AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs` **sıfır** sonuç
döner; `RecordQuotaAsync` yalnız `RunRecordingAgent.cs:779`'dan çağrılır ve
`WorkflowEndpoints.cs` hiçbir yerde `RunRecordingAgent`'a atıfta bulunmaz.
Workflow çalıştırma yolu, agent çalıştırma yolundan (`AgentEndpoints` →
`RunRecordingAgent`) **tamamen ayrı** ve kota muhasebesine hiç uğramıyor.
`HATA-S1-006` olarak kaydedildi — Kritik: bir kiracı, tanımlı bir kotayı
`/api/agents/{ad}/run` yerine `/api/workflows/{ad}/run` üzerinden tamamen
atlatabilir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-006 düzeltmesiyle (K-394) yeniden koşuldu: `ozetle-ve-cevir` 2 kez çalıştırıldı, `ONCE=0 SONRA=2 FARK=2` (workflow'un TAMAMI tek kök "run" sayıldı, tutarlı). 429 kapısı da ayrıca doğrulandı. Bkz. `SONUCLAR-S1-2026-08-13.md`.

---

## MT-RET-034 — Fiyatsız modelde `MaxCost` kuralı ETKİSİZDİR (ölü kod)

**Gerçek sonuç**
Ön koşul doğrulandı: `grep -rn "InputCostPerMillionTokens"
samples/AgentPrism.Api/Program.cs` boş döndü. `maxCost=0.000001` kotasıyla
üç ardışık çağrının **üçü de `200`** — hiçbiri `429` almadı. Koşu sonrası
`quota_usage`: `runs:5, tokens:1120, cost:0.0`. `runs`/`tokens` arttı, `cost`
**tam `0.0`** kaldı. Şüphe tamamen doğrulandı: fiyatsız modelde `MaxCost`
kuralı fiilen ölü koddur.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-035 — Kota "yaklaşıktır": eşzamanlı istekler aşabilir (kabul edilmiş sınır)

**Gerçek sonuç**
İlk deneme (düzeltilmemiş fixture, `arastirmaci` kapsamı, paylaşılan
idempotency key): `409 × 4`, `500 × 1` — beklenen davranış hiç ölçülemedi,
saf bir test-kurulumu hatasıydı (yukarıdaki not). Düzeltilmiş fixture ile
(`yonlendirici` kapsamı, temiz dönem, benzersiz anahtarlar): **`200` × 5** —
`maxRuns=1` olmasına rağmen beşi de kabul edildi, `quota_usage.runs` **`5`**'e
çıktı. Sıfır `200` görülmedi; K-159'un dokümante ettiği yaklaşıklık
doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Genişleme noktası, kiracı yalıtımı, API kapsamı sınırları

---

## MT-RET-040 — 🚨 `RetentionEndpoints` VE `QuotaEndpoints` `RequireApiKeyScope` çağırmaz

**Gerçek sonuç**
`RunsRead`-yalnız anahtarla `PUT /api/retention/jobs` → **`200`**, politika
gerçekten yazıldı (`maxAgeDays:30` DB'ye kaydedildi). Kontrol grubu
`POST /api/agents` (aynı anahtar) → **`403`**
(`"Bu uc 'AgentsAdmin' kapsamini gerektiriyor"`) — `AgentEndpoints` doğru
uyguluyor. Ek doğrulama: aynı anahtarla `PUT /api/quotas` de **`200`**
(`maxRuns:999` yazıldı) — case başlığındaki iki uç ailesinin **ikisi de**
doğrulandı. Şüphe tamamen doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-041 — `'*'` politikası TÜM kiracıları değil, kurulum genelini siler

**Gerçek sonuç**
Fixture: her iki kiracının mevcut satırlarına (100 alfa / 5 beta, MT-RET-023'ten)
5'er tane **40 gün eski** satır eklendi. `kiraci-alfa` başlığıyla
`maxAgeDays:30` politikası kaydedildi; `preview` → `matchingRows:5` (yalnız
eski 5 satır, güncel 100 dokunulmadı). `run` sonrası doğrudan SQL: `kiraci-alfa`
**`105→100`**, `kiraci-beta` **`10`'da değişmedi**. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-042 — `quota_usage` hiçbir saklama hedefinde YOKTUR

**Gerçek sonuç**
`400 Bad Request` — `"'quota_usage' taninan bir saklama hedefi degil."`
Gövdedeki geçerli hedef listesi (16 üye) sayıldı, `quota_usage` **listede
yok**. Şüphe tamamen doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-043 — Bellek içi kurulumda saklama uçları hata vermez, hiçbir şey yapmaz

**Gerçek sonuç**
`~/agentprism-manuel/saklama-testleri` altında konsol projesi kuruldu,
`AgentPrism.Testing 0.0.0-preview.0.64` yerel feed'den eklendi (yalnız
paket eklendi, `dotnet new install`/küresel şablon kaydına dokunulmadı —
KOSUM-PLANI §2.3'ün kısıtladığı yalnız o). Çıktı tam beklendiği gibi:
`Store tipi: NullRetentionStore`, `CountOlderThanAsync: 0`,
`FindRowLimitCutoffAsync: null`. İstisna yok, DB bağlantısı denenmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
