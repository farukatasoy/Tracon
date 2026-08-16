# Faz 17 — Toplu ve Zamanlanmış Çalıştırma

> **Durum:** ✅ Tamamlandı (2026-08-03)
> **Kaynak:** [BEYIN-FIRTINASI.md](arsiv/BEYIN-FIRTINASI.md) · **F-22**
> **Önkoşul:** Yok · Faz 9 önerilir (iş oluşturma Admin yetkisidir)
> **Devreden faz:** [Faz 16](16-WORKFLOWS-ARAYUZ.md) — workflow arayüzü ve human-in-the-loop
> **Sonraki bağımlı:** [Faz 18](18-DEGERLENDIRME.md) — eval bu kuyruğu kullanır
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** 0008 (planlanan sırada)

---

## Bu Faza Başlarken

1. [`MIMARI.md`](MIMARI.md) — bölüm 5 (`runs`), bölüm 6 (çalıştırma yolu)
2. [`KARARLAR.md`](KARARLAR.md) — **K-018** (bellek içi depolar birinci sınıf), **K-014** (`run_events` append-only), **K-025** (`Replace` deseni), **K-130** (yanıt yeni satır açar)
3. [`12-AGENT-CAGRI-GRAFIGI.md`](12-AGENT-CAGRI-GRAFIGI.md) — `runs` ağacı, bütçe
4. [`16-WORKFLOWS-ARAYUZ.md`](16-WORKFLOWS-ARAYUZ.md) — **özellikle `RunStatus.AwaitingInput`** bölümü
5. Bu doküman

---

## Faz 16'dan Devraldıkları

### 🚨 `RunStatus` artık beş değer taşır

```csharp
public enum RunStatus { Running = 0, Completed = 1, Failed = 2, Canceled = 3, AwaitingInput = 4 }
```

`AwaitingInput` **ne çalışıyor ne sonuçlanmış** bir workflow çalıştırmasıdır: graf
bir dış istek portuna ulaşmış, durumu kontrol noktasına yazılmış ve akış
kapanmıştır. Kuyruk tasarımı bunu hesaba katmalıdır:

| Kural | Neden |
|-------|-------|
| Zamanlanmış bir tetikleyici `AwaitingInput` bir çalıştırmayı **yeniden başlatmamalıdır** | O iş bitmedi; yeniden başlatmak insanın verdiği cevabı çöpe atar |
| "Süren iş sayısı" hesabı `Running` **ve** `AwaitingInput` satırlarını ayrı saymalıdır | İkisi farklı kaynaklar tüketir: biri CPU, diğeri yalnızca bir satır |
| Yeniden deneme mantığı `AwaitingInput`'u başarısızlık **saymamalıdır** | Hata değil, bekleyiştir |

`RunStatistics.AwaitingInputRuns` alanı zaten vardır; alt toplamlar `TotalRuns`
ile tutar.

### Yeni olay tipleri (append-only, 11–19)

`WorkflowStarted` (11) … `WorkflowRequest` (18), `RunAwaitingInput` (19).
Faz 17 yeni bir olay tipi eklerse **20'den** devam etmelidir.

### Bekleyen bir çalıştırmanın kontrol noktaları silinmez

`KeepCheckpointsAfterCompletion` kapalı olsa bile `AwaitingInput` bir
çalıştırmanın kontrol noktaları korunur — yanıt tam olarak onlardan devam eder.
Faz 25'in saklama politikası bu kuralı bozmamalıdır.

### 🚨 Bekleyen çalıştırmalar süresiz bekler

`AgentPrismWorkflowOptions.RunTimeout` yalnızca **akış açıkken** çalışır. Bir
çalıştırma `AwaitingInput` olduktan sonra hiçbir zaman aşımı onu kapatmaz. Faz
17 bir "bekleyen işler" görünümü veya bir süre sınırı getirmek isteyebilir;
getirmezse Faz 25'in temizliği bunu ele almalıdır.

### Kalıcı executor kimliği artık garanti

Faz 15'in "uygulama yeniden başlatılırsa kontrol noktaları kullanılamaz" sınırı
**kalktı** (K-127). Bir iş kuyruğu bir workflow'u başlatıp süreç yeniden
başladıktan sonra sürdürebilir; bu ölçülerek doğrulandı.

---

## Amaç

