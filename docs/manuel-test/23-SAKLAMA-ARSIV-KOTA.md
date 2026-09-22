# 23 — Saklama, Arşiv, Kota ve Çalıştırma-İçi Bütçe (`RET`)

> **Alan kodu:** `RET` · **Faz:** 21 (yalnız kota dilimi), 25, 36, 114, 128, 146
> **Kaynak:** `src/Tracon.Abstractions/Retention/` (tümü) ·
> `src/Tracon.Core/Retention/` (tümü) ·
> `src/Tracon.Sql.Shared/Internal/RetentionTargetRegistry.cs` ·
> `src/Tracon.AspNetCore/Endpoints/RetentionEndpoints.cs` ·
> `samples/Tracon.Api/FileSystemArchiveSink.cs` ·
> `src/Tracon.Abstractions/Quotas/` · `src/Tracon.Core/Quotas/`
> (`QuotaEnforcer`, `QuotaPeriodCalculator`, `InMemoryQuotaStore`) ·
> `src/Tracon.AspNetCore/Endpoints/QuotaEndpoints.cs` ·
> `src/Tracon.Core/Recording/RunRecordingAgent.cs`
> (`RecordQuotaAsync`, kök-çalıştırma kapısı) ·
> `src/Tracon.Abstractions/Runs/AgentRunBudget.cs` ·
> `src/Tracon.Core/Models/RunBudgetChatClient.cs` (Faz 114) ·
> `src/Tracon.Core/TraconOptions.cs`
> (`TraconAgentGraphOptions.MaxDuration`, Faz 128) ·
> `src/Tracon.Core/Recording/RunRecordingAgent.Notifications.cs`
> (`WriteQuotaThresholdNoticeAsync`) ·
> `src/Tracon.Abstractions/Runs/RunEventCustomTypes.cs`
> (`QuotaThreshold`) · `src/Tracon.AspNetCore/Endpoints/AgentEndpoints.cs`
> (`WriteQuotaThresholdNoticesAsync`, Faz 146).
>
> 🚨 **Faz 21'in yalnız KOTA dilimi bu dosyanındır.** Hız sınırı
> (`TraconRateLimitFilter`) ve webhook/olay yayını (`WebhookEndpoints`,
> `IWebhookPublisher`) **hiçbir manuel test dosyasına atanmamıştır** — bkz.
> dosyanın sonundaki "Sınır" tablosu ve `00-INDEKS.md` §8'deki not.
>
> 🚨 **§5'in bütçesi (Faz 114) `§4`'ün kotasıyla KARIŞTIRILMAZ.** Kota
> (`§4`) kiracı/agent × gün/ay ölçeğindedir ve devam eden çalıştırmayı asla
> kesmez (MT-RET-032). Faz 114'ün ağaç bütçesi (`AgentGraph.MaxTotalTokens`/
> `MaxTotalCost`) tek bir çalıştırma ağacına özeldir ve model turları
> ARASINDA denetlenir — devam eden bir tool döngüsünü ortasında keser.
>
> 🚨 **§6 (Faz 128) `§5`'in BEŞİNCİ boyutudur, ayrı bir mekanizma değil.**
> `AgentGraph.MaxDuration` aynı `AgentRunBudget` nesnesine, aynı kesme
> noktasına (`RunBudgetChatClient.ThrowIfExhausted`) ve aynı hata sınıfına
> (`QuotaExceeded`, K-630) oturur — yalnız kesen boyut süredir, token/maliyet
> değil. Kuyruğa alınmış (dayanıklı) `run`'da bugüne kadar HİÇBİR zaman
> sınırı yoktu; §6'nın kanıtladığı asıl yenilik budur (T-7).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** son tur (2026-09-16):
> [`../arsiv/manuel-test-kosum-2026-09/23-SAKLAMA-ARSIV-KOTA.md`](../arsiv/manuel-test-kosum-2026-09/23-SAKLAMA-ARSIV-KOTA.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır. 2026-08-13 turunun kaydı silindi (K-847);
> tam metin: git show 64c8a103:docs/manuel-test/kosumlar/2026-08-13/23-SAKLAMA-ARSIV-KOTA.md

---

## Bu dosya neyi kanıtlar

Üç fazın ortak teması: **veri yaşam döngüsü**. Kota, verinin *üretilme hızını*
sınırlar; saklama politikası *ne kadar süre* tutulacağını; `MaxRows` ise
*ne kadar hacim* tutulacağını belirler. Üçü de aynı ilkeyi paylaşır (K1):
varsayılan **hiçbir şey yapmaz** — kullanıcı açıkça bir politika/kota
tanımlamadıkça davranış değişmez.

```mermaid
flowchart TD
    A["POST /api/agents/x/run"] --> B{"Kota asildi mi?"}
    B -->|evet| E1["429 · ProblemDetails"]
    B -->|hayir| R["Calistirma (kok, Depth=0)"]
    R --> U["quota_usage artirilir"]
    R --> EV["run.completed webhook (kapsam disi)"]

    T["Zamanlanmis is: JobKind.Retention"] --> P{"Politika var mi?<br/>DB > config, K4"}
    P -->|MaxAgeDays| C1["esik = simdi - MaxAgeDays"]
    P -->|MaxRows| C2["esik = N. satirin zaman damgasi<br/>(FindRowLimitCutoffAsync)"]
    C1 --> D["parti parti DELETE<br/>tenant_id ile (K-279)"]
    C2 --> D
    D --> S{"archive=true?"}
    S -->|evet, sink YOK| X["HICBIR SATIR SILINMEZ"]
    S -->|evet, sink VAR| W["once arsive yaz, sonra sil"]

    classDef red fill:#7a1f1f,stroke:#3d0f0f,color:#ffffff
    class E1,X red
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Hız sınırı (`TraconRateLimitFilter`, Faz 21.1) | 🚨 **Hiçbir dosyaya atanmamış** — bkz. `00-INDEKS.md` §8 |
| Webhook / olay yayını (Faz 21.3) | 🚨 **Hiçbir dosyaya atanmamış** — bkz. `00-INDEKS.md` §8 |
| Maliyet gözlemlenebilirliği (`RunCost`, `/api/stats`) | `12-GOZLEMLENEBILIRLIK-MALIYET.md` (zaten üretildi) |
| API anahtarı kapsamının GENEL sözleşmesi | `13-KIRACI-VE-GUVENLIK.md` (zaten üretildi) |
| İş kuyruğunun genel davranışı (`JobKind`, zamanlama, `respond-async`) | `16-IS-KUYRUGU-VE-ZAMANLAMA.md` (zaten üretildi) — bu dosya yalnız `JobKind.Retention`'ın **kendine özgü** yükünü sınar |
| `IdempotencyKeys`/`RunInputs` hedeflerinin KENDİ işlevleri (idempotency mekaniği, yeniden oynatma) | `07-HTTP-YONETIM-API.md` / `21-DAYANIKLILIK-VE-IPTAL.md` — bu dosya yalnız onların **saklama** davranışını sınar |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama çalışır: `cd samples/Tracon.Api && dotnet run` → `http://localhost:5080`.
   ```bash
   export APB="Authorization: Bearer manuel-test-token-2026"
   export APU="http://localhost:5080/tracon"
   ```
3. Bazı case'ler (`§1`, `§3`) tabloya doğrudan SQL ile veri yazmayı gerektirir
   (gerçek bir çalıştırma zincirinden 150 satır üretmek pratik değildir — Faz
   36'nın kendi kapanış koşumu da bunu yaptı). SQLite kullanılıyorsa
   (`Tracon:Sqlite:ConnectionString`), veritabanı dosyası `sqlite3` ile
   doğrudan açılabilir.
4. Arşiv case'leri (`§2`) için `Tracon:Retention:ArchivePath` **varsayılan
   olarak boştur** — bu bilinçlidir, MT-RET-012'nin ön koşuludur.

> **Gerçek para uyarısı.** §4 (kota) ve §5 (çalıştırma-içi bütçe) gerçek
> OpenAI çağrısı yapar (küçük ölçekte). §1–§3 hiçbir model çağırmaz — saf
> veri düzlemi/SQL testleridir.

---

# 1 — Saklama politikası: temel yaşam döngüsü (Faz 25)

### MT-RET-001 — Politika kaydedilir, `preview` doğru sayar, `run` gerçekten siler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 25 |
| **İlgili karar** | — |

**Ön koşul**
- SQLite ile çalışıyor (`Tracon:Sqlite:ConnectionString` tanımlı, reset yapıldı).

**Adımlar**
1. `run_events` tablosuna doğrudan SQL ile, 10 tanesi 40 gün önceye tarihli 12
   satır ekle (gerçek bir `run_id`'ye bağlı — `runs` tablosunda önce bir satır olmalı).
2. `MaxAgeDays=30` politikası kaydet.
3. `preview` ile silinecek sayıyı oku.
4. `run` ile gerçekten çalıştır, geçmişi ve tablo satır sayısını doğrula.

**Girilecek veri**
```bash
# 1. Bir run + 12 run_event (10'u 40 gun once, 2'si bugun)
sqlite3 samples/Tracon.Api/tracon-manuel.db <<'SQL'
INSERT INTO tracon_runs (id, tenant_id, agent_name, status, created_at, updated_at)
VALUES ('11111111-1111-1111-1111-111111111111','default','support',1,datetime('now'),datetime('now'));
INSERT INTO tracon_run_events (run_id, seq, type, created_at)
SELECT '11111111-1111-1111-1111-111111111111', value, 0, datetime('now','-40 days')
FROM (SELECT value FROM json_each('[1,2,3,4,5,6,7,8,9,10]'));
INSERT INTO tracon_run_events (run_id, seq, type, created_at)
VALUES ('11111111-1111-1111-1111-111111111111', 11, 0, datetime('now')),
       ('11111111-1111-1111-1111-111111111111', 12, 0, datetime('now'));
SQL

# 2. Politika
curl -s -X PUT "$APU/api/retention/run_events" -H "$APB" -H "content-type: application/json" \
  -d '{"maxAgeDays":30,"archive":false,"enabled":true}' | jq

# 3. Onizleme
curl -s "$APU/api/retention/preview?target=run_events" -H "$APB" | jq

# 4. Calistir
curl -s -X POST "$APU/api/retention/run?target=run_events" -H "$APB" | jq
sleep 2
curl -s "$APU/api/retention/history?target=run_events" -H "$APB" | jq '.[0]'
sqlite3 samples/Tracon.Api/tracon-manuel.db "SELECT count(*) FROM tracon_run_events WHERE run_id='11111111-1111-1111-1111-111111111111';"
```

**Beklenen sonuç**
- `preview` `matchingRows: 10` döner.
- `history`'deki en yeni kayıt `deletedRows: 10`, `error: null` taşır.
- Doğrudan SQL sayımı **`2`** döner (yalnız güncel 2 satır kaldı).
- `runs` tablosundaki özet satır (adım 1'de eklenen) **silinmedi**.

---

### MT-RET-002 — Bilinmeyen hedef adı reddedilir (beyaz liste)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 25 |
| **İlgili karar** | 25.3 |

Negatif senaryo. `target` serbest metin değildir — aksi hâlde tablo adı
enjeksiyonu yüzeyi olurdu.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Var olmayan bir hedef adıyla politika kaydetmeyi dene.

**Girilecek veri**
```bash
curl -s -i -X PUT "$APU/api/retention/users_password_hashes" \
  -H "$APB" -H "content-type: application/json" \
  -d '{"maxAgeDays":1,"archive":false,"enabled":true}'
```

**Beklenen sonuç**
- `HTTP/1.1 400 Bad Request`.
- Gövde `"title":"Bilinmeyen hedef"` ve **tanınan hedeflerin tam listesini**
  (`run_events, tool_invocations, traces, jobs, webhook_deliveries,
  eval_case_results, workflow_checkpoints, skill_script_grants, attachments,
  sessions, conversations, voice_sessions, run_scores, idempotency_keys,
  run_inputs, document_embeddings` — **16 hedef**) taşır.

---

### MT-RET-003 — `preview` hiçbir satır silmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 25 |
| **İlgili karar** | 25.6 |

Negatif/kontrol senaryosu. `preview` ucu zorunludur çünkü **silmez**.

**Ön koşul**
- MT-RET-001'in politikası hâlâ kayıtlı (veya benzer bir eşleşen veri var).

**Adımlar**
1. `preview`'ı art arda üç kez çağır.

**Girilecek veri**
```bash
for i in 1 2 3; do
  curl -s "$APU/api/retention/preview?target=run_events" -H "$APB" | jq '.[0].matchingRows'
done
```

**Beklenen sonuç**
- Üç çağrı da **aynı** sayıyı döner — `preview` çağrısının kendisi veri
  silmez/değiştirmez.

---

### MT-RET-004 — `run` ucu SENKRON silmez, bir iş kuyruğa yazar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 25 |
| **İlgili karar** | — |

Sınır senaryosu. `RetentionEndpoints.RunAsync` doğrudan `IJobStore.EnqueueAsync`
çağırır; hiçbir `IRetentionStore` metodu bu ucun **içinde** çalışmaz.

**Ön koşul**
- Eşleşen bir hedef ve politika var (MT-RET-001).

**Adımlar**
1. `run`'ı çağır ve dönen `jobId`'yi not al.
2. `GET /api/jobs/{jobId}` ile işin türünü doğrula.

**Girilecek veri**
```bash
JOB=$(curl -s -X POST "$APU/api/retention/run?target=run_events" -H "$APB" | jq -r '.jobId')
curl -s "$APU/api/jobs/$JOB" -H "$APB" | jq '{kind, targetName, status}'
```

**Beklenen sonuç**
- Yanıt `{"jobId": "...", "target": "run_events"}` biçimindedir (silinen sayı
  **YOK** — henüz silinmedi).
- İşin `kind` alanı `"Retention"`dır, `targetName` `"run_events"`dır.
- `status` başlangıçta `Pending` (veya kısa süre sonra `Running`/`Completed`)'dir — asla senkron tamamlanmış bir yanıt gövdesiyle gelmez.

---

### MT-RET-005 — Boş politika tablosunda hiçbir şey silinmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 25 |
| **İlgili karar** | K1 |

Negatif/sınır senaryosu. "Varsayılan politika yoktur" iddiasının kanıtı — bir
sürüm yükseltmesi hiçbir şey silmemelidir.

**Ön koşul**
- Reset uygulandı; hiçbir hedef için politika kaydedilmedi.

**Adımlar**
1. Politikasız bir hedef için `preview` çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/retention/preview?target=tool_invocations" -H "$APB" | jq
```

**Beklenen sonuç**
- Yanıt ya boş bir dizi döner ya da `enabled: false` taşıyan bir kayıt döner
  (koşumda hangisi olduğu kaydedilir) — kritik olan `matchingRows` alanının
  **`0`** olması veya hiç hesaplanmamasıdır.

---

### MT-RET-006 — `history` geçmiş çalıştırmaları listeler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 25 |
| **İlgili karar** | — |

**Ön koşul**
- MT-RET-001 en az bir kez koşturuldu.

**Adımlar**
1. Geçmişi hedefe göre filtrele.

**Girilecek veri**
```bash
curl -s "$APU/api/retention/history?target=run_events&take=5" -H "$APB" | jq
```

**Beklenen sonuç**
- Her kayıt `id`, `target`, `deletedRows`, `archivedRows`, `startedAt`,
  `completedAt`, `error` alanlarını taşır.
- `target` alanı istenen filtreyle **eşleşir**, başka hedeflerin kayıtları görünmez.

### MT-RET-010 — `audit_log` beyaz listede YOKTUR, asla otomatik silinmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 25 |
| **İlgili karar** | K-059 |

Negatif senaryo. Denetim izi silinirse kanıt kaybolur; bu MT-RET-002'nin
özel, en kritik durumudur.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `audit_log` hedefiyle bir politika kaydetmeyi dene.

**Girilecek veri**
```bash
curl -s -i -X PUT "$APU/api/retention/audit_log" \
  -H "$APB" -H "content-type: application/json" \
  -d '{"maxAgeDays":1,"archive":false,"enabled":true}'
```

**Beklenen sonuç**
- `400 Bad Request` — `"Bilinmeyen hedef"` (MT-RET-002 ile **aynı** hata yolu,
  çünkü `audit_log` beyaz listede fiilen yoktur).
- `audit_log`'un tanınan hedefler listesinde **hiç geçmediği** doğrulanır.

---

### MT-RET-011 — `Enabled=false` kaydı, config'teki varsayılanı da GÖLGELER

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 25 |
| **İlgili karar** | — |

Sınır senaryosu. `RetentionPolicyResolver`: açık bir DB kaydı varsa
yapılandırmaya **hiç bakılmaz** — kayıt "kapalı" olsa bile. Bir kullanıcı
`appsettings`'te `MaxAgeDays=30` yazsa bile, DB'de `Enabled=false` bir kayıt
varsa **hiçbir şey silinmez**.

**Ön koşul**
- 🚨 `dotnet user-secrets set "Tracon:Retention:Traces:MaxAgeDays" "14"`
  **yanlıştır** — düzeltildi 2026-09-17, ap-s2. `traces` hedefi
  `TraconRetentionOptions.Spans`'a eşlenir (`Traces` diye bir özellik yok),
  VE ayrı bir üst düzey `Tracon:Retention:Enabled=true` bayrağı da
  gerekir (varsayılan `false` — bir paket yükseltmesi config eklenmeden
  veri silmesin diye). Doğru kurulum:
  `dotnet user-secrets set "Tracon:Retention:Enabled" "true"` **ve**
  `dotnet user-secrets set "Tracon:Retention:Spans:MaxAgeDays" "14"`,
  uygulama yeniden başlatılır.

**Adımlar**
1. `traces` hedefi için `Enabled=false` bir DB kaydı yaz.
2. `preview` çağır.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/retention/traces" -H "$APB" -H "content-type: application/json" \
  -d '{"maxAgeDays":14,"archive":false,"enabled":false}'

curl -s "$APU/api/retention/preview?target=traces" -H "$APB" | jq
```

**Beklenen sonuç**
- `preview` `matchingRows` alanı **`0`**'dır (veya hiç dönmez) — config'teki
  `14` günlük varsayılan **devreye girmez**, çünkü DB'de açık bir kayıt (kapalı
  olsa bile) config'i tamamen ezer.
- DB kaydını silip (`DELETE /api/retention/traces`) tekrar `preview`
  çağrıldığında config'in `14` günlük varsayılanı **artık devreye girer**.

---

### MT-RET-012 — Arşiv sink'i yokken `archive=true` HİÇBİR satır silmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 25 |
| **İlgili karar** | K-007 |

Negatif senaryo. `IArchiveSink` kayıtlı değilse arşivlenemeyen veri
düşürülmez — sessiz veri kaybını engelleyen kural.

**Ön koşul**
- `Tracon:Retention:ArchivePath` **ayarlanMAMIŞ** (varsayılan durum —
  `samples/Tracon.Api/Program.cs:653-658` bu anahtar boşsa `IArchiveSink`'i
  hiç kaydetmez).
- MT-RET-001'deki gibi eskimiş satırlar mevcut.

**Adımlar**
1. `archive=true` bir politika kaydet.
2. `run`'ı çalıştır.
3. Geçmişi ve tablo satır sayısını kontrol et.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/retention/run_events" -H "$APB" -H "content-type: application/json" \
  -d '{"maxAgeDays":30,"archive":true,"enabled":true}'

curl -s -X POST "$APU/api/retention/run?target=run_events" -H "$APB" | jq -r '.jobId'
sleep 2
curl -s "$APU/api/retention/history?target=run_events" -H "$APB" | jq '.[0]'
```

**Beklenen sonuç**
- Geçmiş kaydı `deletedRows: 0` taşır (veya `error` alanı sink eksikliğini
  belirtir — koşumda hangisi olduğu kaydedilir).
- Doğrudan SQL sayımı satırların **silinmediğini** doğrular.
- `Tracon:Retention:ArchivePath`'i ayarlayıp uygulamayı yeniden başlatınca
  (`FileSystemArchiveSink` artık kayıtlı), **aynı** politika ile `run`
  çalıştırıldığında satırlar hem arşivlenir hem silinir — `.jsonl.gz`
  dosyaları belirtilen yolda oluşur.

---

### MT-RET-013 — `run_events` silinirken `runs` özeti KORUNUR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 25 |
| **İlgili karar** | 25.1 |

Temel ilke: "özet kalır, ayrıntı düşer." `runs` **süresiz** saklanır ve beyaz
listede yer almaz — maliyet/istatistik onun üzerinden hesaplanır.

**Ön koşul**
- MT-RET-001 koştu (`run_events` silindi).

**Adımlar**
1. `runs` tablosunda ilgili satırın hâlâ var olduğunu doğrula.
2. `runs` hedefiyle bir politika kaydetmeyi dene (beklenen: reddedilir).

**Girilecek veri**
```bash
curl -s "$APU/api/runs/11111111-1111-1111-1111-111111111111" -H "$APB" | jq '.status'
curl -s -i -X PUT "$APU/api/retention/runs" -H "$APB" -H "content-type: application/json" \
  -d '{"maxAgeDays":1,"archive":false,"enabled":true}'
```

**Beklenen sonuç**
- `runs/{id}` `200` döner — özet satırı hâlâ mevcuttur.
- `PUT /api/retention/runs` `400` "Bilinmeyen hedef" döner — `runs` beyaz
  listede **yoktur**, silinemez bir hedeftir.

---

### MT-RET-014 — `MaxRows` için config anahtarı YOKTUR, yalnız açık DB politikası

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 25/36 |
| **İlgili karar** | K1 |

Sınır senaryosu. `RetentionPolicyResolver`'ın kendi yorumu: "yapılandırma
tabanlı varsayılanlar yalnız `MaxAgeDays` taşır... `MaxRows` yapılandırma
yüzeyine büyümez."

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `Tracon:Retention:RunEvents:MaxRows` gibi bir anahtar ayarlamayı dene.
2. Etkisiz olduğunu doğrula.

**Girilecek veri**
```bash
dotnet user-secrets set "Tracon:Retention:RunEvents:MaxRows" "100" \
  --project samples/Tracon.Api
```
(Uygulamayı yeniden başlat.)
```bash
curl -s "$APU/api/retention/preview?target=run_events" -H "$APB" | jq
```

**Beklenen sonuç**
- Bu anahtarın hiçbir etkisi **yoktur** — `TraconRetentionOptions` sınıfı
  bu alanı hiç okumaz (`RetentionPolicyResolver.cs:47-49`'daki açık yorum).
  `preview` politikasız kalmaya devam eder (MT-RET-005'teki gibi).
- `MaxRows`'u etkinleştirmenin **tek** yolu `PUT /api/retention/{target}` ile
  açık bir DB kaydı yazmaktır (MT-RET-020).

---

### MT-RET-015 — `run_inputs`, `Sessions`/`Conversations`'ın aksine varsayılan KAPALI DEĞİLDİR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 25/47 |
| **İlgili karar** | K-107 |

Sınır senaryosu. `RetentionTargets.UserDataTargets` yalnız `sessions` ve
`conversations`'ı taşır; `run_inputs` (yeniden oynatmanın ham kaynağı, aynı
hassasiyet sınıfı) bu listede **yoktur** — config tabanlı bir `MaxAgeDays`
varsayılanı `run_inputs` için normal şekilde çalışır.

**Ön koşul**
- `Tracon:Retention:Enabled` `true` olmalı (ayrı bir üst düzey bayrak,
  bkz. MT-RET-011) **ve**
  `dotnet user-secrets set "Tracon:Retention:RunInputs:MaxAgeDays" "60"`.

**Adımlar**
1. Uygulamayı yeniden başlat.
2. `run_inputs` için `preview` çağır (`sessions`'a hiç dokunmadan, kendi
   config'i verilmeden).

**Girilecek veri**
```bash
curl -s "$APU/api/retention/preview?target=run_inputs" -H "$APB" | jq
curl -s "$APU/api/retention/preview?target=sessions" -H "$APB" | jq
```

**Beklenen sonuç**

> ⚠️⚠️ **Bu beklenti İKİ KEZ yanlış çıktı — özgün metin doğruydu, 2026-08-13
> turunun "düzeltmesi" tam tersini iddia ederek YANLIŞ bir düzeltme yapmıştı,
> 2026-09-17'de (ap-s2) tekrar düzeltildi.** `TraconRetentionOptions.
> ForTarget` switch'inde `RunInputs` İÇİN BİR CASE **VARDIR**
> (`RetentionTargets.RunInputs => RunInputs`) — önceki "düzeltme" bunun
> tersini iddia ediyordu, kaynakla doğrudan çelişiyordu. Gerekçe ve iki
> ayrı deney `Gerçek sonuç` alanındadır.

- Hiçbir hedefe **kendi** `MaxAgeDays`'i verilmeden, yalnız `Enabled=true` +
  `RunInputs:MaxAgeDays=60` ile: `run_inputs` → `enabled:true,
  maxAgeDays:60` (kendi gömülü varsayılanı zaten `30`dur, config bunu
  ezer). `sessions` → `enabled:false, maxAgeDays:null` — **hiç config
  verilmediği için**, kendi gömülü varsayılanı `null`dur.
- Asıl mekanizma: `UserDataTargets` listesi "config yok sayılır" anlamına
  gelmez — her iki hedef de config'ten okunabilir (ikisine de açıkça
  `MaxAgeDays` verilirse ikisi de eşit şekilde etkinleşir). Fark yalnız
  her hedefin KENDİ gömülü varsayılanıdır: `RunInputs.MaxAgeDays = 30`
  (kod içinde), `Sessions.MaxAgeDays`/`Conversations.MaxAgeDays = null`
  ("desteklenir ama kapalı" — tüketici açıkça açmadıkça hiçbir şey
  silinmez).

### MT-RET-020 — Yalnız `MaxRows`, 150 satırlık hedefte fazlayı siler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 36 |
| **İlgili karar** | K-200, K-258 |

Faz kapanışında gerçek koşumla (SQLite, port 5080) doğrulanmış senaryonun
**birebir tekrarı** — `docs/arsiv/fazlar/36-SAKLAMA-HACIM-SINIRI.md`, 2026-08-06.

**Ön koşul**
- SQLite ile çalışıyor, reset yapıldı.

**Adımlar**
1. Bir `run`'a bağlı **150** `run_events` satırı doğrudan SQL ile ekle
   (hepsi güncel zaman damgalı — yaş bazlı silme ile karışmasın).
2. `MaxAgeDays` **boş**, `MaxRows=100` bir politika kaydet.
3. `preview` ile silinecek sayıyı oku.
4. `run` ile gerçekten çalıştır.
5. Kalan satır sayısını doğrudan SQL ile say.

**Girilecek veri**
```bash
sqlite3 samples/Tracon.Api/tracon-manuel.db <<'SQL'
-- 🚨 runs kolonlari created_at/updated_at DEGIL, started_at/is_streaming'dir
-- (MT-RET-001'de de duzeltildi) -- duzeltildi 2026-09-17, ap-s2:
INSERT INTO tracon_runs (id, tenant_id, agent_name, status, started_at, is_streaming)
VALUES ('22222222-2222-2222-2222-222222222222','default','support',1,datetime('now'),0);
WITH RECURSIVE seq(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM seq WHERE x < 150)
INSERT INTO tracon_run_events (run_id, seq, type, created_at)
SELECT '22222222-2222-2222-2222-222222222222', x, 0,
       strftime('%Y-%m-%dT%H:%M:%S.0000000Z', datetime('now', '-' || x || ' seconds'))
FROM seq;
SQL

curl -s -X PUT "$APU/api/retention/run_events" -H "$APB" -H "content-type: application/json" \
  -d '{"maxRows":100,"archive":false,"enabled":true}' | jq

curl -s "$APU/api/retention/preview?target=run_events" -H "$APB" | jq

curl -s -X POST "$APU/api/retention/run?target=run_events" -H "$APB" | jq -r '.jobId'
sleep 4
curl -s "$APU/api/retention/history?target=run_events" -H "$APB" | jq '.[0]'

sqlite3 samples/Tracon.Api/tracon-manuel.db \
  "SELECT count(*) FROM tracon_run_events WHERE run_id='22222222-2222-2222-2222-222222222222';"
```

> **Doküman düzeltmesi (kusur değil):** orijinal `Girilecek veri`
> `datetime('now', ...)` kullanıyordu — SQLite'ın varsayılan biçimi
> (`YYYY-MM-DD HH:MM:SS`, boşluk ayraçlı). `Tracon.Sqlite/Internal/
> SqliteDialect.cs:251-256`'nın `AddTimestamp`'i tüm `created_at` yazımlarında
> **`T` ayraçlı** ISO-8601 kullanır (`yyyy-MM-ddTHH:mm:ss.fffffffZ`) ve
> kod içi yorumu bunun **bilinçli** olduğunu söylüyor: "sözlüksel olarak zaman
> sıralı DEĞİLDİR" uyarısı tam bu yüzden var. İki biçim karışınca SQLite'ın
> metin karşılaştırması bozuluyor: `' ' (0x20) < 'T' (0x54)` olduğundan
> boşluk-ayraçlı HER satır, gerçek saatinden bağımsız olarak T-ayraçlı
> `cutoff`'tan küçük sayılıyor — ölçüldü: düzeltilmeden önce `preview`
> `matchingRows: 150` (hepsi), koşu `deletedRows: 150` (tamamı silindi)
> döndü. Üretim kodu `created_at`'i HER ZAMAN `AddTimestamp` üzerinden yazdığı
> için gerçek veride bu asla oluşmaz — yalnız bu fixture'ın SQLite'ın kısayol
> `datetime()`'ını kullanması hataya yol açtı. Yukarıdaki blok düzeltilmiş
> hâldir. Ayrıca `sleep 2` yetersizdi (iş kuyruğa yazılıp uygulama ayaktayken
> işleniyor, örüntü `MT-RET-001` ile aynı) — `sleep 4`'e çıkarıldı.

**Beklenen sonuç**
- `preview` `matchingRows: 50` döner (150 − 100).
- `history`'nin en yeni kaydı `deletedRows: 50` taşır — **önizleme ile gerçek
  koşu aynı sayıyı verir**.
- Doğrudan SQL sayımı **tam `100`** döner.
- Faz 36'nın kendi kapanış ölçümüyle (150→100, eşik 50) **birebir eşleşir**.

---

### MT-RET-021 — Tablo sınırın ALTINDAYKEN hiçbir satır silinmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 36 |
| **İlgili karar** | — |

Negatif senaryo. `FindRowLimitCutoffAsync` hedef `maxRows`'tan **az** satır
taşıyorsa `null` döner — hiçbir eşik hesaplanmaz, `COUNT(*)` hiç çalışmaz.

**Ön koşul**
- MT-RET-020'nin ortamı (şimdi tabloda 100 satır var).

**Adımlar**
1. `MaxRows=500` (tablodaki satır sayısından fazla) bir politika kaydet.
2. `preview` çağır.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/retention/run_events" -H "$APB" -H "content-type: application/json" \
  -d '{"maxRows":500,"archive":false,"enabled":true}'

curl -s "$APU/api/retention/preview?target=run_events" -H "$APB" | jq
```

**Beklenen sonuç**
- `preview` `matchingRows: 0` döner (veya hiç dönmez) — tablo zaten
  `500`'den küçük.
- `run` çalıştırılsa bile **hiçbir satır** silinmez.

---

### MT-RET-022 — `MaxAgeDays` VE `MaxRows` birlikte: daha YENİ eşik kazanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 36 |
| **İlgili karar** | 36.2 |

**Ön koşul**
- MT-RET-020'nin verisi (100 satır, hepsi güncel zaman damgalı).

**Adımlar**
1. Aynı hedefe **hem** `MaxAgeDays=1` **hem** `MaxRows=10` içeren bir politika
   kaydet (satırlar 1 günden yeni ama sayı 10'dan çok).
2. `preview` çağır.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/retention/run_events" -H "$APB" -H "content-type: application/json" \
  -d '{"maxAgeDays":1,"maxRows":10,"archive":false,"enabled":true}'

curl -s "$APU/api/retention/preview?target=run_events" -H "$APB" | jq
```

**Beklenen sonuç**
- Yaş eşiği (`MaxAgeDays=1`) satırların hiçbirini işaretlemez (hepsi
  güncel), ama hacim eşiği (`MaxRows=10`) 90 satırı işaretler.
- `preview` **daha çok silen** eşiği uygular: `matchingRows: 90`.

---

### MT-RET-023 — `MaxRows` kiracı yalıtımı — Faz 36'nın kendi notu ARTIK YANLIŞ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 36, 41 |
| **İlgili karar** | K-260 (eski), K-279 (güncel — düzeltti) |

Sınır senaryosu — **düzeltici bulgu**. `docs/arsiv/fazlar/36-SAKLAMA-HACIM-SINIRI.md`
(Plandan Sapmalar #2) *"`MaxRows` kiracı başına değil, tablo genelinde
çalışır"* diyor (K-260). Ölçüldü: `src/Tracon.Abstractions/Retention/
IRetentionStore.cs`'in **güncel** `FindRowLimitCutoffAsync` imzası bir
`string? tenantId` parametresi taşıyor ve `RetentionExecutor.cs:230` onu
gerçekten geçiriyor — bu, Faz 41'in `DeleteBatchAsync`'e kiracı sınırlaması
eklediği K-279 değişikliğiyle **aynı anda veya sonrasında** `MaxRows`'a da
uygulanmış. Faz 36'nın metni güncellenmemiş, kod ondan **ileri**.

**Ön koşul**
- Çok kiracılık açık (`Tracon:Tenancy:Enabled=true`, bkz.
  `13-KIRACI-VE-GUVENLIK.md`'nin kurulum notları). SQLite.

**Adımlar**
1. `kiraci-alfa` (`FIX-TENANT-01`) için `run_events` tablosuna **150** güncel satır ekle.
2. `kiraci-beta` (`FIX-TENANT-02`) için aynı tabloya **5** güncel satır ekle.
3. `kiraci-alfa` kapsamında `MaxRows=100` politikası kaydet ve çalıştır.
4. İki kiracının satır sayılarını ayrı ayrı kontrol et.

**Girilecek veri**
```bash
# (adim 1-2: sqlite3 ile tenant_id sutunuyla birlikte 150 + 5 satir eklenir)

curl -s -X PUT "$APU/api/retention/run_events" \
  -H "$APB" -H "X-Tracon-Tenant: kiraci-alfa" -H "content-type: application/json" \
  -d '{"maxRows":100,"archive":false,"enabled":true}'

curl -s -X POST "$APU/api/retention/run?target=run_events" \
  -H "$APB" -H "X-Tracon-Tenant: kiraci-alfa" | jq -r '.jobId'
sleep 4

# 🚨 tracon_run_events'te tenant_id sütunu YOK (kiracı runs'tan JOIN ile
# çözülür) — düzeltildi 2026-09-17, ap-s2:
sqlite3 samples/Tracon.Api/tracon-manuel.db \
  "SELECT r.tenant_id, count(*) FROM tracon_run_events e JOIN tracon_runs r ON e.run_id=r.id GROUP BY r.tenant_id;"
```

**Beklenen sonuç (K-279'un doğrulanması, K-260'ın çürütülmesi)**
- `kiraci-alfa` **`100`**'e iner (150 − 50, eşik yalnız kendi 150 satırına göre hesaplanır).
- `kiraci-beta` **`5`**'te değişmeden kalır — başka bir kiracının `MaxRows`
  politikası ona hiç dokunmaz.
- Doğrularsa: `36-SAKLAMA-HACIM-SINIRI.md`'nin "K-260" bölümü **güncelliğini
  yitirmiştir**; sonraki bir doküman bakım turu bu notu düzeltmelidir (bu
  oturumun kapsamı yalnız `docs/manuel-test/`'tir, faz dokümanı
  değiştirilmedi).

### MT-RET-030 — Günlük kota aşıldığında `429` ve anlaşılır `ProblemDetails`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 21 |
| **İlgili karar** | K-159, K-162 |

Faz kapanışında gerçek OpenAI ile doğrulanmış senaryonun tekrarı.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Kiracı geneli, günlük, `maxRuns=1` bir kota kaydet.
2. Bir kez çalıştır (geçmeli).
3. İkinci kez çalıştır (reddedilmeli).

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/quotas" -H "$APB" -H "content-type: application/json" \
  -d '{"agentName":null,"period":"Daily","maxRuns":1}' | jq

curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"merhaba"}'

curl -s -i -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"merhaba tekrar"}'
```

**Beklenen sonuç**
- İlk çağrı `200`/`201` döner.
- İkinci çağrı `HTTP/1.1 429 Too Many Requests`.
- Gövde `quotaMetric: "Runs"`, `quotaPeriod: "Daily"`, `quotaLimit: 1`,
  `quotaUsed: 1`, `quotaResetsAt` (gece yarısı UTC) alanlarını taşır.
- `Retry-After` başlığı saniye cinsinden bir sayı taşır.

---

### MT-RET-031 — Kullanım sayaçları çalıştırma bittiğinde DÖRT satır üretir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 21 |
| **İlgili karar** | — |

Bir çalıştırma **hem** kiracı-geneli **hem** agent-özel, **hem** günlük
**hem** aylık dönem satırını besler — dördü de bağımsız sayaçlardır.

**Ön koşul**
- Örnek uygulama çalışıyor. Kota tanımı olmasa bile `quota_usage` yazılır.

**Adımlar**
1. Bir agent'ı çalıştır.
2. Kullanım sorgusunu oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"merhaba"}' > /dev/null

curl -s "$APU/api/quotas/usage" -H "$APB" | jq '.usage[] | {agentName, period, runs, tokens}'
```

> **Doküman düzeltmesi (kusur değil):** orijinal `jq '[.[] | ...]'` yanıtı
> düz bir dizi sayıyordu; gerçek gövde `{tenantId, timeZone, usage:[...],
> definitions:[...], ...}` bir nesnedir — doğrusu `.usage[]`. Ayrıca
> kiracı-geneli satırların `agentName`'i `null` değil **boş metin `""`**
> döner (`select(.agentName==null)` hiç eşleşmez).

**Beklenen sonuç**
- Sonuç en az dört farklı `(agentName, period)` kombinasyonu içerir:
  `(null, Daily)`, `(null, Monthly)`, `("support", Daily)`, `("support", Monthly)`.
- Her birinin `runs` alanı en az `1` artmıştır.

---

### MT-RET-032 — Kota aşımında DEVAM EDEN çalıştırma KESİLMEZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 21 |
| **İlgili karar** | K-162 |

Sınır senaryosu. Denetim yalnız **yeni** çalıştırma başlamadan önce yapılır;
zaten başlamış bir çalıştırma kota o sırada dolsa bile kesilmez.

**Ön koşul**
- MT-RET-030'un kotası hâlâ dolu (`maxRuns=1`, o günkü kullanım `1`).

**Adımlar**
1. Kota zaten aşılmışken **uzun sürecek** bir istem gönder (bu istek
   `429` alacaktır — bu case'in amacı, aşımın çalışmakta olan bir isteği
   *kesmediğini*, yalnız *yeni* istekleri reddettiğini göstermektir).
2. Aynı anda (başka bir terminalde) kota sınırı içindeyken başlatılmış
   **uzun bir istem** varsa onun tamamlandığını doğrula.

**Girilecek veri**
```bash
# Kota zaten dolu; bu istek REDDEDILMELI (yeni istek):
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"bu istek kota doluyken gonderiliyor"}'
```

**Beklenen sonuç**
- `429` döner — bu davranışın kendisi zaten MT-RET-030'da kanıtlandı.
- **Not:** "devam eden bir çalıştırmanın kesilmediğini" pratikte göstermek,
  kota dolmadan HEMEN önce başlatılmış uzun bir çalıştırmayı gerektirir —
  zamanlaması güvenilir biçimde tetiklenemez (benzer gerekçeyle
  `03-KALICILIK-POSTGRESQL.md`'de migration atomikliği testi de atlanmıştı).
  Bu case bilinçli olarak yalnız **belgelenen davranışı** (K-162) not düşer;
  güvence Faz 21'in kendi birim testlerine (`QuotaEnforcer` çağrı yeri —
  `RecordAsync` **her zaman** `RunRecordingAgent.CompleteAsync`'ten çağrılır,
  ön kontrol yalnız HTTP girişinde) bırakılmıştır.

---

### MT-RET-033 — Kota sayacı yalnız KÖK çalıştırmada işler (`Depth == 0`)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 21 |
| **İlgili karar** | — |

Sınır senaryosu — belgelenen, kasıtlı davranış (`RunRecordingAgent.cs:773-782`'nin
kendi yorumu: *"Kota ve olay yayını yalnızca kök çalıştırmada işler. Alt
çalıştırma aynı kullanıcı isteğinin parçasıdır."*). Bu case bunu bir
**çok adımlı workflow** ile ampirik olarak ölçer — kaç `quota_usage` satırı
üretildiği koşumdan önce **bilinmiyor**, koşumda kaydedilir.

**Ön koşul**
- `summarize-and-translate` (`FIX` — sıralı, iki agent adımlı) workflow fixture'ı hazır.

**Adımlar**
1. Kullanım sayaçlarını sıfırdan itibaren gözlemlemek için `support` agent'ının
   günlük `runs` sayacını oku.
2. `summarize-and-translate` workflow'unu çalıştır (iki agent adımı içerir).
3. Sayacı tekrar oku, artışı hesapla.

**Girilecek veri**
```bash
ONCE=$(curl -s "$APU/api/quotas/usage" -H "$APB" | jq '[.[] | select(.agentName==null and .period=="Daily")][0].runs // 0')

curl -s -X POST "$APU/api/workflows/summarize-and-translate/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"Bu metni ozetle ve Ingilizceye cevir: Tracon bir NuGet paket ailesidir."}' | jq -r '.runId // .id'

sleep 3
SONRA=$(curl -s "$APU/api/quotas/usage" -H "$APB" | jq '[.[] | select(.agentName==null and .period=="Daily")][0].runs // 0')

echo "ONCE=$ONCE SONRA=$SONRA FARK=$((SONRA - ONCE))"
```

**Beklenen sonuç (koşumda ölçülecek, önceden iddia edilmez)**
- `FARK` değeri **kaydedilir**. Kod yorumuna göre beklenen `1`dir (workflow'un
  kendisi tek bir kök `Depth=0` kapsamı sayılıyorsa) — ama workflow'un HER
  adımının kendi `runId`'si olduğu bilindiğinden (K-014), her adımın da
  KENDİ kök çalıştırması sayılıp sayılmadığı (`FARK=2`) doğrulanmalıdır.
- Hangi sonuç çıkarsa çıksın, davranış tutarlı olmalıdır: art arda aynı
  workflow'u çalıştırmak her seferinde **aynı** artışı üretmelidir.

---

### MT-RET-034 — Fiyatsız modelde `MaxCost` kuralı ETKİSİZDİR (ölü kod)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 21 |
| **İlgili karar** | — |

Negatif senaryo — **önceden not düşülmüş şüpheli bulgunun testi**
(`00-INDEKS.md` §8, `12-GOZLEMLENEBILIRLIK-MALIYET.md` üretilirken bulundu).
`QuotaDecision.CostFellBackToTokens` hiçbir yerde `true` yapılmaz;
`RunRecordingAgent.RecordQuotaAsync`'in kendi yorumu: fiyat tanımsızsa
`Cost` `null` kalır (**sıfır değil**) ve `InMemoryQuotaStore.Increment`
bunu toplama hiç **katmaz** — yani fiyatsız bir modelin çalıştırmaları bir
`MaxCost` kuralına karşı pratikte hiç sayılmaz.

**Ön koşul**
- `samples/Tracon.Api/Program.cs` hiçbir model fiyatı tanımlamaz
  (`grep -rn "InputCostPerMillionTokens" samples/Tracon.Api/Program.cs`
  boş döner — bu ölçüldü, dosya değiştirilmedi).

**Adımlar**
1. Kiracı geneli, günlük, `maxCost=0.000001` (fiilen sıfıra yakın) bir kota kaydet.
2. Agent'ı birkaç kez çalıştır.
3. Kotanın hiç tetiklenmediğini doğrula.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/quotas" -H "$APB" -H "content-type: application/json" \
  -d '{"agentName":null,"period":"Daily","maxCost":0.000001}'

for i in 1 2 3; do
  curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/agents/support/run" \
    -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
    -d '{"message":"merhaba"}'
done

curl -s "$APU/api/quotas/usage" -H "$APB" | jq '[.usage[] | select(.agentName=="" and .period=="Daily")][0]'
```

> **Doküman düzeltmesi (kusur değil):** `.[] | select(.agentName==null...)`
> — aynı `MT-RET-031`/`033` sapması: gövde `.usage[]` içinde, ve kiracı-geneli
> `agentName` `null` değil `""`.

**Beklenen sonuç (şüphenin doğrulanması)**
- Üç çağrının **hiçbiri** `429` almaz — maliyet sıfıra yakın bir sınırla
  bile aşılmaz.
- `quota_usage` kaydında `cost` alanı **`0`** kalır (birikmez) —
  `runs`/`tokens` alanları artarken `cost` sabit kalırsa şüphe doğrulanmıştır.
- Doğrularsa: dokümantasyonun vaat ettiği "maliyet token'a düşer" yedek yolu
  fiilen **çalışmaz** — bu, `MaxCost` kuralı tanımlayan ama fiyat
  yapılandırmayan her kurulum için sessiz bir etkisizliktir.

---

### MT-RET-035 — Kota "yaklaşıktır": eşzamanlı istekler aşabilir (kabul edilmiş sınır)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 21 |
| **İlgili karar** | K-159 |

Sınır senaryosu. Denetim çalıştırma **öncesinde**, sayaç artışı **sonrasında**
yapılır — sıkı bir kilit yoktur. Bu, dokümante edilmiş, **kabul edilmiş**
bir kusurdur ("yaklaşık kota" dürüst bir ifadedir).

**Ön koşul**
- Kiracı geneli, günlük, `maxRuns=1` bir kota kaydet (temiz bir dönemde, henüz kullanım yok).

**Adımlar**
1. Aynı anda (paralel) 5 istek gönder.
2. Kaç tanesinin `200`, kaç tanesinin `429` aldığını say.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/quotas" -H "$APB" -H "content-type: application/json" \
  -d '{"agentName":"support","period":"Daily","maxRuns":1}'

for i in 1 2 3 4 5; do
  ( curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/agents/support/run" \
    -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
    -d "{\"message\":\"esZAMANLI istek $i\"}" ) &
done
wait
```

> **Doküman düzeltmesi (kusur değil):** orijinal `seq 1 5 | xargs -P5 -I{}
> curl ... -H "Idempotency-Key: $(uuidgen)"` **tek** `uuidgen` çağırır —
> `$(uuidgen)` `xargs`'a değil dış kabuğa ait, komut satırı BİR KEZ kurulur.
> Sonuç: 5 istek AYNI `Idempotency-Key`'i paylaşır ve test kotayı değil
> idempotency çakışmasını ölçer (ölçüldü: `409` × 4, `500` × 1 — hiç `200`
> yok, hiç `429` yok). Düzeltme: her istek kendi alt-kabuğunda (`&` ile arka
> plana atılan ayrı bir komut) çalışmalı ki `$(uuidgen)` her seferinde yeniden
> değerlendirilsin. Ayrıca `support` bu dosyanın önceki case'lerinde zaten
> kullanıldığından (kirli sayaç), koşum **temiz** bir agent kapsamıyla
> (`router`) yapıldı — gerçek koşumda dosyanın kendi sırasını izleyen
> bir oturum `support` ile de temiz başlayabilir.

**Beklenen sonuç**
- `maxRuns=1` olmasına rağmen `200` sayısı **1'den fazla olabilir** —
  denetim öncesinde yapıldığı için 5 istek neredeyse aynı anda kotayı
  "boş" görebilir. Bu bir **kusur değildir**; K-159'un dokümante ettiği
  kabul edilmiş yaklaşıklıktır.
- Koşumda gerçek `200`/`429` dağılımı kaydedilir (`1`, `2` veya daha fazla
  `200` görülebilir — makine yüküne bağlıdır). **Sıfır** `200` görülmesi
  ise gerçek bir kusurdur (kotanın hiç izin vermediği anlamına gelir).

### MT-RET-040 — `RetentionEndpoints` VE `QuotaEndpoints` `RequireApiKeyScope` ÇAĞIRIR (düzeltilmiş)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 21, 25 |
| **İlgili karar** | — |

🚨🚨 **Başlık ve önerme 2026-09-17'de (ap-s2) tersine çevrildi — güvenlik
açığı KAPANMIŞ.** Case yazıldığında `grep -n "RequireApiKeyScope"
src/Tracon.AspNetCore/Endpoints/RetentionEndpoints.cs
src/Tracon.AspNetCore/Endpoints/QuotaEndpoints.cs` sıfır sonuç veriyordu.
**Artık böyle değil**: her iki dosyanın HER ucu `.RequireApiKeyScope(
ApiKeyScope.PlatformRead)` veya `PlatformAdmin` taşıyor. Statik bearer
token'a sahip anahtarların kapsamı artık doğru uygulanıyor — bu bir kapanış
kanıtıdır, açık kanıtı değil.

**Ön koşul**
- Bir API anahtarı sistemi kurulu (`POST /api/api-keys` ile) — yalnız
  `ApiKeyScope.RunsRead` taşıyan bir anahtar oluşturulur.

**Adımlar**
1. Bu salt-okunur anahtarla `PUT /api/retention/{target}` dene (beklenen: `403`).
2. Pozitif kontrol: `PlatformAdmin` kapsamlı bir anahtarla aynı istek (beklenen: `200`).

**Girilecek veri**
```bash
RO_KEY=$(curl -s -X POST "$APU/api/api-keys" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"ret-ro-test","scopes":["RunsRead"]}' | jq -r '.plaintextKey')
export RO="Authorization: Bearer $RO_KEY"

curl -s -i -X PUT "$APU/api/retention/jobs" -H "$RO" -H "content-type: application/json" \
  -d '{"maxAgeDays":30,"archive":false,"enabled":true}'

ADMIN_KEY=$(curl -s -X POST "$APU/api/api-keys" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"ret-admin-test","scopes":["PlatformAdmin"]}' | jq -r '.plaintextKey')
curl -s -i -X PUT "$APU/api/retention/jobs" -H "Authorization: Bearer $ADMIN_KEY" \
  -H "content-type: application/json" -d '{"maxAgeDays":30,"archive":false,"enabled":true}'
```

**Beklenen sonuç**
- `RunsRead` kapsamlı anahtar → **`403 Forbidden`** — yetersiz kapsam
  reddedilir.
- `PlatformAdmin` kapsamlı anahtar → **`200 OK`** — doğru kapsam kabul
  edilir.
- `QuotaEndpoints` için de aynı doğrulama geçerlidir (kaynakta
  `.RequireApiKeyScope(ApiKeyScope.PlatformRead/PlatformAdmin)` her ucunda
  mevcut).

---

### MT-RET-041 — `'*'` politikası TÜM kiracıları değil, kurulum genelini siler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 25 |
| **İlgili karar** | K-279 |

Pozitif kontrol. 25.3: `tenantId` `null` ise işlem kurulum genelindedir;
`'*'` "bütün kiracılara uygulanan bir politika" demektir — "tek çağrıda
bütün kiracıları sil" demek **değildir**. `IRetentionStore`'un imzası bunu
Faz 41'den beri (`tenantId` zorunlu parametre) yapısal olarak garanti eder.

**Ön koşul**
- Çok kiracılık açık. `kiraci-alfa` ve `kiraci-beta` her ikisinde de
  eskimiş `run_events` satırları var (MT-RET-023'ün kurulumu).

**Adımlar**
1. `kiraci-alfa` kapsamında (kendi kiracı başlığıyla) bir politika kaydet.
2. `run`'ı **yalnız** `kiraci-alfa` başlığıyla tetikle.
3. `kiraci-beta`'nın satırlarının etkilenmediğini doğrula.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/retention/run_events" \
  -H "$APB" -H "X-Tracon-Tenant: kiraci-alfa" -H "content-type: application/json" \
  -d '{"maxAgeDays":30,"archive":false,"enabled":true}'

curl -s -X POST "$APU/api/retention/run?target=run_events" \
  -H "$APB" -H "X-Tracon-Tenant: kiraci-alfa" | jq -r '.jobId'
sleep 4

# 🚨 tenant_id sütunu run_events'te yok, runs'tan JOIN gerekir (bkz. MT-RET-023)
sqlite3 samples/Tracon.Api/tracon-manuel.db \
  "SELECT r.tenant_id, count(*) FROM tracon_run_events e JOIN tracon_runs r ON e.run_id=r.id GROUP BY r.tenant_id;"
```

**Beklenen sonuç**
- Yalnız `kiraci-alfa`'nın eskimiş satırları silinir.
- `kiraci-beta`'nın satır sayısı **değişmez**.

---

### MT-RET-042 — `quota_usage` hiçbir saklama hedefinde YOKTUR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 21, 25 |
| **İlgili karar** | — |

Negatif senaryo — **şüpheli bulgu**. `docs/arsiv/fazlar/21-KOTA-VE-OLAY-YAYINI.md`'nin
kendi devir notu ("Faz 25 (saklama) için") şunu yazıyordu: *"`quota_usage`
geçmiş dönemleri sonsuza dek tutar — yalnız geçerli dönem sorgulanır,
eskiler ölü veridir."* Bu, Faz 25'in ele alması beklenen bir iş kalemiydi.
Ölçüldü: `RetentionTargets.All`'un güncel **16** üyesinde `quota_usage`
(veya `QuotaUsage` sabiti) **yoktur** — `grep -rn "quota_usage" src/
Tracon.Abstractions/Retention/ src/Tracon.Sql.Shared/Internal/
RetentionTargetRegistry.cs` sıfır sonuç döner. `webhook_deliveries` (aynı
devir notunda anılan ikinci tablo) listeye **eklenmiş**; `quota_usage`
**eklenmemiş**.

**Ön koşul**
- Yok — bu bir beyaz liste denetimidir, veri gerektirmez.

**Adımlar**
1. `quota_usage` hedefiyle bir politika kaydetmeyi dene.
2. Tanınan hedef listesini oku.

**Girilecek veri**
```bash
curl -s -i -X PUT "$APU/api/retention/quota_usage" \
  -H "$APB" -H "content-type: application/json" \
  -d '{"maxAgeDays":90,"archive":false,"enabled":true}'
```

**Beklenen sonuç (şüphenin doğrulanması)**
- `400 Bad Request` — `"Bilinmeyen hedef"`; gövdedeki tanınan hedef
  listesinde `quota_usage` **geçmez**.
- Doğrularsa: `quota_usage` tablosu hiçbir zaman otomatik temizlenemez;
  eski dönem sayaçları (aylar/yıllar sonra binlerce satır) sonsuza dek
  birikir. Bu bir veri kaybı riski değildir (kota geçmişi zararsızdır) ama
  **hacim boşluğudur** — `MT-RET-023`'ün de gösterdiği gibi tablo büyüklüğü
  önemsenen bir sistemde eksik bir hedeftir. `ADAYLAR.md`'ye
  aday olarak yazılabilir (kodlama değil, bu oturumun kapsamı dışı).

---

### MT-RET-043 — Bellek içi kurulumda saklama uçları hata vermez, hiçbir şey yapmaz

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 25 |
| **İlgili karar** | K-018 |

Sınır senaryosu. `NullRetentionStore` (`IRetentionStore`'un bellek içi
uygulaması) her zaman boş/sıfır döner. Tasarım kuralı #1'in ("sıfır sürpriz —
`UsePostgreSql()` çağrılmazsa hiçbir şey kırılmaz") saklama alanındaki karşılığı.

**Ön koşul**
- .NET SDK kurulu. Yerel NuGet feed hazır.

**Adımlar**
1. Hiçbir SQL sağlayıcısı eklenmeden bir `TraconTestHost` kur.
2. Politika kaydetmeyi ve önizleme almayı dene.

**Girilecek veri**
```bash
mkdir -p ~/tracon-manuel/saklama-testleri && cd ~/tracon-manuel/saklama-testleri
dotnet new console
SURUM=$(ls ~/tracon-local-feed/Tracon.Testing.*.nupkg | sed 's#.*Tracon.Testing\.##;s#\.nupkg##')
dotnet add package Tracon.Testing --version "$SURUM"

cat > Program.cs <<'EOF'
using Tracon;
using Tracon.Testing;
using Microsoft.Extensions.DependencyInjection;

await using var host = await TraconTestHost.StartAsync(o =>
{
    o.ModelProvider = new FakeModelProvider().EchoesUserMessage();
    // DIKKAT: UsePostgreSql/UseSqlite/UseSqlServer HIC cagrilmiyor.
});

var store = host.Services.GetRequiredService<IRetentionStore>();
Console.WriteLine("Store tipi: " + store.GetType().Name);

var count = await store.CountOlderThanAsync("run_events", null, DateTimeOffset.UtcNow, CancellationToken.None);
Console.WriteLine("CountOlderThanAsync: " + count);

var cutoff = await store.FindRowLimitCutoffAsync("run_events", null, 10, CancellationToken.None);
Console.WriteLine("FindRowLimitCutoffAsync: " + (cutoff?.ToString() ?? "null"));
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `Store tipi: NullRetentionStore`.
- `CountOlderThanAsync: 0` — istisna atılmaz.
- `FindRowLimitCutoffAsync: null` — istisna atılmaz.
- Hiçbir veritabanı bağlantısı denenmez.

---

### MT-RET-044 — `InMemoryRunStore.MaxRuns` aşılınca en eski `run` ile birlikte event/tool/heartbeat kaydı da düşer (Faz 108)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Düşük |
| **İlgili faz** | Faz 108 |
| **İlgili karar** | — |

`MaxRuns` `IRetentionStore`'un SQL politikasından ayrı, bellek içi store'a
özgü bir hacim sınırıdır (`config` anahtarı yoktur, yalnız kurucu parametresi
— MT-RET-014'ün "yalnız açık DB politikası" kuralının bellek içi karşılığı).
Sınır aşılınca yalnız `runs` satırı değil, o `run`'ın event log'u, tool
invocation log'u ve heartbeat kaydı da düşmelidir; aksi hâlde bellekte
sahipsiz veri birikir.

> 👤 **İnsan gerekir** — `MaxRuns` `AddTracon()` üzerinden `config`'ten
> ayarlanamaz, yalnız `new InMemoryRunStore { MaxRuns = N }` ile kod
> düzeyinde verilir; standart bir `curl` senaryosu yoktur.

**Otomatik karşılığı** (koşuldu, 2026-08-26):
- `InMemoryRunStoreTests.When_the_upper_limit_is_exceeded_the_oldest_run_is_dropped`
  (`tests/Tracon.Core.UnitTests/Storage/InMemoryStoreTests.cs`) — en eski
  `run` satırının düştüğünü kanıtlar.
- `InMemoryRunStoreStructureTests.Trim_drops_the_dropped_runs_events_and_tool_invocations_too`
  (`tests/Tracon.Core.UnitTests/Storage/InMemoryRunStoreStructureTests.cs`) —
  aynı trim'in event log'unu ve tool invocation log'unu da sildiğini kanıtlar
  (Faz 108'in `partial` ayrıştırmasından önce bu ikinci iddia için ayrı bir
  test yoktu).

---

# 5 — Çalıştırma-içi bütçe tavanı (Faz 114)

`support` agent'ı sipariş sorularında **her zaman** bir tool çağırır
(`get_order_status`) — bu, gerçek bir iki turlu tool döngüsü üretir: (1) model
tool'u çağırır, (2) tool sonucu modele döner ve model yanıtı üretir.
`AgentGraph.MaxTotalTokens` çok düşük ayarlanırsa birinci tur tavanı tek
başına aşar; ikinci tur **gerçek sağlayıcıya hiç ulaşmadan** kesilir.

**Ön koşul (tüm case'ler için ortak)**
- Örnek uygulama, düşürülmüş bir tavanla başlatılır:
  ```bash
  export Tracon__AgentGraph__MaxTotalTokens=50
  cd samples/Tracon.Api && dotnet run
  ```

### MT-RET-050 — Düşük token tavanı, uzun tool döngülü bir `run`'ı KESER

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 114 |
| **İlgili karar** | K-627 |

**Ölçüldü (2026-08-26, gerçek OpenAI çağrısı, `samples/Tracon.Api`):**
başarısız bir `POST /api/agents/{name}/run`'ın gövdesi bir `run` kaydı
DEĞİL, bir `ProblemDetails`'tir (`runId` alanı taşımaz) — bu yüzden
`RUN_ID` POST yanıtından değil, `GET /api/runs`'tan okunur.

**Adımlar**
1. `support` agent'ına bir sipariş sorusu sor (tool çağrısını tetikler).
2. `POST` `502` döner; `run` kaydını `GET /api/runs`'tan oku.

**Girilecek veri**
```bash
export Tracon__AgentGraph__MaxTotalTokens=150   # örnek uygulamayı bu env ile başlat

curl -s -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"Where is my order 42?"}' | jq '{status, detail}'

RUN_ID=$(curl -s "$APU/api/runs" -H "$APB" | jq -r '.[0].id')
curl -s "$APU/api/runs/$RUN_ID" -H "$APB" | jq '{status, errorClass: .error.class, errorType: .error.type, errorMessage: .error.message}'
```

**Beklenen sonuç (2026-08-26'da bu adımlarla ölçüldü)**
- `POST` yanıtı `502` — `title: "Agent run failed"`.
- `status: "Failed"`.
- `errorType: "run_budget_exceeded"`.
- `errorClass: "QuotaExceeded"` — **yeni bir hata sınıfı değil**, K-627'nin
  emekliye ayırdığı `9` yeniden kullanılmaz; bu tavan kesmesi mevcut
  `QuotaExceeded` (`4`) altında raporlanır.
- `errorMessage` hangi tavanın (`token`) dolduğunu ve hangi ayarın
  (`Tracon:AgentGraph:MaxTotalTokens`) yükseltileceğini adıyla yazar —
  ölçülen tam metin: *"The run tree's token budget is exhausted (254/150).
  Raise Tracon:AgentGraph:MaxTotalTokens to allow more. No further model
  calls can be made in this run tree."*

---

### MT-RET-051 — Kesilen `run` istemciye YARIM bir tool sonucu veya model mesajı SIZDIRMAZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 114 |
| **İlgili karar** | — |

**Ölçüldü (2026-08-26, gerçek OpenAI çağrısı).** Beklenen ilk varsayım
"son olay tam bir `ToolInvoked`/`MessageCompleted` taşır" idi — ölçüm bunu
DÜZELTTİ: Microsoft Agent Framework'ün `FunctionInvokingChatClient`'ı bir
`exception` fırlattığında (bkz. MT-RET-050, ikinci model turu bütçe
tarafından reddedilir) o ana kadarki KISMİ ilerlemeyi (turnu 1'in
`FunctionCallContent`'i, tool'un GERÇEKTEN çalıştırılmış sonucu) hiç geri
döndürmez — `RunRecordingAgent`'ın `catch` bloğu bu yüzden tool
olaylarını hiç GÖRMEZ. Gerçek ölçülen olay akışı yalnız `RunStarted` →
`RunFailed`'tir; ARADA hiçbir `ToolInvoking`/`ToolInvoked`/`MessageDelta`
olayı YOKTUR. Bu, "yarım bir mesaj sızdırmama" iddiasını DAHA GÜÇLÜ
şekilde sağlar: yarım bir mesaj değil, **hiçbir** ara ilerleme sızmaz.

> 🚨 Sipariş `get_order_status` tool'u bu senaryoda GERÇEKTEN çağrılmıştır
> (yerel fonksiyon çalıştırması bütçe halkasının DIŞINDadır — yalnız
> modele giden İKİNCİ çağrı engellenir) ama sonucu hiçbir yere yazılmaz;
> bu bir veri kaybı değildir çünkü tool zaten yan etkisiz bir okumadır.

**Ön koşul**
- MT-RET-050 çalıştırılmış, aynı `RUN_ID` elde tutuluyor.

**Adımlar**
1. Kesilen `run`'ın olay akışını oku (`events` ucu bir JSON dizisi DEĞİL,
   SSE akışıdır).

**Girilecek veri**
```bash
curl -s "$APU/api/runs/$RUN_ID/events" -H "$APB"
```

**Beklenen sonuç (2026-08-26'da ölçüldü)**
- Akış tam olarak iki olay taşır: `event: run.started` ardından
  `event: run.failed`.
- `run.failed`'in `data.text` alanı MT-RET-050'deki tam hata metnini taşır.
- Aralarında **hiçbir** `run.tool_invoking`/`run.tool_invoked`/
  `run.message_delta` olayı yoktur — ne yarım ne tam bir ara olay sızar.

---

### MT-RET-052 — Hiçbir tavan tanımlı değilken davranış AYNIDIR (gerileme yok)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 114 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama, tavan **olmadan** yeniden başlatılır:
  ```bash
  unset Tracon__AgentGraph__MaxTotalTokens
  cd samples/Tracon.Api && dotnet run
  ```

**Adımlar**
1. Aynı sipariş sorusunu sor.

**Girilecek veri**
```bash
RUN_ID=$(curl -s -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"Where is my order 42?"}' | jq -r '.runId')

curl -s "$APU/api/runs/$RUN_ID" -H "$APB" | jq '.status'
```

**Beklenen sonuç**
- `"Completed"` — varsayılan `200000` tavanı bu kısa `run`'ı hiç
  zorlamaz; MT-RET-050'nin kesmesi yalnız DÜŞÜRÜLMÜŞ tavanın sonucudur.
- (Not: başarılı bir `POST` yanıtının gövdesi bir `run` kaydı değil,
  `{runId, response}` biçimindedir — `status` doğrudan POST yanıtında
  DEĞİL, `GET /api/runs/{id}`'de okunur; bkz. MT-RET-050'nin ölçüm notu.)

---

### MT-RET-053 — Maliyet tavanı tanımlıyken, fiyatı BİLİNMEYEN modelde tavan UYGULANMAZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 114 |
| **İlgili karar** | — |

`115.5`'in kuralı: maliyet fiyatlandırma ister; fiyat bilinmiyorsa
(`PricingSource.Unknown`) maliyet tavanı **zorlanamaz** ve token tavanına
düşülür — `QuotaDefinition.MaxCost`'un zaten uyguladığı kuralın aynısı.

**Ön koşul**
- Örnek uygulamanın modeli fiyat kataloğunda/`Tracon:Pricing`
  yapılandırmasında **tanımlı değil** (varsayılan kurulumda genelde böyledir
  — `MT-RET-034`'ün önkoşuluyla aynı).
- Örnek uygulama düşük bir MALİYET tavanıyla, YÜKSEK bir token tavanıyla
  başlatılır:
  ```bash
  export Tracon__AgentGraph__MaxTotalCost=0.000001
  export Tracon__AgentGraph__MaxTotalTokens=200000
  cd samples/Tracon.Api && dotnet run
  ```

**Adımlar**
1. `support` agent'ını çalıştır.

**Girilecek veri**
```bash
RUN_ID=$(curl -s -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"Where is my order 42?"}' | jq -r '.runId')

curl -s "$APU/api/runs/$RUN_ID" -H "$APB" | jq '.status'
```

**Beklenen sonuç**
- `"Completed"` — maliyet tavanı fiyatsız model yüzünden hiç
  uygulanmaz; token tavanı da (200000, yüksek) bu kısa `run`'ı kesmez.

---

### MT-RET-054 — Ağaçtaki TÜM dallar aynı bütçeyi görür (alt-agent çağrısı)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 114 |
| **İlgili karar** | — |

`router` agent'ı `support`'u alt-agent olarak çağırır (bkz. `AgentCallGraphTests`
ailesi). Bütçe kök ile alt çalıştırma arasında **aynı** `AgentRunBudget`
nesnesidir (`ChildAgentInvoker.CreateChildOptions`); alt çalıştırmanın
harcaması da toplam tavanı besler.

**Ön koşul**
- MT-RET-050'deki gibi düşük token tavanıyla başlatılmış örnek uygulama.

**Adımlar**
1. `router` agent'ını, `support`'u tetikleyecek bir sipariş sorusuyla çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/router/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"Where is my order 42?"}' | jq '{status, detail}'

RUN_ID=$(curl -s "$APU/api/runs" -H "$APB" | jq -r '.[0].id')
curl -s "$APU/api/runs/$RUN_ID" -H "$APB" | jq '{status, errorClass: .error.class}'
```

**Beklenen sonuç**
- `POST` yanıtı `502`.
- Kök `run` `Failed` biter, `errorClass: "QuotaExceeded"` — kesme alt
  çalıştırmanın (`support`) tool döngüsünde olsa bile kök bunu miras alır,
  çünkü ikisi **aynı** bütçe nesnesini paylaşır.

---

### MT-RET-055 — `202 Accepted` ile arka planda koşan `run` da aynı şekilde kesilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 114 |
| **İlgili karar** | — |

**Ön koşul**
- MT-RET-050'deki gibi düşük token tavanıyla başlatılmış örnek uygulama.

**Adımlar**
1. `Prefer: respond-async` başlığıyla aynı sorguyu gönder (dayanıklı
   çalıştırma, Faz 46).
2. Kayıt tamamlanana kadar `run` kaydını poll et.

**Girilecek veri**
```bash
RUN_ID=$(curl -s -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -H "Prefer: respond-async" \
  -d '{"message":"Where is my order 42?"}' | jq -r '.id // .runId')

sleep 3
curl -s "$APU/api/runs/$RUN_ID" -H "$APB" | jq '{status, errorClass: .error.class}'
```

**Beklenen sonuç**
- `status: "Failed"`, `errorClass: "QuotaExceeded"` — arka plan yolu
  senkron yolla **aynı** kesme davranışını üretir; 👤 arayüzde hata görünür.

---

# 6 — Run ağacı süre bütçesi (Faz 128)

`AgentGraph.MaxDuration`, ağaç bütçesinin **beşinci** boyutudur (bkz. dosya
başındaki 🚨 not). MT-RET-050..055'in aynen tekrarıdır — yalnız düşürülen
tavan token değil süredir, ve §5'te yalnız senkron yol ölçülmüşken burada
kuyruklu (dayanıklı) yol da ayrıca ölçülür, çünkü T-7'nin asıl gerekçesi
budur: bugüne kadar kuyruğa alınmış bir `run`'ı zamanla sınırlayan **hiçbir
şey** yoktu.

**Ön koşul (tüm case'ler için ortak)**
- Örnek uygulama, düşürülmüş bir tavanla başlatılır. `1` milisaniyelik bir
  tavan seçilir çünkü gerçek bir OpenAI ağ turu her zaman bundan uzun
  sürer — bu yüzden İLK model turu her zaman biter, kesme İKİNCİ turda olur:
  ```bash
  export Tracon__AgentGraph__MaxDuration="00:00:00.001"
  cd samples/Tracon.Api && dotnet run
  ```

### MT-RET-060 — Düşük süre tavanı, bir tool döngülü `run`'ı KESER

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 128 |
| **İlgili karar** | K-630 |

**Ölçüldü (2026-09-01, gerçek OpenAI çağrısı, `samples/Tracon.Api`,
`export Tracon__AgentGraph__MaxDuration="00:00:00.001"`).**

**Adımlar**
1. `support` agent'ına bir sipariş sorusu sor (tool çağrısını tetikler).
2. `POST` `502` döner; `run` kaydını `GET /api/runs`'tan oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"Where is my order 42?"}' | jq '{status, detail}'

RUN_ID=$(curl -s "$APU/api/runs?agentName=support" -H "$APB" | jq -r '.[0].id')
curl -s "$APU/api/runs/$RUN_ID" -H "$APB" | jq '{status, errorClass: .error.class, errorType: .error.type, errorMessage: .error.message}'
```

**Beklenen sonuç (2026-09-01'de bu adımlarla ölçüldü)**
- `POST` yanıtı `502` — `title: "Agent run failed"`.
- `status: "Failed"`.
- `errorType: "run_budget_exceeded"` — Faz 114'ün aynı istisnası, yeni bir
  tip AÇILMADI.
- `errorClass: "QuotaExceeded"` — token/maliyet kesmesiyle **aynı** sınıf
  (K-630); mesaj hangi boyutun dolduğunu ayırt eder.
- `errorMessage` hangi tavanın (`süre`) dolduğunu, hangi ayarın
  (`Tracon:AgentGraph:MaxDuration`) yükseltileceğini VE tavanın sert bir
  zaman aşımı olmadığını adıyla yazar — ölçülen tam metin: *"The run tree's
  time budget is exhausted (00:00:00.0932770/00:00:00.0010000). Raise
  Tracon:AgentGraph:MaxDuration to allow more. This is a cutoff between
  model turns, not a hard timeout: a tool call already in progress is not
  interrupted. No further model calls can be made in this run tree."*
- Elapsed süre (`0.093...`) tavandan (`0.001`) büyüktür ama **sıfıra
  yakındır** — kesme gerçekten İKİNCİ model turunda oldu, ilk turun
  ortasında değil (tool gerçekten çağrıldı, sipariş sorgusu sonuçlandı).

---

### MT-RET-061 — Kesilen `run` istemciye YARIM bir tool sonucu veya model mesajı SIZDIRMAZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 128 |
| **İlgili karar** | — |

**Ölçüldü (2026-09-01, gerçek OpenAI çağrısı).** MT-RET-051'in aynı ölçümü
— aynı sebeple (bkz. MT-RET-051'in notu, Microsoft Agent Framework kısmi
ilerlemeyi geri döndürmez): olay akışı yalnız `RunStarted` → `RunFailed`
taşır.

**Ön koşul**
- MT-RET-060 çalıştırılmış, aynı `RUN_ID` elde tutuluyor.

**Adımlar**
1. Kesilen `run`'ın olay akışını oku.

**Girilecek veri**
```bash
curl -s --max-time 5 "$APU/api/runs/$RUN_ID/events" -H "$APB"
```

**Beklenen sonuç (2026-09-01'de ölçüldü)**
- Akış tam olarak iki olay taşır: `event: run.started` ardından
  `event: run.failed` (`eventCount: 2`).
- `run.failed`'in `data.text` alanı MT-RET-060'taki tam hata metnini taşır.
- Aralarında **hiçbir** `run.tool_invoking`/`run.tool_invoked`/
  `run.message_delta` olayı yoktur.

---

### MT-RET-062 — Hiçbir tavan tanımlı değilken davranış AYNIDIR (gerileme yok)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 128 |
| **İlgili karar** | — |

**Ölçüldü (2026-09-01, gerçek OpenAI çağrısı).**

**Ön koşul**
- Örnek uygulama, tavan **olmadan** yeniden başlatılır:
  ```bash
  unset Tracon__AgentGraph__MaxDuration
  cd samples/Tracon.Api && dotnet run
  ```

**Adımlar**
1. Aynı sipariş sorusunu sor.

**Girilecek veri**
```bash
RUN_ID=$(curl -s -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"Where is my order 42?"}' | jq -r '.runId')

curl -s "$APU/api/runs/$RUN_ID" -H "$APB" | jq '{status, errorClass: .error.class}'
```

**Beklenen sonuç (2026-09-01'de ölçüldü)**
- `status: "Completed"`, `errorClass: null` — varsayılan (boş) `MaxDuration`
  bu kısa `run`'ı hiç zorlamaz; MT-RET-060'ın kesmesi yalnız DÜŞÜRÜLMÜŞ
  tavanın sonucudur.

---

### MT-RET-063 — `202 Accepted` ile arka planda koşan (kuyruklu/dayanıklı) `run` da aynı şekilde kesilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 128 |
| **İlgili karar** | K-630 |

Bu case, T-7'nin **asıl** gerekçesidir: `JobWorkerBackgroundService` kuyruklu
işi koşarken kirayı sürekli yeniler, HTTP isteğinin doğal bir zaman aşımı
orada yoktur — `AgentGraph.MaxDuration`'dan önce kuyruklu bir `run`'ı zamanla
sınırlayan **hiçbir şey** yoktu.

**Ölçüldü (2026-09-01, gerçek OpenAI çağrısı, düşük tavanla — MT-RET-060'ın
ortamı).**

**Ön koşul**
- MT-RET-060'taki gibi düşük süre tavanıyla (`00:00:00.001`) başlatılmış
  örnek uygulama.

**Adımlar**
1. `Prefer: respond-async` başlığıyla aynı sorguyu gönder (dayanıklı
   çalıştırma, Faz 46).
2. Kayıt tamamlanana kadar `run` kaydını poll et.

**Girilecek veri**
```bash
RUN_ID=$(curl -s -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -H "Prefer: respond-async" \
  -d '{"message":"Where is my order 42?"}' | jq -r '.id // .runId')

sleep 2
curl -s "$APU/api/runs/$RUN_ID" -H "$APB" | jq '{status, errorClass: .error.class, errorType: .error.type}'
```

**Beklenen sonuç (2026-09-01'de ölçüldü, 1 saniye içinde tamamlandı)**
- `status: "Failed"`, `errorClass: "QuotaExceeded"`,
  `errorType: "run_budget_exceeded"` — arka plan yolu senkron yolla
  (MT-RET-060) **aynı** kesme davranışını üretir; kira yenilense de `run`
  kesilir.

---

### MT-RET-064 — İptal, süre tavanından ÖNCE gelirse hata sınıfı `Canceled` KALIR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 128 |
| **İlgili karar** | — |

Sınır senaryosu — MT-RET-032'nin (kota × devam eden çalıştırma) aynı
gerekçesiyle **elle güvenilir biçimde tetiklenemez**: senaryo, bir tool
GERÇEKTEN çalışırken (ve son tarih ÇOKTAN geçmişken) `POST
/api/runs/{id}/cancel`'ın tam o anda çağrılmasını gerektirir — zamanlaması
elle koşumda tesadüfe kalır. Güvence, otomatik fonksiyonel teste
bırakılmıştır:
`tests/Tracon.AspNetCore.FunctionalTests/RunDeadlineTests.cs` →
`Cancelling_a_run_while_the_deadline_has_already_passed_still_classifies_as_Canceled`
— bir `TaskCompletionSource` ile tool'un GERÇEKTEN çalıştığı an
belirlenip tam o anda `cancel` ucu çağrılır, ardından çalıştırmanın
`Canceled` (asla `QuotaExceeded` değil) bittiği doğrulanır.

**Not (davranışın kod-okuması):** `RunBudgetChatClient.ThrowIfExhausted`
son tarih kontrolünü senkron yapar ve `cancellationToken`'a hiç bakmaz —
kesme her zaman bir SONRAKİ model çağrısından önce, cancellation ise devam
eden bir tool çağrısının kendi `await`'inden fırlar. İkisi aynı ana denk
gelirse (`run` zaten iptal edilmeye çalışılıyorken son tarih de dolmuşsa),
hangisinin önce fırlayacağı çalışma zamanının hangi noktada olduğuna
bağlıdır — otomatik test bunu, tool'un cancellation'ı GÖZLEMLEYECEĞİ
noktayı sabitleyerek (bir `TaskCompletionSource.WaitAsync(cancellationToken)`
ile) deterministik hâle getirir.

---

# 7 — Çalıştırmaya bağlı kota eşiği bildirimi (Faz 146)

Eşiği geçiren `run`'ın kendi olay akışına yazılan `tracon.quota.threshold`
bildirimi: sıra (terminal olaydan önce), korelasyon (`run`/kullanıcı), iki
yoldan aynı payload, `Last-Event-ID` ile yeniden okunabilirlik, kalıcı
tekillik ve varsayılan kapalı davranış.

> **Gerçek para uyarısı.** Bu bölümdeki tüm case'ler gerçek bir sağlayıcı
> çağrısı yapar (`support` agent'ının bağlı olduğu model). `PublishThresholdToRunStream`
> varsayılan **kapalıdır**; case'ler `--Tracon:Quotas:PublishThresholdToRunStream=true`
> ile başlatılan bir örnek uygulama koşumu gerektirir.

### MT-RET-070 — Anahtar kapalıyken davranış birebir eskisiyle aynıdır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 146 |
| **İlgili karar** | K1 (146.5) |

**Ön koşul**
- Örnek uygulama **varsayılan** ayarlarla çalışıyor (`PublishThresholdToRunStream` ayarlanmamış).
- `support` agent'ı için `maxRuns=1` bir kota kaydı var.

**Adımlar**
1. Bir `run` çalıştır (eşiği geçirir).
2. Akışta `custom` çerçevesi olmadığını doğrula.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/quotas" -H "$APB" -H "content-type: application/json" \
  -d '{"agentName":"support","period":"Daily","maxRuns":1,"enabled":true}'

curl -N -s "$APU/api/agents/support/run" -H "$APB" -H 'content-type: application/json' \
  -d '{"message":"merhaba"}' | grep -E '^event:'
```

**Beklenen sonuç**
- Sıra yalnız `run … update* … done` — **`custom` çerçevesi yok**.
- `quota.threshold` webhook'u (bir abone kayıtlıysa) eskisi gibi gelir — bu
  bildirim kanalından bağımsızdır.

---

### MT-RET-071 — Anahtar açıkken `custom` çerçevesi `done`'dan ÖNCE gelir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 146 |
| **İlgili karar** | 146.1 |

**Ön koşul**
- Örnek uygulama `--Tracon:Quotas:PublishThresholdToRunStream=true` ile başlatıldı.
- `support` agent'ı için `maxRuns=1` bir kota kaydı var (temiz dönem).

**Adımlar**
1. Bir `run` çalıştır.
2. Çerçeve sırasını oku.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/quotas" -H "$APB" -H "content-type: application/json" \
  -d '{"agentName":"support","period":"Daily","maxRuns":1,"enabled":true}'

curl -N -s "$APU/api/agents/support/run" -H "$APB" -H 'content-type: application/json' \
  -d '{"message":"merhaba"}' | grep -E '^event:'
```

**Gerçek sonuç (2026-09-05, `gpt-5.4-mini`, `manuel-test-token-2026`)**
```
event: run
event: update  (× 13)
event: custom
event: done
```

**Beklenen sonuç**
- `event: custom` **`event: done`'dan önce** gelir. ✅ Doğrulandı.
- Aynı akışın `data:` gövdesi `"type":"Custom"` ve
  `"customType":"tracon.quota.threshold"` taşır.

---

### MT-RET-072 — Doğrudan akış ile `GET /api/runs/{id}/events` AYNI bildirimi sunar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 146 |
| **İlgili karar** | — |

**Ön koşul**
- MT-RET-071'in `run`'ı (aynı koşum).

**Adımlar**
1. `run`'ın kimliğini oku.
2. Olay akışını `GET .../events` ile oku, `custom` çerçevesini karşılaştır.

**Girilecek veri**
```bash
RUN_ID=$(curl -s "$APU/api/runs" -H "$APB" | jq -r '.[0].id')
curl -s "$APU/api/runs/$RUN_ID/events" -H "$APB" | grep -A1 '^event: custom'
```

**Gerçek sonuç (2026-09-05)**
```
event: custom
data: {"runId":"...","sequence":11,"type":"Custom", ...,
       "payload":"{\"noticeId\":\"...\",\"tenantId\":\"default\",\"userId\":null,
                    \"runId\":\"...\",\"sessionId\":null,\"metric\":\"Runs\",
                    \"period\":\"Daily\",\"thresholdPercent\":100,\"limit\":1,
                    \"used\":1,\"resetsAt\":\"2026-09-06T00:00:00+00:00\"}",
       "customType":"tracon.quota.threshold"}
```

**Beklenen sonuç**
- `noticeId` MT-RET-071'in doğrudan akışındaki değerle **birebir aynıdır**. ✅ Doğrulandı — iki yol aynı kalıcı `RunEvent`'i okur, ikinci bir kopya üretmez.

---

### MT-RET-073 — `Last-Event-ID` ile yeniden bağlanma bildirimi tekrar okuyabilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 146 |
| **İlgili karar** | — |

**Ön koşul**
- MT-RET-071/072'nin `run`'ı.

**Adımlar**
1. `Last-Event-ID: 0` ile olay akışını yeniden oku.

**Girilecek veri**
```bash
curl -s "$APU/api/runs/$RUN_ID/events" -H "$APB" -H "Last-Event-ID: 0" | grep -c '^event: custom'
```

**Beklenen sonuç**
- `1` döner — bildirim akışta hâlâ okunabilir, ikinci bir kopya üretmez.

---

### MT-RET-074 — Aynı dönemde ikinci bir eşik geçişi bildirimi TEKRARLAMAZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 146 |
| **İlgili karar** | 146.4 |

**Ön koşul**
- `maxRuns=2`, tek eşik `%50` (varsayılan `[80,100]` yerine).
- Uygulama `--Tracon:Quotas:PublishThresholdToRunStream=true
  --Tracon:Quotas:ThresholdPercents:0=50` ile başlatıldı (veya iki
  eleman varsayılanı kabul edilip yalnız ilk `run`'ın `%50`'yi, ikinci
  `run`'ın `%100`'ü geçtiği ayrı ayrı gözlemlenir).

**Adımlar**
1. İlk `run`'ı çalıştır — eşiği geçirir, bildirim gelir.
2. İkinci `run`'ı çalıştır (kota hâlâ izin veriyor) — **aynı** eşik zaten
   claim edilmiş, yeni bildirim gelmemeli.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/quotas" -H "$APB" -H "content-type: application/json" \
  -d '{"agentName":"support","period":"Daily","maxRuns":2,"enabled":true}'

# 🚨 content-type BAŞLIĞI GEREKİR -- yoksa 415 ile sessizce başarısız olur
# ve grep -c "custom" hep 0 döner (run hiç başlamaz) -- düzeltildi 2026-09-17, ap-s2:
curl -N -s "$APU/api/agents/support/run" -H "$APB" -H 'content-type: application/json' -d '{"message":"once"}' | grep -c '^event: custom'
curl -N -s "$APU/api/agents/support/run" -H "$APB" -H 'content-type: application/json' -d '{"message":"iki"}' | grep -c '^event: custom'
```

**Beklenen sonuç**
- İlk çağrı `1` (bildirim var), ikinci çağrı `0` (aynı eşik ikinci kez
  yayımlanmaz) — otomatik karşılığı: `QuotaRunNoticeTests.A_second_run_that_crosses_no_new_threshold_gets_no_notice`
  (fonksiyonel) ve `QuotaStoreContract.A_threshold_already_claimed_in_this_period_is_not_claimed_again`
  (dört depoda: bellek içi + PostgreSQL + SQL Server + SQLite).

---

### MT-RET-075 — Host yeniden başlatıldığında aynı eşik yeniden yayımlanmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 146 |
| **İlgili karar** | 146.4 |

Otomatik karşılığı `QuotaStoreContract`'ın dört koşumu (kalıcı depo — SQL
sağlayıcılarında) zaten kanıtlıyor: tekillik satırın kendisinde durur, bellek
içi süreç durumunda değil. Bu case yalnız SQL bir sağlayıcı (SQLite/PostgreSQL/
SQL Server) yapılandırıldığında elle koşulabilir; bellek içi kurulumda
"yeniden başlatma" kavramı yoktur (sayaç da sıfırlanır).

**Ön koşul**
- SQLite ile çalışıyor (`Tracon:Sqlite:ConnectionString` tanımlı).
- MT-RET-074'ün ilk `run`'ı (eşik zaten claim edildi).

**Adımlar**
1. Uygulamayı durdur, yeniden başlat (aynı veritabanı dosyasıyla).
2. Aynı dönemde yeni bir `run` çalıştır.

**Beklenen sonuç**
- Yeni `run`'ın akışında `custom` çerçevesi **yok** — claim veritabanı
  satırında durur, süreç yeniden başlasa da hayatta kalır.

---

### MT-RET-076 — Alt-agent ağacı eşiği geçirirse bildirim yalnız KÖK `run`'da bir kez görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 146 |
| **İlgili karar** | — |

Otomatik karşılığı: kota muhasebesi yalnız `Depth == 0`'da çalışır
(`RunRecordingAgent.Completion.cs`'nin mevcut kapısı, Faz 146 yalnız
`RecordQuotaAsync`'in ÇAĞRILDIĞI konumu taşıdı, kapıyı değiştirmedi) —
`AgentDelegationTests.cs`'in ailesi bu kapıyı zaten dolaylı kanıtlıyor.
Elle koşum `router` → `support` (alt-agent) zincirini kullanır.

**Ön koşul**
- `router` agent'ı (`support`'u çağırabilir) için `maxRuns=1` bir kota kaydı.

**Adımlar**
1. `router`'ı çalıştır (kendisi `support`'u çağırır, iki `run` satırı açılır).
2. Kök `run`'ın akışında kaç `custom` çerçevesi olduğunu say.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/quotas" -H "$APB" -H "content-type: application/json" \
  -d '{"agentName":"router","period":"Daily","maxRuns":1,"enabled":true}'

curl -N -s "$APU/api/agents/router/run" -H "$APB" -H 'content-type: application/json' \
  -d '{"message":"merhaba"}' | grep -c '^event: custom'
```

**Beklenen sonuç**
- **`1`** — bildirim yalnız kök `run`'da, tam bir kez görünür; alt-agent'ın
  kendi çalıştırması hiç bildirim yazmaz.
