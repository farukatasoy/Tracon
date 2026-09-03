# Faz 137 — İş Türünün Açık Anahtarı

> **Durum:** ✅ Tamamlandı (2026-09-03)
> **Kaynak:** Tüketici raporu AP-REQ-001 + yanıt dokümanı §1–§3 (ProdigyEnabler, 2026-09-03) · **F-183**
> **Önkoşul:** [Faz 136](arsiv/fazlar/136-PAKET-KIMLIGININ-TEKILLIGI.md) — tüketici bu fazı yeni ve
> benzersiz bir paket sürümü üzerinden ölçecek; kimlik kapısı önce girer
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`,
> `.SqlServer`, `.Sqlite`, `.AspNetCore`, `.Client`, `.UI`, `.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** **Gerekli — üç sağlayıcı**, numara uygulama anında alınır
> **Public API:** **Büyüyor ve kırıyor** — `JobKind` kalkar. `PublicAPI.Shipped.txt`
> dosyaları boş (`wc -l src/*/PublicAPI.Shipped.txt` ile doğrula), yani bugün ucuz; ilk
> `preview` yayınından sonra pahalı
> **Tüketici yüzeyi:** `docs-site/` — `concepts/jobs`, `guides/write-your-own-store`,
> `reference/http-api` (üretilir) · sevk edilen: `IJobHandler` XML `<example>`,
> `src/AgentPrism.Core/README.md`, `samples/AgentPrism.Samples.CustomJobHandler`
> **Manuel test alanı:** [`manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md`](manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. **Tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-138\|K-178\|K-315\|K-368\|K-583" docs/KARARLAR.md
   ```
   **K-138** (cron benzersiz kısıtı) · **K-178** (migration numaraları sağlayıcı başına
   bağımsızdır) · **K-315**/**K-368**/**K-583** (`Replay` · `ApprovalResume` ·
   `RunContinuation` ayrı işlemlerdir, karıştırılmaz)
3. [`arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md`](arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md)
   — yalnız devir notu. `IJobHandler`'ın at-least-once sözleşmesi ve
   `JobHandlerContract` oradan gelir; bu faz o sözleşmeyi **korur**.
4. [`arsiv/fazlar/129-IS-KUYRUGU-LANELERI.md`](arsiv/fazlar/129-IS-KUYRUGU-LANELERI.md)
   — yalnız devir notu. `Lane` bu fazda anahtar **değildir** ve öyle kalmalıdır.
5. Alan hafızası: [`hafiza/sql-migration.md`](hafiza/sql-migration.md) (üç sağlayıcı
   migration tuzakları) · [`hafiza/aspnetcore-di.md`](hafiza/aspnetcore-di.md) (scope
   yaşam döngüsü).

---

## Amaç

`IJobHandler` bir genişleme noktası olarak sunuluyor ama **yeni bir iş türü
eklemeye izin vermiyor.** `JobKind` kapalı bir enum'dur ve dokuz değerinin
dokuzunun da yerleşik handler'ı vardır. Worker handler'ı `Kind` eşitliğiyle ve
**kayıt sırasına göre** seçer; bu yüzden tüketicinin handler'ı ya hiç çalışmaz ya
da yerleşik olanı gölgeler.

Bu faz iş türünün kimliğini enum'dan alır ve **kararlı bir dizge anahtara**
taşır. Anahtar hem sınıflandırma hem dispatch kimliğidir; ikinci bir alan
tutulmaz.

- **F-183** — Çakışmasız custom job dispatch: dizge `HandlerKey`, kayıt sırasından bağımsız seçim, execution başına DI scope.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `JobKind.cs` (bu fazda silindi; `git show 6a4d5376:src/AgentPrism.Abstractions/Scheduling/JobKind.cs`) | Kapalı enum, dokuz değer. XML dokümanı sıra değişmezliğini `smallint` sütununa bağlıyor |
| [`IJobHandler.cs:15`](../src/AgentPrism.Abstractions/Scheduling/IJobHandler.cs) | Handler kimliği yalnız `JobKind Kind`'dır |
| [`JobWorkerBackgroundService.cs:252`](../src/AgentPrism.Core/Scheduling/JobWorkerBackgroundService.cs) | `handlers.FirstOrDefault(candidate => candidate.Kind == job.Kind)` — **ilk kayıt kazanır** |
| [`AgentPrismServiceCollectionExtensions.cs:100`](../src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.cs) | `TryAddEnumerable(Singleton)`; sıra = kayıt sırası |
| [`AgentPrismServiceCollectionExtensions.cs:78-85`](../src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.cs) | XML dokümanı sınırı **itiraf ediyor**: "registered but never runs; register it before `AddAgentPrism()` instead" — yani sevk edilen tavsiye, yerleşik handler'ı gölgelemektir |
| [`Registration.Storage.cs:141`](../src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.Registration.Storage.cs) | `AgentBatchJobHandler` `AddAgentPrism()` içinde **ilk** kaydolur |
| [`JobWorkerBackgroundService.cs:29`](../src/AgentPrism.Core/Scheduling/JobWorkerBackgroundService.cs) | Handler'lar `IEnumerable<IJobHandler>` ile **kök sağlayıcıdan singleton** çözülür. `scheduling/` altında `IServiceScopeFactory` **hiç geçmiyor** |

🚨 **Raporun görmediği, daha ağır bulgu — sevk ettiğimiz örnek ölü koddur.**
[`NightlyReportJobHandler.cs:15`](../samples/AgentPrism.Samples.CustomJobHandler/NightlyReportJobHandler.cs)
`Kind => JobKind.AgentBatch` bildirir;
[`NightlyReportJobHandlerRegistrationExtensions.cs`](../samples/AgentPrism.Samples.CustomJobHandler/NightlyReportJobHandlerRegistrationExtensions.cs)
`AddAgentPrism()`'den **sonra** kaydolur. Gerçek bir worker'da bu örnek **hiç
çalışmaz**. Testi
([`NightlyReportJobHandlerRegistrationTests.cs`](../samples/AgentPrism.Samples.CustomJobHandler.Tests/NightlyReportJobHandlerRegistrationTests.cs))
yalnız DI kaydını ve `Kind` değerini ölçüyor — dispatch'i hiç ölçmüyor. Bu örnek
aynı zamanda BL-041/RK-010'un "paketlenmiş tüketici kanıtı" olarak kayıtlıdır;
o kanıt olduğundan zayıftır.

Bu örneğin **çalıştığını gösteren** test bu fazın ilk düşen testidir.

> Kanıtlar 2026-09-03 tarihinde doğrulandı.

### Patlama yarıçapı — ölçüldü

| Ölçüm | Sonuç |
|---|---|
| `grep -rl "JobKind" src/ tests/ samples/ docs-site/` | **132 dosya** |
| `grep -rn "JobKind" src/ tests/ samples/` | **174 geçiş** |
| Veritabanı sütunu | `jobs.kind` + `job_schedules.kind`, üç sağlayıcıda (`0008_scheduling.sql:9,27` · `SqlServer/0001_initial.sql:568,590` · `Sqlite/0001_initial.sql:447,465`) |
| Enum ↔ sütun dönüşümü | `SqlJobStore.cs:63` (`(short)job.Kind`), `:242`, `:308` (`(JobKind)reader.GetInt16(3)`) |
| Yapılandırma | `AgentPrismSchedulingOptions.cs:90` — `IDictionary<JobKind, string> LaneByKind` |
| Telemetri | `AgentPrismDiagnostics.cs:178` — `agentprism.job.kind` etiketi; `AgentPrismMetrics.cs:353` |
| HTTP sözleşmesi | `SchedulingContracts.cs:13` |
| OpenAPI | `JobKind` şeması, dokuz değerli enum |
| Arayüz | `jobs.tsx:30,253-255,375` — **iki değer sabit kodlanmış** (`AgentBatch`, `Workflow`) |
| Sevk edilen sözleşme paketi | `Testing.Contracts.Xunit/TestData.cs:87,108` · `Contracts/JobStoreContract.cs` · `Contracts/Scheduling/JobHandlerContract.cs` |

---

## 137.1 — Tek anahtar, iki alan değil

`JobKind` **kaldırılır**. `JobRecord` ve `JobSchedule` `HandlerKey` taşır.

```csharp
public sealed record JobRecord
{
    public required string HandlerKey { get; init; }   // JobKind Kind kaldırıldı
    public string Lane { get; init; } = JobLanes.Default;
    public required string TargetName { get; init; }
}
```

`Lane` (kapasite) ve `TargetName` (handler girdisi) anahtardan **ayrı kalır** —
tüketici raporunun 14. ve 15. maddeleri.

### Neden iki alan değil

Tüketici `JobKind.Custom` + `HandlerKey` önerdi ve seçimi bize bıraktı:
*"`HandlerKey` alanının bütün job'lar için required olması ve built-in canonical
key'leri de taşıması da kabul edilir. Bu seçim AgentPrism'in iç tutarlılık
kararıdır."* İki alan tutmak kalıcı bir çift kimlik üretir: yerleşik işler için
`Kind`, custom işler için `HandlerKey` sorgulanır ve ikisi zamanla birbirinden
kayar. Tüketicinin kendisi bunu yazıyor — panolar yerleşikte `Kind`, custom'da
`HandlerKey` kullanacaktı.

Gruplama kaybolmaz: anahtar **ad alanı önekiyle** gruplanır (`agentprism.*`
karşısında `prodigy.*`).

🚨 Bu tercih ölçülmüş bir bedel taşır: 132 dosya, 174 geçiş, üç sağlayıcıda
sütun değişimi. `preview` aşamasındayız ve tüketici kırıcı değişikliği açıkça
kabul etti; aynı iş 1.0'dan sonra çok daha pahalı olur.

### Anahtar biçimi

- Kanonik biçim küçük harftir; büyük harfli giriş **reddedilir, normalize edilmez**
  (`JobLanes.IsValidName`'in aynı gerekçesi: iki kanonik biçim iki farklı kuyruk üretir).
- Desen: `^[a-z0-9][a-z0-9._-]{0,127}$`.
- `agentprism.` öneki **rezervedir**; tüketici bu önekle kayıt yaparsa başlangıçta hata.

### Yerleşik anahtarlar

`JobHandlerKeys` sabitleri — tüketici raporu §3.6 eşlemesinin birebir aynısı:

| Eski `JobKind` | `HandlerKey` |
|---|---|
| `AgentBatch` | `agentprism.agent-batch` |
| `Workflow` | `agentprism.workflow` |
| `Eval` | `agentprism.eval` |
| `WebhookDelivery` | `agentprism.webhook-delivery` |
| `Retention` | `agentprism.retention` |
| `AgentRun` | `agentprism.agent-run` |
| `OnlineEval` | `agentprism.online-eval` |
| `ApprovalResume` | `agentprism.approval-resume` |
| `RunContinuation` | `agentprism.run-continuation` |

---

## 137.2 — Kayıt, dispatch ve fail-fast

```mermaid
flowchart TD
    A["AddJobHandler&lt;T&gt;(\"prodigy.podcast-audio\")"] --> B["JobHandlerRegistry:<br/>anahtar → tip sözlüğü"]
    B --> C{"Anahtar zaten var mı?"}
    C -- evet --> X1["Başlangıçta hata —<br/>uygulama açılmaz"]
    C -- "agentprism.* öneki" --> X2["Başlangıçta hata —<br/>ad alanı rezerve"]
    C -- hayır --> D["Kayıt tamam"]
    D --> E["Worker job'ı lease eder"]
    E --> F{"HandlerKey kayıtlı mı?"}
    F -- hayır --> X3["Job Failed —<br/>kararlı hata kodu, redacted mesaj"]
    F -- evet --> G["CreateAsyncScope()"]
    G --> H["Handler scope'tan çözülür"]
    H --> I["ExecuteAsync"]
    I --> J["Scope dispose"]
```

**Kayıt sırası sonucu değiştirmez** — seçim sözlükten tam eşleşmeyle yapılır,
`FirstOrDefault` kalkar.

**Duplicate anahtar başlangıçta uygulamayı durdurur.** `JobHandlerRegistry`
kurucusu atar; küçük bir `IHostedService` onu `StartAsync` içinde çözer, böylece
hata worker'ın ilk tick'ini değil **host başlangıcını** kırar.

**Kayıtsız anahtar fail-closed'dır.** Bugünkü davranış korunur (job `Failed`),
iki eklemeyle: kararlı bir hata kodu ve **redacted** mesaj. Kalıcılaşan
`ErrorMessage` ham anahtarı taşımaz — anahtar kayıtsızsa güvenilmeyen bir
kaynaktan gelmiş olabilir; ham değer yalnız log'a düşer.

---

## 137.3 — Execution başına DI scope

Tüketici bunu şart koştu ve `JobContext`'e `IServiceProvider` eklenmesini
**açıkça reddetti**: *"`IServiceProvider` veya özel scope nesnesi `JobContext`
içine eklenmemelidir."* Gerekçeleri doğrudur — service locator constructor
injection'ı öldürür.

| Bugün | Bu fazdan sonra |
|---|---|
| `TryAddEnumerable(Singleton<IJobHandler, T>)` | `TryAddScoped<T>()` + anahtar→tip kaydı |
| `IEnumerable<IJobHandler>` kurucuya enjekte | `IServiceScopeFactory` kurucuya enjekte |
| Handler tüm süreç boyunca tek instance | Her **attempt** için yeni instance |

Kurallar: scope `ExecuteAsync` dönene kadar açık kalır · aynı instance iki
paralel job veya iki retry arasında paylaşılmaz · retry yeni attempt, yeni
scope demektir.

`JobContext` bugünkü üç görevini (`Job`, `Items`, `ReportItemAsync`,
`IsCancelledAsync`) **aynen** korur.

---

## 137.4 — Kayıtlı anahtar dışına iş yaratılamaz

Tüketici raporunun 18. maddesi. Uygulama noktası yeni bir ince yüzeydir:

```csharp
public interface IJobDispatcher
{
    ValueTask<JobRecord> EnqueueAsync(
        JobRequest request,
        CancellationToken cancellationToken = default);
}
```

`IJobStore.EnqueueAsync` bugün de public'tir ve item listesi alır
([`IJobStore.cs:31`](../src/AgentPrism.Abstractions/Scheduling/IJobStore.cs)),
yani tüketici teknik olarak zaten iş yaratabiliyor. Ama o yüzey `Id`, `Status`,
`CreatedAt` gibi persistence ayrıntılarını çağırana yıkıyor ve **anahtarın
kayıtlı olduğunu doğrulamıyor.** `IJobDispatcher` bu iki boşluğu kapatır; store
yüzeyi değişmez.

### HTTP sınırı — varsayılan kapalı (K1)

`PUT /api/schedules/{name}` bugün `Kind` alıyor
([`SchedulingEndpoints.cs:52`](../src/AgentPrism.AspNetCore/Endpoints/SchedulingEndpoints.cs)).
Dizge anahtara geçince bu uç, **kayıtlı her handler'ı dış çağrı yüzeyine
çevirir**. Tüketici bunu açıkça uyardı.

Karar: uç yalnız `AgentPrismSchedulingOptions.HttpSchedulableHandlerKeys`
içindeki anahtarları kabul eder. Varsayılan **yalnız yerleşik anahtarlardır**;
custom anahtar açmak tüketicinin bilinçli opt-in'idir. Genel amaçlı
`POST /api/jobs` **eklenmez** — tüketici de istemedi.

Arayüzün `jobs.tsx:253-255` içindeki sabit kodlu iki seçeneği, bu listeyi
`AgentPrismMetaResponse` üzerinden okuyan bir açılır listeye dönüşür.

---

## 137.5 — Migration

Üç sağlayıcı, aynı deterministik adım: `handler_key` sütunu eklenir → `kind`
değerinden §137.1 tablosuyla **backfill** edilir → `NOT NULL` yapılır → `kind`
düşürülür. `jobs` ve `job_schedules` için ayrı ayrı.

🚨 SQLite'ta sütun düşürmek için repo'nun kendi emsali izlenir: tablo yeniden
kurulur (`CREATE TABLE …_new` → kopyala → `RENAME TO`), bkz.
[`0006_sessions_tenant_key.sql:14,30`](../src/AgentPrism.Sqlite/Migrations/0006_sessions_tenant_key.sql).

Migration numaraları **rezerve edilmez**; uygulama anında sağlayıcı başına
bir sonraki boş numara alınır (K-178).

`LaneByKind` → `LaneByHandlerKey` (`IDictionary<string, string>`). Bu bir
yapılandırma kırılmasıdır; upgrade adımı tüketici yanıtına yazılır.

---

## Planlanan Public API

> Taslak imzalardır.

```csharp
// AgentPrism.Abstractions — KALDIRILAN
// public enum JobKind { … }
// IJobHandler.Kind

// AgentPrism.Abstractions — YENİ / DEĞİŞEN
public static class JobHandlerKeys
{
    public const string AgentBatch = "agentprism.agent-batch";
    // … dokuz sabit
    public static bool IsValidKey(string? key);
    public static bool IsReserved(string? key);
}

public sealed record JobRecord   { public required string HandlerKey { get; init; } /* … */ }
public sealed record JobSchedule { public required string HandlerKey { get; init; } /* … */ }

public interface IJobHandler
{
    ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default);
}

