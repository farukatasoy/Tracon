# 23 — Saklama, Arşiv, Kota ve Çalıştırma-İçi Bütçe — koşum kaydı (2026-09-16, ap-s2)

> **Devir notu (oturum 15, ap-s2 — DOSYA 23 TAMAMEN BİTTİ):** MT-RET-001
> ile 076 arası **45/45 Geçti, 0 Kaldı**. Hiç ürün kusuru bulunmadı. Bir
> güvenlik bulgusu (`HATA` DEĞİL, tersine bir kapanış doğrulaması):
> `MT-RET-040`'ın önermesi tam tersine döndü — `RetentionEndpoints`/
> `QuotaEndpoints`'in kapsam sızıntısı zaten düzeltilmiş. **En önemli
> yöntem dersi (gelecek turlar için, `docs/hafiza/`'ya taşınmalı):**
> `QuotaEnforcer.cs`'in `_firedThresholds` süreç-içi önbelleği SQL ile
> `quota_usage` sıfırlamayla temizlenmez — "temiz dönem" gerektiren kota
> eşiği case'lerinde (070-076) uygulamanın **tamamen yeniden başlatılması**
> şart, yalnız DB satırını silmek yetmez (MT-RET-071'de üç art arda yanlış-
> negatif ölçümle keşfedildi). Sıradaki aile: `15-WORKFLOWS.md` (70 case).
>
> **Ortam:** ap-s2'nin PAYLAŞILAN PostgreSQL örneği (port 5082) bu aile için
> UYGUN DEĞİL — case'ler SQLite'a özgü doğrudan SQL fixture'ları kullanıyor.
> Bunun yerine kendi portumda (5087, boş olduğu doğrulandı — 5086 **başka bir
> şeride ait**, dokunulmadı) izole, tazeden kurulmuş bir SQLite örneği açıldı:
> `Tracon__Sqlite__ConnectionString="Data Source=/tmp/ap-s2-ret.db"`,
> `Tracon__PostgreSql__ConnectionString=""` (paketlenmiş `artifacts/bin/
> Tracon.Api/release/Tracon.Api.dll` doğrudan çalıştırılıyor, kod dokunulmadı).
> Bu, dosya 03/05'in "yapılandırma katmanı" tarifiyle aynı desendir. Arşiv
> testi için `/tmp/ap-s2-arsiv` kullanıldı, `/tmp/ap-s2-ret.db` ve `/tmp/ap-s2-
> ret.log` de tur sonunda topluca silinecek. Port 5087'deki geçici uygulama
> durduruldu; ap-s2'nin asıl uygulaması (port 5082, PostgreSQL) hâlâ ayakta.

---

## MT-RET-001 — Politika kaydedilir, `preview` doğru sayar, `run` gerçekten siler

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (kural 1.1 istisnası, ürün kusuru DEĞİL — şema
kolonları yeniden adlandırılmış):** Case'in SQL'i `tracon_runs` için
`created_at`/`updated_at` kolonları kullanıyor; gerçek şemada bunlar
`started_at`/`completed_at`'tır (ayrıca `is_streaming` NOT NULL, doldurulmalı).
Düzeltilmiş INSERT ile 12 `run_events` satırı (10'u 40 gün önce, 2'si bugün)
eklendi. `PUT .../run_events` politikası (`maxAgeDays:30`) kaydedildi.
`preview` → `matchingRows: 10`. `run` → `{"jobId":"...","target":"run_events"}`
(silinen sayı yok — beklenen). `history[0]` → `deletedRows: 10, error: null`.
Doğrudan SQL sayımı → **2** (yalnız güncel satırlar kaldı). `runs` özet satırı
korundu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-002 — Bilinmeyen hedef adı reddedilir (beyaz liste)

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (K-228 dil sınırı, kusur değil):** `400 Bad Request`,
başlık Türkçe "Bilinmeyen hedef" değil İngilizce **"Unknown target"**.
Gövde tanınan 16 hedefin tam listesini taşıyor, spec'teki liste ile birebir
eşleşiyor: `run_events, tool_invocations, traces, jobs, webhook_deliveries,
eval_case_results, workflow_checkpoints, skill_script_grants, attachments,
sessions, conversations, voice_sessions, run_scores, idempotency_keys,
run_inputs, document_embeddings`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-003 — `preview` hiçbir satır silmez

**Gerçek sonuç**
Üç ardışık `preview` çağrısı, üçü de `matchingRows: 0` (MT-RET-001 zaten
eskimiş satırları temizlemişti) — tutarlı, değişmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-004 — `run` ucu SENKRON silmez, bir iş kuyruğa yazar

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (kural 1.1 istisnası, kusur değil — alan adı
değişmiş):** `GET /api/jobs/{jobId}` yanıtında `kind` alanı **yok** —
bunun yerine `handlerKey: "tracon.retention"` var (aynı bilgiyi taşıyor,
farklı adla). `targetName: "run_events"` doğrulandı. `status: "Pending"`
(senkron tamamlanmış yanıt yok) — beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-005 — Boş politika tablosunda hiçbir şey silinmez

**Gerçek sonuç**
`tool_invocations` için (politikasız): `{"enabled":false,"matchingRows":0}`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-006 — `history` geçmiş çalıştırmaları listeler

**Gerçek sonuç**
`?target=run_events&take=5` → her iki kayıt da `id, target, deletedRows,
archivedRows, startedAt, completedAt, error` alanlarını taşıyor, `target`
her ikisinde de `run_events`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-010 — `audit_log` beyaz listede YOKTUR, asla otomatik silinmez

**Gerçek sonuç**
`PUT .../audit_log` → `400`, aynı "Unknown target" hata yolu, 16 hedef
listesinde `audit_log` hiç geçmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-011 — `Enabled=false` kaydı, config'teki varsayılanı da GÖLGELER

**Gerçek sonuç**
🚨🚨 **Doküman düzeltmesi (kural 1.1 istisnası, ürün kusuru DEĞİL — ama
ciddi bir kurulum hatası):** Case'in "Ön koşul"u
`Tracon:Retention:Traces:MaxAgeDays` ayarlamayı söylüyor — bu **yanlış
anahtar**. Kaynakta doğrulandı (`TraconRetentionOptions.cs`): `traces`
hedefi `Spans` özelliğine eşleniyor (`ForTarget`: `RetentionTargets.Traces
=> Spans`), `Traces` diye bir özellik **yok**. Ayrıca case hiç bahsetmiyor
ama `Enabled` özelliği **ayrı bir üst düzey bayraktır**, varsayılan
`false`dır ve "bir paket yükseltmesi tüketici config eklemeden veri
silmemeli" diye özellikle böyle tasarlanmış — `Tracon:Retention:Enabled=true`
**de** verilmeden hiçbir config varsayılanı devreye girmez.

Doğru kurulumla (`Tracon__Retention__Enabled=true` +
`Tracon__Retention__Spans__MaxAgeDays=14`) mekanizmanın **kendisi** tam
beklenen gibi çalıştı: DB kaydı `Enabled=false` iken `preview` →
`{"enabled":false,"maxAgeDays":null}` (config'in 14'ü **gölgeleniyor**,
tam beklenen). DB kaydı silinince (`DELETE`, `204`) `preview` →
`{"enabled":true,"maxAgeDays":14,"cutoff":"..."}` — config devreye
**girdi**, tam beklenen. Mekanizma sağlam; yalnız case'in kurulum
talimatı bayattı/eksikti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-013 — `run_events` silinirken `runs` özeti KORUNUR

**Gerçek sonuç**
`GET /api/runs/{id}` → `200`, `status: "Completed"` — özet satırı hâlâ orada.
`PUT /api/retention/runs` → `400 Bad Request` — `runs` beyaz listede yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-014 — `MaxRows` için config anahtarı YOKTUR, yalnız açık DB politikası

**Gerçek sonuç**
`Tracon:Retention:RunEvents:MaxRows=100` ayarlanıp DB politikası silindikten
sonra `preview` → `{"enabled":false,"maxAgeDays":null}` — hiçbir etki yok.
Kaynakta da doğrulandı: `RetentionTargetOptions` sınıfı yalnız `MaxAgeDays`
ve `Archive` özelliklerine sahip, `MaxRows` diye bir alan **hiç yok** —
yapısal olarak imkansız, çalışma zamanı testi gereksiz ama yine de koşuldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-015 — `run_inputs`, `Sessions`/`Conversations`'ın aksine varsayılan KAPALI DEĞİLDİR

**Gerçek sonuç**
🚨🚨🚨 **Bu case'in spec'teki metni İKİ KEZ yanlış — özgün metin doğruydu,
2026-08-13 turunun "düzeltmesi" YANLIŞTI, şimdi tekrar düzeltiliyor
(2026-09-17, ap-s2).** Üç ayrı deneyle netleştirildi:

1. Yalnız `Enabled=true` + `RunInputs:MaxAgeDays=60` (sessions'a hiç
   dokunulmadan): `run_inputs` → `{"enabled":true,"maxAgeDays":60}`,
   `sessions` → `{"enabled":false,"maxAgeDays":null}`.
2. `Enabled=true` + **her ikisine de** `MaxAgeDays=60` açıkça verilince:
   `run_inputs` → `{"enabled":true,"maxAgeDays":60}`, `sessions` →
   `{"enabled":true,"maxAgeDays":60}` — **ikisi de** config'i kullandı.

**Doğru mekanizma:** İkisi de config'ten okunabilir (`ForTarget` switch'inde
`RunInputs` İÇİN BİR CASE VAR — önceki "düzeltme" bunun tersini iddia
ediyordu, o iddia yanlıştı). Asıl fark her hedefin KENDİ gömülü
varsayılanıdır: `RunInputs.MaxAgeDays` C# tarafında `= 30` ile başlatılır
(`Enabled=true` yeterli, hedefe özel config gerekmez); `Sessions`/
`Conversations.MaxAgeDays` `null` ile başlatılır ("desteklenir ama
kapalı" — tüketici KENDİ `MaxAgeDays` değerini açıkça vermedikçe hiçbir
şey silinmez). Yani "run_inputs config kullanır, sessions kullanmaz"
özgün ifadesi **hiçbir hedefe config verilmediği senaryoda** doğrudur
(deney 1); "ikisi de config'i kullanabilir" ifadesi genel mekanizma
olarak doğrudur (deney 2). Önceki turun düzeltmesi ("run_inputs
KULLANMAZ, sessions KULLANIR") kaynaktaki `ForTarget` switch'iyle
doğrudan çelişiyordu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-020 — Yalnız `MaxRows`, 150 satırlık hedefte fazlayı siler

**Gerçek sonuç**
Spec'in kendi bloğu zaten önceki bir turda düzeltilmişti (`datetime()` biçim
tuzağı) — düzeltilmiş SQL ile 150 güncel satır eklendi. 🚨 **Ek gözlem (ürün
kusuru DEĞİL, bu oturumun kendi test sıralamasından kaynaklanan
kirlenme):** `preview` beklenen `50` değil **`52`** döndü — tabloda §1'den
kalan 2 satır daha vardı (run `11111111...`, toplam 152 satır, 152−100=52,
matematik birebir tutarlı). `run` sonrası `deletedRows: 52`
(`preview`'la birebir eşleşti), nihai SQL sayımı **tam `100`** — tüm
sonraki case'lerin varsaydığı "tabloda 100 satır" durumu doğru şekilde
sağlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-021 — Tablo sınırın ALTINDAYKEN hiçbir satır silinmez

**Gerçek sonuç**
`MaxRows=500` (tablo 100 satır) → `matchingRows: 0`, `cutoff: null`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-022 — `MaxAgeDays` VE `MaxRows` birlikte: daha YENİ eşik kazanır

**Gerçek sonuç**
`maxAgeDays:1, maxRows:10` (100 satır, hepsi güncel) → `matchingRows: 90`
(100−10) — hacim eşiği kazandı, tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-023 — `MaxRows` kiracı yalıtımı — Faz 36'nın kendi notu ARTIK YANLIŞ

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (kural 1.1 istisnası, kusur değil — şema
netliği):** Case'in kendi doğrulama sorgusu (`SELECT tenant_id, count(*)
FROM tracon_run_events GROUP BY tenant_id`) çalışmaz —
`tracon_run_events` tablosunda `tenant_id` sütunu **hiç yok** (kiracı
`runs` tablosundan JOIN ile çözülür). Düzeltilmiş sorgu:
`... e JOIN tracon_runs r ON e.run_id=r.id ... GROUP BY r.tenant_id`.

Uygulama `Tracon:Tenancy:Enabled=true` + `AllowHeaderResolution=true` ile
yeniden başlatıldı. `kiraci-alfa` 150 satır, `kiraci-beta` 5 satır (ayrı
`run` kayıtlarına bağlı). `kiraci-alfa` kapsamında (`X-Tracon-Tenant`
başlığıyla) `MaxRows=100` politikası çalıştırıldı: **`kiraci-alfa` 150 →
tam 100'e indi, `kiraci-beta` 5'te değişmeden kaldı.** Spec'in kendi notuyla
(bu bir "düzeltici bulgu", Faz 36'nın K-260 notu artık yanlış, K-279 sonrası
kod ilerledi) birebir tutarlı — yeniden doğrulandı, yeni bir bulgu değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-030 — Günlük kota aşıldığında `429` ve anlaşılır `ProblemDetails`

**Gerçek sonuç**
`maxRuns:1` kotası kaydedildi. İlk çağrı `200`. İkinci çağrı `429 Too Many
Requests`: `{"quotaMetric":"Runs","quotaPeriod":"Daily","quotaLimit":1,
"quotaUsed":1,"quotaResetsAt":"2026-09-18T00:00:00...Z"}` (gece yarısı UTC),
`Retry-After: 37214` (saniye, sayısal) — tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-031 — Kullanım sayaçları çalıştırma bittiğinde DÖRT satır üretir

**Gerçek sonuç**
Spec'in kendi düzeltmesi (`.usage[]`, kiracı-geneli `agentName:""`) doğru —
dört kombinasyon da mevcut: `("", Daily)`, `("", Monthly)`,
`("support", Daily)`, `("support", Monthly)`, hepsinin `runs≥1`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-032 — Kota aşımında DEVAM EDEN çalıştırma KESİLMEZ

**Gerçek sonuç**
Kota zaten doluyken yeni bir istek → `429` (MT-RET-030'un tekrarı, beklenen).
Case'in kendi notu gereği "devam eden çalıştırma kesilmiyor" iddiası
zamanlama güvenilir tetiklenemediği için ayrıca kanıtlanmadı, belgelenen
davranışa (K-162, kod yorumu) güveniliyor — spec'in kendi kabul ettiği sınır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-033 — Kota sayacı yalnız KÖK çalıştırmada işler (`Depth == 0`)

**Gerçek sonuç**
`summarize-and-translate` (iki agent adımlı workflow) **iki kez** ayrı ayrı
çalıştırıldı. Her ikisinde de günlük kiracı-geneli sayaç tam **`1`** arttı
(FARK=1, FARK2=1) — workflow'un iki adımı da TEK bir kök çalıştırma olarak
sayıldı, kod yorumundaki "yalnız kök işler" beklentisiyle birebir, tutarlı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-034 — Fiyatsız modelde `MaxCost` kuralı ETKİSİZDİR (ölü kod)

**Gerçek sonuç**
Ön koşul doğrulandı (`InputCostPerMillionTokens` `samples/Tracon.Api/
Program.cs`'te hiç yok). `maxCost:0.000001` kotasıyla 3 çağrı, üçü de
`200` — hiçbiri reddedilmedi. Kullanım kaydı: `runs:7, tokens:2064,
cost:0.0` — maliyet hiç birikmedi, şüphe (ölü kod) tam doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-035 — Kota "yaklaşıktır": eşzamanlı istekler aşabilir (kabul edilmiş sınır)

**Gerçek sonuç**
Temiz bir dönem elde etmek için `tracon_quota_usage`/`tracon_quotas`
tabloları SQL ile sıfırlandı (yalnız kendi izole test DB'm, kod değil).
`maxRuns:1` kotasıyla **5 eşzamanlı** istek gönderildi (spec'in kendi
düzeltmesindeki `for`+arka plan deseniyle, her istek kendi `uuidgen`'i ile).
**Beşi de `200` döndü** — hiçbiri `429` almadı; kabul edilmiş sınırın
beklenenden bile daha çarpıcı bir gösterimi (case yalnız "bazıları aşabilir"
diyordu, burada TAMAMI aştı). Kullanım kaydı `runs:5` (limit 1'e karşı) —
denetim-öncesi/artış-sonrası yarış durumu (K-159) doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-040 — 🚨 `RetentionEndpoints` VE `QuotaEndpoints` `RequireApiKeyScope` çağırmaz

**Gerçek sonuç**
🚨🚨 **Bu case'in önermesi TAMAMEN tersine dönmüş — bir GÜVENLİK AÇIĞI
KAPANMIŞ (kural 1.1 istisnası, ürün kusuru DEĞİL, aksine bir düzeltme
doğrulaması):** Kaynakta doğrulandı — `RetentionEndpoints.cs` VE
`QuotaEndpoints.cs`'in HER ucu artık `.RequireApiKeyScope(ApiKeyScope.
PlatformRead)` veya `PlatformAdmin` çağırıyor. Ampirik doğrulama: `RunsRead`
kapsamlı bir API anahtarıyla (`POST /api/api-keys` ile oluşturuldu)
`PUT /api/retention/jobs` → **`403 Forbidden`** (case'in beklediği `200`
DEĞİL). Pozitif kontrol: `PlatformAdmin` kapsamlı bir anahtarla aynı istek
→ `200 OK`. Kapsam denetimi artık doğru uygulanıyor — case'in tespit ettiği
sızıntı, bu case yazıldıktan SONRA düzeltilmiş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-041 — `'*'` politikası TÜM kiracıları değil, kurulum genelini siler

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (aynı `tenant_id` sütun eksikliği, MT-RET-023 ile
aynı kök neden — kusur değil):** Doğrulama sorgusu JOIN'siz `tenant_id`
sütununu arıyordu, düzeltildi. Çok kiracılık açık, `kiraci-alfa`'ya 5,
`kiraci-beta`'ya 5 eski (40 gün önce) satır eklendi (toplam alfa=105,
beta=10). Yalnız `kiraci-alfa` başlığıyla politika + `run`: **alfa 105 →
100** (5 eski satır silindi), **beta 10'da değişmeden kaldı** — tam
beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-042 — `quota_usage` hiçbir saklama hedefinde YOKTUR

**Gerçek sonuç**
`PUT /api/retention/quota_usage` → `400`, "Unknown target" (K-228, aynı
dil deseni), 16 hedef listesinde `quota_usage` yok — şüphe doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-043 — Bellek içi kurulumda saklama uçları hata vermez, hiçbir şey yapmaz

**Gerçek sonuç**
Repo dışı bir tüketici projesinde (`UsePostgreSql`/`UseSqlite`/`UseSqlServer`
hiç çağrılmadan) `TraconTestHost` kuruldu. `IRetentionStore`
çözümlendiğinde: `Store tipi: NullRetentionStore`,
`CountOlderThanAsync: 0`, `FindRowLimitCutoffAsync: null` — istisna
atılmadı, hiçbir veritabanı denenmedi. Birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-044 — `InMemoryRunStore.MaxRuns` aşılınca en eski `run` ile birlikte event/tool/heartbeat kaydı da düşer (Faz 108)

**Gerçek sonuç**
👤 case'in kendi notu gereği doğrudan `curl` ile koşulamaz (`MaxRuns` yalnız
kod düzeyinde kurucu parametresi). Spec'in kendi kaydettiği iki otomatik
karşılığı (`InMemoryRunStoreTests.When_the_upper_limit_is_exceeded_the_
oldest_run_is_dropped`, `InMemoryRunStoreStructureTests.Trim_drops_the_
dropped_runs_events_and_tool_invocations_too`) bu oturumda yeniden koşuldu:
**2/2 Geçti**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-050 — Düşük token tavanı, uzun tool döngülü bir `run`'ı KESER

**Gerçek sonuç**
`Tracon:AgentGraph:MaxTotalTokens=150` ile başlatıldı (önce MT-RET-035'ten
kalan bloklayıcı kota politikası fark edildi ve silindi). `POST
/api/agents/support/run` → `502`, `"Agent run failed"`, detay: `"The run
tree's token budget is exhausted (367/150)..."` (rakam 2026-08-26'nın
254/150'sinden farklı — gerçek token kullanımı çağrı başına değişir, kusur
değil). `GET /api/runs/{id}`: `status: Failed`, `error.type:
"run_budget_exceeded"`, `error.class: "QuotaExceeded"` — tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-051 — Kesilen `run` istemciye YARIM bir tool sonucu veya model mesajı SIZDIRMAZ

