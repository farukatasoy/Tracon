# Faz 2 — PostgreSQL Kalıcılık Katmanı

> **Durum:** ✅ Tamamlandı (2026-08-02)
> **Önkoşul:** [01-CEKIRDEK-SOYUTLAMALAR.md](01-CEKIRDEK-SOYUTLAMALAR.md) — tamamlandı
> **Sonraki:** [03-SAGLAYICI-VE-DERLEYICI.md](03-SAGLAYICI-VE-DERLEYICI.md)
> **Paketler:** `AgentPrism.PostgreSql` (dolduruldu), `AgentPrism.Core` (oturum yönetimi eklendi)

---

## Amaç

Tüm durumu PostgreSQL'e taşımak. Uygulama yeniden başladığında oturumlar, agent tanımları ve çalıştırma geçmişi yerinde durmalıdır.

**Sonuç:** hedefe ulaşıldı. Örnek API durdurulup yeniden başlatıldığında çalıştırma geçmişi, oturum ve sohbet geçmişi yerinde kaldı (aşağıdaki DoD tablosunda gerçek çıktı var).

---

## Gerçekleşen Public API

Plandaki taslak değil, koddaki gerçek imzalar.

### `AgentPrism.PostgreSql`

```csharp
// AgentPrismPostgreSqlBuilderExtensions — üç aşırı yükleme
public static IAgentPrismBuilder UsePostgreSql(this IAgentPrismBuilder builder, string connectionString);
public static IAgentPrismBuilder UsePostgreSql(this IAgentPrismBuilder builder, IConfiguration configurationSection);
public static IAgentPrismBuilder UsePostgreSql(this IAgentPrismBuilder builder, Action<AgentPrismPostgreSqlOptions> configure);

public sealed class AgentPrismPostgreSqlOptions
{
    public const string SectionName = "AgentPrism:PostgreSql";   // ← plandaki "AgentPrism" kökü değil
    public string? ConnectionString { get; set; }
    public string SchemaName { get; set; } = "agentprism";
    public bool AutoApplyMigrations { get; set; } = true;
    public int CommandTimeoutSeconds { get; set; } = 30;
}

public sealed class AgentPrismPostgreSqlOptionsValidator : IValidateOptions<AgentPrismPostgreSqlOptions>;

public sealed class MigrationRunner
{
    public MigrationRunner(NpgsqlDataSource dataSource, IOptions<AgentPrismPostgreSqlOptions> options, ILogger<MigrationRunner> logger);
    public ValueTask<int> ApplyAsync(CancellationToken cancellationToken = default);   // uygulanan migration sayısı
}

public sealed class MigrationHostedService : IHostedService;

public sealed class PostgresAgentDefinitionStore : IAgentDefinitionStore;
public sealed class PostgresRunStore            : IRunStore;
public sealed class PostgresSessionStore        : ISessionStore { public const int CurrentSchemaVersion = 1; }
public sealed class PostgresChatHistoryProvider : Microsoft.Agents.AI.ChatHistoryProvider
{
    public const string SessionStateKey = "AgentPrism.ChatHistory";
    public override IReadOnlyList<string> StateKeys { get; }
}
```

Her deponun kurucusu aynıdır: `(NpgsqlDataSource, IOptions<AgentPrismPostgreSqlOptions>, ITenantContext)`.

### `AgentPrism.Core` — bu fazda eklenenler

```csharp
// Oturum yaşam döngüsü. Sağlayıcıdan bağımsızdır; bellek içi depoyla da çalışır.
public sealed class AgentSessionManager
{
    public AgentSessionManager(ISessionStore store, ITenantContext tenantContext, TimeProvider? timeProvider = null);

    public ValueTask<AgentSession> GetOrCreateSessionAsync(AIAgent agent, string sessionId, CancellationToken ct = default);
    public ValueTask<string>       SaveSessionAsync(AIAgent agent, AgentSession session, CancellationToken ct = default);
    public ValueTask<bool>         DeleteSessionAsync(string sessionId, CancellationToken ct = default);
    public ValueTask<IReadOnlyList<SessionRecord>> QuerySessionsAsync(SessionQuery query, CancellationToken ct = default);
}

// Oturum kimliğini AgentSession.StateBag içine damgalar.
public static class AgentSessionIdentity
{
    public const string StateKey = "AgentPrism.SessionId";
    public static void    SetId(AgentSession session, string sessionId);
    public static string? GetId(AgentSession session);
}
```