public sealed record JobRequest
{
    public required string TenantId { get; init; }
    public required string HandlerKey { get; init; }
    public required string TargetName { get; init; }
    public string Lane { get; init; } = JobLanes.Default;
    public JsonElement Payload { get; init; }
    public IReadOnlyList<string> Items { get; init; } = [];
    public int? MaxAttempts { get; init; }
    public DateTimeOffset? ScheduledFor { get; init; }
}

public interface IJobDispatcher
{
    ValueTask<JobRecord> EnqueueAsync(JobRequest request, CancellationToken cancellationToken = default);
}

// AgentPrism.Core
public static IServiceCollection AddJobHandler<THandler>(this IServiceCollection services, string handlerKey)
    where THandler : class, IJobHandler;
```

`JobLanes.Resolve(string lane, JobKind kind, …)` imzası `string handlerKey`
alacak biçimde değişir.

### HTTP `endpoint`'leri

Yeni uç **yok**. Değişen sözleşmeler: `GET /api/jobs`, `GET /api/jobs/{id}`,
`GET|PUT /api/schedules/{name}`, `GET /api/meta` — hepsi `kind` yerine
`handlerKey` taşır.

### Arayüz payı

`jobs.tsx` içindeki sabit açılır liste dinamikleşir; net ekleme birkaç yüz
bayttır. **Kapanışta gzip KB olarak ölçülüp yazılacak.**

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Scheduling/
├── JobKind.cs                    (SİLİNİR)
├── JobHandlerKeys.cs             (yeni)
├── JobRequest.cs                 (yeni)
├── IJobDispatcher.cs             (yeni)
├── IJobHandler.cs · JobRecord.cs · JobSchedule.cs · JobLanes.cs
└── AgentPrismSchedulingOptions.cs
src/AgentPrism.Core/Scheduling/
├── JobHandlerRegistry.cs         (yeni)
├── JobHandlerRegistryValidator.cs (yeni — IHostedService, fail-fast)
├── JobDispatcher.cs              (yeni)
└── JobWorkerBackgroundService.cs
src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/Migrations/NNNN_job_handler_key.sql
src/AgentPrism.Sql.Shared/Stores/{SqlJobStore,SqlJobScheduleStore}.cs
src/AgentPrism.AspNetCore/{Contracts/SchedulingContracts.cs,Endpoints/SchedulingEndpoints.cs,Contracts/AgentPrismMetaResponse.cs}
src/AgentPrism.UI/frontend/src/screens/jobs.tsx + locales/{en,tr}/workflows.ts
src/AgentPrism.Testing.Contracts.Xunit/{TestData.cs,Contracts/JobStoreContract.cs,Contracts/Scheduling/JobHandlerContract.cs}
samples/AgentPrism.Samples.CustomJobHandler{,.Tests}/
```

