# Faz 2 — PostgreSQL Kalıcılık Katmanı

> **Durum:** Planlandı
> **Önkoşul:** [01-CEKIRDEK-SOYUTLAMALAR.md](01-CEKIRDEK-SOYUTLAMALAR.md)
> **Sonraki:** [03-SAGLAYICI-VE-DERLEYICI.md](03-SAGLAYICI-VE-DERLEYICI.md)
> **Paket:** `AgentPrism.PostgreSql`

---

## Amaç

Tüm durumu PostgreSQL'e taşımak. Uygulama yeniden başladığında oturumlar, agent tanımları ve çalıştırma geçmişi yerinde durmalıdır.

---

## Tasarım Kararları

### Ayrı `agentprism` şeması

Tüketici uygulamanın `public` şemasına **hiç dokunulmaz**. Tablo adı çakışması, migration çakışması ve yanlışlıkla veri silme riski böylece ortadan kalkar.

### Elden yazılmış SQL, EF Core değil

Bir kütüphane EF Core'a bağımlılık dayatırsa tüketiciye sürüm kısıtı getirir. EF Core sürüm çakışmaları .NET ekosisteminde en sık görülen bağımlılık sorunlarından biridir. Ayrıca EF Core migration'larını tüketicinin projesinden çalıştırmak gerekir — kütüphane kendi şemasını kendi yönetemez.

Ham `Npgsql` + gömülü `.sql` dosyaları bu iki sorunu da ortadan kaldırır. Maliyet: LINQ yok, elle SQL yazılır. Kabul edilir — sorgu sayısı sınırlı ve şema bizim kontrolümüzde.

### `uuid` v7 birincil anahtar

Zaman sıralı UUID. Rastgele UUID'nin B-tree index parçalanması sorununu ortadan kaldırır, `bigserial`'ın merkezî sıra darboğazını getirmez.

### `run_events` append-only

Çalıştırma olayları hiç güncellenmez, yalnızca eklenir. `(run_id, seq)` birincil anahtardır. Arayüz aynı veriyi hem canlı hem geçmişe dönük okur.

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
| `sessions` | `id`, `tenant_id`, `agent_name`, `state jsonb`, `updated_at` | Serileştirilmiş `AgentSession` |
| `conversations` | `id`, `tenant_id`, `agent_name`, `metadata jsonb` | OpenAI uyumlu |
| `conversation_items` | `id`, `conversation_id`, `seq`, `item jsonb` | Sıralı mesajlar |
| `responses` | `id`, `conversation_id`, `session_id`, `payload jsonb` | Responses API kayıtları |
| `runs` | `id`, `tenant_id`, `agent_name`, `session_id`, `status`, `started_at`, `completed_at`, `input_tokens`, `output_tokens`, `error jsonb` | Çalıştırma özeti |
| `run_events` | `run_id`, `seq`, `type`, `payload jsonb`, `created_at` | Append-only |
| `tool_invocations` | `id`, `run_id`, `tool_name`, `arguments jsonb`, `result jsonb`, `duration_ms`, `error` | |
| `traces`, `spans` | — | Şema Faz 2'de kurulur, Faz 6'da doldurulur |
| `audit_log` | `id`, `tenant_id`, `actor`, `action`, `entity`, `before jsonb`, `after jsonb` | |

Kurallar:

- Zaman alanları `timestamptz`, her zaman UTC
- Serbest yapılı alanlar `jsonb`; sorgulanan yollarda GIN index
- Her tabloda `tenant_id`; tek kiracıda sabit varsayılan
- `run_events` partition'a hazır (`created_at`); partition Faz 6'da açılır

---

## Migration Runner

```
Migrations/
├── 0001_initial.sql
├── 0002_...
```

Gömülü kaynak olarak paketlenir (`EmbeddedResource`, `AgentPrism.PostgreSql.csproj` içinde zaten tanımlı).

Çalışma akışı:

1. `pg_advisory_lock(<sabit anahtar>)` alınır — çoklu replika başlangıcında yarış koşulunu engeller
2. `__migrations` tablosu yoksa oluşturulur
3. Uygulanmış her migration'ın checksum'ı doğrulanır; uyuşmazlık **hata** verir
4. Uygulanmamış migration'lar sıra ile, her biri kendi transaction'ında çalıştırılır
5. Kilit bırakılır