**Değişen imza:** `AgentDefinitionCompiler` kurucusuna beşinci, isteğe bağlı parametre eklendi:

```csharp
public AgentDefinitionCompiler(
    IModelProviderRegistry models,
    IToolRegistry tools,
    ILoggerFactory? loggerFactory = null,
    IServiceProvider? services = null,
    ChatHistoryProvider? chatHistoryProvider = null);   // ← YENİ
```

Derleyici bu sağlayıcıyı ürettiği her agent'ın `ChatClientAgentOptions.ChatHistoryProvider` / `HarnessAgentOptions.ChatHistoryProvider` alanına koyar. DI'da kayıtlı değilse `null` geçer ve MAF'ın bellek içi varsayılanı kullanılır.

---

## Plandan Sapmalar

Sapmalar gizlenmez; gerekçesi en değerli bilgidir.

| # | Plan ne diyordu | Ne yapıldı | Gerekçe |
|---|-----------------|------------|---------|
| 1 | Ayarlar `AgentPrismOptions` altına eklenecek; örnek `appsettings.json` `AgentPrism:ConnectionString` gösteriyordu | Ayrı `AgentPrism:PostgreSql` bölümü *(kullanıcı kararı)* | Faz 3'teki `AgentPrism:Providers:OpenAI` ile aynı desen; ileride `AgentPrism.SqlServer` eklenirse ad çakışması olmaz. `SchemaName` ve `CommandTimeoutSeconds` için kökte yer yoktu. |
| 2 | Oturum kalıcılığının nasıl bağlanacağı yazılmamıştı; MAF'ın `AgentSessionStore` sınıfı K-008 gereği Faz 4'e bırakılmıştı | `AgentPrism.Core` içine `AgentSessionManager` eklendi *(kullanıcı kararı)* | `ISessionStore`'un çağıranı olmadan faz DoD'u kapanmazdı. Yönetici sağlayıcıdan bağımsızdır; Faz 4'teki `AgentSessionStore` uygulaması buna delege eder. Karar K-026. |
| 3 | `PostgresChatHistoryProvider` dosya listesindeydi ama bağlanacağı yer yazılmamıştı | Derleyici DI'daki `ChatHistoryProvider`'ı her agent'a otomatik bağlar *(kullanıcı kararı)* | Bağlanmayan bir sağlayıcı ölü koddur. Sohbet geçmişi `conversation_items` tablosunda yaşar; oturum satırı küçük kalır ve geçmiş SQL ile sorgulanabilir. |
| 4 | "Serbest yapılı alanlar `jsonb`" | `sessions.state` ve `conversation_items.item` **`json`** oldu | 🚨 PostgreSQL `jsonb` nesne anahtarlarını yeniden sıralar. System.Text.Json'ın polimorfik `$type` ayracı ilk özellik olmak zorundadır; `jsonb` bunu bozar ve okuma `JsonException` verir. Ölçüldü: `Sohbet_gecmisi_oturumlar_arasi_surer` testi bu yüzden kırıldı. Karar K-027. |
| 5 | `run_events.payload` için tip belirtilmemişti | `text` | `RunEventWriter` tool argümanlarını AOT uyumlu kalmak için elle biçimlendirir (`key=value`); çıktı geçerli JSON değildir. `jsonb` sütunu çalıştırmayı kesen bir hata üretirdi. |
| 6 | `tenants` tablosu tanımlıydı, ilişkisi yazılmamıştı | `tenant_id` her tabloda `text`; `tenants` tablosuna **yabancı anahtar yok** | Kısıtı şimdiden koymak, kaydı olmayan bir kiracı için çalışma anında beklenmedik hata üretirdi. Varsayılan kiracı satırı `MigrationHostedService` tarafından açılışta eklenir. **Güncelleme (Faz 6):** kiracı yönetimi geldi ancak kısıt **eklenmedi** — kiracı kaydı bilerek isteğe bağlıdır; gerekçe `KARARLAR.md`. |
| 7 | Entegrasyon testleri "`InMemory*` ile aynı senaryolar" diyordu | Senaryolar **tek bir soyut sınıfta** yazıldı, iki uygulamada da koşuyor | Kopyalanan test, kopyalandığı anda birbirinden ayrılmaya başlar. `AgentDefinitionStoreContract` / `RunStoreContract` / `SessionStoreContract` her iki uygulamaya da uygulanır. |
| 8 | `SqlQueries.cs` tek dosya olarak öngörülmüştü | `SqlQueries` + `SqlIdentifier` + `NpgsqlHelpers` | Şema adı yapılandırmadan gelir ve SQL metnine doğrudan girer (tanımlayıcılar parametre olamaz). `SqlIdentifier` bunu katı biçimde doğrular — enjeksiyon yüzeyi kapanır. `NpgsqlHelpers`, `ConfigureAwait(false)` gerektiren `await using` kalıbını tek yerde toplar. |
| 9 | `InMemorySessionStore` değişmeyecekti | `SaveAsync` artık `CreatedAt` değerini korur | İki uygulama arasında davranış farkı hatadır. `PostgresSessionStore` upsert'i `created_at` sütununa dokunmaz; bellek içi depo da aynı davranışı göstermelidir. Sözleşme testi bunu zorlar. |

