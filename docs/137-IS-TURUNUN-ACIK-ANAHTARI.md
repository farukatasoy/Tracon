# Faz 137 — İş Türünün Açık Anahtarı

> **Durum:** 📋 Planlandı (2026-09-03)
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
| [`JobKind.cs`](../src/AgentPrism.Abstractions/Scheduling/JobKind.cs) | Kapalı enum, dokuz değer. XML dokümanı sıra değişmezliğini `smallint` sütununa bağlıyor |
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

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur. Sıradaki faz: [Faz 138](138-SES-TANIMININ-SAGLAYICI-USTVERISI.md).