Bir agent'ı bir veri kümesi üzerinde toplu çalıştırmak ve zamanlanmış (cron)
tetiklemek. Beyin fırtınası belgesi bunun eval (F-14) ile **aynı altyapıyı
paylaştığını** söylüyor; bu yüzden kuyruk **önce** yapılır ve eval onun üzerine
kurulur.

Bu, AgentPrism'i "istek geldiğinde çalışan" bir kütüphaneden "kendi kendine iş
yapan" bir kontrol düzlemine dönüştürür. Bu dönüşümün bir bedeli vardır ve
tasarımın merkezinde o bedel durur: **arka plan işçisi, kütüphanenin
tüketicisinin sürecinde çalışır.**

---

## Plandan Sapmalar

Aşağıdakiler bu belgenin ilk taslağıyla gerçekleşen arasındaki farklardır.
Gerekçeleri `docs/KARARLAR.md` içinde K-134 … K-138 olarak numaralıdır.

| Sapma | Gerekçe |
|-------|---------|
| `WorkflowJobHandler` ayrı bir pakete değil, `AgentPrism.Core`'a kondu | `IWorkflowRunner` zaten `AgentPrism.Abstractions`'ta; Core zaten ona bağımlı. Somut uygulama yalnız `UseWorkflows()` çağrılırsa DI'a girer — `runner` nullable'dır (K-134) |
| `jobs` tablosuna `(schedule_id, scheduled_for)` üzerinde benzersiz kısıt eklendi | Bölüm 17.5'in metni bu kısıtın var olduğunu söylüyordu ama 17.2'deki DDL örneğinde eksikti; migration 0008 metne göre tamamlandı (K-138) |
| İptal için ayrı bir `cancel_requested` sütunu **açılmadı** | `jobs.status` tek gerçek kaynak: CAS ile `Cancelled` yapılır, işçi ogeler arasında bunu tekrar okur (K-137) |
| Zamanlanmış bir işin kiracısı `AsyncLocal` tabanlı `AmbientTenantScope` ile taşınır | `ITenantContext` uygulamaları singleton'dır ve ne HTTP bağlamı ne sabit varsayılan "bu iş hangi kiracı için" sorusunu cevaplayabilir (K-136) |
| `IJobStore`'a doc taslağında olmayan `MarkRunningAsync` eklendi | `Leased` → `Running` geçişi açık bir CAS adımı gerektirir; `RenewLeaseAsync` durumu değiştirmez |
| `IJobStore.CompleteAsync` tek bir `JobCompletion` kaydı alır (4 ayrı parametre değil) | `IRunStore.CompleteRunAsync`/`RunCompletion` ile aynı sözleşme biçimi — belirlenimcilik ve tutarlılık için |

---

## 17.1 — Merkezî Kısıt: Bu Bir Kütüphanedir

AgentPrism bir uygulama değil, NuGet paketidir. Arka plan işçisi eklemek şunları
getirir:

| Sorun | Çözüm |
|-------|-------|
| Tüketici üç örnek (instance) çalıştırıyorsa iş üç kez koşar mı? | **Hayır.** İşler `FOR UPDATE SKIP LOCKED` ile kiralanır; her iş bir kez çalışır |
| Bellek içi kurulumda ne olur? | Çalışır ama **tek süreçte** ve süreç ölünce iş kaybolur. `/api/meta` bunu bildirir (K-018 deseni) |
| Tüketici arka plan işçisi istemiyorsa? | `UseScheduling(o => o.RunWorker = false)` — kuyruk yazılır, işleyen başka bir dağıtım olur |
| İşçi ne kadar kaynak tüketir? | `MaxConcurrentJobs` (varsayılan 2) ve yoklama aralığı (varsayılan 10 sn) |

> Zamanlayıcı **`IHostedService`** olarak kaydedilir. `MigrationHostedService`
> zaten bu deseni kullanıyor; aynı yaşam döngüsüne bağlanır.

---

## 17.2 — Veri Modeli (Migration 0008)