---

## Şema

`agentprism` şeması (karar K-013). Tüketicinin `public` şemasına **hiç dokunulmaz** — test ile doğrulandı.

| Tablo | Anahtar alanlar | Not |
|-------|----------------|-----|
| `__migrations` | `id`, `name`, `checksum`, `applied_at` | Runner oluşturur, `0001` değil |
| `tenants` | `id uuid`, `slug`, `display_name`, `created_at` | Varsayılan satır açılışta eklenir |
| `agent_definitions` | `id`, `tenant_id`, `name`, `version`, `definition jsonb` | `(tenant_id, name)` benzersiz + GIN index |
| `agent_definition_versions` | `id`, `agent_id`, `version`, `definition jsonb`, `created_by` | Değişmez geçmiş; `agent_id` FK cascade |
| `sessions` | `id text`, `tenant_id`, `agent_name`, `state **json**`, `schema_version` | `json` — bkz. sapma 4 |
| `conversations` | `id`, `tenant_id`, `agent_name`, `metadata jsonb` | Sohbet geçmişinin başlığı |
| `conversation_items` | `id`, `conversation_id`, `seq`, `item **json**` | `(conversation_id, seq)` benzersiz |
| `responses` | `id`, `conversation_id`, `session_id`, `payload jsonb` | Boş — Faz 4 doldurur |
| `runs` | `id`, `tenant_id`, `agent_name`, `session_id`, `status smallint`, token sütunları, `error_*` | `RunRecord` ile birebir |
| `run_events` | `run_id`, `seq`, `type smallint`, `text`, `tool_name`, `tool_call_id`, `payload **text**`, `created_at` | PK `(run_id, seq)`; `run_id` FK cascade |
| `tool_invocations` | `id`, `run_id`, `tool_name`, `arguments jsonb`, `result jsonb`, `duration_ms` | Boş — Faz 6 doldurur |
| `traces`, `spans` | — | Boş — Faz 6 doldurur |
| `audit_log` | `id`, `tenant_id`, `actor`, `action`, `entity`, `before/after jsonb` | Boş — Faz 4/6 doldurur |

Toplam 13 tablo + `__migrations` = 14.

`{schema}` yer tutucusu çalışma anında değiştirilir; ad `SqlIdentifier.RequireSchemaName` ile doğrulanır (küçük harf/rakam/alt çizgi, en çok 63 karakter, `public` yasak).

---

## Migration Runner

```
src/AgentPrism.PostgreSql/Migrations/
├── MigrationDescriptor.cs      (gömülü kaynak keşfi + SHA-256 checksum)
├── MigrationRunner.cs          (advisory lock + checksum doğrulama + uygulama)
├── MigrationHostedService.cs   (AutoApplyMigrations + varsayılan kiracı satırı)
└── 0001_initial.sql            (EmbeddedResource)
```

Akış:

