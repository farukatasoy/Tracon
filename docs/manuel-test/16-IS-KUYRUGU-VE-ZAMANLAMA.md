# 16 — İş Kuyruğu, Zamanlama, Tek Yürütücü Seçimi ve Dayanıklı Çalıştırma (`JOB`)

> **Alan kodu:** `JOB` · **Faz:** 17, 42, 46, 66, 120, 129, 137
> **Kaynak:** `src/Tracon.Abstractions/Scheduling/` (tümü) ·
> `src/Tracon.Abstractions/Coordination/` (tümü — `ISingletonLeaseStore`,
> `SingletonExecutionOptions`) ·
> `src/Tracon.Abstractions/Runs/RunStatus.cs` (yalnız `Queued`) ·
> `src/Tracon.Core/Scheduling/` (tümü: `JobWorkerBackgroundService`,
> `AgentBatchJobHandler`, `WorkflowJobHandler`, `AgentRunJobHandler`,
> `CronExpression`, `InMemoryJobStore`, `InMemoryJobScheduleStore`,
> `TraconSchedulingOptions*`, `TraconAsyncRunOptions`) ·
> `src/Tracon.Core/Coordination/` (tümü — `SingletonGuard`,
> `InMemorySingletonLeaseStore`, `SingletonExecutionOptionsValidator`) ·
> `src/Tracon.Core/Models/ModelProviderHealthBackgroundService.cs` (yalnız
> `SingletonGuard` sarmalaması) · `src/Tracon.Mcp/Internal/McpDiscoveryService.cs`
> (aynı) · `src/Tracon.Sql.Shared/Stores/SqlSingletonLeaseStore.cs` ·
> `src/Tracon.PostgreSql/Migrations/0008_scheduling.sql`,
> `0019_singleton_leases.sql` · `src/Tracon.AspNetCore/Endpoints/SchedulingEndpoints.cs` ·
> `src/Tracon.AspNetCore/Contracts/SchedulingContracts.cs` ·
> `src/Tracon.AspNetCore/Endpoints/AgentEndpoints.cs` (yalnız `Prefer:
> respond-async` dalı — `WantsAsync`/`RunQueuedAsync`) ·
> `src/Tracon.AspNetCore/Endpoints/RunEndpoints.cs` (yalnız `Queued`'a özgü
> `/cancel` dalı ve SSE akışının `Queued` beklemesi) ·
> `src/Tracon.AspNetCore/Contracts/AgentContracts.cs` (yalnız
> `AcceptedRunResponse`) · `src/Tracon.UI/frontend/src/screens/jobs.tsx`,
> `job-detail.tsx` · `src/Tracon.Abstractions/Triggers/` (tümü) ·
> `src/Tracon.Core/Triggers/` (tümü — `InboundTriggerDispatcher`,
> `InboundTriggerSecretResolver`, `InboundTriggerRateLimiter`,
> `InboundTriggerPayloadReader`) · `src/Tracon.Core/Storage/InMemoryInboundTriggerStore.cs` ·
> `src/Tracon.Sql.Shared/Stores/SqlInboundTriggerStore.cs` ·
> `src/Tracon.PostgreSql/Migrations/0033_inbound_triggers.sql` ·
> `src/Tracon.AspNetCore/Endpoints/TriggerEndpoints.cs` ·
> `src/Tracon.AspNetCore/Contracts/TriggerContracts.cs` ·
> `src/Tracon.UI/frontend/src/screens/triggers.tsx` (Faz 66) ·
> `src/Tracon.Abstractions/Scheduling/JobLanes.cs` ·
> `src/Tracon.PostgreSql/Migrations/0041_job_lanes.sql`,
> `src/Tracon.SqlServer/Migrations/0028_job_lanes.sql`,
> `src/Tracon.Sqlite/Migrations/0028_job_lanes.sql` (Faz 129).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** [`kosumlar/2026-08-13/16-IS-KUYRUGU-VE-ZAMANLAMA.md`](kosumlar/2026-08-13/16-IS-KUYRUGU-VE-ZAMANLAMA.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır.

---

## Bu dosya neyi kanıtlar

Faz 17, Tracon'e **kütüphane sınırları içinde** bir arka plan işçisi
getirdi: `jobs`/`job_schedules`/`job_items` tabloları, `FOR UPDATE SKIP LOCKED`
ile kiralama ve cron'un elle yazılmış bir alt kümesi. Faz 42 bunun üzerine,
çok örnekli dağıtımlarda MCP keşfi ve model sağlık yoklamasının **tek**
örnekte koşmasını sağlayan ayrı bir kira mekanizması (`singleton_leases`)
ekledi — iş kuyruğunun kendisi zaten kira tabanlı olduğu için **dokunulmadı**.
Faz 46, bir agent çalıştırmasını HTTP isteğinin ömründen ayırdı:
`Prefer: respond-async` başlığı `202 Accepted` + `Location` döner, çalıştırma
aynı kuyrukta (`JobKind.AgentRun`) koşar.

```mermaid
flowchart TD
    S["PUT api/schedules/name<br/>cron dogrulanir"] --> J["jobs + job_items<br/>Pending"]
    T["POST api/schedules/name/trigger"] --> J
    C["cron zamani geldi"] --> J

    J --> L["IJobStore.LeaseAsync<br/>FOR UPDATE SKIP LOCKED"]
    L --> H{"IJobHandler<br/>Kind'a gore"}
    H -->|AgentBatch| AB["agent, ogeler sirayla"]
    H -->|Workflow| WF["IWorkflowRunner sirayla"]
    H -->|AgentRun| AR["tek calistirma<br/>RunId ONCEDEN ayrilmis"]

    P["POST agents/name/run<br/>Prefer: respond-async"] --> Q["runs satiri: Queued<br/>UPSERT-safe"]
    Q --> J

    G["SingletonGuard<br/>McpDiscovery / ModelHealth"] -.->|"ayri kira, ayni desen"| SL["singleton_leases<br/>Enabled=false ise HIC dokunulmaz"]

    style Q fill:#5f4a1e,stroke:#302510,color:#ffffff
    style SL fill:#1f4a6f,stroke:#0d2740,color:#ffffff
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Genel HTTP zarfı, idempotency, `Prefer` başlığının SSE/kota etkileşimi dışındaki genel kuralları | `07-HTTP-YONETIM-API.md` (zaten üretildi) |
| Üç katmanlı erişim koruması, kiracı izolasyonu, API anahtarı kapsamları | `13-KIRACI-VE-GUVENLIK.md` (zaten üretildi) — burada TEKRARLANMAZ, yalnız §8'deki bu dosyaya özgü kapsam boşluğu buradadır |
| Workflow tanımı, graf, human-in-the-loop, checkpoint | `15-WORKFLOWS.md` (zaten üretildi) — burada yalnız `JobKind.Workflow` işinin **iş kuyruğu tarafı** test edilir, workflow'un kendisi tekrarlanmaz |
| Eval (`JobKind.Eval`), online değerlendirme (`OnlineEval`) | `17-EVAL-VE-DENEYLER.md` (henüz üretilmedi) |
| Webhook teslimi (`JobKind.WebhookDelivery`), geri adımlı bekleme merdiveni | `21-DAYANIKLILIK-VE-IPTAL.md` (henüz üretilmedi) |
| Saklama süpürmesi (`JobKind.Retention`) | `23-SAKLAMA-ARSIV-KOTA.md` (henüz üretilmedi) |
| Onay kararından sonra sürdürme (`JobKind.ApprovalResume`) | `21-DAYANIKLILIK-VE-IPTAL.md` (henüz üretilmedi) |
| Örneklenmiş çalıştırma puanlaması (`JobKind.OnlineEval`) | `17-EVAL-VE-DENEYLER.md` (henüz üretilmedi) |
| Kota denetiminin `429` mantığı | `23-SAKLAMA-ARSIV-KOTA.md` (kota kural motoru) — burada yalnız kuyruğa almanın kota kapısından **geçtiği** doğrulanır, kuralın kendisi kurulmaz |

> **Rol matrisi burada da NO-OP'tur, tekrar test edilmez.** `SchedulingEndpoints`
> her ucu `RequireRole(roles.Admin/Operator/Reader)` ile işaretler ama
> `TraconPolicies.*` örnek uygulamada kayıtlı değildir (bkz.
> `00-INDEKS.md` §8, `14-SKILL-VE-SCRIPT.md`). **Bu dosyaya özgü olan**,
> `SchedulingEndpoints`'in de (tıpkı `15-WORKFLOWS.md`'nin bulduğu
> `WorkflowEndpoints` gibi) **hiçbir ucunda** `RequireApiKeyScope(...)`
> çağrısının bulunmaması — §8'de ayrı bir case ile ölçülür.

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. İş kuyruğu ve zamanlama depoları **her kalıcılık sağlayıcısında** (bellek
   içi dahil) aynı arayüzle çalışır (K-018). Bu dosyanın SQL doğrulama
   sorguları **PostgreSQL** varsayar; diğer sağlayıcılar `04-KALICILIK-DIGER.md`'nin işidir.
3. Örnek uygulama çalışır: `cd samples/Tracon.Api && dotnet run` →
   `http://localhost:5080/tracon`.
4. Örnek uygulama **hiçbir zamanlama önceden tanımlamaz**
   (`grep -n "UseScheduling\|AddJobHandler" samples/Tracon.Api/Program.cs`
   boş döner) — işçi ve depolar yine de `AddTracon()` tarafından
   koşulsuz kaydedilidir (`.UseScheduling()` çağrısı yalnızca ayar
   değiştirmek için gerekir, işlevi açmak için değil).
5. `Tracon:Scheduling`, `Tracon:SingletonExecution`,
   `Tracon:AsyncRun` ve `Tracon:Health` bölümlerinin **tümü**
   `IConfiguration`'dan (`dotnet user-secrets`) bağlanır — MCP'nin
   `RefreshInterval` kısıtının (Faz 42 devir notu) **aksine**. §6'nın kanıtı
   bunu ölçer.

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/tracon"
export PG="docker exec -i ap-pg psql -U postgres -d tracon"
```

> **Gerçek para uyarısı.** §2 (toplu çalıştırma), §3 (workflow işi) ve §7'nin
> gerçek çalıştırma içeren case'leri (`MT-JOB-070`–`075`) gerçek OpenAI
> modeli çağırır. §1, §4, §5, §6, §8 ve §7'nin geri kalanı hiçbir model
> çağırmaz.

---

## Bu dosyanın yerel fixture'ları

| Kimlik | Değer |
|---|---|
| `FIX-JOB-01` | Zamanlama adı `ozet-toplu` · `kind: AgentBatch` · `targetName: "ozetleyici"` · cron yok (yalnız elle tetiklenir) · `payload: ["Tracon bir NuGet paket ailesidir.", "Workflow yurutmesi Faz 15te geldi."]` |
| `FIX-JOB-02` | Zamanlama adı `wf-toplu` · `kind: Workflow` · `targetName: "ozetle-ve-cevir"` · cron yok · `payload: ["Tracon, Microsoft Agent Framework uzerine kurulu bir NuGet paket ailesidir."]` |
| `FIX-JOB-03` | Zamanlama adı `dakikalik-ozet` · `kind: AgentBatch` · `targetName: "ozetleyici"` · `cron: "*/1 * * * *"` · `payload: ["Otomatik tetiklenen test girdisi."]` |

---

# 1 — Zamanlama CRUD ve Cron Doğrulama (Faz 17)

### MT-JOB-001 — `PUT /api/schedules/{name}` yeni bir zamanlama oluşturur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Adımlar**
1. `FIX-JOB-01`'i oluştur (cron'suz — yalnız elle tetiklenir).

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/schedules/ozet-toplu" -H "$APB" \
     -H "content-type: application/json" -d '{
  "kind": "AgentBatch",
  "targetName": "ozetleyici",
  "timeZone": "UTC",
  "payload": ["Tracon bir NuGet paket ailesidir.", "Workflow yurutmesi Faz 15te geldi."],
  "enabled": true
}'
```

**Beklenen sonuç**
- `HTTP: 200` (`WorkflowSaveRequest` deseniyle aynı — `SaveScheduleAsync` her
  zaman `TypedResults.Ok` döner, `201` değil).
- Gövdede `id` dolu bir GUID, `nextRunAt: null` (cron yok),
  `createdAt == updatedAt`.

---

### MT-JOB-002 — Aynı adı tekrar `PUT` etmek GÜNCELLER; `id` ve `createdAt` sabit kalır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Ön koşul**
- MT-JOB-001 geçti; dönen `id`'yi not al.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/schedules/ozet-toplu" -H "$APB" -H "content-type: application/json" -d '{
  "kind": "AgentBatch", "targetName": "ozetleyici", "timeZone": "UTC",
  "payload": ["Guncellenmis tek girdi."], "enabled": true
}'
```

**Beklenen sonuç**
- `id` MT-JOB-001'dekiyle **birebir aynı** (`SaveScheduleAsync`, `Id = existing?.Id
  ?? Guid.Empty`, `SchedulingEndpoints.cs:154`).
- `createdAt` DEĞİŞMEZ, `updatedAt` ilerler.

---

### MT-JOB-003 — `GET /api/schedules/{name}` tekil kaydı döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s "$APU/api/schedules/ozet-toplu" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 200`, MT-JOB-002'nin sonucuyla birebir aynı gövde.

---

### MT-JOB-004 — `GET /api/schedules` kiracının tüm zamanlamalarını listeler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s "$APU/api/schedules" -H "$APB" | python3 -c "import json,sys; print([s['name'] for s in json.load(sys.stdin)])"
```

**Beklenen sonuç**
- `ozet-toplu` listede.

---

### MT-JOB-005 — `DELETE` zamanlamayı siler, sonraki `GET` `404` verir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/schedules/ozet-toplu" -H "$APB"
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/schedules/ozet-toplu" -H "$APB"
```

**Beklenen sonuç**
- Silme: `HTTP: 204`. Sonraki `GET`: `HTTP: 404`, `title: "Zamanlama bulunamadi"`.
- MT-JOB-001'i bu case sonrası yeniden oluştur — sonraki case'ler onu kullanır.

---

### MT-JOB-006 — Var olmayan bir zamanlamayı silmek → `404`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/schedules/hic-yok-boyle-zamanlama" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404`.

---

### MT-JOB-007 — Boş `targetName` → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/schedules/hedefsiz" -H "$APB" \
     -H "content-type: application/json" -d '{"kind":"AgentBatch","targetName":"","timeZone":"UTC","payload":[]}'
```

**Beklenen sonuç**
- `HTTP: 400`, `title: "Zamanlama gecersiz"`, `detail: "'targetName' alani
  zorunludur."` (`SchedulingEndpoints.cs:113-116`).

---

### MT-JOB-008 — Geçersiz saat dilimi → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/schedules/yanlis-tz" -H "$APB" \
     -H "content-type: application/json" -d '{
  "kind":"AgentBatch","targetName":"ozetleyici","timeZone":"Dunya/Hicbiryer",
  "cron":"0 3 * * *","payload":[]
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail: "'Dunya/Hicbiryer' gecerli bir saat dilimi degil."`
  (`SchedulingEndpoints.cs:120-127`).

---

### MT-JOB-009 — Cron 5 alan yerine 6 alan taşırsa → `400`

Negatif senaryo — saniye alanı desteklenmez.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/schedules/6-alanli" -H "$APB" \
     -H "content-type: application/json" -d '{
  "kind":"AgentBatch","targetName":"ozetleyici","timeZone":"UTC",
  "cron":"0 0 3 * * *","payload":[]
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail: "'0 0 3 * * *' desteklenen bes alanli cron alt
  kumesiyle eslesmiyor."` (`SchedulingEndpoints.cs:131-136`, `CronExpression.Parse`
  önce alan sayısını denetler: `"Cron ifadesi tam olarak 5 alan icermelidir
  ... Saniye alani desteklenmez."`).

---

### MT-JOB-010 — Desteklenmeyen cron uzantısı (`L`) → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/schedules/vixie-uzantisi" -H "$APB" \
     -H "content-type: application/json" -d '{
  "kind":"AgentBatch","targetName":"ozetleyici","timeZone":"UTC",
  "cron":"0 0 L * *","payload":[]
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "desteklenen bes alanli cron alt kumesiyle
  eslesmiyor" içerir (`CronExpression.ParseField`, `'L'` sayı olarak
  ayrıştırılamaz → `FormatException`).