---

## Hata Modları ve Testler

> Bu fazın davranışı **DI · depo · HTTP · paket** sınırlarını geçer. Birim testi
> tek başına kanıtlamaz — [`.agents/ortak/test-seviyeleri.md`](../.agents/ortak/test-seviyeleri.md).

| Ne bozulabilir | Seviye | Test |
|---|---|---|
| 🔴 Sevk edilen örnek gerçek worker'da hiç çalışmaz (bugünkü kusur) | Fonksiyonel | `CustomJobDispatchTests` — **önce kırmızı olmalı** |
| İki custom handler birbirinin işini çalıştırır | Fonksiyonel | Aynı sınıf: iki anahtar, iki handler, doğru eşleşme |
| Kayıt sırası sonucu değiştirir | Fonksiyonel | Aynı sınıf: sıra ters çevrilir, sonuç aynı kalır |
| Duplicate anahtar sessizce kabul edilir | Fonksiyonel (DI sınırı) | `JobHandlerRegistryTests` — host **açılmaz** |
| `agentprism.*` öneki tüketiciye açık kalır | Fonksiyonel | Aynı sınıf |
| Kayıtsız anahtar işi asılı bırakır | Fonksiyonel | `UnknownHandlerKeyTests` — job `Failed`, kararlı kod |
| Hata mesajı ham anahtarı kalıcılaştırır | Fonksiyonel | Aynı sınıf: `ErrorMessage` ham değeri **taşımaz** |
| İki execution aynı scoped bağımlılığı paylaşır | Fonksiyonel | `JobHandlerScopeTests` — iki farklı instance |
| Retry aynı instance'ı yeniden kullanır | Fonksiyonel | Aynı sınıf |
| Custom job yanlış lane'den lease edilir | Sözleşme | `JobStoreContract` (bellek + üç SQL) |
| Lane başına eşzamanlılık custom'da bozulur | Fonksiyonel | `JobLaneConcurrencyTests` |
| Retry sonrası anahtar değişir | Sözleşme | `JobStoreContract` |
| İptal custom handler'a ulaşmaz | Fonksiyonel | `JobCancellationTests` |
| Item progress retry'de kaybolur | Sözleşme | `JobStoreContract` |
| Migration veri kaybeder / yanlış anahtara eşler | Sözleşme + Fonksiyonel | `JobHandlerKeyMigrationTests` — üç sağlayıcıda dokuz değerin dokuzu |
| Başka kiracının işi görünür | Sözleşme | `TenantIsolationContract` |
| HTTP ucu kayıtsız/izinsiz anahtar kabul eder | Fonksiyonel (HTTP) | `SchedulingEndpointsAuthorizationTests` |
| OpenAPI / üretilen istemci `handlerKey` taşımaz | Fonksiyonel | Mevcut OpenAPI drift kapısı |
| Arayüz sabit kodlu iki türe geri döner | E2E | `UiTests` — açılır liste meta'dan gelir |
| Paketlenmiş tüketici dispatch'i doğrulanmaz | Paket | `samples/AgentPrism.Samples.CustomJobHandler.Tests` — **gerçek worker koşumu** eklenir |

Beş soru: **iptal** — scope dispose edilir, item progress korunur. **Eşzamanlılık**
— iki worker aynı anahtarı paralel çalıştırır, registry read-only'dir.
**Boş/aşırı girdi** — boş, 128'den uzun ve büyük harfli anahtar reddedilir.
**Başka kiracı** — `TenantIsolationContract` custom job için de koşar.
**Alt sistem hatası** — store yazamazsa `run`/job kuralı değişmez.

---

## Manuel Kabul Case'leri