`AgentPrismOptions.Migrations.AutoApply` bayrağı ile kontrol edilir. Üretimde bilinçli bir karardır; `false` yapılırsa `AgentPrism.PostgreSql` bir CLI yolu sunar (Faz 7).

---

## Store Implementasyonları

```csharp
PostgresAgentSessionStore        : Microsoft.Agents.AI.Hosting.AgentSessionStore
PostgresChatHistoryProvider      : Microsoft.Agents.AI.ChatHistoryProvider
PostgresAgentDefinitionStore     : IAgentDefinitionStore
PostgresRunStore                 : IRunStore
```

`IConversationStorage`, `IAgentConversationIndex`, `IResponsesService` implementasyonları Faz 4'te eklenir — bu arayüzler `Microsoft.Agents.AI.Hosting.OpenAI` paketinden gelir ve o paket Faz 4'e kadar referans edilmez.

### `PostgresChatHistoryProvider` — kritik nokta

MAF dokümanının açık uyarısı:

> A `ChatHistoryProvider` instance is attached to an agent and the same instance would be used for all sessions. This means that the `ChatHistoryProvider` should not store any session specific state in the provider instance.

Bu yüzden veritabanı anahtarı `ProviderSessionState<T>` ile `AgentSession` içinde saklanır:

```csharp
private readonly ProviderSessionState<HistoryState> _sessionState = new(
    stateInitializer: _ => new HistoryState { HistoryId = Guid.CreateVersion7() },
    stateKey: nameof(PostgresChatHistoryProvider));
```

Provider örneği yalnızca `NpgsqlDataSource` referansını tutar.

---

## Bağlantı Yönetimi

Tek `NpgsqlDataSource`, DI'da singleton. Npgsql kendi havuzunu yönetir.

`AgentPrismOptions.PostgreSql` altında: komut zaman aşımı, yeniden deneme sayısı, havuz sınırları.

---

## Test Stratejisi

> **Bu faz `tests/AgentPrism.PostgreSql.IntegrationTests` projesini oluşturur** (`Testcontainers.PostgreSql` ile). Faz 0 yalnız `AgentPrism.Core.UnitTests`'i kurdu; test projeleri test edecekleri şeyle birlikte gelir.

`tests/AgentPrism.PostgreSql.IntegrationTests` — **gerçek PostgreSQL**, Testcontainers ile. Sahte veritabanı kullanılmaz.

| Test | Neyi doğrular |
|------|---------------|
| `MigrationRunnerTests` | Idempotency; iki kez çalıştırma güvenli |
| `ConcurrentMigrationTests` | 5 eşzamanlı runner, tek uygulama |
| `ChecksumValidationTests` | Değiştirilmiş migration hata verir |
| `SessionStoreTests` | `AgentSession` round-trip, serileştirme bütünlüğü |
| `AgentDefinitionStoreTests` | CRUD, versiyonlama, geri alma |
| `RunStoreTests` | Olay sırası, `fromSequence` ile replay |
| `SchemaIsolationTests` | `public` şeması değişmez |
| `TenantIsolationTests` | Kiracı A, kiracı B'nin verisini göremez |

> Uzak paylaşılan PostgreSQL sunucusuna **hiçbir test bağlanmaz**. Testcontainers her çalıştırmada yerel, tek kullanımlık bir container ayağa kaldırır.

---

## Bitiş Ölçütleri (DoD)

- [ ] `UsePostgreSql(connectionString)` zincire eklenir
- [ ] Uygulama başlar, migration'lar uygulanır, `agentprism` şeması oluşur
- [ ] Uygulama yeniden başlatılır — oturum kaldığı yerden devam eder
- [ ] `public` şemasının değişmediği doğrulanır
- [ ] Tüm entegrasyon testleri Testcontainers ile geçer
- [ ] 5 eşzamanlı başlangıçta migration tek kez uygulanır

---

## Riskler

| Risk | Önlem |
|------|-------|
| `AgentSession` serileştirme formatı MAF sürümleri arasında değişebilir | `sessions` tablosunda `schema_version` sütunu; uyumsuz sürüm okunduğunda anlaşılır hata |
| Uzun süren migration üretimde başlangıcı kilitler | Migration'lar küçük tutulur; `AutoApply=false` üretim seçeneği |
| `jsonb` alanlarda şema kayması | Yazma yolunda System.Text.Json source generator; okuma yolunda tolerant deserialization |