---

### MT-JOB-011 — Geçerli cron kaydedilince `nextRunAt` doğru hesaplanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Adımlar**
1. Şu anki UTC saatini not al (`date -u`).
2. Bir sonraki dakikaya denk gelen bir cron ile kaydet (ör. şu an `10:15` ise
   `"16 10 * * *"`).

**Girilecek veri**
```bash
date -u +"%H:%M"
curl -s -X PUT "$APU/api/schedules/hesapli-cron" -H "$APB" -H "content-type: application/json" -d '{
  "kind":"AgentBatch","targetName":"ozetleyici","timeZone":"UTC",
  "cron":"<dakika> <saat> * * *","payload":["test"]
}' | python3 -c "import json,sys; print(json.load(sys.stdin)['nextRunAt'])"
```

**Beklenen sonuç**
- `nextRunAt`, girilen dakika/saat ile eşleşen bugünkü (veya yarınki, geçmişse)
  UTC zaman damgasıdır (`CronExpression.GetNextOccurrence`,
  `SchedulingEndpoints.cs:138`).

---

### MT-JOB-012 — `payload` dizisi `MaxItemsPerJob`'ı aşarsa `PUT` → `400`

Negatif senaryo, sınır değer. Geçici olarak sınır düşürülür (varsayılan 1000
öge göndermek yerine ucuz bir tetikleyici).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Adımlar**
1. `dotnet user-secrets set "Tracon:Scheduling:MaxItemsPerJob" "2"`,
   yeniden başlat.
2. 3 ögeli bir `payload` ile kaydetmeyi dene.
3. Ayarı `dotnet user-secrets remove "Tracon:Scheduling:MaxItemsPerJob"`,
   yeniden başlat.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/schedules/cok-oge" -H "$APB" \
     -H "content-type: application/json" -d '{
  "kind":"AgentBatch","targetName":"ozetleyici","timeZone":"UTC",
  "payload":["bir","iki","uc"]
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail: "Yuk 3 oge tasiyor; en fazla 2 oge desteklenir."`
  (`SchedulingEndpoints.cs:141-147`).

---

### MT-JOB-013 — Cron olmadan zamanlama: `nextRunAt` `null`, yalnız elle tetiklenir

Sınır durumu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Ön koşul**
- MT-JOB-001 (`ozet-toplu`, cron'suz) hâlâ kayıtlı.

**Beklenen sonuç**
- `GET /api/schedules/ozet-toplu` `nextRunAt: null` döner ve reset sonrası
  bekleme yapılsa bile otomatik iş üretilmez — yalnız `POST .../trigger`
  (§2) bir iş açar.

---

### MT-JOB-014 — `FIX-JOB-03` (dakikalık cron) otomatik tetiklenir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Adımlar**
1. `FIX-JOB-03`'ü kaydet.
2. `nextRunAt`'in geçmesini bekle (en fazla 1 dakika + `PollInterval`, varsayılan 10 sn).
3. `GET /api/jobs?scheduleId=<id>` ile üretilen işi doğrula.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/schedules/dakikalik-ozet" -H "$APB" -H "content-type: application/json" -d '{
  "kind":"AgentBatch","targetName":"ozetleyici","timeZone":"UTC",
  "cron":"*/1 * * * *","payload":["Otomatik tetiklenen test girdisi."]
}'
sleep 75
curl -s "$APU/api/jobs?scheduleId=<schedule-id>" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- En az bir `JobRecord`, `status` en az `Pending`'i geçmiş (`Running`/`Completed`).
- Zamanlamanın `lastRunAt` alanı ilerlemiştir; `nextRunAt` bir sonraki
  dakikaya geçmiştir. Case sonunda bu zamanlama SİLİNİR (aksi hâlde her
  dakika iş üretmeye devam eder).

---

### MT-JOB-015 — Arayüz: Jobs ekranı zamanlama formu, `Kind` seçenekleri yalnız `AgentBatch`/`Workflow`

Sınır durumu — arayüzden yalnızca iki tür zamanlanabilir; diğer beş
(`Eval`, `WebhookDelivery`, `Retention`, `AgentRun`, `OnlineEval`,
`ApprovalResume`) yalnızca sistemin kendisi tarafından iç akışlarda üretilir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Adımlar**
1. Kabukta **Jobs** ekranını aç, **New schedule** düğmesine tıkla.
2. **Kind** açılır listesine bak.

**Beklenen sonuç**
- Yalnızca iki seçenek: `AgentBatch`, `Workflow` (`jobs.tsx:231-233`).

---

### MT-JOB-016 — `RunWorker = false` iken Jobs ekranında uyarı bandı görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | K-018 |

**Adımlar**
1. `dotnet user-secrets set "Tracon:Scheduling:RunWorker" "false"`,
   yeniden başlat.
2. Jobs ekranını aç.
3. Ayarı kaldır, yeniden başlat.

**Beklenen sonuç**
- Sayfa başında `jobs.workerOff.title`/`jobs.workerOff.body` metniyle bir
  uyarı paneli görünür; metin `RunWorker` adını içerir (`jobs.tsx:203-210`,
  `meta.storage.jobWorkerEnabled === false` koşulu).
- `GET /api/meta` gövdesinde `storage.jobWorkerEnabled: false`.

### MT-JOB-020 — `POST .../trigger` zamanlamayı hemen çalıştırır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-JOB-01` (`ozet-toplu`) kayıtlı.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/schedules/ozet-toplu/trigger" -H "$APB" \
     -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç**
- `HTTP: 200`, gövde bir `JobRecord`: `status: "Pending"`, `totalItems: 2`
  (zamanlamanın kendi `payload`'ından), `scheduledFor` şimdiki zamana yakın.

---

### MT-JOB-021 — İşçi işi alır, ögeleri SIRAYLA gerçek modelle çalıştırır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Ön koşul**
- MT-JOB-020'nin `id`'si elde; OpenAI anahtarı tanımlı.

**Adımlar**
1. `PollInterval` (varsayılan 10 sn) kadar bekle.
2. İşi tekrar oku.

**Girilecek veri**
```bash
curl -s "$APU/api/jobs/<job-id>" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `job.status: "Completed"` (birkaç saniye içinde, iki kısa özetleme çağrısı).
- `job.doneItems: 2`, `job.failedItems: 0`.
- `items[0].status`/`items[1].status`: `"Completed"`, ikisinin de `runId`'si
  dolu ve **farklı** (her öge kendi `runs` satırını üretir).

---

### MT-JOB-022 — Ögenin `runId`'si gerçek bir `runs` satırına işaret eder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Ön koşul**
- MT-JOB-021 geçti.

**Girilecek veri**
```bash
curl -s "$APU/api/runs/<items[0].runId>" -H "$APB" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['agentName'], d['status'])"
```

**Beklenen sonuç**
- `agentName: "ozetleyici"`, `status: "Completed"`. Run'ın kendisi
  `RunKind.Agent` (`kind: "Agent"`) taşır — toplu iş için ayrı bir `RunKind`
  yoktur, her öge normal bir agent çalıştırmasıdır.

---

### MT-JOB-023 — `GET /api/jobs/{id}` iş kaydı ve ögeleri TEK çağrıda döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s "$APU/api/jobs/<job-id>" -H "$APB" | python3 -c "import json,sys; d=json.load(sys.stdin); print(list(d.keys()))"
```

**Beklenen sonuç**
- Üst düzey anahtarlar tam olarak `['job', 'items']`
  (`JobDetailResponse`, `SchedulingContracts.cs:44-52`).

---

### MT-JOB-024 — `trigger` gövdesindeki `payload`, zamanlamanın kendi yükünün YERİNE geçer

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/schedules/ozet-toplu/trigger" -H "$APB" -H "content-type: application/json" \
     -d '{"payload": ["Tek seferlik ozel girdi."]}' | python3 -c "import json,sys; print(json.load(sys.stdin)['totalItems'])"
```

**Beklenen sonuç**
- `1` (zamanlamanın kayıtlı 2 ögesi DEĞİL) — `TriggerScheduleAsync`,
  `request?.Payload ?? schedule.Payload` (`SchedulingEndpoints.cs:200`).

---

### MT-JOB-025 — `trigger` sırasında `MaxItemsPerJob` aşımı → `400`

Negatif senaryo — MT-JOB-012'nin tetikleme yolundaki eşdeğeri.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Adımlar**
1. `dotnet user-secrets set "Tracon:Scheduling:MaxItemsPerJob" "1"`,
   yeniden başlat.
2. 2 ögeli bir `payload` ile tetikle.
3. Ayarı kaldır, yeniden başlat.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/schedules/ozet-toplu/trigger" -H "$APB" \
     -H "content-type: application/json" -d '{"payload": ["bir", "iki"]}'
```

**Beklenen sonuç**
- `HTTP: 400`, `title: "Tetikleme basarisiz"`, `detail: "Yuk 2 oge tasiyor;
  en fazla 1 oge desteklenir."` (`SchedulingEndpoints.cs:204-210`).

---

### MT-JOB-026 — Bir öge başarısız olursa iş DEVAM eder, `failedItems` sayılır

