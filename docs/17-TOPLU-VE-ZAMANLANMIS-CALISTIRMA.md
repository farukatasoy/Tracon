# Faz 17 — Toplu ve Zamanlanmış Çalıştırma

> **Durum:** 📋 Planlandı
> **Kaynak:** [BEYIN-FIRTINASI.md](BEYIN-FIRTINASI.md) · **F-22**
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
    created_at    timestamptz NOT NULL
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

## 17.4 — Public API

```csharp
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
}

public enum JobKind { AgentBatch, Workflow, Eval }
public enum JobStatus { Pending, Leased, Running, Completed, Failed, Cancelled }

public interface IJobStore
{
    ValueTask<JobRecord> EnqueueAsync(JobRecord job, CancellationToken ct = default);
    ValueTask<JobRecord?> LeaseAsync(string owner, TimeSpan leaseDuration, CancellationToken ct = default);
    ValueTask RenewLeaseAsync(Guid jobId, string owner, TimeSpan leaseDuration, CancellationToken ct = default);
    ValueTask CompleteAsync(Guid jobId, JobStatus status, string? error, CancellationToken ct = default);
    ValueTask<IReadOnlyList<JobRecord>> QueryAsync(JobQuery query, CancellationToken ct = default);
    ValueTask ReportItemAsync(JobItemResult item, CancellationToken ct = default);
}

public interface IJobHandler                      // genisleme noktasi (K4)
{
    JobKind Kind { get; }
    ValueTask ExecuteAsync(JobContext context, CancellationToken ct = default);
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
```

`IJobHandler` bir genişleme noktasıdır: Faz 18 (eval) kendi işleyicisini
`AddJobHandler<EvalJobHandler>()` ile ekler. Faz 17 iki işleyici getirir:
`AgentBatchJobHandler` ve `WorkflowJobHandler`.

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

Arayüz: yeni **Jobs** ekranı — zamanlamalar listesi, çalışan işler, ilerleme
çubuğu (`done/total`), başarısız öğeler ve her öğenin çalıştırma bağlantısı.

Bütçe hedefi: **+6 KB gzip'ten az**.

---

## Testler

| Proje | Yeni test |
|-------|-----------|
| `AgentPrism.Core.UnitTests` | Cron ayrıştırma (geçerli/geçersiz, sınır durumlar, saat dilimi); yeniden deneme sayacı; işleyici seçimi |
| `AgentPrism.PostgreSql.IntegrationTests` | `JobStoreContract`; **eşzamanlı iki işçi aynı işi almaz** (gerçek Postgres ile, `SKIP LOCKED` kanıtı); süresi dolmuş kira yeniden alınır; kiracı yalıtımı |
| `AgentPrism.AspNetCore.FunctionalTests` | Uçlar, roller, iptal, `MaxItemsPerJob` sınırı |
| `AgentPrism.Ui.E2ETests` | Zamanlama oluşturma, elle tetikleme, ilerleme |

**Eşzamanlılık testi zorunludur.** İki `IJobStore` örneği aynı veritabanına
bağlanır ve 50 iş için yarışır; hiçbir iş iki kez alınmamalıdır.

---

## Bu Fazda Verilecek Kararlar

1. **Kuyruk PostgreSQL üzerinde, `SKIP LOCKED` ile** — ek altyapı gerektirmez.
2. **Cron ayrıştırıcısı elle yazılır** — K-007; desteklenen alt küme yazılır.
3. **İşçi kapatılabilir** (`RunWorker = false`) — barındırma modeli tüketicinindir.
4. **`IJobHandler` genişleme noktasıdır** — K4; Faz 18 bunu kullanır.
5. **Bellek içi kuyruk desteklenir ama sınırları `/api/meta`'da bildirilir** — K-018.

---

## Açık Sorular

1. **Toplu işin öğeleri paralel mi çalışsın?** Paralellik hızlıdır ama sağlayıcı
   hız sınırına takılır. Öneri: iş başına **sıralı**, işler arası paralel
   (`MaxConcurrentJobs`).
2. **Başarısız öğede iş devam etsin mi?** Öneri: **evet**, `failed_items`
   sayılır; tümü başarısızsa iş `Failed`.
3. **`MaxItemsPerJob` 1000 uygun mu?** Daha büyük kümeler için iş bölünür.
   Öneri: **1000**.
4. **Zamanlanmış iş hangi kiracıyla çalışır?** HTTP bağlamı yoktur.
   Öneri: zamanlama kaydındaki `tenant_id`; `ITenantContext` iş süresince o
   değere sabitlenir (`AsyncLocal` yerine **açık parametre** ile).

---

## Bitiş Ölçütleri (DoD)

- [ ] Bir agent 20 girdilik bir küme üzerinde toplu çalıştırılıyor; 20 `runs`
      satırı oluşuyor
- [ ] Cron ile zamanlanmış iş tetikleniyor (gerçek çıktı dokümanda)
- [ ] İki işçi aynı anda koşarken iş **tam bir kez** çalışıyor
- [ ] İşçi öldürülüp yeniden başlatıldığında yarım kalan iş devam ediyor
- [ ] `RunWorker = false` ile işçi hiç başlamıyor
- [ ] Zamanlanmış işte kiracı doğru; sızıntı yok
- [ ] Dört doğrulama kapısı sıfır uyarı

---

## Riskler

| Risk | Önlem |
|------|-------|
| Kütüphaneye arka plan süreci girmesi | `RunWorker` kapatılabilir; kaynak sınırları varsayılan düşük |
| İş kuyruğu veritabanını yorar | Kısmi indeks; yoklama aralığı; Faz 25 temizliği |
| Cron alt kümesi kullanıcıyı şaşırtır | Desteklenmeyen sözdizimi kaydetmede `400` verir, sessizce yok sayılmaz |
| Yaz saati geçişinde çift çalışma | UTC hesap + `(schedule_id, scheduled_for)` benzersizliği |
| Toplu iş maliyeti patlar | Faz 12'nin `AgentRunBudget` mekanizması iş başına uygulanır |

---

## Sonraki Faza Devir Notu

- **Faz 18 (eval) bu kuyruğun ilk gerçek kullanıcısıdır.** `IJobHandler`
  sözleşmesi eval'in ihtiyacını karşılayacak biçimde tasarlanmalıdır: bir iş,
  N girdi çalıştırır ve her biri için sonuç raporlar.
- Faz 21 (webhook) iş tamamlanma olayını yayınlayabilir.
- Faz 25 (saklama) tamamlanmış işleri ve öğelerini temizlemekle yükümlüdür.