**Gerçek sonuç**
Aynı run'ın olay akışı: yalnız `run.started` → `run.failed`, arada
`tool_invoking`/`tool_invoked`/`message_delta` YOK. Spec'in kendi
"ölçüldü" notuyla birebir — hiçbir ara ilerleme sızmıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-052 — Hiçbir tavan tanımlı değilken davranış AYNIDIR (gerileme yok)

**Gerçek sonuç**
Tavan olmadan yeniden başlatıldı. Aynı sipariş sorusu tam bir yanıt
üretti (tool çağrısı + sonuç + son mesaj), `status: "Completed"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-053 — Maliyet tavanı tanımlıyken, fiyatı BİLİNMEYEN modelde tavan UYGULANMAZ

**Gerçek sonuç**
`MaxTotalCost=0.000001` + `MaxTotalTokens=200000` ile yeniden başlatıldı.
`status: "Completed"` — maliyet tavanı hiç uygulanmadı (fiyatsız model),
yüksek token tavanı da bu kısa run'ı kesmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-054 — Ağaçtaki TÜM dallar aynı bütçeyi görür (alt-agent çağrısı)

**Gerçek sonuç**
`MaxTotalTokens=150` ile yeniden başlatıldı. `router` agent'ı (`support`'u
alt-agent olarak çağırıyor) → `502`. Kök run: `status: Failed,
errorClass: QuotaExceeded, agentName: router` — alt çalıştırmanın (support)
harcaması kökü de kesti, aynı bütçe nesnesi paylaşıldığı doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-055 — `202 Accepted` ile arka planda koşan `run` da aynı şekilde kesilir

**Gerçek sonuç**
`Prefer: respond-async` ile aynı sorgu → kabul yanıtı (`runId`/`jobId`).
3 saniye sonra poll: `status: "Failed", errorClass: "QuotaExceeded"` —
arka plan yolu senkron yolla aynı kesme davranışını üretti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-060 — Düşük süre tavanı, bir tool döngülü `run`'ı KESER

**Gerçek sonuç**
`Tracon:AgentGraph:MaxDuration=00:00:00.001` ile başlatıldı. `502`,
`detail`: `"The run tree's time budget is exhausted
(00:00:00.0693770/00:00:00.0010000)..."` — kaydedilen metin biçimiyle
birebir. `status: Failed`, `error.type: "run_budget_exceeded"`,
`error.class: "QuotaExceeded"`. Geçen süre (0.069s) tavandan (0.001s)
büyük ama sıfıra yakın — kesme ikinci model turunda oldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-061 — Kesilen `run` istemciye YARIM bir tool sonucu veya model mesajı SIZDIRMAZ