```sql
CREATE TABLE {schema}.job_schedules (
    id            uuid        NOT NULL PRIMARY KEY,
    tenant_id     text        NOT NULL,
    name          text        NOT NULL,
    kind          smallint    NOT NULL,        -- 0=AgentBatch, 1=Workflow, 2=Eval
    target_name   text        NOT NULL,        -- agent veya workflow adi
    cron          text,                        -- NULL = yalniz elle tetiklenir
    time_zone     text        NOT NULL DEFAULT 'UTC',
    payload       jsonb       NOT NULL,        -- girdi kumesi veya parametreler
    enabled       boolean     NOT NULL DEFAULT true,
    next_run_at   timestamptz,
    last_run_at   timestamptz,
    created_by    text,
    created_at    timestamptz NOT NULL,
    updated_at    timestamptz NOT NULL,
    CONSTRAINT job_schedules_tenant_name_uq UNIQUE (tenant_id, name)
);

CREATE TABLE {schema}.jobs (
    id            uuid        NOT NULL PRIMARY KEY,
    tenant_id     text        NOT NULL,
    schedule_id   uuid        REFERENCES {schema}.job_schedules (id) ON DELETE SET NULL,
    kind          smallint    NOT NULL,
    target_name   text        NOT NULL,
    status        smallint    NOT NULL,        -- 0=Pending 1=Leased 2=Running 3=Completed 4=Failed 5=Cancelled
    payload       jsonb       NOT NULL,
    total_items   integer     NOT NULL DEFAULT 0,
    done_items    integer     NOT NULL DEFAULT 0,
    failed_items  integer     NOT NULL DEFAULT 0,
    attempt       smallint    NOT NULL DEFAULT 0,
    lease_owner   text,
    lease_until   timestamptz,
    scheduled_for timestamptz NOT NULL,
    started_at    timestamptz,
    completed_at  timestamptz,
    error_message text,
    created_at    timestamptz NOT NULL,
    -- Yaz saati gecisinde ayni zamanlamanin iki kez tetiklenmesine karsi ikinci
    -- savunma hatti (birincisi TryClaimNextRunAsync'in atomik CAS'i). NULL
    -- schedule_id PostgreSQL'de birbirinden ayirt edilir; elle olusturulan
    -- (zamanlamasiz) isler bu kisitla cakismaz.
    CONSTRAINT jobs_schedule_scheduled_uq UNIQUE (schedule_id, scheduled_for)
);

CREATE INDEX IF NOT EXISTS jobs_claim_idx
    ON {schema}.jobs (status, scheduled_for)
    WHERE status IN (0, 1);

CREATE INDEX IF NOT EXISTS jobs_tenant_created_idx
    ON {schema}.jobs (tenant_id, created_at DESC);

CREATE TABLE {schema}.job_items (
    id          uuid        NOT NULL PRIMARY KEY,
    job_id      uuid        NOT NULL REFERENCES {schema}.jobs (id) ON DELETE CASCADE,
    seq         integer     NOT NULL,
    input       text        NOT NULL,
    run_id      uuid,                          -- olusan calistirma
    status      smallint    NOT NULL,
    error       text,
    CONSTRAINT job_items_job_seq_uq UNIQUE (job_id, seq)
);
```

`jobs_claim_idx` **kısmi indekstir**: tamamlanmış işler indekste yer tutmaz.
Kuyruk büyüdükçe alma sorgusu yavaşlamamalıdır.

---

## 17.3 — İş Alma: `FOR UPDATE SKIP LOCKED`

```sql
UPDATE {schema}.jobs
   SET status = 1, lease_owner = @owner, lease_until = @until, attempt = attempt + 1
 WHERE id = (
       SELECT id FROM {schema}.jobs
        WHERE (status = 0 AND scheduled_for <= now())
           OR (status = 1 AND lease_until < now())      -- suresi dolmus kira
        ORDER BY scheduled_for
        FOR UPDATE SKIP LOCKED
        LIMIT 1)
RETURNING *;
```

Bu desen PostgreSQL'de kuyruk almanın standart yoludur ve ek bir altyapı
(Redis, RabbitMQ) gerektirmez — K-004'ün "kütüphane kendi şemasını kendi
yönetir" duruşuyla uyumludur.

- **Kira süresi** varsayılan 5 dakika, işçi çalışırken yenilenir
- İşçi çökerse kira dolar ve iş **yeniden alınır**
- `attempt` sayacı; `MaxAttempts` (varsayılan 3) aşılırsa `Failed`
- Bellek içi uygulamada aynı sözleşme `SemaphoreSlim` ve zaman damgası ile
  karşılanır

---

## 17.4 — Gerçekleşen Public API