Negatif/sınır senaryo — var olmayan bir agent hedefli tetikleme ile ölçülür.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Adımlar**
1. Var olmayan bir agent hedefleyen geçici bir zamanlama kaydet.
2. Tetikle, sonucu oku.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/schedules/bozuk-hedef" -H "$APB" -H "content-type: application/json" -d '{
  "kind":"AgentBatch","targetName":"yok-boyle-bir-agent","timeZone":"UTC","payload":["tek girdi"]
}'
curl -s -X POST "$APU/api/schedules/bozuk-hedef/trigger" -H "$APB" -d '{}'
sleep 12
curl -s "$APU/api/jobs/<job-id>" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `AgentBatchJobHandler.ExecuteAsync` ilk satırda `catalog.ResolveAsync`
  `null` döner ve `TraconException` fırlatır ("'yok-boyle-bir-agent'
  adinda bir agent bulunamadi...") — bu, TEK ögeli işin öge döngüsüne hiç
  girmeden İŞ SEVİYESİNDE bir hatadır (bkz. MT-JOB-052, yeniden deneme
  merdiveni). Birden çok öge içeren ve YALNIZCA bir ögesi (ör. bozuk bir
  girdi metni) hata üreten bir senaryo bu case'te KOŞULMAZ çünkü
  `AgentBatchJobHandler` ögeleri değil agent'ın kendisini çözer; öge bazlı
  hata yalnızca `agent.RunAsync` istisna atarsa oluşur ve bunu manuel
  olarak deterministik tetiklemek pratik değildir (bkz. `PROMPT.md` §6,
  doldurma yapılmaz). Bu case yalnız İŞ seviyesindeki hatayı doğrular.

---

### MT-JOB-027 — Tüm ögeler başarısız olursa iş `Failed` olur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Ön koşul**
- MT-JOB-026'nın `bozuk-hedef` işinin son denemesini (MaxAttempts=3,
  bkz. MT-JOB-052) tamamlanmasını bekle.

**Beklenen sonuç**
- `job.status: "Failed"`, `job.errorMessage` agent bulunamadı mesajını
  içerir (`JobWorkerBackgroundService.ExecuteJobAsync`, `job.Attempt >=
  maxAttempts` dalı).

### MT-JOB-030 — Workflow hedefli bir zamanlama tetiklenir, gerçek workflow çalışır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Adımlar**
1. `FIX-JOB-02`'yi (`wf-toplu`, `kind: Workflow`, hedef `ozetle-ve-cevir`)
   kaydet.
2. Tetikle, sonucu oku.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/schedules/wf-toplu" -H "$APB" -H "content-type: application/json" -d '{
  "kind": "Workflow", "targetName": "ozetle-ve-cevir", "timeZone": "UTC",
  "payload": ["Tracon, Microsoft Agent Framework uzerine kurulu bir NuGet paket ailesidir."]
}'
curl -s -X POST "$APU/api/schedules/wf-toplu/trigger" -H "$APB" -d '{}'
sleep 15
curl -s "$APU/api/jobs/<job-id>" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `job.status: "Completed"`, `items[0].status: "Completed"`,
  `items[0].runId` dolu.

---

### MT-JOB-031 — `items[0].runId` workflow'un KÖK `runs` satırına işaret eder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Ön koşul**
- MT-JOB-030 geçti.

**Girilecek veri**
```bash
curl -s "$APU/api/runs/<items[0].runId>" -H "$APB" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['kind'], d['workflowName'])"
curl -s "$APU/api/runs/<items[0].runId>/tree" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- `kind: "Workflow"`, `workflowName: "ozetle-ve-cevir"`.
- `/tree` **3** satır döner (1 workflow + 2 agent — `15-WORKFLOWS.md`
  `MT-WF-040`'ın aynı yapısı, bu kez iş kuyruğu üzerinden tetiklenmiş).

---

### MT-JOB-032 — `UseWorkflows()` kayıtlı değilken bir Workflow işi `Failed` olur (geçici kod değişikliği)

Negatif senaryo — `MT-SEC-070`/`MT-WF-090` desenini izler.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Adımlar**
1. `Program.cs`'teki `.UseWorkflows()` çağrısını GEÇİCİ olarak yorum satırına
   al, yeniden başlat.
2. `wf-toplu`'yu tetikle.
3. Değişikliği geri al, yeniden başlat.

**Beklenen sonuç**
- `job.status: "Failed"` (ilk denemede — `WorkflowJobHandler` `runner is
  null` dalında `throw` eder, ama bu HTTP `501` DEĞİLDİR; iş kuyruğu HTTP
  katmanından bağımsızdır). `job.errorMessage: "Workflow motoru kayitli
  degil. 'Tracon.Workflows' paketini ekleyip UseWorkflows() cagirin."`
  (`WorkflowJobHandler.cs:36-40`) — ama `MaxAttempts` (varsayılan 3)
  yüzünden bu mesajın `job.errorMessage`'a yazılması için işin ÜÇÜNCÜ
  denemesinin bitmesi gerekir; ara denemelerde `ReleaseForRetryAsync` ile
  `Pending`'e döner. Koşum kaç deneme sonra `Failed` olduğunu kaydeder.

### MT-JOB-040 — `Pending` bir işi iptal etmek → `204`, durum `Cancelled`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Adımlar**
1. `dotnet user-secrets set "Tracon:Scheduling:RunWorker" "false"`,
   yeniden başlat (işçi işi ASLA almasın diye — iptal penceresini genişletir).
