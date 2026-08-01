# Faz 2 — PostgreSQL Kalıcılık Katmanı

> **Durum:** Sıradaki faz
> **Önkoşul:** [01-CEKIRDEK-SOYUTLAMALAR.md](01-CEKIRDEK-SOYUTLAMALAR.md) — tamamlandı
> **Sonraki:** [03-SAGLAYICI-VE-DERLEYICI.md](03-SAGLAYICI-VE-DERLEYICI.md)
> **Paket:** `AgentPrism.PostgreSql` (iskeleti Faz 0'da kuruldu, içi boş)

---

## Bu Faza Başlarken

Önce şunları okuyun:

1. [`MIMARI.md`](MIMARI.md) — özellikle bölüm 4 (MAF genişleme noktaları) ve bölüm 5 (veri modeli)
2. [`KARARLAR.md`](KARARLAR.md) — kapatılmış tartışmaları yeniden açmayın
3. [`../MEMORY.md`](../MEMORY.md) — önceki oturumların keşfettiği tuzaklar
4. Bu doküman

Skill'ler: `.agents/skills/maf-api-kesfi/` (MAF imzalarını doğrulama), `.agents/skills/faz-tamamlama/` (faz sonu protokolü).

---

## Amaç

Tüm durumu PostgreSQL'e taşımak. Uygulama yeniden başladığında oturumlar, agent tanımları ve çalıştırma geçmişi yerinde durmalıdır.

---

## Devraldığınız Sözleşmeler

Bu arayüzler Faz 1'de **tamamlandı** ve `AgentPrism.Abstractions` içinde yaşıyor. Faz 2 bunların PostgreSQL uygulamalarını yazar. İmzalar birebir budur:

```csharp
public interface IAgentDefinitionStore
{
    ValueTask<AgentDefinition?> GetAsync(string name, CancellationToken ct = default);
    ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken ct = default);
    ValueTask<AgentDefinition> SaveAsync(AgentDefinition definition, CancellationToken ct = default);
    ValueTask<bool> DeleteAsync(string name, CancellationToken ct = default);
    ValueTask<IReadOnlyList<AgentDefinition>> ListVersionsAsync(string name, CancellationToken ct = default);
    ValueTask<AgentDefinition> RollbackAsync(string name, int version, CancellationToken ct = default);
}

public interface IRunStore
{
    ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken ct = default);
    ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken ct = default);   // sıra numarası ÇAĞIRAN tarafından atanır
    ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken ct = default);
    ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken ct = default);
    IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken ct = default);
}

public interface ISessionStore
{
    ValueTask SaveAsync(SessionRecord record, CancellationToken ct = default);
    ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken ct = default);
    ValueTask<bool> DeleteAsync(string sessionId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(SessionQuery query, CancellationToken ct = default);
}
```

Davranış sözleşmeleri (`InMemory*` uygulamaları ve testleri referanstır):

| Kural | Nerede doğrulanıyor |
|-------|--------------------|
| `SaveAsync` sürümü artırır, `Origin`'i `Database` yapar, `UpdatedAt` yazar | `InMemoryAgentDefinitionStoreTests` |
| `ListVersionsAsync` yeniden eskiye sıralar | aynı |
| `RollbackAsync` eski sürümü **silmez**, içeriğini yeni sürüm olarak kaydeder | aynı |
| `QueryRunsAsync` en yeniden eskiye sıralar | `InMemoryRunStoreTests` |
| `ReadEventsAsync(fromSequence)` o numaradan itibaren döner (dahil) | aynı |
| Olmayan çalıştırmaya olay eklemek `AgentPrismException` atar | aynı |

> Faz 2'nin entegrasyon testleri, `InMemory*` testleriyle **aynı senaryoları** çalıştırmalıdır. İki uygulama arasındaki davranış farkı hatadır.

---

## 🚨 Kritik Tuzak: `TryAdd` Sırası

`AddAgentPrism()` bellek içi depoları **`TryAddSingleton` ile** kaydeder:

```csharp
// AgentPrismServiceCollectionExtensions.cs:76-78
services.TryAddSingleton<IAgentDefinitionStore, InMemoryAgentDefinitionStore>();
services.TryAddSingleton<IRunStore, InMemoryRunStore>();
services.TryAddSingleton<ISessionStore, InMemorySessionStore>();
```

`UsePostgreSql()` zincirde **sonra** çalışır:

```csharp
builder.AddAgentPrism()        // ← TryAdd burada çalıştı, InMemory kazandı
       .UsePostgreSql(...)     // ← burada TryAdd YAZARSANIZ HİÇBİR ŞEY OLMAZ
```

**Doğru yol:** `UsePostgreSql()` içinde `Replace` kullanın.

```csharp
using Microsoft.Extensions.DependencyInjection.Extensions;

builder.Services.Replace(ServiceDescriptor.Singleton<IAgentDefinitionStore, PostgresAgentDefinitionStore>());
builder.Services.Replace(ServiceDescriptor.Singleton<IRunStore, PostgresRunStore>());
builder.Services.Replace(ServiceDescriptor.Singleton<ISessionStore, PostgresSessionStore>());
```

`Replace` burada doğrudur çünkü `UsePostgreSql()` tüketicinin **açık** tercihidir — sessiz bir üzerine yazma değildir. K4 kuralı ("TryAdd ile kaydet") AgentPrism'in *varsayılanları* içindir, açık çağrılar için değil.

**Bu davranış bir testle korunmalıdır:** `UsePostgreSql()` sonrası `IRunStore` çözümlemesi `PostgresRunStore` dönmelidir.

---

## Tasarım Kararları

### Ayrı `agentprism` şeması

Tüketici uygulamanın `public` şemasına **hiç dokunulmaz**. Tablo adı çakışması, migration çakışması ve yanlışlıkla veri silme riski böylece ortadan kalkar.

### Elden yazılmış SQL, EF Core değil

Karar K-004. Gerekçe [`KARARLAR.md`](KARARLAR.md) bölüm 1'de; yeniden açmayın.

### `AgentPrismId.NewId()` kullanın

Birincil anahtarlar UUIDv7'dir. **`Guid.NewGuid()` kullanmayın** — rastgele UUID B-tree index'i parçalar.

```csharp
var id = AgentPrismId.NewId();                  // şimdi
var id = AgentPrismId.NewId(timestamp);         // belirli bir an
var ts = AgentPrismId.GetTimestamp(id);         // kimlikten zaman damgası
```

PostgreSQL tarafında `uuid` tipi kullanılır; `gen_random_uuid()` **kullanılmaz** (o v4 üretir).

### `run_events` append-only

Çalıştırma olayları hiç güncellenmez. `(run_id, seq)` birincil anahtardır. Sıra numarasını `RunEventWriter` üretir; depo yalnızca yazar.

### AOT: System.Text.Json kaynak üreteci zorunlu

`AgentPrism.PostgreSql` AOT uyumlu işaretlidir (`AgentPrismAotCompatible` varsayılanı `true`). `jsonb` alanlarını serileştirirken **`JsonSerializerContext` kaynak üreteci** kullanın; `JsonSerializer.Serialize(object)` aşırı yüklemeleri `IL2026` üretir ve build'i kırar.

```csharp
[JsonSerializable(typeof(AgentDefinition))]
[JsonSerializable(typeof(RunEvent))]
internal sealed partial class AgentPrismJsonContext : JsonSerializerContext;
```

---

## Şema

```sql
CREATE SCHEMA IF NOT EXISTS agentprism;
```

| Tablo | Anahtar alanlar | Not |
|-------|----------------|-----|
| `__migrations` | `id`, `name`, `checksum`, `applied_at` | Checksum uyuşmazlığı başlangıçta hata verir |
| `tenants` | `id`, `slug`, `display_name` | Tek kiracıda tek varsayılan satır |
| `agent_definitions` | `id`, `tenant_id`, `name`, `version`, `definition jsonb` | `(tenant_id, name)` benzersiz |
| `agent_definition_versions` | `id`, `agent_id`, `version`, `definition jsonb`, `created_by` | Değişmez geçmiş |
| `sessions` | `id`, `tenant_id`, `agent_name`, `state jsonb`, `schema_version`, `created_at`, `updated_at` | Serileştirilmiş `AgentSession` |
| `conversations` | `id`, `tenant_id`, `agent_name`, `metadata jsonb` | OpenAI uyumlu (Faz 4 doldurur) |
| `conversation_items` | `id`, `conversation_id`, `seq`, `item jsonb` | Sıralı mesajlar |
| `responses` | `id`, `conversation_id`, `session_id`, `payload jsonb` | Responses API (Faz 4) |
| `runs` | `id`, `tenant_id`, `agent_name`, `session_id`, `status`, `started_at`, `completed_at`, `is_streaming`, `input_tokens`, `output_tokens`, `total_tokens`, `event_count`, `error_type`, `error_message` | `RunRecord` ile birebir |
| `run_events` | `run_id`, `seq`, `type`, `text`, `tool_name`, `tool_call_id`, `payload`, `created_at` | Append-only, PK `(run_id, seq)` |
| `tool_invocations` | `id`, `run_id`, `tool_name`, `arguments jsonb`, `result jsonb`, `duration_ms`, `error` | |
| `traces`, `spans` | — | Şema burada kurulur, Faz 6'da doldurulur |
| `audit_log` | `id`, `tenant_id`, `actor`, `action`, `entity`, `before jsonb`, `after jsonb` | |

Kurallar:

- Zaman alanları `timestamptz`, her zaman UTC
- Serbest yapılı alanlar `jsonb`; sorgulanan yollarda GIN index
- Birincil anahtarlar `uuid` (v7, uygulama üretir)
- Her tabloda `tenant_id`; tek kiracıda `AgentPrismOptions.DefaultTenantId` (varsayılan `"default"`)
- `run_events` partition'a hazır (`created_at`); partition Faz 6'da açılır
- `RunStatus` ve `RunEventType` veritabanında **`smallint`** olarak saklanır (enum değerleri kararlıdır; bkz. `RunEventType` XML dokümanı)

---

## Migration Runner

```
src/AgentPrism.PostgreSql/Migrations/
├── 0001_initial.sql
└── 0002_....sql
```

Gömülü kaynak olarak paketlenir — `AgentPrism.PostgreSql.csproj` içinde `<EmbeddedResource Include="Migrations/**/*.sql" />` **zaten tanımlı** (Faz 0'da eklendi).

Çalışma akışı:

1. `pg_advisory_lock(<sabit anahtar>)` alınır — çoklu replika başlangıcında yarış koşulunu engeller
2. `agentprism.__migrations` tablosu yoksa oluşturulur
3. Uygulanmış her migration'ın checksum'ı doğrulanır; uyuşmazlık **hata** verir
4. Uygulanmamış migration'lar sıra ile, her biri kendi transaction'ında çalıştırılır
5. `pg_advisory_unlock`

`AgentPrismOptions` altına eklenecek:

```csharp
public sealed class AgentPrismPostgreSqlOptions
{
    public string? ConnectionString { get; set; }
    public string SchemaName { get; set; } = "agentprism";
    public bool AutoApplyMigrations { get; set; } = true;
    public int CommandTimeoutSeconds { get; set; } = 30;
}
```

> `AgentPrismOptionsValidator` elle yazılmıştır (AOT). Yeni ayar eklerken doğrulamayı oraya ekleyin; `[Required]` gibi attribute'lar **çalışmaz**.

---

## Store Implementasyonları

```csharp
PostgresAgentDefinitionStore     : IAgentDefinitionStore     // AgentPrism.Abstractions
PostgresRunStore                 : IRunStore                 // AgentPrism.Abstractions
PostgresSessionStore             : ISessionStore             // AgentPrism.Abstractions
PostgresChatHistoryProvider      : Microsoft.Agents.AI.ChatHistoryProvider
```

`AgentSessionStore` (MAF `Hosting` paketi, **ön sürüm**) uygulaması Faz 4'e bırakılır — K-008 gereği `AgentPrism.PostgreSql` ön sürüm paketlere bağlanmamalıdır. Aynı şekilde `IConversationStorage`, `IAgentConversationIndex`, `IResponsesService` de Faz 4'te `AgentPrism.AspNetCore` içinde uygulanır.

### `PostgresChatHistoryProvider` — kritik nokta

MAF dokümanının açık uyarısı:

> A `ChatHistoryProvider` instance is attached to an agent and the same instance would be used for all sessions. This means that the `ChatHistoryProvider` should not store any session specific state in the provider instance.

Bu yüzden veritabanı anahtarı `ProviderSessionState<T>` ile `AgentSession` içinde saklanır:

```csharp
private readonly ProviderSessionState<HistoryState> _sessionState = new(
    stateInitializer: _ => new HistoryState { HistoryId = AgentPrismId.NewId() },
    stateKey: nameof(PostgresChatHistoryProvider));
```

Provider örneği yalnızca `NpgsqlDataSource` referansını tutar.

---

## Bağlantı Yönetimi

Tek `NpgsqlDataSource`, DI'da singleton. Npgsql kendi havuzunu yönetir.

`NpgsqlDataSourceBuilder` ile kurulur; `EnableDynamicJson()` **kullanılmaz** (yansıma). `jsonb` alanları `string` olarak yazılıp okunur, serileştirme kaynak üreteci ile uygulama tarafında yapılır.

---

## Dosya Listesi

```
src/AgentPrism.PostgreSql/
├── AgentPrismPostgreSqlBuilderExtensions.cs   (UsePostgreSql — Replace kullanır)
├── AgentPrismPostgreSqlOptions.cs
├── AgentPrismJsonContext.cs                   (JsonSerializerContext)
├── Migrations/
│   ├── MigrationRunner.cs
│   ├── MigrationDescriptor.cs
│   ├── 0001_initial.sql                       (EmbeddedResource)
│   └── MigrationHostedService.cs              (AutoApplyMigrations için)
├── Stores/
│   ├── PostgresAgentDefinitionStore.cs
│   ├── PostgresRunStore.cs
│   ├── PostgresSessionStore.cs
│   └── PostgresChatHistoryProvider.cs
└── Internal/
    ├── NpgsqlDataSourceFactory.cs
    └── SqlQueries.cs

tests/AgentPrism.PostgreSql.IntegrationTests/    ← BU FAZDA OLUŞTURULUR
└── (Testcontainers.PostgreSql)
```

---

## Test Stratejisi

> **Bu faz `tests/AgentPrism.PostgreSql.IntegrationTests` projesini oluşturur** (`Testcontainers.PostgreSql` sürümü `Directory.Packages.props` içinde zaten sabitli). Faz 0 yalnız `AgentPrism.Core.UnitTests`'i kurdu; test projeleri test edecekleri şeyle birlikte gelir.

**Gerçek PostgreSQL**, Testcontainers ile. Sahte veritabanı kullanılmaz.

| Test | Neyi doğrular |
|------|---------------|
| `MigrationRunnerTests` | Idempotency; iki kez çalıştırma güvenli |
| `ConcurrentMigrationTests` | 5 eşzamanlı runner, tek uygulama |
| `ChecksumValidationTests` | Değiştirilmiş migration hata verir |
| `SessionStoreTests` | `SessionRecord` round-trip, `JsonElement` bütünlüğü |
| `AgentDefinitionStoreTests` | **`InMemoryAgentDefinitionStoreTests` ile aynı senaryolar** |
| `RunStoreTests` | **`InMemoryRunStoreTests` ile aynı senaryolar** |
| `SchemaIsolationTests` | `public` şeması değişmez |
| `TenantIsolationTests` | Kiracı A, kiracı B'nin verisini göremez |
| `ServiceRegistrationTests` | `UsePostgreSql()` sonrası `IRunStore` → `PostgresRunStore` |

> ⚠️ **Uzak paylaşılan PostgreSQL sunucusuna hiçbir test bağlanmaz.** Testcontainers her çalıştırmada yerel, tek kullanımlık bir container ayağa kaldırır. `AgentPrism:ConnectionString` yalnız örnek uygulamanın elle çalıştırılması içindir.

---

## Bitiş Ölçütleri (DoD)

- [ ] `UsePostgreSql(connectionString)` zincire eklenir ve `Replace` ile depoları değiştirir
- [ ] Uygulama başlar, migration'lar uygulanır, `agentprism` şeması oluşur
- [ ] Örnek API yeniden başlatılır — agent tanımı ve çalıştırma geçmişi yerinde durur
- [ ] `public` şemasının değişmediği doğrulanır
- [ ] 5 eşzamanlı başlangıçta migration tek kez uygulanır
- [ ] `InMemory*` ile `Postgres*` aynı davranış testlerini geçer
- [ ] `dotnet build -c Release` — 0 uyarı (AOT dahil)
- [ ] `dotnet test` — mevcut 42 test + yeni entegrasyon testleri

Manuel doğrulama:

```bash
cd samples/AgentPrism.Api
dotnet run                      # user-secrets'taki ConnectionString kullanılır
curl -X POST localhost:5081/agents/support/run -H 'Content-Type: application/json' -d '{"message":"test"}'
# uygulamayı durdur, yeniden başlat
curl localhost:5081/runs        # önceki çalıştırma hâlâ orada olmalı
psql "$CONN" -c "\dt agentprism.*"
psql "$CONN" -c "\dt public.*"  # boş veya değişmemiş olmalı
```

---

## Faz 1'den Devreden Açık İşler

Bu faz şunları da kapatmalıdır:

1. **`RunRecordingAgent.GetSessionId`** yer tutucu bir değer üretiyor (`session.GetType().Name`). Gerçek oturum kimliği `ISessionStore` ile bağlanmalı.
2. **`ISessionStore` hiç kullanılmıyor.** Oturum kalıcılığı bu fazda devreye girer.
3. **`SessionRecord.State`** `JsonElement` tipinde — `AIAgent.SerializeSessionAsync` çıktısı. Opak kabul edilir, yorumlanmaz.

---

## Riskler

| Risk | Önlem |
|------|-------|
| `AgentSession` serileştirme formatı MAF sürümleri arasında değişebilir | `sessions.schema_version` sütunu; uyumsuz sürüm okunduğunda anlaşılır hata |
| Uzun süren migration üretimde başlangıcı kilitler | Migration'lar küçük tutulur; `AutoApplyMigrations=false` üretim seçeneği |
| `Replace` yerine `TryAdd` yazılması | Sessizce bellek içi depoda kalınır — `ServiceRegistrationTests` bunu yakalar |
| `jsonb` serileştirmede yansıma kullanılması | AOT analyzer build'i kırar; kaynak üreteci zorunlu |