`AgentPrism.Abstractions/Scheduling/` altında. Doc taslağından farkları
"Plandan Sapmalar" bölümünde listelenmiştir.

```csharp
public enum JobKind { AgentBatch = 0, Workflow = 1, Eval = 2 }
public enum JobStatus { Pending = 0, Leased = 1, Running = 2, Completed = 3, Failed = 4, Cancelled = 5 }
public enum JobItemStatus { Pending = 0, Completed = 1, Failed = 2 }

public sealed record JobSchedule
{
    public Guid Id { get; init; }
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public required JobKind Kind { get; init; }
    public required string TargetName { get; init; }
    public string? Cron { get; init; }
    public string TimeZone { get; init; } = "UTC";
    public JsonElement Payload { get; init; }
    public bool Enabled { get; init; } = true;
    public DateTimeOffset? NextRunAt { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    public string? CreatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record JobRecord      // jobs satiri; TotalItems/DoneItems/FailedItems/Attempt/LeaseOwner/LeaseUntil/ScheduledFor/StartedAt/CompletedAt/ErrorMessage tasir
public sealed record JobItemRecord  // job_items satiri; Seq/Input/RunId/Status/Error tasir
public sealed record JobItemResult  // ReportItemAsync girdisi: JobId/Seq/Status/RunId/Error
public sealed record JobCompletion  // CompleteAsync girdisi: JobId/Status/CompletedAt/ErrorMessage
public sealed record JobQuery       // QueryAsync filtresi: TenantId/Kind/Status/ScheduleId/Skip/Take

public interface IJobStore
{
    ValueTask<JobRecord> EnqueueAsync(JobRecord job, IReadOnlyList<string> items, CancellationToken ct = default);
    ValueTask<JobRecord?> LeaseAsync(string owner, TimeSpan leaseDuration, CancellationToken ct = default);
    ValueTask RenewLeaseAsync(Guid jobId, string owner, TimeSpan leaseDuration, CancellationToken ct = default);
    ValueTask<bool> MarkRunningAsync(Guid jobId, string owner, CancellationToken ct = default);
    ValueTask CompleteAsync(JobCompletion completion, CancellationToken ct = default);
    ValueTask ReleaseForRetryAsync(Guid jobId, string errorMessage, CancellationToken ct = default);
    ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken ct = default);
    ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<JobRecord>> QueryAsync(JobQuery query, CancellationToken ct = default);
    ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken ct = default);
    ValueTask ReportItemAsync(JobItemResult item, CancellationToken ct = default);
}

public interface IJobScheduleStore
{
    ValueTask<JobSchedule?> GetAsync(string tenantId, string name, CancellationToken ct = default);
    ValueTask<IReadOnlyList<JobSchedule>> ListAsync(string tenantId, CancellationToken ct = default);
    ValueTask<JobSchedule> SaveAsync(JobSchedule schedule, CancellationToken ct = default);
    ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken ct = default);
    ValueTask<IReadOnlyList<JobSchedule>> ListDueAsync(DateTimeOffset asOfUtc, CancellationToken ct = default);
    ValueTask<bool> TryClaimNextRunAsync(Guid scheduleId, DateTimeOffset expected, DateTimeOffset newValue, DateTimeOffset ranAt, CancellationToken ct = default);
}

public interface IJobHandler                      // genisleme noktasi (K4)
{
    JobKind Kind { get; }
    ValueTask ExecuteAsync(JobContext context, CancellationToken ct = default);
}

public sealed class JobContext
{
    public required JobRecord Job { get; init; }
    public required IReadOnlyList<JobItemRecord> Items { get; init; }
    public required Func<JobItemResult, CancellationToken, ValueTask> ReportItemAsync { get; init; }
    public required Func<CancellationToken, ValueTask<bool>> IsCancelledAsync { get; init; }
}

public sealed class AgentPrismSchedulingOptions
{
    public const string SectionName = "AgentPrism:Scheduling";
    public bool Enabled { get; set; } = true;
    public bool RunWorker { get; set; } = true;
    public int MaxConcurrentJobs { get; set; } = 2;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxAttempts { get; set; } = 3;
    public int MaxItemsPerJob { get; set; } = 1000;
}

public static class AmbientTenantScope              // K-136
{
    public static string? Current { get; }
    public static IDisposable Begin(string tenantId);
}
```