1. `pg_advisory_lock(0x41505249534D0001)` — çoklu replika başlangıcında yalnızca biri uygular
2. Şema ve `__migrations` defteri yoksa oluşturulur
3. Uygulanmış her migration'ın checksum'ı doğrulanır; uyuşmazlık **hata verir**
4. Uygulanmamışlar sıra ile, her biri kendi transaction'ında çalıştırılır
5. `pg_advisory_unlock`

Checksum, şema yer tutucusu değiştirilmeden **önce** hesaplanır ve satır sonu farkı (`CRLF`/`LF`) normalleştirilir. Böylece `SchemaName` ayarını değiştirmek veya depoyu farklı bir git ayarıyla klonlamak uygulanmış migration'ları geçersiz kılmaz.

Tüm adımlar **tek bir bağlantı** üzerinde yürür — advisory lock oturum kapsamlıdır.

---

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.PostgreSql/
├── AgentPrismPostgreSqlBuilderExtensions.cs   (UsePostgreSql ×3, elle yapılandırma bağlama)
├── AgentPrismPostgreSqlOptions.cs
├── AgentPrismPostgreSqlOptionsValidator.cs
├── AgentPrismJsonContext.cs                   (JsonSerializerContext — AOT)
├── Internal/
│   ├── AgentDefinitionPayload.cs              (+ ChatHistoryState)
│   ├── NpgsqlDataSourceFactory.cs
│   ├── NpgsqlHelpers.cs                       (ConfigureAwait kalıbı tek yerde)
│   ├── SqlIdentifier.cs                       (şema adı doğrulaması)
│   └── SqlQueries.cs                          (şemaya göre kurulmuş SQL metinleri)
├── Migrations/
│   ├── 0001_initial.sql                       (EmbeddedResource)
│   ├── MigrationDescriptor.cs
│   ├── MigrationHostedService.cs
│   └── MigrationRunner.cs
└── Stores/
    ├── PostgresAgentDefinitionStore.cs
    ├── PostgresChatHistoryProvider.cs
    ├── PostgresRunStore.cs
    └── PostgresSessionStore.cs

src/AgentPrism.Core/                           (bu fazda değişenler)
├── AgentPrismCoreJsonContext.cs               (YENİ — StateBag serileştirmesi)
├── Sessions/AgentSessionIdentity.cs           (YENİ)
├── Sessions/AgentSessionManager.cs            (YENİ)
├── Compilation/AgentDefinitionCompiler.cs     (ChatHistoryProvider bağlantısı)
├── Recording/RunRecordingAgent.cs             (GetSessionId gerçek kimliği okuyor)
├── Storage/InMemorySessionStore.cs            (CreatedAt korunuyor)
└── AgentPrismServiceCollectionExtensions.cs   (AgentSessionManager kaydı)

