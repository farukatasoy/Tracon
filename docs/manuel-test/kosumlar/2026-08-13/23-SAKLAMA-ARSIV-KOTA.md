# 23 — Saklama, Arşiv ve Kota (`RET`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../23-SAKLAMA-ARSIV-KOTA.md`](../../23-SAKLAMA-ARSIV-KOTA.md) — `Ön koşul`, `Adımlar`,
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
> her case'in bloğu AYNEN durur. Tam metin:
> `git log --follow -- <bu dosya>`

---

## Temiz geçen case'ler (18)

| Case | Durum | Başlık |
|---|---|---|
| MT-RET-002 | ☑ | Bilinmeyen hedef adı reddedilir (beyaz liste) |
| MT-RET-003 | ☑ | `preview` hiçbir satır silmez |
| MT-RET-005 | ☑ | Boş politika tablosunda hiçbir şey silinmez |
| MT-RET-006 | ☑ | `history` geçmiş çalıştırmaları listeler |
| MT-RET-010 | ☑ | `audit_log` beyaz listede YOKTUR, asla otomatik silinmez |
| MT-RET-012 | ☑ | Arşiv sink'i yokken `archive=true` HİÇBİR satır silmez |
| MT-RET-013 | ☑ | `run_events` silinirken `runs` özeti KORUNUR |
| MT-RET-014 | ☑ | `MaxRows` için config anahtarı YOKTUR, yalnız açık DB politikası |
| MT-RET-021 | ☑ | Tablo sınırın ALTINDAYKEN hiçbir satır silinmez |
| MT-RET-022 | ☑ | `MaxAgeDays` VE `MaxRows` birlikte: daha YENİ eşik kazanır |
| MT-RET-023 | ☑ | `MaxRows` kiracı yalıtımı — Faz 36'nın kendi notu ARTIK YANLIŞ |
| MT-RET-030 | ☑ | Günlük kota aşıldığında `429` ve anlaşılır `ProblemDetails` |
| MT-RET-031 | ☑ | Kullanım sayaçları çalıştırma bittiğinde DÖRT satır üretir |
| MT-RET-032 | ☑ | Kota aşımında DEVAM EDEN çalıştırma KESİLMEZ |
| MT-RET-034 | ☑ | Fiyatsız modelde `MaxCost` kuralı ETKİSİZDİR (ölü kod) |
| MT-RET-041 | ☑ | `'*'` politikası TÜM kiracıları değil, kurulum genelini siler |
| MT-RET-042 | ☑ | `quota_usage` hiçbir saklama hedefinde YOKTUR |
| MT-RET-043 | ☑ | Bellek içi kurulumda saklama uçları hata vermez, hiçbir şey yapmaz |

## Ayrıntı taşıyan case'ler (8)

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
- SQL sayımı → `tracon_run_events` **2** satır (yalnız güncel olanlar).
- `tracon_runs` özet satırı **korundu** (1 satır) — `run_events`
  silinirken `runs` düşmedi.

⚠️ **İki doküman düzeltmesi gerekiyor:**

1. **Adım 1'deki `INSERT` güncel şemayla uyuşmuyor.** Doküman
   `tracon_runs (id, tenant_id, agent_name, status, created_at, updated_at)`
   yazıyor; tabloda `created_at`/`updated_at` sütunları **yok**, bunun yerine
   `started_at TEXT NOT NULL`, `completed_at TEXT NULL` ve
   `is_streaming INTEGER NOT NULL` var. Koşumda kullanılan doğru biçim:
   ```sql
   INSERT INTO tracon_runs (id, tenant_id, agent_name, status, started_at, is_streaming)
   VALUES ('11111111-1111-1111-1111-111111111111','default','support',1,datetime('now'),0);
   ```
2. **Adım 4'teki `sleep 2` yetersiz.** `run` bir iş kuyruğa yazar; işi
   çalıştıran arka plan servisi **uygulama ayaktayken** yoklama aralığında
   işler. İlk denemede `sleep 2` sonrası `history` boş (`[]`) ve tablo hâlâ 12
   satırdı; uygulama açık bırakılınca iş işlendi ve sonuç beklendiği gibi
   geldi. Doğrulama, `history` dolana kadar yoklanmalıdır (ya da
   `tracon_jobs.status` = 3 beklenmelidir).

**Durum:** ☐ Beklemede · ☑ Geçti (doküman SQL ve bekleme düzeltmesiyle) · ☐ Kaldı · ☐ Atlandı

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