`IJobHandler` bir genişleme noktasıdır: `AddJobHandler<T>()` ile eklenir
(`AgentPrism.Core`). Faz 17 iki işleyici getirir: `AgentBatchJobHandler`
(sıralı, sırayla ogeleri çalıştırır) ve `WorkflowJobHandler` (`IWorkflowRunner?`
opsiyonel — motor kayıtlı değilse iş açık hata mesajıyla `Failed` olur). Faz 18
(eval) kendi işleyicisini `AddJobHandler<EvalJobHandler>()` ile aynı şekilde
ekler. `UseScheduling(Action<AgentPrismSchedulingOptions>?)` kod-ilk ayar
değiştiricisidir; depolar ve işçi zaten `AddAgentPrism()` ile kayıtlıdır.

---

## 17.5 — Cron

Cron ifadesi ayrıştırma için **paket alınmaz**. Desteklenen alt küme elle
yazılır ve dokümante edilir:

```
dakika saat ayin-gunu ay haftanin-gunu
*    */5   1-5   ,   sayi
```

Gerekçe: `Cronos` veya `NCrontab` küçük paketlerdir ama K-007 gereği her yeni
bağımlılık tüketiciye geçer. Beş alanlı standart cron'un ayrıştırıcısı ~150
satırdır ve testi kolaydır. Saniye alanı, `L`, `W`, `#` gibi uzantılar
**desteklenmez** ve bu README'de yazılır.

Saat dilimi `TimeZoneInfo.FindSystemTimeZoneById` ile çözülür. Geçersiz saat
dilimi kaydetmede `400` verir.

> **Yaz saati tuzağı:** yerel saatte 02:30'da çalışacak bir iş, saat ileri
> alındığı gün **hiç** çalışmaz veya iki kez çalışır. Kural: bir sonraki
> çalışma zamanı UTC olarak hesaplanır ve `jobs` satırı **oluşturulduktan
> sonra** bir sonraki zaman hesaplanır. Aynı `schedule_id` + `scheduled_for`
> için ikinci bir `jobs` satırı oluşmasını benzersiz bir kısıt engeller.

---

## 17.6 — HTTP Uçları ve Arayüz

| Uç | Rol | Ne yapar |
|----|-----|----------|
| `GET/PUT/DELETE {prefix}/api/schedules[/{name}]` | Admin | Zamanlama tanımları |
| `POST {prefix}/api/schedules/{name}/trigger` | Operator | Şimdi çalıştır |
| `GET {prefix}/api/jobs` | Reader | İş listesi, durum filtresi |
| `GET {prefix}/api/jobs/{id}` | Reader | İş + öğe sonuçları + `run_id` bağlantıları |
| `POST {prefix}/api/jobs/{id}/cancel` | Operator | İptal |

`GET /api/jobs/{id}` yanıtı `JobDetailResponse { Job, Items }` şeklindedir —
kayıt ve öğeler tek çağrıda döner. `/api/meta`'nın `storage` alanına
`jobStore` (aktif depo tip adı) ve `jobWorkerEnabled` (bu süreçte işçi
çalışıyor mu) eklendi — K-018 deseni.

Arayüz: yeni **Jobs** ekranı — zamanlamalar listesi (oluştur/düzenle/sil/tetikle),
çalışan işler, ilerleme çubuğu (`done/total`, başarılı/başarısız renk ayrımı) ve
her öğenin çalıştırma bağlantısı. Gerçekleşen bütçe etkisi: **+2,1 KB gzip**
(105,2 → 107,3 KB toplam; bkz. `dotnet build` çıktısı), hedefin (+6 KB) altında.

---

## Testler