**Gerçek sonuç**
Olay akışı tam 2 olay: `run.started` → `run.failed`, arada hiçbir tool/mesaj
olayı yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-062 — Hiçbir tavan tanımlı değilken davranış AYNIDIR (gerileme yok)

**Gerçek sonuç**
Tavansız yeniden başlatıldı. `status: "Completed"`, `error: null`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-063 — `202 Accepted` ile arka planda koşan (kuyruklu/dayanıklı) `run` da aynı şekilde kesilir

**Gerçek sonuç**
`Prefer: respond-async` ile aynı süre tavanı altında: ilk poll `Queued`,
birkaç saniye sonra `status: "Failed"`, `error.type: "run_budget_exceeded"`,
`error.class: "QuotaExceeded"` — senkron yolla aynı davranış.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-064 — İptal, süre tavanından ÖNCE gelirse hata sınıfı `Canceled` KALIR

**Gerçek sonuç**
Spec'in kendi notu gereği elle güvenilir tetiklenemez (zamanlama şansı
gerektirir). Otomatik karşılığı yeniden koşuldu:
`RunDeadlineTests.Cancelling_a_run_while_the_deadline_has_already_passed_
still_classifies_as_Canceled` → **1/1 Geçti**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-070 — Anahtar kapalıyken davranış birebir eskisiyle aynıdır