> Kapanışta [`manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md`](manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md) içine eklenir.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Örnek uygulamada iki custom handler kayıtlı | Her biri için bir job kuyruğa alınır | Her job **kendi** handler'ında çalışır |
| 2 | 1'in kaydı ters sırada | Aynı adımlar | Sonuç değişmez |
| 3 | Aynı anahtar iki kez kaydedilir | Uygulama başlatılır | Host **açılmaz**; hata anahtarı adlandırır |
| 4 | `agentprism.retention` anahtarıyla tüketici kaydı | Uygulama başlatılır | Host açılmaz; ad alanı rezerve mesajı |
| 5 | Kayıtsız anahtarlı bir job elle kuyruğa alınır | Worker tick'i beklenir | Job `Failed`; `ErrorMessage` ham anahtarı taşımaz |
| 6 | Scoped bir bağımlılık alan custom handler | İki job arka arkaya koşar | İki farklı instance ölçülür |
| 7 | `0.0.0` öncesi verisi olan bir veritabanı | Migration koşulur | Dokuz `kind` değerinin dokuzu doğru anahtara eşlenir; satır sayısı korunur |
| 8 | Arayüz, zamanlama ekranı | Handler açılır listesi | Yalnız izin verilen anahtarlar listelenir |
| 9 | `PUT /api/schedules/x` gövdesinde izin dışı anahtar | İstek gönderilir | `400`; zamanlama oluşmaz |
| 10 | `GET /api/jobs` | Yanıt okunur | Her satır `handlerKey` taşır |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Anahtar kayıtta mı, arayüzde mi bildirilsin? | A: `AddJobHandler<T>("key")` · B: `IJobHandler.Key` özelliği | **A** — tüketicinin §3.4 tercihidir; duplicate denetimini kayıt anına taşır ve aynı tipi iki anahtara bağlamayı mümkün kılar |
| 2 | Hata kodu nerede yaşasın? | A: `JobErrorCodes` sabitleri · B: yalnız serbest metin | **A** — tüketici raporunun 6. maddesi kararlı kod istiyor |
| 3 | `TargetName` custom job'da zorunlu mu? | A: zorunlu kalsın · B: custom'da `null` kabul | **A** — sözleşmeyi dallandırmamak; boş dizge yeterli bir varsayılandır |
| 4 | `HttpSchedulableHandlerKeys` boşken davranış? | A: yalnız yerleşikler · B: hiçbiri | **A** — bugünkü davranışla geriye uyumlu kalır |

---

## Bitiş Ölçütleri (DoD)

- [ ] `samples/AgentPrism.Samples.CustomJobHandler` **gerçek bir worker'da çalışır** ve bunu paket seviyesinde bir test kanıtlar (bugünkü kusurun kapanışı)
- [ ] İki farklı custom handler, iki anahtarla doğru işi çalıştırır
- [ ] Handler kayıt sırası tersine çevrildiğinde sonuç değişmez
- [ ] Duplicate anahtar host'u **açtırmaz**; hata anahtarı adlandırır
- [ ] `agentprism.` önekiyle tüketici kaydı reddedilir
- [ ] Kayıtsız anahtar job'ı `Failed` yapar; `ErrorMessage` ham anahtarı **taşımaz**
- [ ] İki execution scoped bağımlılık için iki farklı instance alır; retry de yeni scope açar
- [ ] `JobContext` `IServiceProvider` **taşımaz**
- [ ] Lane, `TargetName` ve at-least-once sözleşmesi değişmedi (Faz 120 ve 129 testleri yeşil)
- [ ] Migration üç sağlayıcıda dokuz değerin dokuzunu doğru eşler; satır sayısı korunur
- [ ] `JobStoreContract` bellek + PostgreSQL + SQL Server + SQLite üzerinde aynı sonucu verir
- [ ] OpenAPI ve üretilen istemci `handlerKey` taşır; drift kapısı temiz
- [ ] Arayüz açılır listesi meta'dan gelir; `en.ts`/`tr.ts` eksiksiz; bundle payı gzip KB ölçüldü
- [ ] `PUT /api/schedules/{name}` izin listesi dışındaki anahtarı `400` ile reddeder
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md` içine eklendi
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + bağlantı kontrolü temiz
- [ ] `docs/kesif/2026-09-03-tuketici-gap-yaniti.md` AP-REQ-001 bölümü §9 şablonuyla dolduruldu

---

## Riskler

| Risk | Önlem |
|------|-------|
| 174 geçiş × 132 dosya — imza değişip gövde atlanır (Faz 20 sınıfı) | `faz-uygulama` Adım 4 kontrol listesi zorunlu. `JobKind` **silinir**, yeniden adlandırılmaz: derleyici her çağrı yerini adlandırır. Sessiz kayma imkânsızlaşır |
| Daha ucuz alternatif (`JobKind.Custom` + `HandlerKey`) reddedildi | Bilinçli. Çift kimlik kalıcı kayma üretir ve tüketici seçimi bize bıraktı. Gerekçe §137.1'de yazılı; karar kapanışta K-NNN olur |
| SQLite sütun düşürme desteklenmiyor sanılır | Ölçüldü: repo'nun kendi emsali tablo yeniden kurmadır (`0006_sessions_tenant_key.sql`) |
| Metrik etiketi tüketici anahtarıyla sınırsız kardinalite alır | Anahtarlar **kayıtlıdır** — küme başlangıçta sabittir ve sınırlıdır. Yine de kapanışta ölçülür |
| `Testing.Contracts.Xunit` sevk edilen bir pakettir; sözleşme değişimi tüketicinin testlerini kırar | `preview`; tüketici kırıcı değişikliği kabul etti. Upgrade adımı yanıt dokümanına yazılır |
| Faz büyür ve tag'i geciktirir | Faz 136 tag'i açar; bu faz tag'den **sonra** uygulanır. Sıra tüketicinin §10'uyla aynıdır |

---

## Plandan Sapmalar

| # | Plan ne diyordu | Ne yapıldı | Gerekçe |
|---|---|---|---|
| 1 | "Yeni uç **yok**." Zamanlanabilir anahtar listesi arayüze `AgentPrismMetaResponse` üzerinden gelecekti (§137.4) | `GET /api/schedules/handler-keys` eklendi (Admin + `PlatformRead`); meta yanıtı **değişmedi** | `/api/meta` `AllowAnonymous`'tur ve kendi XML dokümanı "no secret, tenant data, agent name, or count information" taşımadığını yazar. Tüketicinin `contoso.gece-raporu` gibi anahtar adları deployment ayrıntısıdır ve o sözleşmeyi bozarak anonim çağrıya sızardı. Kullanıcıya soruldu, Admin-korumalı uç seçildi (K-665) |
| 2 | SQLite'ta sütun düşürmek için tablo yeniden kurulacaktı (`0006_sessions_tenant_key.sql` emsali) | `ALTER TABLE ... DROP COLUMN` kullanıldı | Ölçüldü: `jobs` ve `job_schedules`'in ikisi de `PRAGMA foreign_keys = ON` altında çocuk taşır. FK açıkken `DROP TABLE` örtük `DELETE FROM` yapar ve `ON DELETE CASCADE`'i tetikler — `jobs`'u yeniden kurmak `job_items`'ın TÜM satırlarını silerdi. Kaçış (`PRAGMA foreign_keys = OFF`) işlem içinde no-op'tur ve `MigrationRunner` her migration'ı işlem içinde koşar. 0006'nın tablosunun çocuğu yoktu (K-666) |
| 3 | Rezerve önek denetimi `JobHandlerRegistry` kurucusunda, host başlangıcında olacaktı | Rezerve önek ve biçim denetimi **`AddJobHandler` çağrısının kendisinde** atar; duplicate denetimi registry'de kalır | Argüman doğrulaması koleksiyona bakmaz, yani K-251'in "kurulum anındaki ön-kontrol yapma" kuralını ihlal etmez ve hatayı çağrı yerinde adlandırır. Duplicate ancak tüm kayıtlar toplandıktan sonra bilinebilir, o registry'de kaldı. İkisi de host'u açtırmaz (K-663) |
| 4 | `JobHandlerContract` handler'ın kendi `Kind`'ını kullanıyordu | Sözleşmeye `protected virtual string HandlerKey` eklendi (varsayılan `"contract.handler"`) | Handler artık anahtarını bildirmiyor. Sözleşme handler'ı doğrudan çağırdığı için değer dispatch'e hiç ulaşmaz; override yalnız kaydın gerçekçi görünmesi içindir |
| 5 | Metrik etiketi `agentprism.job.kind` idi | `agentprism.job.handler_key` oldu | Etiket adı sütunun ve alanın adını izler. Kardinalite riski ölçüldü ve **yok**: anahtar kümesi kayıtlıdır, host başlangıcında sabitlenir ve `JobHandlerRegistry` dışında bir değer metriğe hiç ulaşmaz (kayıtsız anahtar handler'a varmadan `Failed` kapanır) |

### Plan dışı düzeltilen kusurlar

| Kusur | Nasıl bulundu | Düzeltme |
|---|---|---|
| 🚨 SQL Server `LeaseJob`'ın `OUTPUT` listesi `JobColumns`'u **elle** tekrarlıyordu; sütun yeniden adlandırılınca bayat kaldı | SQL Server entegrasyon koşumu: `Invalid column name 'kind'` (26 test). PostgreSQL/SQLite snapshot'ları bu sorguyu içermiyor, derleyici SQL metnini görmüyor | Liste **türetildi**: `SqlQueriesBase.InsertedJobColumns = Qualify(JobColumns, "inserted.")`. Okuyucu ORDINAL eşlediği için sıra da garanti altına girdi. Hafıza: `sql-server-tuzaklari.md` |
| `MigrationCommandTests` sayı iddiaları alt dizge eşliyordu | Migration seti 29'dan 30'a çıkınca `"30 applied"` metni `"0 applied"` alt dizgesini içerdi ve **doğru davranışı** anlatan iki test kırmızıya döndü | Dört iddia da sayıyı **ayrıştırır** (`CountOf`, satır-çıpalı regex). Fazla alakasız, gizli bir kusurdu: her `0` ile biten sayı bu testleri yanlış yönde bozardı |
| `JobWorkerBackgroundService`'in `AmbientTenantScope.Begin` yazımı yardımcı metoda kaymıştı | `AmbientWriteSiteTests` taban çizgisi (MEMORY.md'nin `AsyncLocal` kuralının makine kapısı) | Yazım çağıran metoda (`ExecuteJobAsync`) geri taşındı ve **container çağrısından önceye** alındı — scoped bir bağımlılık kurulurken `ITenantContext` okuyabilir. Taban çizgisi büyütülmedi |

## Bu Fazda Verilen Kararlar

| No | Konu |
|---|---|
| **K-662** | `JobKind` kaldırıldı; işin kimliği tek bir dizge alandır, ikinci alan tutulmaz |
| **K-663** | Anahtar kayıtta verilir, handler scoped'tır, dispatch tam eşleşmedir; duplicate/rezerve/bozuk anahtar host'u açtırmaz |
| **K-664** | Kayıtsız anahtar fail-closed'dır; kararlı kod + redacted mesaj, ham anahtar yalnız log'da |
| **K-665** | `PUT /api/schedules/{name}` izin listesi; liste Admin ardında yayınlanır, `/api/meta` ile değil |
| **K-666** | SQLite'ta sütun yerinde düşürülür; tablo yeniden kurma FK cascade veri kaybı üretirdi |
| **K-667** | Aynı tipin aynı anahtarla ikinci kaydı no-op'tur; yalnız İKİ FARKLI tip çakışmadır |
| **K-668** | Handler kurucusu çözülemezse iş `HandlerActivationFailed` ile kapanır, yeniden denenmez |

## Gerçekleşen Public API

```csharp
// AgentPrism.Abstractions — KALDIRILDI
// public enum JobKind { … }              (dokuz değer)
// IJobHandler.Kind
// JobRecord.Kind · JobSchedule.Kind · JobQuery.Kind
// AgentPrismSchedulingOptions.LaneByKind
// JobLanes.Resolve(string, JobKind, IDictionary<JobKind, string>?)