| Proje | Sınıf | Ne doğrular |
|-------|-------|-------------|
| `AgentPrism.Core.UnitTests` | `CronExpressionTests` (11 test) | Geçerli/geçersiz sözdizimi, adım değeri, haftanın günü, ayın-günü/haftanın-günü OR kuralı, yaz saati ileri atlaması |
| | `InMemoryJobStoreTests` (13 test) | Kiralama, kira süresi dolumu, `MarkRunningAsync` sahiplik denetimi, `ReportItemAsync` idempotency, `CancelAsync` durum makinesi, kiracı yalıtımı |
| | `InMemoryJobScheduleStoreTests` (8 test) | Upsert kimlik koruması, `ListDueAsync` filtreleri, `TryClaimNextRunAsync` CAS |
| | `AgentBatchJobHandlerTests` (5 test), `WorkflowJobHandlerTests` (4 test) | İşleyici seçimi, sıralı öğe işleme, yeniden deneme atlaması, iptal, motor kayıtlı değilken açık hata |
| `AgentPrism.PostgreSql.IntegrationTests` | `JobStoreContract` + `JobScheduleStoreContract` (`InMemory*`/`Postgres*` ikisi de) | Davranış sözleşmesi iki uygulamada da aynı |
| | `JobStoreConcurrencyTests.Iki_isci_ayni_isi_iki_kez_kiralamaz` | **Gerçek Postgres'e karşı**: iki gerçek `IJobStore` örneği 50 iş için yarışır, `FOR UPDATE SKIP LOCKED` kanıtı |
| `AgentPrism.AspNetCore.FunctionalTests` | `SchedulingEndpointTests` (9 test) | CRUD, cron/saat dilimi doğrulama, `MaxItemsPerJob` sınırı, tetikleme, iptal + ikinci iptalde `409`, rol ayrımı |
| `AgentPrism.Ui.E2ETests` | `UiTests.Zamanlama_olusturulur_tetiklenir_ve_is_tamamlanir` | Gerçek tarayıcı: zamanlama oluşturma, elle tetikleme, gerçek işçinin işi tamamlaması, öğe detayına gidiş |