tests/AgentPrism.PostgreSql.IntegrationTests/  (YENİ)
├── Contracts/     AgentDefinitionStoreContract · RunStoreContract · SessionStoreContract
│                  InMemoryStoreContractTests · PostgresStoreContractTests
├── Infrastructure/ PostgresFixture · PostgresTestContext · TestData · EchoModelProvider · AssemblyFixtures
├── IsolationTests.cs        (SchemaIsolationTests · TenantIsolationTests)
├── MigrationTests.cs        (MigrationRunnerTests)
├── ServiceRegistrationTests.cs
└── SessionPersistenceTests.cs
```

---

## Testler

`tests/AgentPrism.PostgreSql.IntegrationTests` — **88 test, hepsi geçiyor.**
`tests/AgentPrism.Core.UnitTests` — 42 test, değişmedi.

| Sınıf | Kapsam |
|-------|--------|
| `AgentDefinitionStoreContract` (×2 uygulama) | Sürümleme, geri alma, silme, sıralama, tüm alanların round-trip'i |
| `RunStoreContract` (×2 uygulama) | Sıra numarası, olay alanları, filtreleme, sayfalama, sonlandırma özeti |
| `SessionStoreContract` (×2 uygulama) | Opak durumun bozulmadan dönmesi, `CreatedAt` koruması, sıralama, sayfalama |
| `MigrationRunnerTests` | Idempotency, defter kaydı, **5 eşzamanlı runner → tek uygulama**, bozuk checksum → hata, özel şema adı, geçersiz şema adı reddi |
| `SchemaIsolationTests` | `public` şeması migration öncesi/sonrası **değişmiyor** |
| `TenantIsolationTests` | Kiracı A, kiracı B'nin tanımını/çalıştırmasını/oturumunu/olaylarını göremiyor; aynı agent adı iki kiracıda bağımsız |
| `ServiceRegistrationTests` | `UsePostgreSql()` sonrası üç depo da `Postgres*`; ayar doğrulaması; derlenen agent geçmişi veritabanına yazıyor |
| `SessionPersistenceTests` | Oturum yeni bir `ServiceProvider`'da geri yükleniyor; sohbet geçmişi sürüyor; çalıştırma kaydı **gerçek** oturum kimliğini taşıyor |

**Testcontainers** kullanılır; her çalıştırmada yerel, tek kullanımlık `postgres:18-alpine` container'ı ayağa kalkar. Uzak veya paylaşılan bir sunucuya hiçbir test bağlanmaz. Container tüm derleme için bir kez başlar; testler **ayrı şema** kullanarak yalıtılır — bu aynı zamanda `SchemaName` ayarının çalıştığını her testte doğrular.

Sözleşme testleri bellek içi uygulama üzerinde de koşar ve **Docker gerektirmez**; yalnızca `Postgres*` varyantları container'a ihtiyaç duyar.

---

## Bitiş Ölçütleri (DoD)

| Ölçüt | Durum |
|-------|-------|
| `UsePostgreSql(...)` zincire eklenir ve `Replace` ile depoları değiştirir | ✅ `ServiceRegistrationTests` |
| Uygulama başlar, migration'lar uygulanır, `agentprism` şeması oluşur | ✅ `AgentPrism 1 migration uyguladi. Sema: agentprism.` |
| Örnek API yeniden başlatılır — agent tanımı ve çalıştırma geçmişi yerinde durur | ✅ aşağıdaki çıktı |
| `public` şemasının değişmediği doğrulanır | ✅ `Did not find any tables named "public.*"` |
| 5 eşzamanlı başlangıçta migration tek kez uygulanır | ✅ `Bes_es_zamanli_kosuda_migration_tek_kez_uygulanir` (1 uygulama, 4 atlama) |
| `InMemory*` ile `Postgres*` aynı davranış testlerini geçer | ✅ ortak sözleşme sınıfları |
| `dotnet build -c Release` — 0 uyarı (AOT dahil) | ✅ `0 Warning(s) / 0 Error(s)` |
| `dotnet test` — mevcut 42 test + yeni entegrasyon testleri | ✅ 42 + 88 = **130** |
| `dotnet pack` — 0 uyarı | ✅ `AgentPrism.PostgreSql` nuspec'inde **2** doğrudan bağımlılık (`AgentPrism.Core`, `Npgsql`) |
| `dotnet format --verify-no-changes` | ✅ değişiklik yok |
| Sır taraması | ✅ boş |

### Manuel doğrulama — gerçek çıktı

```bash
docker run -d --name pg -e POSTGRES_PASSWORD=... -e POSTGRES_DB=agentprism_demo -p 55432:5432 postgres:18-alpine
cd samples/AgentPrism.Api
AgentPrism__PostgreSql__ConnectionString="Host=localhost;Port=55432;..." dotnet run -c Release
```

```
GET /health
{"status":"healthy","phase":"2 - postgresql kalicilik",
 "storage":{"persistent":true,"runStore":"PostgresRunStore","sessionStore":"PostgresSessionStore"}}

POST /agents/support/run  {"message":"siparisim nerede","sessionId":"musteri-7"}
{"text":"Echo: siparisim nerede","sessionId":"musteri-7"}

POST /agents/support/run  {"message":"tesekkurler","sessionId":"musteri-7"}
{"text":"Echo: tesekkurler","sessionId":"musteri-7"}

GET /sessions
[{"id":"musteri-7","agentName":"support",
  "state":{"stateBag":{"AgentPrism.SessionId":"musteri-7",
                       "AgentPrism.ChatHistory":{"conversationId":"019fbfa1-2ca1-7712-8df9-ab8695893871"}}},
  "createdAt":"...","updatedAt":"...","tenantId":"default"}]

--- uygulama durduruldu ve yeniden başlatıldı ---