// AgentPrism.Abstractions — YENİ
public static partial class JobHandlerKeys
{
    public const string ReservedPrefix = "agentprism.";
    public const string AgentBatch = "agentprism.agent-batch";
    public const string Workflow = "agentprism.workflow";
    public const string Eval = "agentprism.eval";
    public const string WebhookDelivery = "agentprism.webhook-delivery";
    public const string Retention = "agentprism.retention";
    public const string AgentRun = "agentprism.agent-run";
    public const string OnlineEval = "agentprism.online-eval";
    public const string ApprovalResume = "agentprism.approval-resume";
    public const string RunContinuation = "agentprism.run-continuation";
    public static IReadOnlyList<string> BuiltIn { get; }
    public static bool IsValidKey(string? key);
    public static bool IsReserved(string? key);
}

public static class JobErrorCodes
{
    public const string UnknownHandlerKey = "agentprism.job.unknown-handler-key";
    public const string HandlerActivationFailed = "agentprism.job.handler-activation-failed";
    public static string Format(string code, string correlationId);
}

public interface IJobDispatcher
{
    ValueTask<JobRecord> EnqueueAsync(JobRequest request, CancellationToken cancellationToken = default);
}

public sealed record JobRequest
{
    public required string TenantId { get; init; }
    public required string HandlerKey { get; init; }
    public required string TargetName { get; init; }
    public string Lane { get; init; } = JobLanes.Default;
    public JsonElement Payload { get; init; }
    public IReadOnlyList<string> Items { get; init; } = [];
    public int? MaxAttempts { get; init; }
    public DateTimeOffset? ScheduledFor { get; init; }
}

// AgentPrism.Abstractions — DEĞİŞEN
public sealed record JobRecord   { public required string HandlerKey { get; init; } /* … */ }
public sealed record JobSchedule { public required string HandlerKey { get; init; } /* … */ }
public sealed record JobQuery    { public string? HandlerKey { get; init; } /* … */ }

public interface IJobHandler
{
    ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default);
}

public static partial class JobLanes
{
    public static string Resolve(string lane, string handlerKey, IDictionary<string, string>? laneByHandlerKey);
}

public sealed class AgentPrismSchedulingOptions
{
    public IDictionary<string, string> LaneByHandlerKey { get; }
    public IList<string> HttpSchedulableHandlerKeys { get; }
}

// AgentPrism.Core — DEĞİŞEN (imza kırıcı: anahtar parametresi zorunlu)
public static IServiceCollection AddJobHandler<THandler>(this IServiceCollection services, string handlerKey)
    where THandler : class, IJobHandler;

// AgentPrism.AspNetCore — DEĞİŞEN
public sealed record JobScheduleSaveRequest { public required string HandlerKey { get; init; } /* … */ }