Toplam: 279 Core birim testi (54'ü Faz 17), 274 Postgres entegrasyon testi
(49'u Faz 17), 190 AspNetCore fonksiyonel testi (19'u Faz 17), 23 arayüz E2E
testi (1'i Faz 17).

---

## Dosya Listesi (Gerçekleşen)

| Katman | Dosyalar |
|--------|----------|
| `AgentPrism.Abstractions/Scheduling/` | `JobKind.cs`, `JobStatus.cs`, `JobItemStatus.cs`, `JobSchedule.cs`, `JobRecord.cs`, `JobItemRecord.cs`, `JobSupportTypes.cs` (`JobQuery`/`JobCompletion`/`JobItemResult`), `JobPayload.cs`, `IJobStore.cs`, `IJobScheduleStore.cs`, `IJobHandler.cs` (+ `JobContext`), `AgentPrismSchedulingOptions.cs` |
| `AgentPrism.Abstractions/Tenancy/` | `AmbientTenantScope.cs` (yeni) |
| `AgentPrism.Core/Scheduling/` | `InMemoryJobStore.cs`, `InMemoryJobScheduleStore.cs`, `CronExpression.cs`, `JobWorkerBackgroundService.cs`, `AgentBatchJobHandler.cs`, `WorkflowJobHandler.cs`, `AgentPrismSchedulingOptionsValidator.cs` |
| `AgentPrism.Core/` | `AgentPrismServiceCollectionExtensions.cs` (`AddJobHandler<T>()`, `UseScheduling()`, `BindScheduling`), `Tenancy/SingleTenantContext.cs` (`AmbientTenantScope.Current` kontrolü eklendi) |
| `AgentPrism.PostgreSql/` | `Migrations/0008_scheduling.sql`, `Internal/SqlQueries.cs` (yeni sorgular), `Stores/PostgresJobStore.cs`, `Stores/PostgresJobScheduleStore.cs`, `AgentPrismPostgreSqlBuilderExtensions.cs` (Replace kaydı) |
| `AgentPrism.AspNetCore/` | `Contracts/SchedulingContracts.cs`, `Endpoints/SchedulingEndpoints.cs`, `AgentPrismEndpointRouteBuilderExtensions.cs` (rota kaydı), `Contracts/AgentPrismMetaResponse.cs` + `Endpoints/MetaEndpoints.cs` (`jobStore`/`jobWorkerEnabled`), `Tenancy/HttpTenantContext.cs` (`AmbientTenantScope.Current` kontrolü eklendi) |
| `AgentPrism.UI/frontend/src/` | `screens/jobs.tsx`, `screens/job-detail.tsx`, `components/icons.tsx` (`JobsIcon`), `components/layout.tsx` (nav girdisi), `lib/api.ts`/`lib/types.ts` (schedule/job uçları), `app.tsx` (rota kaydı) |
| Testler | `tests/AgentPrism.Core.UnitTests/Scheduling/*` (5 dosya), `tests/AgentPrism.PostgreSql.IntegrationTests/Contracts/JobStoreContract.cs` + `JobScheduleStoreContract.cs` + `InMemoryStoreContractTests.cs`/`PostgresStoreContractTests.cs` girdileri, `tests/AgentPrism.PostgreSql.IntegrationTests/JobStoreConcurrencyTests.cs`, `tests/AgentPrism.PostgreSql.IntegrationTests/Infrastructure/{PostgresTestContext,TestData}.cs` (genişletildi), `tests/AgentPrism.AspNetCore.FunctionalTests/SchedulingEndpointTests.cs`, `tests/AgentPrism.Ui.E2ETests/UiTests.cs` (genişletildi) |

---

## Bu Fazda Verilecek Kararlar

1. **Kuyruk PostgreSQL üzerinde, `SKIP LOCKED` ile** — ek altyapı gerektirmez.
2. **Cron ayrıştırıcısı elle yazılır** — K-007; desteklenen alt küme yazılır.
3. **İşçi kapatılabilir** (`RunWorker = false`) — barındırma modeli tüketicinindir.
4. **`IJobHandler` genişleme noktasıdır** — K4; Faz 18 bunu kullanır.
5. **Bellek içi kuyruk desteklenir ama sınırları `/api/meta`'da bildirilir** — K-018.

---

## Çözülen Açık Sorular

1. **Toplu işin öğeleri paralel mi çalışsın?** **Sıralı** (iş başına), işler
   arası paralellik `MaxConcurrentJobs` ile. Uygulandığı gibi: `AgentBatchJobHandler`/
   `WorkflowJobHandler` `context.Items`'ı `foreach` ile sırayla işler.
2. **Başarısız öğede iş devam etsin mi?** **Evet.** `failed_items` sayılır;
   tüm öğeler başarısızsa iş `Failed` olur (bkz. `JobWorkerBackgroundService.ExecuteJobAsync`).
3. **`MaxItemsPerJob` 1000 uygun mu?** **1000**, değiştirilmedi. Hem zamanlama
   kaydında hem elle tetiklemede kontrol edilir (`SchedulingEndpoints`).
4. **Zamanlanmış iş hangi kiracıyla çalışır?** Zamanlama kaydındaki `tenant_id`.
   🚨 **Öneriden sapma:** doc taslağı "açık parametre" öneriyordu ama
   `IAgentCatalog.ResolveAsync`/`RunRecordingAgent`/`WorkflowRunner` kiracıyı
   zaten `ITenantContext` üzerinden **ambient** okuyor — parametre olarak
   geçirilecek bir çağrı zinciri yok. Çözüm `AmbientTenantScope` (AsyncLocal,
   K-136): `IHttpContextAccessor` ile aynı desen.

---

## Bitiş Ölçütleri (DoD)

- [x] Bir agent bir girdi kümesi üzerinde toplu çalıştırılıyor; her öge kendi
      `runs` satırını üretiyor. Gerçek çalıştırmada doğrulandı (örnek uygulama,
      `support` agent'ı, gerçek OpenAI modeli): 2 ögeli iş ~5 saniyede
      `Completed`, her ikisi de ayrı `run_id` ile `runs`'a bağlandı (gerçek
      `usage.totalTokens=527`). Mekanizma öge sayısından bağımsızdır; 20 öge
      aynı kod yolunu izler
- [x] Cron ile zamanlanmış iş tetikleniyor — **gerçek çıktı:** `48 8 * * *`
      cron'lu bir zamanlama saat `08:46:58`'de oluşturuldu, `nextRunAt =
      08:48:00` hesaplandı; hiçbir elle tetikleme yapılmadan saat `08:48:00`'de
      iş otomatik olarak `Running` durumuna geçti (`scheduledFor = 08:48:00`,
      `startedAt = 08:48:00.32`). Tamamlanınca `nextRunAt` bir sonraki güne
      (`2026-08-04T08:48:00Z`) ilerledi ve `lastRunAt` yazıldı
- [x] İki işçi aynı anda koşarken iş **tam bir kez** çalışıyor — gerçek
      PostgreSQL'e karşı kanıtlandı: `JobStoreConcurrencyTests` iki gerçek
      `IJobStore` örneğini 50 iş için yarıştırır, hiçbiri iki kez kiralanmaz
- [x] İşçi öldürülüp yeniden başlatıldığında yarım kalan iş devam ediyor —
      `JobStoreContract.Kira_suresi_dolunca_is_yeniden_kiralanabilir`
      (hem InMemory hem Postgres) kira süresi dolan bir işin `attempt`
      artırılarak yeniden kiralandığını doğrular
- [x] `RunWorker = false` ile işçi hiç başlamıyor — `JobWorkerBackgroundService.ExecuteAsync`
      `ModelProviderHealthBackgroundService` ile aynı desen: seçenek kapalıyken
      hiçbir zamanlayıcı kurulmadan hemen döner
- [x] Zamanlanmış işte kiracı doğru; sızıntı yok — `AmbientTenantScope`
      mekanizması `default` kiracısıyla gerçek çalıştırmada doğrulandı;
      `JobStoreContract`'in kiracı yalıtım testleri (`GetAsync_baska_kiracidan_null_doner`,
      `CancelAsync_baska_kiracinin_isini_etkilemez`) hem InMemory hem Postgres'te geçiyor
- [x] Dört doğrulama kapısı sıfır uyarı — `dotnet build`/`test`/`pack`/`format`
      hepsi temiz; `dotnet pack` 9 paket üretiyor (yeni paket yok)

---

## Riskler

| Risk | Önlem |
|------|-------|
| Kütüphaneye arka plan süreci girmesi | `RunWorker` kapatılabilir; kaynak sınırları varsayılan düşük |
| İş kuyruğu veritabanını yorar | Kısmi indeks; yoklama aralığı; Faz 25 temizliği |
| Cron alt kümesi kullanıcıyı şaşırtır | Desteklenmeyen sözdizimi kaydetmede `400` verir, sessizce yok sayılmaz |
| Yaz saati geçişinde çift çalışma | UTC hesap + `(schedule_id, scheduled_for)` benzersizliği |
| Toplu iş maliyeti patlar | 🚨 **Uygulanmadı — bilinen sınırlama.** `AgentRunBudget` her öge için **ayrı** ve sınırsız bir çalıştırma açar (`AgentPrismRunOptions.Budget` set edilmez); ilk taslak "iş başına paylaşılan bütçe" öngörüyordu ama `AgentBatchJobHandler` bunu uygulamadı. Tek sınır `MaxItemsPerJob` (öge sayısı). Bir sonraki iyileştirme: `JobContext`'e bir `AgentRunBudget` eklenip her ögeye aynı örnek verilir. |

---

## Sonraki Faza Devir Notu

- **Faz 18 (eval) bu kuyruğun ilk gerçek kullanıcısıdır.** `IJobHandler`
  sözleşmesi zaten eval'in ihtiyacını karşılayacak biçimde tasarlandı: bir iş
  N girdi çalıştırır (`JobContext.Items`), her biri için `ReportItemAsync` ile
  sonuç raporlar. Eval kendi `EvalJobHandler : IJobHandler`'ını (`Kind =>
  JobKind.Eval`) yazıp `services.AddJobHandler<EvalJobHandler>()` ile ekler —
  yeni bir depo, yeni bir tablo veya `JobWorkerBackgroundService`'te değişiklik
  **gerekmez**. `JobItemResult.RunId` alanı eval'in "bu değerlendirme hangi
  çalıştırmaya karşılık geliyor" sorusunu zaten cevaplar.
- 🚨 **`IJobHandler` isleyici, `IJobStore`'a dogrudan erismez** (bkz. `JobContext`).
  Eval kendi sonuçlarını (skor, geçti/kaldı) `job_items`'a değil kendi
  tablosuna yazmalıdır; `JobItemResult` yalnızca `Completed`/`Failed` +
  `RunId` + `Error` taşır, serbest bir skor alanı yoktur.
- `AmbientTenantScope.Begin(tenantId)` yalnızca `JobWorkerBackgroundService`
  içinde çağrılır. Eval kendi arka plan döngüsünü **yazmaz** — `IJobHandler`
  üzerinden bu kuyruğa katılır, dolayısıyla kiracı bağlamı otomatik gelir.
- Faz 21 (webhook) iş tamamlanma olayını yayınlayabilir.
- Faz 25 (saklama) tamamlanmış işleri ve öğelerini temizlemekle yükümlüdür.
  `jobs`/`job_items`'ın `runs`/`run_events` gibi büyüyeceği unutulmamalı.