## MT-RET-011 — `Enabled=false` kaydı, config'teki varsayılanı da GÖLGELER

**Gerçek sonuç**
Config varsayılanı `Tracon__Retention__Enabled=true` +
`Tracon__Retention__Spans__MaxAgeDays=14` ile verildi (aşağıdaki nota bak),
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
   `TraconRetentionOptions.Spans` nesnesine eşlenir
   (`TraconRetentionOptions.cs:88`, `RetentionTargets.Traces => Spans`).
   Dokümandaki `Tracon:Retention:Traces:MaxAgeDays` anahtarı **hiçbir şey
   yapmaz**; doğrusu `Tracon:Retention:Spans:MaxAgeDays`'tir.
2. **`Tracon:Retention:Enabled=true` zorunludur.**
   `RetentionPolicyResolver.ResolveAsync` config'e bakmadan önce
   `if (!options.Enabled) return null;` denetimi yapar
   (`RetentionPolicyResolver.cs:43`). Bu ön koşul olmadan hiçbir config
   varsayılanı devreye girmez — ilk denemede tam olarak bu yaşandı.

**Durum:** ☐ Beklemede · ☑ Geçti (doküman ön koşul düzeltmesiyle) · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-015 — `run_inputs`, `Sessions`/`Conversations`'ın aksine varsayılan KAPALI DEĞİLDİR

**Gerçek sonuç**
`Tracon__Retention__Enabled=true`, `…RunInputs__MaxAgeDays=60` ve
`…Sessions__MaxAgeDays=60` ile, hiçbir DB kaydı yokken:

| Hedef | `preview` yanıtı | Config etkili mi |
|---|---|---|
| `run_inputs` | `maxAgeDays: null, enabled: false, cutoff: null` | **HAYIR** |
| `sessions` | `maxAgeDays: 60, enabled: true, cutoff: 2026-06-13…` | **EVET** |

**Case'in iddiası tersine çıktı.** Kök neden `TraconRetentionOptions.ForTarget`
(`TraconRetentionOptions.cs:85-101`) — 16 saklama hedefinden yalnız **12'si**
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
`Tracon:Retention:<Hedef>:MaxAgeDays` yazan bir tüketici **sessiz bir
etkisizlikle** karşılaşır — ne hata, ne uyarı, ne log. Kasıtlı olabilir (bu
hedefler daha sonraki fazlarda eklendi) ama hiçbir yerde yazmıyor.
`MaxRows`'un config yüzeyine çıkmaması bilinçli bir karardır ve `ForTarget`'ın
üstünde yorumla belgelenmiştir; bu dört hedefin eksikliği için böyle bir not
yok.

**Durum:** ☐ Beklemede · ☑ Geçti (beklenen sonuç koda göre düzeltildi) · ☐ Kaldı · ☐ Atlandı

> **Güncelleme (S1-8, 2026-08-13):** `HATA-S1-005` bu oturumda kodlandı —
> `TraconRetentionOptions`'a dört hedef eklendi VE (ayrıca yakalanan
> ikincil bir eksiklik olarak) `TraconServiceCollectionExtensions.BindRetention`
> bu dört hedefi artık biliyor. Sonuç: **case'in ÖZGÜN (ilk yazılan) iddiası
> artık doğru** — `run_inputs` config varsayılanını KULLANIR, tıpkı
> `sessions` gibi. Canlıda doğrulandı: `Tracon__Retention__RunInputs__MaxAgeDays=60`
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
src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs` **sıfır** sonuç
döner; `RecordQuotaAsync` yalnız `RunRecordingAgent.cs:779`'dan çağrılır ve
`WorkflowEndpoints.cs` hiçbir yerde `RunRecordingAgent`'a atıfta bulunmaz.
Workflow çalıştırma yolu, agent çalıştırma yolundan (`AgentEndpoints` →
`RunRecordingAgent`) **tamamen ayrı** ve kota muhasebesine hiç uğramıyor.
`HATA-S1-006` olarak kaydedildi — Kritik: bir kiracı, tanımlı bir kotayı
`/api/agents/{ad}/run` yerine `/api/workflows/{ad}/run` üzerinden tamamen
atlatabilir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-006 düzeltmesiyle (K-394) yeniden koşuldu: `ozetle-ve-cevir` 2 kez çalıştırıldı, `ONCE=0 SONRA=2 FARK=2` (workflow'un TAMAMI tek kök "run" sayıldı, tutarlı). 429 kapısı da ayrıca doğrulandı. Bkz. `SONUCLAR-S1-2026-08-13.md`.

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