**Gerçek sonuç**
🚨 **Yöntem notu (kural 1.1 istisnası, kusur değil):** `support` agent'ının
günlük sayacı bu turun önceki case'lerinden zaten yüksekti (12) — SQL ile
`tracon_quota_usage`'dan silinerek temiz döneme getirildi. Varsayılan
(`PublishThresholdToRunStream` ayarlanmamış) yapılandırmayla: sıra `run →
update×14 → done`, **`custom` çerçevesi yok**. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-071 — Anahtar açıkken `custom` çerçevesi `done`'dan ÖNCE gelir

**Gerçek sonuç**
🚨🚨 **Önemli yöntem bulgusu (ürün kusuru DEĞİL — test metodolojisi
dersi, gelecek oturumlar için not edilmeli):** İlk denemede `custom`
çerçevesi göründü, ama SQL ile `quota_usage`'ı sıfırlayıp SÜRECİ
YENİDEN BAŞLATMADAN tekrar denendiğinde **hiç görünmedi** (3 art arda
deneme, hepsi 0). Kök neden kaynakta bulundu: `QuotaEnforcer.cs:325`
`_firedThresholds` adlı **süreç-içi bellek önbelleği** tutuyor ("Fast
path: already handled by THIS process in this period — no store
round-trip needed"). SQL ile DB satırını silmek bu önbelleği
**temizlemez** — yalnız uygulamayı yeniden başlatmak temizler. Süreç
yeniden başlatılıp DB satırı sıfırlandığında: `custom` çerçevesi
`done`'dan **önce** geldi, tam beklenen. Payload doğrulandı:
`"type":"Custom"`, `"customType":"tracon.quota.threshold"`,
`thresholdPercent:100, limit:1, used:1, metric:"Runs", period:"Daily"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-072 — Doğrudan akış ile `GET /api/runs/{id}/events` AYNI bildirimi sunar

**Gerçek sonuç**
Aynı `noticeId` (`01a0afa5-a744-72ed-a8d2-7428e99c2b5e`) hem doğrudan akışta
hem `GET .../events`'te — ikinci bir kopya üretilmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-073 — `Last-Event-ID` ile yeniden bağlanma bildirimi tekrar okuyabilir

**Gerçek sonuç**
`Last-Event-ID: 0` ile yeniden okuma → `1` (`custom` çerçevesi hâlâ okunabilir).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-074 — Aynı dönemde ikinci bir eşik geçişi bildirimi TEKRARLAMAZ

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (kusur değil):** Case'in kendi `Girilecek veri`
scripti `-H 'content-type: application/json'` **atlıyor** — bu yüzden
istek `415 Unsupported Media Type` ile sessizce başarısız oluyor, run hiç
başlamıyor (ölçüldü: başlıksız istekle iki denemenin ikisi de `custom
sayısı: 0` verdi, ama gerçekte HİÇ run olmamıştı). Başlık eklenip
`ThresholdPercents:0=50` + `maxRuns=2` ile temiz bir süreçte: **ilk çağrı
`1`** (50% eşiği ilk kez geçildi, bildirim var), **ikinci çağrı `0`**
(aynı eşik zaten claim edilmiş, tekrar yayımlanmadı) — tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-075 — Host yeniden başlatıldığında aynı eşik yeniden yayımlanmaz

**Gerçek sonuç**
MT-RET-074'ün eşiği claim edilmiş durumdayken (`notified_thresholds:
0:50` DB'de kalıcı) `maxRuns` yükseltilip **süreç tamamen yeniden
başlatıldı** (bellek içi `_firedThresholds` böylece temizlendi — bkz.
MT-RET-071'in bulgusu). Yeni bir `run`'ın akışında `custom` çerçevesi
**yok** (`0`) — kalıcı DB claim'i, bellek içi önbellek sıfırlansa bile
korundu. Tam beklenen; durabilite (146.4) doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-076 — Alt-agent ağacı eşiği geçirirse bildirim yalnız KÖK `run`'da bir kez görünür

**Gerçek sonuç**
Bu case'in kendi scripti zaten `content-type` başlığını taşıyordu (074'ün
aksine). `router` (`support`'u alt-agent olarak çağırıyor) için `maxRuns=1`
politikasıyla:
kök run'ın akışında tam **`1`** `custom` çerçevesi — alt çalıştırmanın
(`support`) kendi işlemi ayrı bir bildirim yazmadı, `Depth==0` kapısı
burada da geçerli.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-012 — Arşiv sink'i yokken `archive=true` HİÇBİR satır silmez

**Gerçek sonuç**
İki eski satır eklendi (40 gün önce). `archive=true` politikasıyla `run`
çalıştırıldı — **sink henüz kayıtlı değilken**: `deletedRows: 0,
archivedRows: 0`, satır sayısı değişmedi (4). Uygulama
`Tracon:Retention:ArchivePath=/tmp/ap-s2-arsiv` ile yeniden başlatıldı
(`FileSystemArchiveSink` artık kayıtlı) — aynı politika ile `run`:
**`deletedRows: 2, archivedRows: 2`**, `/tmp/ap-s2-arsiv/run_events/
2026-09-17.jsonl.gz` dosyası oluştu. İki yarı da tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı
