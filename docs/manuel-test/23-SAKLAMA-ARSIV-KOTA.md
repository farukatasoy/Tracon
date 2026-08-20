# 23 — Saklama, Arşiv ve Kota (`RET`)

> **Alan kodu:** `RET` · **Faz:** 21 (yalnız kota dilimi), 25, 36
> **Kaynak:** `src/AgentPrism.Abstractions/Retention/` (tümü) ·
> `src/AgentPrism.Core/Retention/` (tümü) ·
> `src/AgentPrism.Sql.Shared/Internal/RetentionTargetRegistry.cs` ·
> `src/AgentPrism.AspNetCore/Endpoints/RetentionEndpoints.cs` ·
> `samples/AgentPrism.Api/FileSystemArchiveSink.cs` ·
> `src/AgentPrism.Abstractions/Quotas/` · `src/AgentPrism.Core/Quotas/`
> (`QuotaEnforcer`, `QuotaPeriodCalculator`, `InMemoryQuotaStore`) ·
> `src/AgentPrism.AspNetCore/Endpoints/QuotaEndpoints.cs` ·
> `src/AgentPrism.Core/Recording/RunRecordingAgent.cs`
> (`RecordQuotaAsync`, kök-çalıştırma kapısı).
>
> 🚨 **Faz 21'in yalnız KOTA dilimi bu dosyanındır.** Hız sınırı
> (`AgentPrismRateLimitFilter`) ve webhook/olay yayını (`WebhookEndpoints`,
> `IWebhookPublisher`) **hiçbir manuel test dosyasına atanmamıştır** — bkz.
> dosyanın sonundaki "Sınır" tablosu ve `00-INDEKS.md` §8'deki not.
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** [`kosumlar/2026-08-13/23-SAKLAMA-ARSIV-KOTA.md`](kosumlar/2026-08-13/23-SAKLAMA-ARSIV-KOTA.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır.

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
| Hız sınırı (`AgentPrismRateLimitFilter`, Faz 21.1) | 🚨 **Hiçbir dosyaya atanmamış** — bkz. `00-INDEKS.md` §8 |
| Webhook / olay yayını (Faz 21.3) | 🚨 **Hiçbir dosyaya atanmamış** — bkz. `00-INDEKS.md` §8 |
| Maliyet gözlemlenebilirliği (`RunCost`, `/api/stats`) | `12-GOZLEMLENEBILIRLIK-MALIYET.md` (zaten üretildi) |
| API anahtarı kapsamının GENEL sözleşmesi | `13-KIRACI-VE-GUVENLIK.md` (zaten üretildi) |
| İş kuyruğunun genel davranışı (`JobKind`, zamanlama, `respond-async`) | `16-IS-KUYRUGU-VE-ZAMANLAMA.md` (zaten üretildi) — bu dosya yalnız `JobKind.Retention`'ın **kendine özgü** yükünü sınar |
| `IdempotencyKeys`/`RunInputs` hedeflerinin KENDİ işlevleri (idempotency mekaniği, yeniden oynatma) | `07-HTTP-YONETIM-API.md` / `21-DAYANIKLILIK-VE-IPTAL.md` — bu dosya yalnız onların **saklama** davranışını sınar |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama çalışır: `cd samples/AgentPrism.Api && dotnet run` → `http://localhost:5080`.
   ```bash
   export APB="Authorization: Bearer manuel-test-token-2026"
   export APU="http://localhost:5080/agentprism"
   ```
3. Bazı case'ler (`§1`, `§3`) tabloya doğrudan SQL ile veri yazmayı gerektirir
   (gerçek bir çalıştırma zincirinden 150 satır üretmek pratik değildir — Faz
   36'nın kendi kapanış koşumu da bunu yaptı). SQLite kullanılıyorsa
   (`AgentPrism:Sqlite:ConnectionString`), veritabanı dosyası `sqlite3` ile
   doğrudan açılabilir.
4. Arşiv case'leri (`§2`) için `AgentPrism:Retention:ArchivePath` **varsayılan
   olarak boştur** — bu bilinçlidir, MT-RET-012'nin ön koşuludur.

> **Gerçek para uyarısı.** Yalnız §4 (kota) gerçek OpenAI çağrısı yapar (küçük
> ölçekte). §1–§3 hiçbir model çağırmaz — saf veri düzlemi/SQL testleridir.

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
- SQLite ile çalışıyor (`AgentPrism:Sqlite:ConnectionString` tanımlı, reset yapıldı).

**Adımlar**
1. `run_events` tablosuna doğrudan SQL ile, 10 tanesi 40 gün önceye tarihli 12
   satır ekle (gerçek bir `run_id`'ye bağlı — `runs` tablosunda önce bir satır olmalı).
2. `MaxAgeDays=30` politikası kaydet.
3. `preview` ile silinecek sayıyı oku.
4. `run` ile gerçekten çalıştır, geçmişi ve tablo satır sayısını doğrula.

**Girilecek veri**
```bash
# 1. Bir run + 12 run_event (10'u 40 gun once, 2'si bugun)
sqlite3 samples/AgentPrism.Api/agentprism-manuel.db <<'SQL'
INSERT INTO agentprism_runs (id, tenant_id, agent_name, status, created_at, updated_at)
VALUES ('11111111-1111-1111-1111-111111111111','default','support',1,datetime('now'),datetime('now'));
INSERT INTO agentprism_run_events (run_id, seq, type, created_at)
SELECT '11111111-1111-1111-1111-111111111111', value, 0, datetime('now','-40 days')
FROM (SELECT value FROM json_each('[1,2,3,4,5,6,7,8,9,10]'));
INSERT INTO agentprism_run_events (run_id, seq, type, created_at)
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
sqlite3 samples/AgentPrism.Api/agentprism-manuel.db "SELECT count(*) FROM agentprism_run_events WHERE run_id='11111111-1111-1111-1111-111111111111';"
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
- `dotnet user-secrets set "AgentPrism:Retention:Traces:MaxAgeDays" "14"` ile
  config tabanlı bir varsayılan tanımlı, uygulama yeniden başlatıldı.

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
- `AgentPrism:Retention:ArchivePath` **ayarlanMAMIŞ** (varsayılan durum —
  `samples/AgentPrism.Api/Program.cs:653-658` bu anahtar boşsa `IArchiveSink`'i
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
- `AgentPrism:Retention:ArchivePath`'i ayarlayıp uygulamayı yeniden başlatınca
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
1. `AgentPrism:Retention:RunEvents:MaxRows` gibi bir anahtar ayarlamayı dene.
2. Etkisiz olduğunu doğrula.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:Retention:RunEvents:MaxRows" "100" \
  --project samples/AgentPrism.Api
```
(Uygulamayı yeniden başlat.)
```bash
curl -s "$APU/api/retention/preview?target=run_events" -H "$APB" | jq
```

**Beklenen sonuç**
- Bu anahtarın hiçbir etkisi **yoktur** — `AgentPrismRetentionOptions` sınıfı
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
- `dotnet user-secrets set "AgentPrism:Retention:RunInputs:MaxAgeDays" "60"`.

**Adımlar**
1. Uygulamayı yeniden başlat.
2. `run_inputs` için `preview` çağır (politika hiç kaydedilmeden).

**Girilecek veri**
```bash
curl -s "$APU/api/retention/preview?target=run_inputs" -H "$APB" | jq
curl -s "$APU/api/retention/preview?target=sessions" -H "$APB" | jq
```

**Beklenen sonuç**

> ⚠️ **Bu beklenti koşumda yanlış bulundu ve koda göre düzeltildi
> (KOSUM-PLANI §2.1 istisnası).** Özgün metin, `run_inputs`'ın config
> varsayılanını kullandığını, `sessions`'ın ise kullanmadığını iddia
> ediyordu. Gerçek davranış **tam tersidir**. Gerekçe `Gerçek sonuç`
> alanındadır.

- `run_inputs` için `preview` config'teki varsayılanı **KULLANMAZ** —
  `AgentPrismRetentionOptions.ForTarget` içinde `run_inputs` için bir case
  **yoktur**, `_ => null` dalına düşer. Yanıt `enabled: false`,
  `maxAgeDays: null` olur.
- `sessions` için config varsayılanı **KULLANILIR**. `UserDataTargets`
  listesinin anlamı "config yok sayılır" değil, "yerleşik varsayılanı
  `null`'dur, yani açıkça açılmadıkça kapalıdır"tır.

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
sqlite3 samples/AgentPrism.Api/agentprism-manuel.db <<'SQL'
INSERT INTO agentprism_runs (id, tenant_id, agent_name, status, created_at, updated_at)
VALUES ('22222222-2222-2222-2222-222222222222','default','support',1,datetime('now'),datetime('now'));
WITH RECURSIVE seq(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM seq WHERE x < 150)
INSERT INTO agentprism_run_events (run_id, seq, type, created_at)
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

sqlite3 samples/AgentPrism.Api/agentprism-manuel.db \
  "SELECT count(*) FROM agentprism_run_events WHERE run_id='22222222-2222-2222-2222-222222222222';"
```

> **Doküman düzeltmesi (kusur değil):** orijinal `Girilecek veri`
> `datetime('now', ...)` kullanıyordu — SQLite'ın varsayılan biçimi
> (`YYYY-MM-DD HH:MM:SS`, boşluk ayraçlı). `AgentPrism.Sqlite/Internal/
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
çalışır"* diyor (K-260). Ölçüldü: `src/AgentPrism.Abstractions/Retention/
IRetentionStore.cs`'in **güncel** `FindRowLimitCutoffAsync` imzası bir
`string? tenantId` parametresi taşıyor ve `RetentionExecutor.cs:230` onu
gerçekten geçiriyor — bu, Faz 41'in `DeleteBatchAsync`'e kiracı sınırlaması
eklediği K-279 değişikliğiyle **aynı anda veya sonrasında** `MaxRows`'a da
uygulanmış. Faz 36'nın metni güncellenmemiş, kod ondan **ileri**.

**Ön koşul**
- Çok kiracılık açık (`AgentPrism:Tenancy:Enabled=true`, bkz.
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
  -H "$APB" -H "X-AgentPrism-Tenant: kiraci-alfa" -H "content-type: application/json" \
  -d '{"maxRows":100,"archive":false,"enabled":true}'

curl -s -X POST "$APU/api/retention/run?target=run_events" \
  -H "$APB" -H "X-AgentPrism-Tenant: kiraci-alfa" | jq -r '.jobId'
sleep 2

sqlite3 samples/AgentPrism.Api/agentprism-manuel.db \
  "SELECT tenant_id, count(*) FROM agentprism_run_events GROUP BY tenant_id;"
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
- `ozetle-ve-cevir` (`FIX` — sıralı, iki agent adımlı) workflow fixture'ı hazır.

**Adımlar**
1. Kullanım sayaçlarını sıfırdan itibaren gözlemlemek için `support` agent'ının
   günlük `runs` sayacını oku.
2. `ozetle-ve-cevir` workflow'unu çalıştır (iki agent adımı içerir).
3. Sayacı tekrar oku, artışı hesapla.

**Girilecek veri**
```bash
ONCE=$(curl -s "$APU/api/quotas/usage" -H "$APB" | jq '[.[] | select(.agentName==null and .period=="Daily")][0].runs // 0')

curl -s -X POST "$APU/api/workflows/ozetle-ve-cevir/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"Bu metni ozetle ve Ingilizceye cevir: AgentPrism bir NuGet paket ailesidir."}' | jq -r '.runId // .id'

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
- `samples/AgentPrism.Api/Program.cs` hiçbir model fiyatı tanımlamaz
  (`grep -rn "InputCostPerMillionTokens" samples/AgentPrism.Api/Program.cs`
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
> (`yonlendirici`) yapıldı — gerçek koşumda dosyanın kendi sırasını izleyen
> bir oturum `support` ile de temiz başlayabilir.

**Beklenen sonuç**
- `maxRuns=1` olmasına rağmen `200` sayısı **1'den fazla olabilir** —
  denetim öncesinde yapıldığı için 5 istek neredeyse aynı anda kotayı
  "boş" görebilir. Bu bir **kusur değildir**; K-159'un dokümante ettiği
  kabul edilmiş yaklaşıklıktır.
- Koşumda gerçek `200`/`429` dağılımı kaydedilir (`1`, `2` veya daha fazla
  `200` görülebilir — makine yüküne bağlıdır). **Sıfır** `200` görülmesi
  ise gerçek bir kusurdur (kotanın hiç izin vermediği anlamına gelir).

### MT-RET-040 — 🚨 `RetentionEndpoints` VE `QuotaEndpoints` `RequireApiKeyScope` çağırmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 21, 25 |
| **İlgili karar** | — |

Negatif senaryo — bilinen ailenin **sekizinci** (`Retention`) ve
**dokuzuncu** (`Quota`) bağımsız tekrarı (önceki yediyi bkz.
`15/16/17/18/20/21`). `grep -n "RequireApiKeyScope" src/AgentPrism.AspNetCore/
Endpoints/RetentionEndpoints.cs src/AgentPrism.AspNetCore/Endpoints/
QuotaEndpoints.cs` **sıfır** sonuç döner — ikisi de yalnız
`RequireRole(roles.Admin)` taşır, ve rol politikaları örnek uygulamada hiç
kayıtlı değildir (`14-SKILL-VE-SCRIPT.md`'nin bulgusu). Sonuç: statik bearer
token'a sahip **herhangi bir** otomasyon anahtarı — kapsamı `RunsRead` bile
olsa — saklama politikası yazabilir, gerçek bir silme çalıştırması tetikleyebilir
ve kota kurallarını değiştirebilir.

**Ön koşul**
- Bir API anahtarı sistemi kurulu (`13-KIRACI-VE-GUVENLIK.md`'nin ortam kurulumu) —
  yalnız `ApiKeyScope.RunsRead` taşıyan bir anahtar tanımlı.

**Adımlar**
1. Bu salt-okunur anahtarla `PUT /api/retention/{target}` dene (beklenen: kabul edilir).
2. Kontrol grubu: aynı anahtarla `AgentsAdmin` gerektiren bir uca yaz (beklenen: `403`).

**Girilecek veri**
```bash
export RO="Authorization: Bearer <RunsRead-KAPSAMLI-ANAHTAR>"

curl -s -i -X PUT "$APU/api/retention/jobs" -H "$RO" -H "content-type: application/json" \
  -d '{"maxAgeDays":30,"archive":false,"enabled":true}'

# Kontrol grubu — bunun 403 vermesi gerekir:
curl -s -i -X POST "$APU/api/agents" -H "$RO" -H "content-type: application/json" \
  -d '{"name":"kontrol-grubu","instructions":"x","model":{"provider":"openai","model":"gpt-5.4-mini"}}'
```

**Beklenen sonuç (şüphenin doğrulanması)**
- İlk istek `200`/`201` ile **başarılı olur** — salt-okunur bir anahtar
  gerçek bir saklama politikası yazabilir.
- Kontrol grubu `403` döner — `AgentEndpoints`'in kendisi kapsamı doğru
  uyguluyor, yalnız `Retention`/`Quota` yüzeyi bu denetimden **muaf**.
- Doğrularsa: `13-KIRACI-VE-GUVENLIK.md`'nin kapsam matrisine bu iki uç
  ailesi eklenmelidir; önem derecesi **Yüksek** — veri SİLME yetkisi salt
  okunur bir anahtara sızıyor olabilir.

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
  -H "$APB" -H "X-AgentPrism-Tenant: kiraci-alfa" -H "content-type: application/json" \
  -d '{"maxAgeDays":30,"archive":false,"enabled":true}'

curl -s -X POST "$APU/api/retention/run?target=run_events" \
  -H "$APB" -H "X-AgentPrism-Tenant: kiraci-alfa" | jq -r '.jobId'
sleep 2

sqlite3 samples/AgentPrism.Api/agentprism-manuel.db \
  "SELECT tenant_id, count(*) FROM agentprism_run_events GROUP BY tenant_id;"
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
AgentPrism.Abstractions/Retention/ src/AgentPrism.Sql.Shared/Internal/
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
1. Hiçbir SQL sağlayıcısı eklenmeden bir `AgentPrismTestHost` kur.
2. Politika kaydetmeyi ve önizleme almayı dene.

**Girilecek veri**
```bash
mkdir -p ~/agentprism-manuel/saklama-testleri && cd ~/agentprism-manuel/saklama-testleri
dotnet new console
SURUM=$(ls ~/agentprism-local-feed/AgentPrism.Testing.*.nupkg | sed 's#.*AgentPrism.Testing\.##;s#\.nupkg##')
dotnet add package AgentPrism.Testing --version "$SURUM"

cat > Program.cs <<'EOF'
using AgentPrism;
using AgentPrism.Testing;
using Microsoft.Extensions.DependencyInjection;

await using var host = await AgentPrismTestHost.StartAsync(o =>
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