2. `ozet-toplu`'yu tetikle, `job-id`'yi al.
3. Hemen iptal et.
4. Ayarı kaldır, yeniden başlat.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/jobs/<job-id>/cancel" -H "$APB"
curl -s "$APU/api/jobs/<job-id>" -H "$APB" | python3 -c "import json,sys; print(json.load(sys.stdin)['job']['status'])"
```

**Beklenen sonuç**
- İptal: `HTTP: 204`.
- Sonraki okuma: `status: "Cancelled"`.

---

### MT-JOB-041 — Zaten iptal edilmiş bir işi tekrar iptal etmek → `409`

Negatif senaryo — kaydın varlığı ile durumunun uygunluğu ayrı denetlenir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Ön koşul**
- MT-JOB-040'ın iptal edilmiş `job-id`'si.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/jobs/<job-id>/cancel" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 409`, `title: "Is iptal edilemedi"`, `detail: "Is zaten
  'Cancelled' durumunda."` (`SchedulingEndpoints.cs:293-298` — kayıt
  BULUNUR ama `CancelAsync` `false` döner çünkü durum uygun değil; bu
  `404`'ten ayrılır).

---

### MT-JOB-042 — `Running` bir iş iptal edilirse işçi ögeler arasında bunu fark eder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Ön koşul**
- `ozet-toplu`yu tetikle (2 öge), işçinin ilk ögeyi işlemeye BAŞLADIĞI anı
  yakalamaya çalış (ilk ögeden hemen sonra, ikinciden önce iptal etmek
  zamanlamaya bağlıdır — koşum gerçek sonucu kaydeder).

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/schedules/ozet-toplu/trigger" -H "$APB" -d '{}'
curl -s -X POST "$APU/api/jobs/<job-id>/cancel" -H "$APB"
sleep 3
curl -s "$APU/api/jobs/<job-id>" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `job.status: "Cancelled"`. İlk öge zaten tamamlanmışsa `doneItems: 1`,
  ikinci öge `Pending` kalır (asla işlenmez) — `AgentBatchJobHandler`'ın
  döngü başındaki `IsCancelledAsync` kontrolü (`AgentBatchJobHandler.cs:44-47`).
  İki öge de henüz başlamadıysa `doneItems: 0` ve ikisi de `Pending` kalır.

---

### MT-JOB-043 — Var olmayan bir iş kimliğini iptal etmek → `404`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/jobs/00000000-0000-0000-0000-000000000000/cancel" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404`, `title: "Is bulunamadi"`.

---

### MT-JOB-044 — Arayüzden iptal: `Cancel` düğmesi yalnız `Pending`/`Leased`/`Running`'de görünür

Sınır durumu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Adımlar**
1. Jobs ekranında tamamlanmış bir işe (MT-JOB-021'in işi) bak.
2. Devam eden veya bekleyen bir işe bak.

**Beklenen sonuç**
- Tamamlanmış işte `Cancel` düğmesi YOK
  (`jobs.tsx:445-450`/`job-detail.tsx:48-62`, `cancellable` koşulu).
- Bekleyen/çalışan işte düğme görünür (rol `Operator` da gerekir, ama sample
  uygulamada statik token her role sahiptir).

### MT-JOB-050 — `RunWorker = false` iken hiçbir iş kiralanmaz, kuyrukta `Pending` bekler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Adımlar**
1. `dotnet user-secrets set "Tracon:Scheduling:RunWorker" "false"`,
   yeniden başlat.
2. `ozet-toplu`'yu tetikle.
3. 30 saniye bekle, işi tekrar oku.
4. Ayarı kaldır, yeniden başlat — iş bu kez normal şekilde tamamlanır.

**Beklenen sonuç**
- Adım 3: `job.status: "Pending"` (hiçbir zaman `Leased`/`Running` olmaz —
  `JobWorkerBackgroundService.ExecuteAsync`, `!options.RunWorker` dalı
  hiçbir zamanlayıcı kurmadan hemen döner, `JobWorkerBackgroundService.cs:42-45`).
  Depolar ve HTTP uçları normal çalışmaya devam eder — yalnız kiralama yok.

---

### MT-JOB-051 — Var olmayan `targetName` (agent) hedefli iş, `MaxAttempts` denemesinde `Failed` olur

Negatif senaryo, geri adımsız yeniden deneme merdiveni.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | K-160 |

**Ön koşul**
- MT-JOB-026/027'nin `bozuk-hedef` zamanlaması.

**Adımlar**
1. `bozuk-hedef`'i yeniden tetikle.
2. `PollInterval` (10 sn) aralıklarla `job.attempt`'i izle.

**Girilecek veri**
```bash
for i in 1 2 3 4; do sleep 11; curl -s "$APU/api/jobs/<job-id>" -H "$APB" | python3 -c "import json,sys; d=json.load(sys.stdin)['job']; print(d['attempt'], d['status'])"; done
```

**Beklenen sonuç**
- `attempt` sırayla `1`, `2`, `3` olur (varsayılan `MaxAttempts: 3`); her
  aradan sonra durum `Pending`'e döner (`ReleaseForRetryAsync`,
  `retryAfter: null` — hemen yeniden kiralanabilir). `attempt = 3`
  tamamlandığında durum `Failed`'e kilitlenir
  (`JobWorkerBackgroundService.cs:210-223`, `job.Attempt >= maxAttempts`).

---

### MT-JOB-052 — `PollInterval <= 0` ayarlanırsa işçi hiçbir tur atmadan hemen döner

Sınır durumu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Adımlar**
1. `dotnet user-secrets set "Tracon:Scheduling:PollInterval" "00:00:00"`,
   yeniden başlat.
2. Herhangi bir zamanlamayı tetikle.
3. 20 saniye bekle, işin `Pending` kaldığını doğrula.
4. Ayarı kaldır, yeniden başlat.

**Beklenen sonuç (doküman düzeltmesi)**
- **Doküman kusuru:** `Tracon__Scheduling__PollInterval="00:00:00"` ile
  uygulama hiç **başlamaz** —
  `TraconSchedulingOptionsValidator.cs:28-32`, `PollInterval <=
  TimeSpan.Zero` için `OptionsValidationException` fırlatır (`ValidateOnStart`).
  `JobWorkerBackgroundService.cs:42`'deki `options.PollInterval <=
  TimeSpan.Zero` erken-çıkış dalı bu yüzden **ölü koddur** — standart
  `IConfiguration`/`UseScheduling()` yoluyla asla ulaşılamaz (yalnız kod
  tabanlı, doğrulayıcıyı atlayan bir `TraconSchedulingOptions` örneği
  bu dalı tetikleyebilir). Doğru beklenti: uygulama başlamayı reddeder.

---

### MT-JOB-053 — Zamanlama/iş eylemleri denetim izine YAZILMAZ

Sınır durumu — şüpheli boşluk, doğrudan koddan ölçüldü.
`AuditingWorkflowDefinitionStore`/`AuditingSkillScriptGrantStore`'un aksine,
`src/Tracon.Core/Audit/` altında bir `AuditingJobScheduleStore` veya
`AuditingJobStore` **yoktur** (`find src/Tracon.Core/Audit -type f` bu
ikisini içermiyor); `SchedulingEndpoints.CancelJobAsync` da (RunEndpoints'in
`CancelRunAsync`'inin aksine) hiçbir `AuditRecorder.WriteAsync` çağrısı
yapmaz.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Adımlar**
1. Yeni bir zamanlama oluştur, sil, bir iş tetikle, iptal et.

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM tracon.audit_log WHERE occurred_at > now() - interval '2 minutes'
  AND (action LIKE 'schedule.%' OR action LIKE 'job.%');
```

**Beklenen sonuç**
- `0` — hiçbir zamanlama/iş eylemi denetim izine düşmez. Karşılaştırma:
  aynı dakikada bir workflow tanımı kaydedilirse `workflow.save` satırı
  görülür (`15-WORKFLOWS.md`'de zaten doğrulandı). Bu, zamanlanmış işlerin
  gerçek para harcayabilmesi (bir agent'ı onlarca kez çalıştırabilir)
  göz önüne alındığında bir gözlemlenebilirlik boşluğudur.

---

### MT-JOB-054 — `MaxConcurrentJobs` sınırı: 3. iş, ilk ikisi bitene kadar kiralanmaz

Sınır durumu — eşzamanlılık sınırı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 17 |
| **İlgili karar** | — |

**Adımlar**
1. `dotnet user-secrets set "Tracon:Scheduling:MaxConcurrentJobs" "1"`,
   yeniden başlat.
2. Art arda 2 kez `ozet-toplu`'yu tetikle (2 ayrı `job-id`).
3. Hemen her ikisinin durumunu oku.
4. Ayarı kaldır, yeniden başlat.

**Beklenen sonuç**
- Bir iş `Leased`/`Running`, diğeri `Pending` kalır — `TickAsync`'in
  `SemaphoreSlim slots` mantığı (`JobWorkerBackgroundService.cs:60,91-118`)
  eşzamanlı `LeaseAsync` çağrısını `MaxConcurrentJobs` ile sınırlar.

### MT-JOB-060 — `SingletonExecution.Enabled = false` (varsayılan) iken `singleton_leases` tablosuna HİÇ satır yazılmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 42 |
| **İlgili karar** | K1 |

**Ön koşul**
- Varsayılan yapılandırma (`SingletonExecution:Enabled` ayarlanmamış).
- Uygulama en az bir kez çalıştırılmış (MCP keşfi ve model sağlık servisleri
  başlamış olmalı).

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM tracon.singleton_leases;
```

**Beklenen sonuç**
- `0` — `SingletonGuard.RunAsync`, `Enabled` kontrolünü depoya HİÇ
  dokunmadan yapar (K1, `SingletonDisabledTests`'in kanıtladığı davranış).
  Tablo migration ile VARDIR ama boştur.

---

### MT-JOB-061 — `Enabled = true` + `Health.BackgroundInterval` açılınca `model-provider-health` kirası yazılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 42 |
| **İlgili karar** | — |

**Adımlar**
1. `dotnet user-secrets set "Tracon:SingletonExecution:Enabled" "true"`
2. `dotnet user-secrets set "Tracon:SingletonExecution:LeaseDuration" "00:00:12"`
3. `dotnet user-secrets set "Tracon:Health:BackgroundInterval" "00:00:05"`
4. `dotnet user-secrets set "Tracon:Mcp:RefreshInterval" "00:00:05"`
   (🚨 bu ayar artık `IConfiguration`'dan bağlanıyor — bkz. Koşmadan önce §5;
   Faz 42'nin devir notundaki "MCP `RefreshInterval` yapılandırmadan
   bağlanmaz" tuzağı `samples/Tracon.Api/Program.cs:93`'ün
   `.UseMcp(builder.Configuration.GetSection(...))` çağrısıyla ÇÖZÜLMÜŞTÜR —
   bu, faz kapanışından sonra düzeltilmiş, henüz hiçbir manuel test dosyasında
   kaydedilmemiş bir düzeltmedir).
5. Yeniden başlat, birkaç saniye bekle.

**Doğrulama sorgusu**
```sql
SELECT name, owner_id, expires_at, updated_at FROM tracon.singleton_leases ORDER BY name;
```

**Beklenen sonuç (doküman düzeltmesi)**
- **Doküman güncelliğini yitirmiş:** İki satır değil **üç** satır beklenir
  — `mcp-discovery`, `model-provider-health` ve `approval-expiration`
  (`src/Tracon.Core/Approvals/ApprovalExpirationService.cs`, Faz 42'nin
  dokümante edildiği tarihten SONRA eklenmiş üçüncü bir `SingletonGuard`
  tüketicisi). Üçünün de `owner_id`'si `{MachineName}:{ProcessId}:{Guid}`
  biçiminde ve **aynı sürece** ait olmalıdır.

---

### MT-JOB-062 — İki süreç aynı veritabanına bağlanınca kira yalnız BİRİNDE kalır

| | |
|---|---|
| **İzlek** | A (repo dışı derlenmiş DLL) |
| **Önem** | Kritik |
| **İlgili faz** | Faz 42 |
| **İlgili karar** | K-284 |

Bu case `dotnet run` ile ÇALIŞTIRILAMAZ — `launchSettings.json` her zaman
`5080` portunu açar ve `--urls`'i ezer. Derlenmiş DLL doğrudan çalıştırılır.

**Ön koşul**
- MT-JOB-061'in ayarları hâlâ `user-secrets`'ta.
- `dotnet publish samples/Tracon.Api -c Release -o /tmp/ap-publish`

**Adımlar**
1. Aynı SQLite dosyasına (veya PostgreSQL bağlantısına) bağlanan iki süreci
   farklı portlarda başlat.
2. Kirayı sorgula — ikisi de aynı `owner_id`'yi mi taşıyor.

**Girilecek veri**
```bash
TRACON__SQLITE__CONNECTIONSTRING="Data Source=singleton-demo.db" \
  dotnet /tmp/ap-publish/Tracon.Api.dll --urls http://localhost:5091 &
TRACON__SQLITE__CONNECTIONSTRING="Data Source=singleton-demo.db" \
  dotnet /tmp/ap-publish/Tracon.Api.dll --urls http://localhost:5092 &

sleep 8
sqlite3 singleton-demo.db "SELECT name, owner_id FROM tracon_singleton_leases;"
```

**Beklenen sonuç**
- İki satır (`mcp-discovery`, `model-provider-health`), her ikisinde de
  `owner_id` **AYNI** sürece (A veya B, hangisi önce kazandıysa) ait —
  iki farklı `owner_id` GÖRÜLMEZ. B'nin (kaybedeni) günlüğünde MCP keşif
  tamamlanma satırı **hiç görünmez**.

---

### MT-JOB-063 — Kira sahibi süreç öldürülünce diğer süreç DEVRALIR; süre ölçülür

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 42 |
| **İlgili karar** | K-286 |

**Ön koşul**
- MT-JOB-062 çalışıyor; kirayı tutan sürecin PID'si biliniyor.

**Adımlar**
1. Kirayı TUTAN süreci `kill -TERM <PID>` ile durdur, saatini not al.
2. `watch -n 2` ile `owner_id`'nin diğer sürece geçişini izle.

**Girilecek veri**
```bash
kill -TERM <sahip-PID>
watch -n 2 'sqlite3 singleton-demo.db "SELECT name, owner_id, expires_at FROM tracon_singleton_leases;"'
```

**Beklenen sonuç**
- `owner_id` en geç `~1,5 × LeaseDuration` (12 sn kirada Faz 42'nin ölçtüğü
  gerçek değer **~17,6 sn**'ydi; bu ortamda yeniden ölçülüp buraya
  yazılır) içinde HAYATTA KALAN sürece geçer. Hayatta kalan sürecin
  günlüğünde bu andan sonra `"MCP kesfi tamamlandi"` satırları YENİDEN
  görünmeye başlar.
- İki süreci de durdur (`kill`), `/tmp/ap-publish` ve `singleton-demo.db`'yi
  temizle (bu dosyalar geçicidir, iş bitince silinir — `PROMPT.md`'nin
  workspace hijyeni kuralı bu oturumlar için geçerli değildir ama koşum
  sonrası temizlik iyi pratiktir).

---

### MT-JOB-064 — Yenileme aralığı `LeaseDuration/3`tür: `updated_at` düzenli aralıklarla ilerler

Sınır durumu — dolaylı gözlem.

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Düşük |
| **İlgili faz** | Faz 42 |
| **İlgili karar** | — |

**Ön koşul**
- MT-JOB-061'in tek süreçli kurulumu, `LeaseDuration=00:00:12`.

**Adımlar**
1. `updated_at` alanını art arda birkaç kez oku, aralarındaki farkı hesapla.

**Girilecek veri**
```bash
for i in 1 2 3; do sqlite3 singleton-demo.db "SELECT name, updated_at FROM tracon_singleton_leases;"; sleep 5; done
```

**Beklenen sonuç**
- `updated_at` yaklaşık **4 saniyede bir** ilerler (`12 / 3 = 4`,
  `SingletonGuard`'ın yenileme aralığı `LeaseDuration/3`, en az 1 sn tabanı
  vardır).

---

### MT-JOB-065 — İki süreç aynı anda kuyruktan iş çeker, İKİSİ DE aynı işi almaz

Sınır durumu — `singleton_leases` mekanizması BURADA devrede DEĞİLDİR;
`FOR UPDATE SKIP LOCKED` zaten Faz 17'den beri güvenlidir (Faz 42.2).

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 17, 42 |
| **İlgili karar** | — |

**Ön koşul**
- MT-JOB-062'nin iki süreci hâlâ çalışıyor (veya yeniden başlatılır).
  `SingletonExecution.Enabled` bu case için ÖNEMSİZDİR — iş kuyruğu ondan
  bağımsız çalışır.

**Adımlar**
1. Her iki portta da art arda birkaç `trigger` çağrısı yap (toplam ör. 6 iş).
2. Her işin hangi `lease_owner`'a gittiğini oku.

**Girilecek veri**
```bash
for i in 1 2 3; do curl -s -X POST http://localhost:5091/tracon/api/schedules/ozet-toplu/trigger -d '{}'; done
for i in 1 2 3; do curl -s -X POST http://localhost:5092/tracon/api/schedules/ozet-toplu/trigger -d '{}'; done
sleep 12
sqlite3 singleton-demo.db "SELECT id, lease_owner, status FROM tracon_jobs ORDER BY created_at DESC LIMIT 6;"
```

**Beklenen sonuç**
- Her `id` yalnız BİR `lease_owner`'a sahiptir; hiçbir işin iki farklı
  sürece ait olduğu görülmez, hiçbir öge iki kez `runs` satırı üretmez.

---

### MT-JOB-066 — Kira durumunu görmenin TEK yolu veritabanı sorgusudur — hiçbir HTTP ucu yoktur

Sınır durumu — dokümantasyon amaçlı, negatif doğrulama.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 42 |
| **İlgili karar** | — |

**Adımlar**
1. `/api/diagnostics` (veya benzeri teşhis ucu) yanıtında `singleton`/`lease`
   kelimesini ara.

**Girilecek veri**
```bash
curl -s "$APU/api/diagnostics" -H "$APB" | grep -io "singleton\|lease" | sort -u
```

**Beklenen sonuç**
- Boş çıktı — Faz 42'nin devir notu bunu "Faz 33'ün teşhis ucuna aittir"
  diye ertelemişti; bu ölçüm o ertelemenin hâlâ geçerli olduğunu (Faz 33
  bunu almadı) doğrular. Kira durumunu görmek isteyen bir operatör
  bugün yalnız doğrudan veritabanı sorgusuna (bu bölümün yaptığı gibi)
  başvurabilir.

### MT-JOB-070 — `Prefer: respond-async` → `202` + `Location` + `Preference-Applied` + `AcceptedRunResponse`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 46 |
| **İlgili karar** | K-307 |

**Girilecek veri**
```bash
curl -s -D - -o /tmp/accepted-body.json -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" -H "Prefer: respond-async" \
     -d '{"message":"ORD-1001 siparisim nerede?"}'
grep -i "^HTTP/\|^location:\|^preference-applied:" < /dev/stdin <<< "$(cat /tmp/accepted-body.json 2>/dev/null)" || true
cat /tmp/accepted-body.json
```

**Beklenen sonuç**
- `HTTP/1.1 202 Accepted`.
- `Location: /tracon/api/runs/<runId>`.
- `Preference-Applied: respond-async`.
- Gövde `AcceptedRunResponse`: `runId`, `jobId` (`runId` ile **birebir
  aynı** — K-305), `location`, `eventsLocation` (`.../events` ile biter).

---

### MT-JOB-071 — `202`den HEMEN sonra `GET /api/runs/{runId}` → `Queued`, `404` DEĞİL

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 46 |
| **İlgili karar** | — |

**Ön koşul**
- MT-JOB-070'in `runId`'si.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/runs/<runId>" -H "$APB" | python3 -c "import json,sys; lines=sys.stdin.read(); print(lines)"
```

**Beklenen sonuç**
- `HTTP: 200`, `status: "Queued"` — işçi işi HENÜZ almamış olsa bile satır
  önceden yazılmıştır (`AgentEndpoints.cs:583-593`).

---

### MT-JOB-072 — İşçi işi alır: `Queued` → `Running` → `Completed`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 46 |
| **İlgili karar** | — |

**Adımlar**
1. MT-JOB-071'in `runId`'sini birkaç saniye arayla tekrar tekrar oku.

**Beklenen sonuç**
- Durum sırayla `Queued` → (kısaca) `Running` → `Completed` geçer.
- `support` agent'ı `ORD-1001` için `get_order_status` tool'unu çağırır
  (deterministik iddia: tool tam bir kez çağrılır — §4.1 kuralı, metne
  bakılmaz).

---

### MT-JOB-073 — `runs.id` `Location`'daki kimlikle BİREBİR eşleşir; iş kimliği de aynıdır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 46 |
| **İlgili karar** | K-304, K-305 |

**Doğrulama sorgusu**
```sql
SELECT id, kind, workflow_name FROM tracon.runs WHERE id = '<runId>';
```
```bash
curl -s "$APU/api/jobs/<runId>" -H "$APB" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['job']['id'], d['job']['kind'])"
```

**Beklenen sonuç**
- SQL: bir satır, `id = <runId>`.
- `GET /api/jobs/{runId}`: `job.id == runId`, `job.kind: "AgentRun"` — iş
  kimliği ile çalıştırma kimliği bilerek AYNI GUID'i taşır.

---

### MT-JOB-074 — `202`den sonra `/events`e bağlanan istemci BAŞLANGIÇ olaylarını kaçırmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 46 |
| **İlgili karar** | K-014 |

**Adımlar**
1. Yeni bir `Prefer: respond-async` isteği gönder, `runId`'yi al.
2. HEMEN (işçi almadan önce, `sleep` KOYMADAN) `/events`e bağlan.

**Girilecek veri**
```bash
curl -sN "$APU/api/runs/<runId>/events" -H "$APB"
```

**Beklenen sonuç**
- İlk olay `run.started`'tır (veya eşdeğeri) — akış işçi işi henüz almasa
  bile bağlantı açık kalır ve TÜM olaylar baştan gelir; hiçbir olay
  kaçırılmaz (`RunEndpoints`'in `Queued`'u da bekleyecek şekilde
  genişletilmiş döngüsü, Faz 46 Plandan Sapmalar madde 6).

---

### MT-JOB-075 — Başlık GÖNDERİLMEYEN istekte davranış DEĞİŞMEZ (K1)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 46 |
| **İlgili karar** | K1 |

**Girilecek veri**
```bash
curl -s -D - -o /dev/null -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" -d '{"message":"ORD-1002 siparisim nerede?"}' | \
     grep -i "^HTTP/\|^content-type:"
```

**Beklenen sonuç**
- `HTTP/1.1 200 OK`, `Content-Type: text/event-stream` — bugünkü davranış
  bit bit aynı, `Location`/`Preference-Applied` YOKTUR.

---

### MT-JOB-076 — Boş `message` ile `Prefer: respond-async` → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 46 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" -H "Prefer: respond-async" -d '{}'
```

**Beklenen sonuç**
- `HTTP: 400`, `title: "Istek bos"`, `detail: "Kuyruga alinan bir
  calistirmada 'message' zorunludur."` (`AgentEndpoints.cs:536-542`).

---

### MT-JOB-077 — Ek (`attachmentIds`) veya onay kararı ile birlikte `respond-async` → `400`

Negatif senaryo — bu sürümde desteklenmiyor.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 46 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" -H "Prefer: respond-async" \
     -d '{"message":"test","attachmentIds":["00000000-0000-0000-0000-000000000001"]}'
```

**Beklenen sonuç**
- `HTTP: 400`, `title: "Desteklenmiyor"`, `detail: "Kuyruga alinan
  ('Prefer: respond-async') bir calistirma onay kararlarini veya ekleri bu
  surumde desteklemez."` (`AgentEndpoints.cs:544-551`). Gerekçe: ek
  referansı `MapTracon`'in `prefix`'ine ihtiyaç duyar ve bu değer
  `AgentRunJobHandler`'ın DI kayıt anında bilinmez.

---

### MT-JOB-078 — `Tracon:AsyncRun:Enabled = false` → başlık taşıyan istek `501` alır

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 46 |
| **İlgili karar** | — |

**Adımlar**
1. `dotnet user-secrets set "Tracon:AsyncRun:Enabled" "false"`,
   yeniden başlat.
2. `Prefer: respond-async` ile istek gönder.
3. Ayarı kaldır, yeniden başlat.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" -H "Prefer: respond-async" -d '{"message":"test"}'
```

**Beklenen sonuç**
- `HTTP: 501`, `title: "Kuyruga alma destegi kapali"`, `detail`
  `TraconAsyncRunOptions.Enabled = false` ifadesini içerir
  (`AgentEndpoints.cs:527-534`). Sessizce SSE'ye düşmez.

---

### MT-JOB-079 — Kota dolu iken kuyruğa alma `429` alır, iş AÇILMAZ

Negatif senaryo — kota kuralının kendisi `23-SAKLAMA-ARSIV-KOTA.md`'nin
işidir; burada yalnız kuyruğa almanın kota kapısından geçtiği doğrulanır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 46 |
| **İlgili karar** | K-162 |

**Ön koşul**
- `support` agent'ı için `MaxCost`/`MaxRuns` gibi çok düşük (ör. `0`) bir
  kota kuralı geçici olarak tanımlanmış olmalı (kota kural motoru bu
  dosyanın kapsamı dışıdır; kurulum adımı `23-SAKLAMA-ARSIV-KOTA.md`'ye
  bakılarak yapılır — burada yalnız SONUÇ doğrulanır).

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" -H "Prefer: respond-async" -d '{"message":"test"}'
curl -s "$APU/api/jobs?kind=AgentRun" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- `HTTP: 429`. Hiçbir yeni `AgentRun` işi açılmamıştır — `QuotaGate.CheckAsync`
  `WantsAsync` dallanmasından ÖNCE çalışır (`AgentEndpoints.cs:119-128`).

---

### MT-JOB-080 — `Queued` durumdaki bir çalıştırma iptal edilebilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 46 |
| **İlgili karar** | — |

**Ön koşul**
- `dotnet user-secrets set "Tracon:Scheduling:RunWorker" "false"`,
  yeniden başlat (işçi işi almasın diye).

**Girilecek veri**
```bash
RESP=$(curl -s -D - -o /tmp/q.json -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" -H "Prefer: respond-async" -d '{"message":"ORD-1001 siparisim nerede?"}')
RUN_ID=$(python3 -c "import json;print(json.load(open('/tmp/q.json'))['runId'])")
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/$RUN_ID/cancel" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 202`, gövde `RunRecord`: `status: "Canceled"`,
  `completedAt` dolu (`RunEndpoints.cs:652-674`, `Queued` dalı — iş
  kuyruktan `IJobStore.CancelAsync` ile iptal edilir ve `runs` satırı
  DOĞRUDAN kapatılır, işçi asla almadığı için orphan OLUŞMAZ).
- İşi kaldır: `dotnet user-secrets remove "Tracon:Scheduling:RunWorker"`,
  yeniden başlat.

---

### MT-JOB-081 — İptal edilmiş bir kuyruk çalıştırmasını TEKRAR iptal etmek → `409`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 46 |
| **İlgili karar** | — |

**Ön koşul**
- MT-JOB-080'in iptal edilmiş `RUN_ID`'si.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<RUN_ID>/cancel" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 409`, `title: "Calistirma zaten sonlanmis"`, `detail` `'Canceled'`
  durumunu belirtir (`RunEndpoints.cs:654-660`, ikinci `jobs.CancelAsync`
  çağrısı `false` döner çünkü iş zaten `Cancelled`).

---

### MT-JOB-082 — Var olmayan `runId` iptali → `404`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 46 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/00000000-0000-0000-0000-000000000000/cancel" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404`.

---

### MT-JOB-083 — `Prefer: respond-async` + AYNI `Idempotency-Key` → TEK iş, aynı `Location`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 43, 46 |
| **İlgili karar** | — |

**Adımlar**
1. Aynı `Idempotency-Key` ile iki kez, `Prefer: respond-async` ile istek at.

**Girilecek veri**
```bash
curl -s -D - -o /tmp/first.json -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" -H "Prefer: respond-async" \
     -H "Idempotency-Key: tekillestirme-testi-01" -d '{"message":"ORD-1001 siparisim nerede?"}' | grep -i "^location:"
curl -s -D - -o /tmp/second.json -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" -H "Prefer: respond-async" \
     -H "Idempotency-Key: tekillestirme-testi-01" -d '{"message":"ORD-1001 siparisim nerede?"}' | grep -i "^location:"
```

**Beklenen sonuç**
- İki `Location` başlığı **birebir aynı** — ikinci istek YENİ bir iş
  açmaz, ilk `202`'nin tekilleştirilmiş gövdesini döner (`IdempotencyFilter`,
  gövdedeki `stream` alanına bakar; `AgentRunRequest`'in böyle bir alanı
  olmadığı için akışlı-istek reddi bu ucu HİÇ tetiklemez — Faz 46 §46.5).

---

### MT-JOB-084 — Akışlı istek (başlık YOK) + `Idempotency-Key` → Faz 43'ün `400`'ü KORUNUR

Negatif senaryo — `Prefer: respond-async` OLMADAN idempotency anahtarı akışlı
bir uçta hâlâ reddedilir.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 43 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" -H "Idempotency-Key: akisli-red-testi" \
     -d '{"message":"test"}'
```

**Beklenen sonuç**
- `HTTP: 400` — akışlı bir istek `Idempotency-Key` taşıyamaz. `detail`
  metni `Prefer: respond-async` kullanmayı ÖNERİR (Faz 46 §46.5.1'in
  DoD'de doğrulanan sonucu: `/api/agents/{name}/run` için bu mesaj
  güncellenmiştir; OpenAI uyumlu uçlar için AYNI mesaj güncellenmemiştir —
  bkz. Plandan Sapmalar madde 8, o uçlar `Prefer`'i hiç tanımadığı için).

**Beklenen sonuç (doküman düzeltmesi)**
- **Doküman güncelliğini yitirmiş.** Gerçek davranış `400` DEĞİL: kaynak
  doğrulandı — `AgentEndpoints.cs`'deki `RunAsync`, akış SEÇİMİNİ
  `var streaming = !httpContext.Request.Headers.ContainsKey(
  IdempotencyFilter.HeaderName);` ile kendi içinde yapıyor (kod yorumu:
  "🚨 Faz 43: 'Idempotency-Key' tasiyan bir istek akissiz calisir...").
  Yani `Idempotency-Key` başlığının VARLIĞI tek başına, `Prefer:
  respond-async` OLMADAN da, akışsız (buffered JSON) modu seçmeye
  yeterlidir — genel `IdempotencyFilter`'ın `400` reddi bu uca hiç
  uygulanmıyor (o yalnız OpenAI uyumlu uçlarda devrede). Doğru beklenti:
  `HTTP: 200`, normal buffered JSON yanıtı.

---

### MT-JOB-085 — Jobs ekranında `AgentRun` türü iş görünür ama Ögeler listesi BOŞTUR

Sınır durumu — `Prefer: respond-async` ile açılan iş `job_items` tablosuna
hiç satır yazmaz; yükü doğrudan `job.payload` taşır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 46 |
| **İlgili karar** | — |

**Adımlar**
1. MT-JOB-072'nin tamamlanmış `AgentRun` işini Jobs ekranında aç
   (`jobs/{runId}`).

**Beklenen sonuç**
- Üst bilgi paneli `kind: AgentRun`, `status: Completed` gösterir.
- **Items** paneli BOŞ durum (`jobs.noItems`/`jobs.noItemsBody`) gösterir —
  `JobDetailScreen`'in `items.length === 0` dalı (`job-detail.tsx:97-99`).
  Bu, işin BAŞARISIZ olduğu anlamına GELMEZ; yalnızca `AgentRunJobHandler`'ın
  öge kümesi kullanmadığının arayüz yansımasıdır.

### MT-JOB-090 — 🚨 `RunsRead`-kapsamlı bir API anahtarı zamanlama silebiliyor/iş iptal edebiliyor mu?

Şüpheli davranış — koddan ölçüldü, koşumda doğrulanacak/çürütülecek. Aynı
kalıp `15-WORKFLOWS.md` `MT-WF-100`'de `WorkflowEndpoints` için doğrulandı;
`SchedulingEndpoints.cs` de (`grep -n "RequireApiKeyScope"
src/Tracon.AspNetCore/Endpoints/SchedulingEndpoints.cs` boş döner)
**hiçbir ucunda** `RequireApiKeyScope(...)` çağırmaz. `TraconEndpointFilter.CheckScope`
metadata yoksa denetimi koşulsuz geçirir (bkz. `15-WORKFLOWS.md`'nin aynı
bulgusu). Rol politikaları zaten no-op olduğu için, doğrularsa yalnız
`RunsRead` taşıyan bir OKUMA-amaçlı anahtar zamanlama silebilir, iş
iptal edebilir ve (dolaylı olarak, bir `AgentBatch` zamanlaması aracılığıyla)
gerçek para harcayan çalıştırmalar tetikleyebilir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 17, 53 |
| **İlgili karar** | K-… (53.3, kapsam ∩ rol ilkesi) |

**Ön koşul**
- `13-KIRACI-VE-GUVENLIK.md` `MT-SEC-050`'nin API anahtarı oluşturma deseni.

**Adımlar**
1. Yalnız `RunsRead` kapsamıyla bir API anahtarı üret.
2. Bu anahtarla bir zamanlama YAZMAYI dene.
3. Aynı anahtarla o zamanlamayı SİLMEYİ dene.
4. Kontrol grubu: aynı anahtarla `AgentsAdmin` gerektiren bir agent ucuna
   yazmayı dene, `403` aldığını doğrula.

**Girilecek veri**
```bash
KEY_JSON=$(curl -s -X POST "$APU/api/api-keys" -H "$APB" -H "content-type: application/json" \
  -d '{ "name": "job-kapsam-testi", "scopes": ["RunsRead"] }')
echo "$KEY_JSON" | python3 -c "import json,sys; print(json.load(sys.stdin)['rawKey'])"
export JOBKEY="Authorization: Bearer <yukaridaki-rawKey>"

curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/schedules/kapsam-testi" -H "$JOBKEY" \
     -H "content-type: application/json" -d '{"kind":"AgentBatch","targetName":"ozetleyici","timeZone":"UTC","payload":[]}'

curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/schedules/kapsam-testi" -H "$JOBKEY"

curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/agents/kapsam-kontrol" -H "$JOBKEY" \
     -H "content-type: application/json" -d '{"name":"kapsam-kontrol","instructions":"test"}'
```

**Beklenen sonuç (şüphe)**
- Adım 2 ve 3: `HTTP: 200`/`204` — kapsam kısıtı UYGULANMAZ.
- Adım 4: `HTTP: 403`, `title: "Kapsam yetersiz"` — kontrol grubu kapsam
  sisteminin AgentEndpoints'te çalıştığını, ama SchedulingEndpoints'te (ve
  daha önce görüldüğü gibi WorkflowEndpoints'te) HİÇ devrede olmadığını
  gösterir.
- Doğrularsa: bu, iki bağımsız uç grubunda (Workflow + Scheduling)
  tekrarlayan bir kalıptır ve muhtemelen üçüncü bir yerde de (kontrol
  edilmeyen diğer uçlar) mevcuttur — **Kusur, Önem: Yüksek**, kapsam
  sisteminin `RequireApiKeyScope` eklenmesi UNUTULMUŞ uç gruplarını
  tarayan sistematik bir denetim önerilir.

---

## Gelen Tetikleyiciler (Faz 66)

Bu bölümün kaynağı `docs/arsiv/fazlar/66-GELEN-TETIKLEYICILER.md`'dir. İmza `openssl` ile
elle hesaplanır — `$APU`/`$APB` bu dosyanın da temel değişkenleridir
([`00-INDEKS.md`](00-INDEKS.md)), ayrıca `$TRIGSECRET` bu bölüme özeldir.

**Ortak kurulum (her case'den önce, bir kez):**
```bash
export TRIGSECRET="whsec_manuel_test_66"
dotnet user-secrets set "Tracon:TriggerSecrets:Slack" "$TRIGSECRET" \
  --project samples/Tracon.Api

curl -s -X PUT "$APU/api/triggers/slack" -H "$APB" -H "content-type: application/json" -d '{
  "targetKind":"agent","targetName":"support",
  "signingSecretConfigurationName":"Tracon:TriggerSecrets:Slack",
  "payloadMode":"path","payloadPath":"event.text"
}'
```

İmzalı bir istek göndermenin yordamı (her case bunu `BODY`/`TS` değerleriyle
tekrarlar):
```bash
BODY='{"event":{"text":"merhaba"}}'
TS=$(date +%s)
SIG="sha256=$(printf '%s.%s' "$TS" "$BODY" | openssl dgst -sha256 -hmac "$TRIGSECRET" | sed 's/^.* //')"

curl -s -i -X POST "$APU/api/triggers/default/slack" \
  -H "content-type: application/json" \
  -H "X-Tracon-Timestamp: $TS" \
  -H "X-Tracon-Signature: $SIG" \
  -d "$BODY"
```

### MT-JOB-091 — Doğru imzalı istek `202` döner ve çalıştırma kuyruktan koşar

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 66 |
| **İlgili karar** | — |

**Ön koşul:** ortak kurulum uygulandı.

**Girilecek veri**
```bash
BODY='{"event":{"text":"merhaba manuel test"}}'
TS=$(date +%s)
SIG="sha256=$(printf '%s.%s' "$TS" "$BODY" | openssl dgst -sha256 -hmac "$TRIGSECRET" | sed 's/^.* //')"

curl -s -i -X POST "$APU/api/triggers/default/slack" \
  -H "content-type: application/json" \
  -H "X-Tracon-Timestamp: $TS" -H "X-Tracon-Signature: $SIG" -d "$BODY"
```

**Beklenen sonuç**
- `HTTP: 202`, `Location` başlığı `/tracon/api/runs/{runId}` biçiminde.
- Gövdede `runId` ve `jobId` **aynı** değeri taşır (Faz 46'nın kalıbı).
- `GET $APU/api/runs/{runId}` birkaç saniye içinde `status: "Completed"`
  (veya modele bağlı `Failed`) gösterir — `Queued` değil.

---

### MT-JOB-092 — İmzasız istek `401` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 66 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/triggers/default/slack" \
  -H "content-type: application/json" -d '{"event":{"text":"x"}}'
```

**Beklenen sonuç**
- `HTTP: 401`, `title: "Signature verification failed"`.

---

### MT-JOB-093 — Gövde bir bayt değişince aynı imza artık geçmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 66 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
BODY='{"event":{"text":"orijinal"}}'
TS=$(date +%s)
SIG="sha256=$(printf '%s.%s' "$TS" "$BODY" | openssl dgst -sha256 -hmac "$TRIGSECRET" | sed 's/^.* //')"

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/triggers/default/slack" \
  -H "content-type: application/json" \
  -H "X-Tracon-Timestamp: $TS" -H "X-Tracon-Signature: $SIG" \
  -d '{"event":{"text":"degistirildi"}}'
```

**Beklenen sonuç**
- `HTTP: 401` — imza gövde+zaman damgası üzerinden hesaplanır, tek bir
  karakter değişikliği `WebhookSigner.Verify`'ı geçersiz kılar.

---

### MT-JOB-094 — On dakika eski zaman damgası `401` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 66 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
BODY='{"event":{"text":"eski"}}'
TS=$(( $(date +%s) - 600 ))
SIG="sha256=$(printf '%s.%s' "$TS" "$BODY" | openssl dgst -sha256 -hmac "$TRIGSECRET" | sed 's/^.* //')"

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/triggers/default/slack" \
  -H "content-type: application/json" \
  -H "X-Tracon-Timestamp: $TS" -H "X-Tracon-Signature: $SIG" -d "$BODY"
```

**Beklenen sonuç**
- `HTTP: 401` — varsayılan `TimestampTolerance` beş dakikadır; on dakika
  eski bir damga pencere dışındadır (imza doğru hesaplanmış olsa bile).

---

### MT-JOB-095 — Aynı imza ikinci kez `409` döner; ikinci çalıştırma açılmaz

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 66 |
| **İlgili karar** | — |

**Adımlar**
1. MT-JOB-091'deki gibi bir istek gönder, `runId`'yi not al.
2. **Aynı** `BODY`/`TS`/`SIG` ile isteği tekrar gönder.
3. `GET /api/runs` listesinde bu `targetName` için tek bir yeni satır
   olduğunu doğrula (ikinci istek yeni bir `runs` satırı AÇMADI).

**Beklenen sonuç**
- Adım 2: `HTTP: 409`, `title: "Request already processed"`.
- Adım 3: ikinci bir çalıştırma yok.

---

### MT-JOB-096 — Bilinmeyen kiracı `401` döner; varsayılan kiracıya düşmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 66 |
| **İlgili karar** | K-382 emsali |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/triggers/boyle-bir-kiraci-yok/slack" \
  -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç**
- `HTTP: 401` (`title: "Signature verification failed"`) — `default`
  kiracısındaki `slack` tetikleyicisi bu isteğe **cevap vermez**;
  `tenant_id` sütunu tam eşleşme arar, `??` zinciriyle varsayılana
  düşmez. Kod `404` DEĞİL: 66.2'nin "tetikleyici yok ile imza yanlış aynı
  gövde/kod" kuralı gereği bilinmeyen kiracı da imza hatasıyla AYNI
  jenerik `401` yanıtını alır (bkz. MT-JOB-092/093/094).

---

### MT-JOB-097 — Devre dışı tetikleyici reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 66 |
| **İlgili karar** | — |

**Adımlar**
1. Tetikleyiciyi `enabled: false` ile güncelle.
2. MT-JOB-091'deki gibi geçerli imzalı bir istek gönder.
3. Tetikleyiciyi tekrar `enabled: true` yaparak eski haline getir (sonraki
   case'ler için).

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/triggers/slack" -H "$APB" -H "content-type: application/json" -d '{
  "targetKind":"agent","targetName":"support",
  "signingSecretConfigurationName":"Tracon:TriggerSecrets:Slack",
  "payloadMode":"path","payloadPath":"event.text","enabled":false
}'

BODY='{"event":{"text":"devre disi test"}}'
TS=$(date +%s)
SIG="sha256=$(printf '%s.%s' "$TS" "$BODY" | openssl dgst -sha256 -hmac "$TRIGSECRET" | sed 's/^.* //')"
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/triggers/default/slack" \
  -H "content-type: application/json" \
  -H "X-Tracon-Timestamp: $TS" -H "X-Tracon-Signature: $SIG" -d "$BODY"
```

**Beklenen sonuç**
- `HTTP: 401` — devre dışı bir tetikleyici, imza-hatalı bir istekle AYNI
  jenerik gövdeyi döner (bilinmeyen isimden de ayırt edilmez).

---

### MT-JOB-098 — Kota dolu olunca `429` döner, kota bypass edilmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 66 |
| **İlgili karar** | K-394 emsali |

**Ön koşul:** `support` agent'ı için `runs` metriğinde düşük bir kota
tanımlı (`13-KIRACI-VE-GUVENLIK.md`'nin kota case'lerindeki desen).

**Adımlar**
1. Kotayı 1 istek/dakika olacak şekilde ayarla.
2. MT-JOB-091'deki gibi bir istek gönder (kabul edilmeli).
3. Farklı bir `BODY`/imzayla hemen ikinci bir istek gönder.

**Beklenen sonuç**
- Adım 3: `HTTP: 429`, `Retry-After` başlığı mevcut. Tetikleyici, bearer
  token taşımadığı için kota kapısını ATLAYAMAZ — `QuotaGate.CheckAsync`
  agent uçlarıyla aynı `QuotaEnforcer`'ı kullanır.

---

### MT-JOB-099 — `Path` modunda alan yoksa `400` döner, çalıştırma başlamaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 66 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
BODY='{"event":{"baska_alan":"x"}}'
TS=$(date +%s)
SIG="sha256=$(printf '%s.%s' "$TS" "$BODY" | openssl dgst -sha256 -hmac "$TRIGSECRET" | sed 's/^.* //')"

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/triggers/default/slack" \
  -H "content-type: application/json" \
  -H "X-Tracon-Timestamp: $TS" -H "X-Tracon-Signature: $SIG" -d "$BODY"
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` `"event.text"` yolunun gövdede çözülemediğini söyler.
- `GET /api/runs` listesinde bu istek için **hiçbir** yeni satır yok.

---

### MT-JOB-100 — İmza `secret`'ı veritabanında hiç yaşamaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 66 |
| **İlgili karar** | K-059 |

**Girilecek veri**
```bash
# PostgreSQL örneği; sağlayıcıya göre bağlan
psql "$PGCONN" -c "SELECT signing_secret_configuration_name FROM tracon.inbound_triggers;"
pg_dump "$PGCONN" --schema=tracon | grep -c "$TRIGSECRET"
```

**Beklenen sonuç**
- Sütun yalnız `Tracon:TriggerSecrets:Slack` (yapılandırma anahtarının
  ADI) taşır.
- `grep -c` çıktısı `0` — gerçek `secret` değeri (`$TRIGSECRET`) dökümde
  hiç geçmez.

---

### MT-JOB-101 — 100 istek arka arkaya gönderilince hız sınırı devreye girer

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 66 |
| **İlgili karar** | K-158 |

**Girilecek veri**
```bash
for i in $(seq 1 100); do
  BODY="{\"event\":{\"text\":\"yuk-$i\"}}"
  TS=$(date +%s)
  SIG="sha256=$(printf '%s.%s' "$TS" "$BODY" | openssl dgst -sha256 -hmac "$TRIGSECRET" | sed 's/^.* //')"
  curl -s -o /dev/null -w "%{http_code} " -X POST "$APU/api/triggers/default/slack" \
    -H "content-type: application/json" \
    -H "X-Tracon-Timestamp: $TS" -H "X-Tracon-Signature: $SIG" -d "$BODY"
done
echo
```

**Beklenen sonuç**
- Varsayılan `MaxRequestsPerMinute` (60) aşıldıktan sonraki istekler
  `429` döner; bu, tek bir süreç içindir (K-158: dağıtık sayaç yok).

---

### MT-JOB-102 — Tetikleyici arayüzden tanımlanır ve listelenir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 66 |
| **İlgili karar** | — |

**Adımlar**
1. Arayüzde **Triggers** ekranına git, **New trigger** ile bir tetikleyici
   oluştur (`name`, `targetName`, `signingSecretConfigurationName`
   doldurulmuş).
2. Kaydet, liste ekranına dön.
3. Yeni tetikleyicinin satırında `Resolved`/`Unresolved` rozetini kontrol
   et (`Tracon:TriggerSecrets:...` anahtarı `user-secrets`'ta tanımlı
   değilse `Unresolved` beklenir).
4. Düzenleme ekranında **Accept URL** alanının `{origin}/tracon/api/
   triggers/{tenantId}/{name}` biçiminde göründüğünü doğrula.

**Beklenen sonuç**
- Yeni tetikleyici listede görünür; `Resolved` rozeti gerçek durumu
  yansıtır; **Accept URL** alanı gerçek kabul adresini gösterir ve
  hiçbir yerde `secret` değeri görünmez.

---

### MT-JOB-103 — Worker öldürülünce yalnız kalan öge'ler yeniden işlenir

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 120 |
| **İlgili karar** | — |

**Ön koşul**
- Kısa `LeaseDuration` (ör. 10 saniye) ile yapılandırılmış bir worker süreci.

**Adımlar**
1. En az 3 öge'li bir `AgentBatch` job'ı kuyruğa al (`PUT /api/schedules/{name}`
   + `POST .../trigger`, veya doğrudan job endpoint'i).
2. Job `Leased`/`Running` durumuna geçtiğinde, ilk öge işlendikten hemen sonra
   worker sürecini öldür (`kill -9`).
3. `LeaseDuration` süresi dolana kadar bekle.
4. Aynı veya farklı bir worker sürecini başlat.
5. `GET /api/jobs/{id}` ile job'ı izle; tamamlanmasını bekle.

**Beklenen sonuç**
- İkinci koşumda **yalnız Pending kalan öge'ler** işlenir; ilk öge'nin yan
  etkisi (ör. gönderilen e-posta, çalıştırılan agent) **tekrarlanmaz**.
- Job sonunda `doneItems` toplam öge sayısına eşittir; `Attempt` alanı 1'den
  büyüktür (yeniden kiralandığını gösterir).

---

### MT-JOB-104 — Dokümanı izleyen dış bir `IJobHandler` sözleşme testini geçer

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 120 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
dotnet test samples/Tracon.Samples.CustomJobHandler.Tests/Tracon.Samples.CustomJobHandler.Tests.csproj -c Release
```

**Beklenen sonuç**
- `NightlyReportJobHandlerContractTests`'in üç case'i de geçer: zaten
  `Completed` bir öge yeniden işlenmez, retry'de yalnız `Pending` öge'ler
  işlenir, iptal öge'ler arasında gözlenir.
- Proje `PackageReference` ile `Tracon`/`Tracon.Testing.Contracts.Xunit`
  paketlerini kullanır — `ProjectReference` değil; gerçek bir dış tüketicinin
  göreceği yüzeyi kanıtlar.

# 9 — Lane'ler (Faz 129)

> Bir job bir `lane` taşır (varsayılan `"default"`). Worker yalnız seçtiği
> `lane`'lere abone olur; bir `lane` kendi eşzamanlılık bütçesini alabilir.
> Aşağıdaki case'ler `docs/129-IS-KUYRUGU-LANELERI.md`'nin plan taslağındaki
> `POST /api/agents/{name}/batch` referansını **düzeltir** — böyle bir uç
> yoktur; kuyruklu tek çalıştırma gerçek uçtan (`POST
> /api/agents/{name}/run`, `Prefer: respond-async`) geçer.

### MT-JOB-110 — Ayar yapılmayan kurulumda her job `default` `lane`'inde çalışır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 129 |
| **İlgili karar** | K1 |

**Ön koşul**
- `Tracon:Scheduling:Lanes`/`MaxConcurrentJobsPerLane`/`LaneByKind` HİÇ
  ayarlanmamış (varsayılan kurulum).

**Adımlar**
1. `ozet-toplu`'yu tetikle (§2'deki gibi).

**Girilecek veri**
```bash
curl -s "$APU/api/jobs/<job-id>" -H "$APB" | python3 -c "import json,sys; print(json.load(sys.stdin)['job']['lane'])"
```

**Beklenen sonuç**
- `"default"`. Davranış Faz 128 ile birebir aynıdır — hiçbir `lane` filtresi
  uygulanmaz, işçi her `lane`'den kiralar.

---

### MT-JOB-111 — `Lanes: ["media"]` olan worker `default` `lane`'inden kiralayamaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 129 |
| **İlgili karar** | — |

**Adımlar**
1. `dotnet user-secrets set "Tracon:Scheduling:Lanes:0" "media"`,
   yeniden başlat.
2. `ozet-toplu`'yu tetikle (`lane` alanı yok → `default`).
3. 30 saniye bekle.
4. `dotnet user-secrets remove "Tracon:Scheduling:Lanes:0"`, yeniden
   başlat — iş bu kez normal tamamlanır.

**Girilecek veri**
```bash
curl -s "$APU/api/jobs/<job-id>" -H "$APB" | python3 -c "import json,sys; print(json.load(sys.stdin)['job']['status'])"
curl -s "$APU/api/jobs?lane=default" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- Adım 3 sonrası: `status: "Pending"` — worker yalnız `media`'ya abone,
  `default` işini hiç kiralamaz.
- `GET /api/jobs?lane=default` işi listede gösterir — iş kayıp değildir,
  yalnız kimse dinlemiyordur.

---

### MT-JOB-112 — `MaxConcurrentJobsPerLane` dolu bir `lane`, `default`'u aç bırakır

Sınır durumu — head-of-line blocking'in çözüldüğünün kanıtı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 129 |
| **İlgili karar** | — |

**Ön koşul**
- İki zamanlama: biri `lane: "media"` taşıyan ve UZUN süren bir agent'a
  hedefli (ör. çok adımlı bir workflow), biri `lane` alanı boş (`default`)
  ve KISA süren `ozetleyici`'ye hedefli.

**Adımlar**
1. `dotnet user-secrets set "Tracon:Scheduling:MaxConcurrentJobsPerLane:media" "1"`,
   yeniden başlat.
2. `media` zamanlamasını İKİ kez tetikle (iki iş, `media` `lane`'inde).
3. Hemen ardından `default` zamanlamasını tetikle.
4. Ayarı kaldır, yeniden başlat.

**Girilecek veri**
```bash
curl -s "$APU/api/jobs/<default-job-id>" -H "$APB" | python3 -c "import json,sys; print(json.load(sys.stdin)['job']['status'])"
```

**Beklenen sonuç**
- `default` işi, `media`'nın iki (uzun) işi hâlâ sürerken `Completed` olur —
  `media`'nın dolu olması `default`'u BEKLETMEZ.

---

### MT-JOB-113 — Kuyruklu tek çalıştırmada geçersiz `lane` → `400`

Negatif senaryo. `Prefer: respond-async` yolu — plandaki (var olmayan)
`POST .../batch` DEĞİL.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 129 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/ozetleyici/run" -H "$APB" \
     -H 'Prefer: respond-async' -H "content-type: application/json" \
     -d '{"message":"test","lane":"Media"}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` küçük harf kuralını söyler (`'Media' is not a valid
  lane name...`). İş kuyruğa hiç yazılmaz.

---

### MT-JOB-114 — Kuyruklu tek çalıştırma istenen `lane`'i taşır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 129 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/ozetleyici/run" -H "$APB" \
     -H 'Prefer: respond-async' -H "content-type: application/json" \
     -d '{"message":"test","lane":"media"}' | python3 -c "import json,sys; print(json.load(sys.stdin)['runId'])"
curl -s "$APU/api/jobs/<run-id>" -H "$APB" | python3 -c "import json,sys; print(json.load(sys.stdin)['job']['lane'])"
```

**Beklenen sonuç**
- `"media"`. `lane` alanı boş bırakılırsa `"default"` (veya
  `LaneByKind[AgentRun]` ayarlıysa onun eşlediği değer).

---

### MT-JOB-115 — `retry` `lane`'i korur; geçersiz `lane`'li zamanlama `400` alır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 129 |
| **İlgili karar** | — |

**Adımlar**
1. `lane: "Media"` (büyük harf) ile bir zamanlama kaydetmeyi dene.
2. `lane: "media"` ile kaydet, var olmayan bir agent hedefle (MT-JOB-026
   deseni), tetikle.
3. `attempt` ilerlerken (§2'nin retry merdiveni) her denemede `lane`'i oku.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/schedules/buyuk-harf-lane" -H "$APB" \
     -H "content-type: application/json" -d '{
  "kind":"AgentBatch","targetName":"ozetleyici","timeZone":"UTC","lane":"Media","payload":[]
}'
```

**Beklenen sonuç**
- Adım 1: `HTTP: 400`.
- Adım 3: `job.lane` her denemede `"media"` — `ReleaseForRetryAsync`
  `lane`'i değiştirmez.

---

### MT-JOB-116 — Jobs ekranı: `lane` sütunu ve süzgeci

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 129 |
| **İlgili karar** | — |

**Adımlar**
1. Jobs ekranını aç. Schedules ve Recent jobs tablolarındaki `Lane`
   sütununa bak.
2. "Recent jobs" panelindeki `Filter by lane…` kutusuna `media` yaz.

**Beklenen sonuç**
- Her iki tabloda da `Lane` sütunu değeri gösterir (varsayılan işlerde
  `default`).
- Süzgeç yazıldıktan sonra yalnız o `lane`'deki işler kalır; kutu
  boşaltılınca liste eski hâline döner.

---

### MT-JOB-117 — Job sayacı ve süre histogramı ayar olmadan yazılır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 133 |
| **İlgili karar** | — |

**Ön koşul:** `Observability` altında hiçbir ayar yok (varsayılan kurulum).
OTel konsol exporter'ı `TraconDiagnostics.MeterName` metre'sini dinliyor.

**Adımlar**
1. İki `AgentBatch` işi kuyruğa at ve bitmelerini bekle.
2. OTel çıktısını süz:
   ```bash
   grep -E "tracon\.job\.(executions|duration|queue\.depth)" <otel-log>
   ```

**Beklenen sonuç**
- `tracon.job.executions` toplam **2** sayar; her ölçüm
  `tracon.job.lane`, `tracon.job.kind`, `tracon.job.status`
  ve `tracon.tenant.id` etiketlerini taşır.
- `tracon.job.duration` saniye biriminde iki ölçüm taşır; `tenant`
  etiketi **yoktur**.
- `tracon.job.queue.depth` çıktıda **hiç yoktur** — gauge varsayılan
  kapalıdır ve veritabanına gitmez.

---

### MT-JOB-118 — Kuyruk derinliği gauge'ı açıldığında `lane` × `status` raporlar

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 133 |
| **İlgili karar** | — |

**Ön koşul:** `Tracon__Observability__EnableJobQueueDepthGauge=true`.
İşçi kapalı (`Scheduling:RunWorker=false`) — işler kuyrukta beklesin.

**Adımlar**
1. `default` lane'ine üç iş at, çalıştırma.
2. Metrikleri bir kez scrape et.
3. **Aynı 30 sn içinde** ikinci kez scrape et ve veritabanı sorgu logunu izle.
4. `JobQueueDepthRefreshInterval` (30 sn) geçtikten sonra tekrar scrape et.

**Beklenen sonuç**
- Adım 2: `tracon.job.queue.depth` `lane=default`, `status=Pending`
  etiketleriyle **3** gösterir. `tracon.tenant.id` etiketi **yoktur**.
- Adım 3: `jobs` tablosuna **ikinci sorgu gitmez** (önbellek).
- Adım 4: tam bir sorgu daha gider.

---

### MT-JOB-119 — `retry` bırakması sayaca girmez, yalnız nihai durum sayılır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 133 |
| **İlgili karar** | — |

**Ön koşul:** `Scheduling:MaxAttempts=3`, sürekli hata veren bir handler
(var olmayan bir agent hedefleyen `AgentBatch` işi, MT-JOB-026 deseni).

**Adımlar**
1. İşi at ve üç denemenin de bitmesini bekle.
2. `tracon.job.executions` ölçümlerini say.
3. `tracon.job.duration` ölçümlerini say.

**Beklenen sonuç**
- Sayaç **1** artar, 3 değil; etiketi `status=Failed`.
- Histogram **tek** ölçüm taşır ve o ölçüm **son denemenin** süresidir,
  işin toplam ömrü değil.
- `/api/jobs` üzerinde işin `attempt` alanı **3**'tür — denemeler gerçekten
  yapıldı, yalnız sayılmadı.

---

### MT-JOB-120 — `lane` kardinalite muhafızı `other`'a düşürür

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 133 |
| **İlgili karar** | — |

**Ön koşul:** `Tracon__Observability__MaxJobLaneCardinality=2`.
🚨 **Taze süreç gerekir.** Kardinalite kümesi süreç ömrü boyunca yalnız büyür;
daha önce bir `default` işi koşulduysa bütçenin biri zaten harcanmıştır ve
beklenen sıra tutmaz. Uygulamayı bu case için yeniden başlat.

**Adımlar**
1. `alpha`, `beta` ve `gamma` lane'lerine birer iş at, bitmelerini bekle.
2. `tracon.job.executions` ölçümlerinin `tracon.job.lane`
   etiketlerini oku.
3. `alpha` lane'ine bir iş daha at.

**Beklenen sonuç**
- Adım 2: etiketler sırayla `alpha`, `beta`, **`other`**.
- Adım 3: `alpha` **adını korur** — küme yalnız büyür, küçülmez. Süreç
  yeniden başlatılana kadar bu eşleme sabittir.
- `EnableJobQueueDepthGauge` de açıksa `tracon.job.queue.depth`
  **aynı** eşlemeyi kullanır: `gamma` orada da `other`'dır. İki enstrüman tek
  bir kümeyi paylaşır.

---

### MT-JOB-121 — Derinlik sorgusu `jobs_claim_idx` kullanır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 133 |
| **İlgili karar** | — |

**Ön koşul:** PostgreSQL. `jobs` tablosunda çoğu **terminal** olan çok
sayıda satır (ör. 60 000 satır, 3 000'i açık).

**Adımlar**
```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT lane, status, CAST(COUNT(*) AS bigint)
FROM tracon.jobs
WHERE status IN (0, 1, 2)
GROUP BY lane, status;
```

**Beklenen sonuç**
- Plan `Bitmap Index Scan on jobs_claim_idx` içerir; `Seq Scan on jobs`
  **içermez**.
- Taranan satır sayısı **açık iş** sayısı kadardır (3 000), tablonun
  tamamı kadar değil — maliyet kuyruk geçmişiyle büyümez.

---

### MT-JOB-122 — İki custom handler kendi işini çalıştırır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 137 |
| **İlgili karar** | K-663 |

**Ön koşul:** Örnek uygulamada iki custom handler kayıtlı:
`AddJobHandler<AHandler>("acme.a")` ve `AddJobHandler<BHandler>("acme.b")`.
Her handler işlediği anahtarı log'a yazar.

**Adımlar**
1. `IJobDispatcher` ile `acme.a` anahtarlı bir iş kuyruğa al.
2. `IJobDispatcher` ile `acme.b` anahtarlı bir iş kuyruğa al.
3. İkisi de terminal duruma gelene kadar bekle.

**Beklenen sonuç**
- `AHandler` yalnız `acme.a` işini, `BHandler` yalnız `acme.b` işini çalıştırır.
- İki iş de `Completed` kapanır; hiçbir handler diğerinin işini görmez.
- `GET /api/jobs?handlerKey=acme.a` yalnız birinci işi döner.

---

### MT-JOB-123 — Kayıt sırası sonucu değiştirmez

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 137 |
| **İlgili karar** | K-663 |

**Ön koşul:** MT-JOB-122'nin kaydı, ama `AddJobHandler<...>` çağrıları
`AddTracon()`'den **önce** yapılır.

**Adımlar** MT-JOB-122'nin adımlarının aynısı.

**Beklenen sonuç**
- Sonuç MT-JOB-122 ile **birebir aynıdır**. Faz 137 öncesinde bu iki sıra
  zıt sonuç veriyordu; anahtar eşleşmesi tam olduğu için sıra artık
  kazananı belirlemez.

---

### MT-JOB-124 — Aynı anahtarın iki kez kaydı host'u açtırmaz

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 137 |
| **İlgili karar** | K-663 |

**Ön koşul:** İki **farklı** tip aynı anahtarla kaydedilir:
`AddJobHandler<AHandler>("acme.a")` ve `AddJobHandler<BHandler>("acme.a")`.

**Adımlar** Uygulamayı başlat.

**Beklenen sonuç**
- Host **açılmaz**; `InvalidOperationException` atılır.
- Mesaj çakışan **anahtarı** ve **iki tip adını** birden içerir.
- Hata worker'ın ilk tick'inde değil, host başlangıcında çıkar.

---

### MT-JOB-125 — `tracon.` öneki tüketiciye kapalıdır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 137 |
| **İlgili karar** | K-663 |

**Ön koşul:** `AddJobHandler<AHandler>("tracon.retention")`.

**Adımlar** Uygulamayı başlat.

**Beklenen sonuç**
- Uygulama **başlamaz**; `ArgumentException` atılır.
- Mesaj `tracon.` önekinin rezerve olduğunu söyler ve kendi önek
  seçmeyi önerir.
- Yerleşik `tracon.retention` handler'ı **gölgelenmemiştir**.

---

### MT-JOB-126 — Kayıtsız anahtarlı iş fail-closed'dır ve anahtarı sızdırmaz

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 137 |
| **İlgili karar** | K-664 |

**Ön koşul:** Worker açık. `IJobStore.EnqueueAsync` ile **doğrudan**
`HandlerKey = "acme.no-such-handler"` olan bir iş yazılır.

**Adımlar**
1. Worker tick'ini bekle.
2. `GET /api/jobs/{id}` ile işi oku.
3. Sunucu log'una bak.

**Beklenen sonuç**
- İş `Failed`'dır.
- `errorMessage` `tracon.job.unknown-handler-key` kodunu **içerir**.
- `errorMessage` ham anahtarı (`acme.no-such-handler`) **içermez**.
- Ham anahtar yalnız log'dadır ve mesajdaki `ref:` ile aynı korelasyon
  kimliğini taşır.

---

### MT-JOB-127 — Her execution kendi DI scope'unu alır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 137 |
| **İlgili karar** | K-663 |

**Ön koşul:** `services.AddScoped<Marker>()` (kurucusunda yeni bir `Guid`
üreten sıradan bir tip) ve o `Marker`'ı kurucusunda alan bir custom handler.
Handler `Marker`'ın kimliğini log'a yazar.

**Adımlar**
1. Aynı anahtarla arka arkaya iki iş kuyruğa al.
2. Bir işi hata verdirerek **retry**'a düşür ve ikinci denemeyi bekle.

**Beklenen sonuç**
- İki iş **farklı** `Marker` kimliği raporlar.
- Retry de **farklı** bir kimlik raporlar — attempt başına yeni scope.
- `JobContext` üzerinde `IServiceProvider` **yoktur**; bağımlılık yalnız
  kurucudan gelir.

---

### MT-JOB-128 — `handler_key` migration'ı dokuz değerin dokuzunu eşler

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 137 |
| **İlgili karar** | K-662 · K-666 |

**Ön koşul:** Faz 137 **öncesi** şemaya sahip bir veritabanı (üç sağlayıcının
her biri için ayrı ayrı). `jobs` ve `job_schedules` tablolarında `kind`
sütununun dokuz değerinin dokuzu için de en az bir satır.

**Adımlar**
1. Satır sayılarını not al: `SELECT COUNT(*) FROM jobs;` ve
   `SELECT COUNT(*) FROM job_schedules;`
2. Yeni sürümü başlat (veya `tracon migrate` koş).
3. `SELECT handler_key, COUNT(*) FROM jobs GROUP BY handler_key;`

**Beklenen sonuç**
- Satır sayıları **değişmemiştir**.
- Eşleme: `0→tracon.agent-batch`, `1→tracon.workflow`,
  `2→tracon.eval`, `3→tracon.webhook-delivery`,
  `4→tracon.retention`, `5→tracon.agent-run`,
  `6→tracon.online-eval`, `7→tracon.approval-resume`,
  `8→tracon.run-continuation`.
- `kind` sütunu **artık yoktur**; `handler_key` `NOT NULL`'dur.
- Üç sağlayıcıda da sonuç aynıdır.

---

### MT-JOB-129 — Zamanlama ucu izin listesi dışındaki anahtarı reddeder

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 137 |
| **İlgili karar** | K-665 |

**Ön koşul:** `HttpSchedulableHandlerKeys` **boş** (varsayılan). Bir custom
handler `acme.a` anahtarıyla kayıtlı.

**Adımlar**
1. `GET /api/schedules/handler-keys` çağır.
2. `PUT /api/schedules/x` gövdesinde `"handlerKey": "acme.a"` gönder.
3. `HttpSchedulableHandlerKeys`'e `acme.a` ekleyip uygulamayı yeniden başlat
   ve 1–2'yi tekrarla.

**Beklenen sonuç**
- 1. adımda yanıt yalnız **dokuz yerleşik anahtarı** listeler.
- 2. adım `400` döner; mesaj `HttpSchedulableHandlerKeys`'i adlandırır;
  zamanlama **oluşmaz**.
- 3. adımda liste `acme.a`'yı içerir ve `PUT` `200` döner.
- `GET /api/schedules/handler-keys` **Admin** ister; Reader `403` alır.

---

### MT-JOB-130 — Arayüz handler açılır listesi sunucudan gelir

| | |
|---|---|
| **İzlek** | B · 👤 insan gerekir |
| **Önem** | Orta |
| **İlgili faz** | Faz 137 |
| **İlgili karar** | K-665 |

**Ön koşul:** Örnek uygulama ayakta, Jobs ekranı açık, Admin rolü.

**Adımlar**
1. "New schedule" düğmesine bas.
2. "Handler key" açılır listesini aç.
3. `HttpSchedulableHandlerKeys`'e bir custom anahtar ekleyip uygulamayı
   yeniden başlat ve tekrar bak.

**Beklenen sonuç**
- Liste sabit kodlu iki seçenek değil, `GET /api/schedules/handler-keys`
  yanıtıdır — varsayılanda dokuz yerleşik anahtar.
- 3. adımdan sonra custom anahtar da listede görünür.
- Zamanlama ve iş tablolarının sütun başlığı **"Handler key"**'dir ve hücre
  tam anahtarı gösterir (`tracon.agent-batch`).

---

### MT-JOB-131 — `GET /api/jobs` her satırda `handlerKey` taşır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 137 |
| **İlgili karar** | K-662 |

**Ön koşul:** En az bir tamamlanmış iş.

**Adımlar**
1. `GET /api/jobs` çağır.
2. `GET /api/jobs?handlerKey=tracon.agent-batch` çağır.
3. `GET /api/jobs?handlerKey=tracon.retention` çağır.

**Beklenen sonuç**
- Her satır `handlerKey` alanı taşır; `kind` alanı **yoktur**.
- 2. adım yalnız o anahtarın işlerini döner.
- 3. adım (o türde iş yoksa) boş dizi döner — süzgeç gerçekten uygulanır.

---

### MT-JOB-098 — Yarıda kalan approval handoff'u, AYNI kararı tekrarlayarak tamamlanır (B03)

Regresyon. Karar uygulanıp resume run/job yazılamazsa, eski davranışta tekrar
isteği `409 AlreadyDecided` alıyor ve onaylanmış tool çağrısı HİÇ çalışmıyordu;
`RunReconciliationService` bunu kurtarmaz (yalnız `Running` claim eder, default
kapalı). Resume run kimliği artık approval'dan türetilir, bu yüzden tekrar aynı
satıra iner.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | K-726 |

**Ön koşul**
- Onay isteyen bir tool ve `Prefer: respond-async` ile başlatılmış,
  `AwaitingApproval` durumunda bir run.
- Kuyruk yazımını bir kez düşürebilecek bir ortam (ör. `IJobStore`'u saran
  hata enjekte eden bir dekoratör; otomatik karşılığı
  `ApprovalResumeHandoffTests`).

**Adımlar**
1. `POST {prefix}/api/approvals/{id}/decide` `{"approved": true}` gönder;
   enqueue adımı hata versin.
2. AYNI gövdeyle (`{"approved": true}`) isteği TEKRARLA.
3. Oturumdaki run'ları listele.

**Beklenen sonuç**
- İkinci istek `409` DEĞİL `200` döner ve approval kaydını verir.
- Oturumda özgün run'ın yanında **tam bir** devam run'ı vardır — iki tane
  değil; ikinci istek yeni kimlik üretmez.
- Devam run'ı `Completed` olur.
- Aynı approval'a `{"approved": false}` gönderilirse `409` döner: ters karar
  hâlâ çatışmadır.