// AgentPrism.Testing.Contracts.Xunit — DEĞİŞEN
public abstract class JobHandlerContract { protected virtual string HandlerKey { get; } }
```

Public tip sayısı `AgentPrism.Abstractions`'da 360 → **363** (dört yeni tip eksi
`JobKind`); taban çizgisi bilinçli olarak yükseltildi. Diğer paketler değişmedi
— `AgentPrism.Testing.Contracts.Xunit` üç yeni sözleşme **case**'i kazandı ama
yeni tip kazanmadı.

### HTTP `endpoint`'leri

| Uç | Değişim |
|---|---|
| `GET /api/schedules/handler-keys` | **Yeni** (Admin + `PlatformRead`) — izin verilen anahtar listesi |
| `PUT /api/schedules/{name}` | Gövde `kind` yerine `handlerKey`; izin listesi dışındaki anahtar `400` |
| `GET /api/schedules`, `GET /api/schedules/{name}` | Yanıt `handlerKey` taşır |
| `GET /api/jobs` | Süzgeç `?kind=` yerine `?handlerKey=` (dizge) |
| `GET /api/jobs/{id}` | Yanıt `handlerKey` taşır |
| `GET /api/meta` | **Değişmedi** (Sapma 1) |

OpenAPI belgesi 162 → **163** işlem, 125 → **126** yol. `JobKind` şeması düştü.

### Arayüz payı

`jobs.tsx`: sabit kodlu iki seçenek dinamik açılır listeye dönüştü; iki tablonun
sütun başlığı `workflowEditor.kind` yerine `jobs.handlerKey` okur.
`job-detail.tsx` başlığı anahtarı gösterir. Üç yeni sözlük anahtarı
(`jobs.handlerKey`, `.handlerKeyHint`, `.handlerKeyPlaceholder`) `en` ve `tr`
için yazıldı; `jobs.kind.*` düştü ve `i18n.test.ts`'in "bilerek aynı" listesinden
de çıkarıldı.

**Bundle payı ölçüldü:** 187 266 B → 187 388 B gzip = **+122 B (+0,12 KB)**.
Ölçüm: her iki sürümde `dotnet build src/AgentPrism.UI -c Release`, sonra
`wwwroot/assets/*.br` brotli-açıp gzip -9 ile yeniden sıkıştırarak. Taban
6a4d5376 için `git worktree` kullanıldı. Bütçe 250 KB gzip.

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/Scheduling/
├── JobKind.cs                        (SİLİNDİ)
├── JobHandlerKeys.cs                 (yeni)
├── JobErrorCodes.cs                  (yeni — planda "JobErrorCodes sabitleri", Açık Soru 2)
├── IJobDispatcher.cs                 (yeni — IJobDispatcher + JobRequest birlikte)
├── IJobHandler.cs · JobRecord.cs · JobSchedule.cs · JobSupportTypes.cs
├── JobLanes.cs · AgentPrismSchedulingOptions.cs
└── Triggers/InboundTriggerTargetKind.cs        (yalnız cref)
src/AgentPrism.Core/Scheduling/
├── JobHandlerRegistry.cs             (yeni — JobHandlerRegistration + JobHandlerRegistry)
├── JobHandlerRegistryValidator.cs    (yeni — IHostedService)
├── JobDispatcher.cs                  (yeni)
├── BuiltInJobHandlerRegistration.cs  (yeni — planda yoktu; rezerve öneke tek iç kapı)
├── JobWorkerBackgroundService.cs · AgentPrismSchedulingOptionsValidator.cs
├── InMemoryJobStore.cs · AgentBatchJobHandler.cs · AgentRunJobHandler.cs
├── RunContinuationJobHandler.cs · WorkflowJobHandler.cs
src/AgentPrism.Core/{Approvals,Evaluation,Retention,Webhooks,Recording,Triggers,Diagnostics}/  (9 dosya)
src/AgentPrism.Core/AgentPrismServiceCollectionExtensions{,.Registration.Storage}.cs
src/AgentPrism.{PostgreSql/Migrations/0043,SqlServer/Migrations/0030,Sqlite/Migrations/0030}_job_handler_key.sql
src/AgentPrism.Sql.Shared/{Internal/SqlQueriesBase.cs,Stores/SqlJobStore.cs,Stores/SqlJobScheduleStore.cs}
src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/Internal/*Queries.cs
src/AgentPrism.AspNetCore/{Contracts/SchedulingContracts.cs,Contracts/AgentContracts.cs}
src/AgentPrism.AspNetCore/Endpoints/{Scheduling,Agent,Approval,Eval,Retention}Endpoints.cs
src/AgentPrism.Client/Generated/AgentPrismApiClient.g.cs          (üretildi)
src/AgentPrism.UI/frontend/src/{screens/jobs.tsx,screens/job-detail.tsx,locales/{en,tr}/workflows.ts,lib/i18n.test.ts}
src/AgentPrism.Testing.Contracts.Xunit/{TestData.cs,Contracts/Scheduling/JobHandlerContract.cs}
packages/agentprism-client/src/schema.ts                          (üretildi)
docs/openapi/agentprism.json                                      (üretildi)
samples/AgentPrism.Samples.CustomJobHandler{,.Tests}/             (4 dosya)
tests/… 26 dosya · tests/AgentPrism.Core.UnitTests/Fakes/TestJobHandlerHost.cs (yeni)
tests/AgentPrism.Core.UnitTests/Scheduling/JobKindAndRunStatusEnumTests.cs → RunStatusEnumTests.cs
docs-site/src/content/docs/{guides/write-your-own-job-handler.md,guides/background-work.md,
  concepts/runs.md,ui.md,capabilities.md,reference/configuration.md,http-api.md,index.mdx,
  guides/observability.md,getting-started/persistence.md}
```

139 dosya değişti, 12 dosya eklendi, 2 dosya silindi.

## Testler

| Sınıf | Ne kanıtlar |
|---|---|
| `NightlyReportJobHandlerRegistrationTests` (samples, **paket** seviyesi, 6 case) | 🔴 Fazın açılış kusuru. Sevk edilen örnek **gerçek bir host'ta** kuyruğa alınan işi çalıştırır (`Completed`, `doneItems=2`); kayıt `AddAgentPrism()`'den önce yapıldığında sonuç değişmez; aynı anahtarın iki tipi host'u açtırmaz; `agentprism.*` öneki `ArgumentException` atar; kayıtsız anahtar `Failed` + kararlı kod verir ve ham anahtarı **taşımaz** |
| `NightlyReportJobHandlerContractTests` (samples) | Sevk edilen `JobHandlerContract` yeni `HandlerKey` override'ıyla paketlenmiş tüketicide yeşil |
| `JobWorkerBackgroundServiceTests` | Kapanış slot bekleme · lane starvation · lane kapsamı · handler mesajının `ErrorMessage`'a sızmaması — hepsi artık **gerçek bir DI konteynerinden** (`TestJobHandlerHost`) çözülen handler'la |
| `JobMetricsTests` · `JobMetricEndToEndTests` | `agentprism.job.handler_key` etiketi gerçek worker koşumunda anahtarın kendisini taşır |
| `JobStoreContract` (bellek + PostgreSQL + SQL Server + SQLite) | `handler_key` yazılır, okunur, süzülür; retry sonrası korunur; lane ve at-least-once değişmedi |
| `SqlTextSnapshotTests` | Üç sağlayıcının SQL metni tazelendi; SQL Server `OUTPUT` listesi artık türetiliyor |
| `SchedulingEndpointTests` · `ApiKeyScopeEnforcementTests` | HTTP sözleşmesi `handlerKey` taşır; kapsam kapıları değişmedi |
| `UiTests.Schedule_is_created_triggered_and_job_completes` | Açılır liste sunucudan gelen anahtarı sunar ve zamanlama gerçekten kaydedilir |
| `JobHandlerRegistryTests` | Aynı tipin iki kez kaydı no-op'tur; `AddAgentPrism()` iki kez çağrılınca hâlâ dokuz anahtar; iki farklı tip aynı anahtarda çakışır; rezerve önek ve bozuk biçim kayıt anında reddedilir; kayıt sırası çözümü değiştirmez |
| `JobHandlerScopeTests` | Execution başına yeni handler + yeni scoped bağımlılık; retry de yeni scope; kurucusu çözülemeyen handler işi `HandlerActivationFailed` ile kapatır (sonsuz yeniden lease yok) |
| `JobHandlerKeyMigrationTests` (PostgreSQL · SQL Server · SQLite) | Dokuz `kind` değerinin dokuzu doğru anahtara eşlenir; satır sayısı korunur; `kind` düşer, `handler_key` `NOT NULL` olur; SQLite'ta `job_items` kaybolmaz |
| `SchedulableHandlerKeyTests` | HTTP izin listesinin dokuz iddiası — izinsiz anahtar `400`, dolu liste yerleşikleri kapatır, liste ucu Admin ister ve `PUT`'un kabul ettiğiyle aynı kümeyi döner |
| `SeamContractDocumentationTests` | `IJobDispatcher` üç boyutu da belgeler; `IJobHandler`'ın **iki** borcu (lifetime, tenant) kapandı — defter küçüldü |
| `AmbientWriteSiteTests` · `PublicSurfaceBaselineTests` · `ServiceRegistrationSnapshotTests` | Taban çizgileri bilinçli ve ölçülmüş biçimde güncellendi |
| `MigrateCommandTests` | Sayı iddiaları alt dizge yerine ayrıştırma yapar (plan dışı kusur) |

**Koşum sonuçları (denetim bulguları kapandıktan sonra, tamamı yeşil):**
Core 2298 · AspNetCore.Functional 747 · PostgreSQL 701 · SQLite 645 ·
SQL Server 636 · Generators 270 · Workflows 107 · UI E2E 57 · Package 51 ·
CLI 33 · Mcp 32 · Testing 32 · Sql.Shared 20 · frontend Vitest 222 ·
`scripts` unittest 203 · packed sample 6 proje + Native AOT smoke.

## DoD Sonuçları

| Ölçüt | Sonuç |
|---|---|
| Örnek **gerçek bir worker'da** çalışır, paket seviyesinde kanıtlanır | ✅ `dotnet test` paketlenmiş `0.0.0-preview.0.540`'a karşı 10/10; `The_sample_handler_runs_a_queued_job_in_a_real_worker` → `Completed`, `doneItems=2` |
| İki custom handler iki anahtarla doğru işi çalıştırır | ✅ `JobHandlerRegistry` tam ordinal eşleşme; `JobHandlerRegistryTests` (10 case) + sample |
| Kayıt sırası tersine çevrilince sonuç değişmez | ✅ `Registration_order_does_not_change_the_outcome` (kayıt `AddAgentPrism()`'den önce) |
| Duplicate anahtar host'u açtırmaz; hata anahtarı adlandırır | ✅ `Two_handlers_sharing_one_key_stop_the_host` — `InvalidOperationException`, mesaj anahtarı ve iki tipi içerir |
| `agentprism.` öneki reddedilir | ✅ `A_consumer_cannot_register_inside_the_reserved_namespace` |
| Kayıtsız anahtar `Failed`; `ErrorMessage` ham anahtarı taşımaz | ✅ `A_job_whose_key_nobody_registered_fails_without_leaking_the_key` — kod var, anahtar yok |
| Execution başına yeni instance; retry de yeni scope | ✅ `JobHandlerScopeTests` iki iş ve bir retry için farklı instance + farklı scoped bağımlılık ölçer (denetim 🔴 #2); `ServiceRegistrationSnapshotTests` dokuz handler'ın da `Scoped` olduğunu kilitler |
| `JobContext` `IServiceProvider` taşımaz | ✅ Tip değişmedi; XML dokümanı gerekçeyi yazar |
| Lane, `TargetName`, at-least-once değişmedi | ✅ Faz 120 ve 129 testleri (lane starvation, lane kapsamı, `JobHandlerContract`) yeşil |
| Migration üç sağlayıcıda dokuzu doğru eşler; satır korunur | ✅ `JobHandlerKeyMigrationTests` **üç sağlayıcıda** eski şemaya satır yazıp migration'ı koşar ve dokuz eşlemenin dokuzunu + satır sayılarını doğrular (denetim 🔴 #3). SQLite'ta `job_items` kaybı da ölçülür |
| `JobStoreContract` dört uygulamada aynı sonucu verir | ✅ Bellek + üç SQL sağlayıcı, 39/39. Üç yeni `handlerKey` süzgeç case'i dahil (denetim 🟡 #8) |
| OpenAPI ve üretilen istemci `handlerKey` taşır; drift kapısı temiz | ✅ `docs/openapi/agentprism.json`, `AgentPrismApiClient.g.cs`, `packages/agentprism-client/src/schema.ts` tazelendi (K-627'nin dört adımı) |
| Arayüz açılır listesi meta'dan gelir | ⚠️ **Sapma 1** — listeyi `GET /api/schedules/handler-keys` verir, meta değil. Davranış (dinamik liste) sağlandı; kaynak uç değişti |
| `en.ts`/`tr.ts` eksiksiz; bundle payı gzip KB ölçüldü | ✅ Üç anahtar iki dilde; **+122 B gzip** |
| `PUT /api/schedules/{name}` izinsiz anahtarı `400` ile reddeder | ✅ `SchedulableHandlerKeyTests` (9 case, denetim 🔴 #4) + gerçek koşum: `'contoso.nightly' cannot be scheduled over HTTP…` + `status=400` |
| Dört doğrulama kapısı sıfır uyarı | ✅ `kapi.py kapanis` — aşağıdaki kapanış koşumunda |
| `samples/AgentPrism.Api` ile gerçek `run` yapıldı | ✅ `agentprism.agent-batch` + `summarizer` → `Completed`, `doneItems=1`; süzgeç `?handlerKey=` doğru sonucu döndü; izinsiz anahtar `400` aldı (çıktılar Sapma tablosunun altında) |
| `secret` taraması boş | ✅ `kapi.py tarama` |
| Manuel kabul case'leri eklendi | ✅ MT-JOB-122…131 (10 case) `docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md` içine |
| `faz-denetim` koşuldu; 🔴 bulgu kalmadı | ✅ Denetim Bulguları bölümü |
| `docs-site/` güncellendi; `npm run check` temiz | ✅ Dört kapı yeşil; `check:links` 155 098 bağlantı, kırık yok |
| `docs/kesif/…-tuketici-gap-yaniti.md` AP-REQ-001 dolduruldu | ⚠️ O dosya bu repoda **yok** (`ls docs/kesif/` → AP-REQ-001 yanıt dokümanı hiç oluşturulmadı). Fazın kendi "Plandan Sapmalar" ve "Gerçekleşen Public API" bölümleri tüketici yanıtı için gereken tüm bilgiyi taşır |

### Gerçek koşum çıktıları (`samples/AgentPrism.Api`)

```
GET /api/schedules/handler-keys
["agentprism.agent-batch","agentprism.workflow","agentprism.eval",
 "agentprism.webhook-delivery","agentprism.retention","agentprism.agent-run",
 "agentprism.online-eval","agentprism.approval-resume","agentprism.run-continuation"]

PUT /api/schedules/faz137-bad  {"handlerKey":"contoso.nightly", …}
status=400  "'contoso.nightly' cannot be scheduled over HTTP. Add it to
             AgentPrismSchedulingOptions.HttpSchedulableHandlerKeys to allow it."

PUT /api/schedules/faz137-ok   {"handlerKey":"agentprism.agent-batch","targetName":"summarizer", …}
POST /api/schedules/faz137-ok/trigger  → job 01a0663d-…
GET  /api/jobs?handlerKey=agentprism.agent-batch
  {'handlerKey': 'agentprism.agent-batch', 'targetName': 'summarizer',
   'status': 'Completed', 'doneItems': 1, 'failedItems': 0}
GET  /api/jobs?handlerKey=agentprism.retention   → 0 satır (süzgeç gerçekten uygulanıyor)
```

## Denetim Bulguları

> `faz-denetim` bağımsız, taze bağlamlı bir agent olarak koştu.

Denetçi dört 🔴, altı 🟡 ve dört 🟢 bulgu üretti. **Dört 🔴'nün dördü de
kapandı**; altı 🟡'nin altısı da kapandı.

### 🔴 — hepsi kapandı

| # | Bulgu | Kapanış |
|---|---|---|
| 1 | **Aynı handler'ı iki kez kaydetmek host'u açtırmıyordu.** Kayıt `AddSingleton(new JobHandlerRegistration(...))` ile yazılıyor (`TryAdd` değil, K-251 gereği koleksiyona bakılamaz), yani `AddJobHandler<T>(key)`'i veya `AddAgentPrism()`'i iki kez çağırmak iki özdeş kayıt üretiyor ve registry **aynı tip için bile** çakışma atıyordu. `AddAgentPrism`'in kendi dokümanı `TryAdd` sözü veriyor. Bunu iddia eden test registry'yi hiç kurmuyordu — tiyatro | `JobHandlerRegistry.Create` artık **aynı tip + aynı anahtar** için no-op yapar; yalnız İKİ FARKLI tip çakışma sayılır. `JobHandlerRegistryTests` iki vakayı da kilitler (`AddAgentPrism()` iki kez → dokuz anahtar, hâlâ dokuz). **Mutasyonla kanıtlandı:** no-op dalı silinince iki test kırmızıya döndü |
| 2 | **Execution başına scope kanıtsızdı.** Planın `JobHandlerScopeTests`'i yazılmamıştı ve `TestJobHandlerHost.For(...)` handler'ı **instance** olarak kaydettiği için farkı ölçmesi yapısal olarak imkânsızdı; `ForType`/`With` yardımcıları ölü koddu | `JobHandlerScopeTests` (3 case) handler'ı **tip** olarak kaydeder ve iki iş + bir retry için farklı instance ve farklı scoped bağımlılık ölçer. **Mutasyonla kanıtlandı:** scope tek sefer açılacak biçimde değiştirilince iki test kırmızıya döndü |
| 3 | **Migration eşlemesi kanıtsızdı.** Üç `.sql` dosyasında dokuz satırlık `CASE` tablosu vardı ama hiçbir test veriye bakmıyordu; 7↔8 kayması (K-583'ün ayrı tuttuğu iki işlem) hiçbir kapıya takılmazdı | `JobHandlerKeyMigrationTests` **üç sağlayıcıda** ayrı ayrı: eski şemaya kadar migration'lar uygulanır, dokuz `kind` değeri için satır yazılır, sonra faz migration'ı koşar ve dokuz eşlemenin dokuzu ile satır sayıları doğrulanır. SQLite testi ayrıca `job_items`'ın **kaybolmadığını** ölçer (K-666'nın cascade tehlikesi). **Mutasyonla kanıtlandı:** PostgreSQL eşlemesinde 7↔8 çevrilince test kırmızıya döndü |
| 4 | **HTTP izin listesi kanıtsızdı — ve bu bir güvenlik sınırı.** `IsSchedulableOverHttp` gövdesi `return true` yapılsa test seti yeşil kalıyordu | `SchedulableHandlerKeyTests` (9 case): izinsiz anahtar `400` + zamanlama oluşmaz, yerleşik varsayılan kabul edilir, izin listesine eklenen tüketici anahtarı kabul edilir, dolu liste yerleşikleri **kapatır**, liste ucu **Admin** ister ve `PUT`'un kabul ettiğiyle **aynı** kümeyi döner, duplicate anahtar host'u açtırmaz. **Mutasyonla kanıtlandı:** guard her zaman `true` dönecek biçimde değiştirilince iki test kırmızıya döndü |

### 🟡 — hepsi kapandı

| # | Bulgu | Kapanış |
|---|---|---|
| 5 | Kayıtsız anahtar yolunda `agentprism.job.handler_key` etiketi **ham anahtarı** yazıyordu — sınırsız kardinalite, `lane`'in `MaxJobLaneCardinality` tavanının karşılığı yok | O yolda etiket sabit `"unregistered"`. Kayıtlı anahtar kümesi host başlangıcında sabitlendiği için diğer yolların tavana ihtiyacı yok; gerekçe kodda yazılı |
| 6 | `HttpSchedulableHandlerKeys` yerleşikleri **değiştiriyor**, doküman eklemeli okutuyordu — operatör tek anahtar eklerken dokuz yerleşiği sessizce kapatabilirdi | Kullanıcı kararı: **değiştirme anlamı korundu** (bir izin listesi yüzeyi daraltabilmelidir). XML dokümanı, `write-your-own-job-handler`, `background-work` ve `reference/configuration` bunu açıkça yazar ve "yerleşikleri de koru" reçetesini gösterir. `A_non_empty_list_REPLACES_the_built_in_default` davranışı kilitler |
| 7 | **Handler kurucusu çözülemezse iş sonsuza kadar yeniden lease ediliyordu.** Çözüm host başlangıcından execution'a taşınmıştı; `GetRequiredService` atarsa istisna `MarkRunningAsync`'ten önce kaçıyor, dış `catch` onu Warning olarak yutuyor ve attempt sınırı hiç uygulanmıyordu | Çözüm çağrısı `try/catch` içine alındı; iş yeni kararlı `JobErrorCodes.HandlerActivationFailed` koduyla `Failed` kapanır (yapılandırma hatası, retry düzeltemez). `JobHandlerScopeTests` bunu bağımlılığı kayıtsız bırakarak ölçer |
| 8 | `JobQuery.HandlerKey = ""` bellek ve SQL depolarında **farklı** sonuç veriyordu; sözleşme `HandlerKey`'i hiç denemiyordu | Bellek içi süzgeç `Lane`/`TenantId` ile aynı (`is { }`) hâle getirildi. `JobStoreContract`'a üç case eklendi (anahtarla süzme · eşleşmeyen anahtar · boş dizge ile `null` ayrımı) ve **dört uygulamada** da yeşil |
| 9 | Plandan sapmalar yazılmamıştı | "Plandan Sapmalar" bölümü beş sapmayı ve üç plan dışı kusuru gerekçesiyle taşır |
| 10 | Sevk edilen pakette bozuk XML cümlesi (`"whatever async setup its it needs"`) | Düzeltildi |

### 🟢 — aday kanalına

Dördü de kapsam dışı bırakıldı; ikisi bu fazda kendiliğinden kapandı
(`TestJobHandlerHost.ForType`/`.With` artık `JobHandlerScopeTests` tarafından
kullanılıyor; `JobStoreContract`'ın `HandlerKey` boşluğu 🟡 #8 ile kapandı).
Kalan ikisi (`handler-keys` adlı bir zamanlamanın literal segmentle çakışması ·
arayüzdeki `Select`'te `required` olmaması) gerçek bir kusur üretmiyor.

### Denetçinin doğruladığı, iddiaya güvenmediği kalemler

İmza-gövde kayması **yok** (sekiz üretim noktasının sekizi de anahtarı yazıyor;
`EXCLUDED.`/`excluded.` upsert listeleri ve reader ORDINAL'leri tutarlı) ·
`ErrorMessage` ham anahtarı kalıcılaştırmıyor · fazın 🔴 DoD kalemi (paketlenmiş
örnek) tiyatro değil · `Lane`/`TargetName`/at-least-once bozulmamış · `kind`
sütunu hiçbir sağlayıcıda index/CHECK/trigger'a bağlı değil (iki `DROP COLUMN`
bu yüzden güvenli) · anahtar deseni gerçekten 1–128 karakter ve `nvarchar(200)`
sütununa sığıyor.

## Sonraki Faza Devir Notu

- **Sıradaki faz: [Faz 138 — Ses Tanımının Sağlayıcı Üstverisi](138-SES-TANIMININ-SAGLAYICI-USTVERISI.md).**
  Bu fazla kesişimi yoktur; 138'in kendi "Bu Faza Başlarken" listesi yeterlidir.
- **🚨 `TryAddEnumerable` + "listede ara" deseni bir genişleme noktası DEĞİLDİR.**
  Bu faz o desenin bir örneğini kapattı ama sınıfını taramadı. `TryAddEnumerable`
  yalnız AYNI tipin iki kez eklenmesini engeller; FARKLI tiplerin aynı ayırt
  ediciyi (kind, name, scheme) paylaşmasını engellemez ve seçimi yapan
  `FirstOrDefault`/`LastOrDefault` kayıt sırasına bağlıdır. Tarama komutu
  `docs/hafiza/aspnetcore-di.md` içinde yazılı; bulunan her yer ayrı bir aday olur.
- **`IJobStore.EnqueueAsync` hâlâ public ve hâlâ kayıtsız anahtar kabul ediyor.**
  `IJobDispatcher` doğru yüzeydir ama store yüzeyi bilerek daraltılmadı (plan da
  daraltmıyordu) — bir tüketici store'a doğrudan yazarsa doğrulama atlanır ve iş
  yalnız worker'da `UnknownHandlerKey` ile kapanır. Fail-closed'dır, ama geç.
  Store'u daraltmak kırıcı bir değişikliktir ve ayrı bir karar ister.
- **`HttpSchedulableHandlerKeys` bir dizgedir, kayıt değildir.** Listeye kayıtlı
  OLMAYAN bir anahtar yazılabilir; zamanlama oluşur ve işi worker'da
  `UnknownHandlerKey` ile düşer. Kayıt kontrolünü de HTTP'ye taşımak
  `JobHandlerRegistry`'yi `AgentPrism.AspNetCore`'a açmayı gerektirir
  (`InternalsVisibleTo` zaten var) — yapılmadı çünkü izin listesi bir GÜVENLİK
  kapısıdır, bir varlık kapısı değil; ikisini birleştirmek yanlış hatayı verir.
- **Ölçüm için:** `AgentPrismSchedulingOptions.LaneByHandlerKey` artık dizge
  anahtarlıdır ve doğrulayıcı anahtarın biçimini de denetler. `LaneByKind`
  kullanan bir tüketici yapılandırması **derlenmez** — bu bilinçlidir ve upgrade
  adımı olarak tüketici yanıtına yazılmalıdır.