GET /runs        → 2 çalıştırma, ikisi de session=musteri-7, eventCount=4, status=Completed
POST .../run     → {"text":"Echo: tekrar merhaba","sessionId":"musteri-7"}    (aynı oturum sürdü)
psql -c "SELECT count(*) FROM agentprism.conversation_items"   → 6      (3 tur × 2 mesaj)
psql -c "\dt public.*"                                         → Did not find any tables named "public.*"
psql -c "SELECT count(*) ... table_schema='agentprism'"        → 14
```

Oturum durumundaki `stateBag` iki şeyi kanıtlıyor: oturum kimliği damgası (`AgentPrism.SessionId`) ve sohbet geçmişi sağlayıcısının konuşma kimliği (`AgentPrism.ChatHistory`) oturumla birlikte kalıcılaşıyor.

---

## Faz 1'den Devralınan Açık İşler — Durum

| # | İş | Durum |
|---|-----|-------|
| 1 | `RunRecordingAgent.GetSessionId` yer tutucu değer üretiyordu (`session.GetType().Name`) | ✅ Kapandı. `AgentSessionIdentity.GetId(session)` okuyor; damga yoksa `null` yazılıyor (yer tutucu değil). |
| 2 | `ISessionStore` hiç kullanılmıyordu | ✅ Kapandı. `AgentSessionManager` çağırıyor; örnek API `/sessions` uçlarını sunuyor. |
| 3 | `SessionRecord.State` opak kabul edilir | ✅ Korundu. `json` sütununda **aynen** saklanıyor; içeriği yorumlanmıyor. |

---

## Faz 3'e Devreden Notlar

1. **`AgentSessionManager.SaveSessionAsync` çağrısı tüketiciye ait.** Örnek API her çalıştırmadan sonra elle çağırıyor. Faz 4'te HTTP katmanı bunu otomatikleştirmeli; Faz 4'teki `AgentSessionStore` (MAF `Hosting`) uygulaması bu sınıfa delege eder.
2. **`tool_invocations` tablosu boş.** Şema kuruldu ancak yazan yok. `RunEvent` çiftlerinden süre çıkarmak korelasyon gerektirir; Faz 6'ya bırakıldı.
3. **`conversations.metadata`, `responses`, `traces`, `spans`, `audit_log` boş.** Faz 4 ve Faz 6 doldurur.
4. **`run_events` partition'a hazır değil, aday.** `created_at` sütunu var ancak birincil anahtar `(run_id, seq)`. Faz 6'da partition açılırsa PK'nın `created_at` sütununu da içermesi gerekecek — bu bir migration ister.
5. **`CompiledAgentCache.Evict` hâlâ çağrılmıyor.** Faz 4'te agent tanımı güncellenince çağrılmalı.
6. **`MigrationRunner` public'tir.** `AutoApplyMigrations=false` ile ayrı bir dağıtım adımında çalıştırılabilir; bu yol test edilmedi (yalnızca doğrudan `ApplyAsync()` çağrısı test edildi).
7. **`NpgsqlDataSource` `TryAddSingleton` ile kaydedilir.** Tüketici kendi veri kaynağını `UsePostgreSql()` çağrısından **önce** kaydederse onunki kazanır — bağlantı havuzunu paylaşmak isteyenler için kasıtlı bir kapı.

---

## Riskler — güncel durum

| Risk | Durum |
|------|-------|
| `AgentSession` serileştirme formatı MAF sürümleri arasında değişebilir | `sessions.schema_version` sütunu yazılıyor; ileri sürüm okunursa anlaşılır hata veriliyor. `AgentSessionManager` ayrıca `DeserializeSessionAsync` hatalarını sarmalayıp hangi agent'a ait olduğunu söylüyor. |
| Uzun süren migration üretimde başlangıcı kilitler | `AutoApplyMigrations=false` seçeneği var; `MigrationRunner` public. |
| `Replace` yerine `TryAdd` yazılması | `ServiceRegistrationTests` üç depoyu da denetliyor. |
| `jsonb` serileştirmede yansıma kullanılması | `AgentPrismJsonContext` kaynak üreteci; build AOT analyzer'ları ile temiz. |
| **YENİ:** `jsonb` anahtar sırasını bozar | `json` sütununa geçildi (karar K-027); sözleşme testi ham metni karşılaştırıyor. |
