# Faz 2 — PostgreSQL Kalıcılık Katmanı

> **Durum:** ✅ Tamamlandı (2026-08-02)
> **Önkoşul:** [01-CEKIRDEK-SOYUTLAMALAR.md](01-CEKIRDEK-SOYUTLAMALAR.md) — tamamlandı
> **Sonraki:** [03-SAGLAYICI-VE-DERLEYICI.md](03-SAGLAYICI-VE-DERLEYICI.md)
> **Paketler:** `AgentPrism.PostgreSql` (dolduruldu), `AgentPrism.Core` (oturum yönetimi eklendi)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/02-POSTGRESQL-KALICILIK.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tüm durumu PostgreSQL'e taşımak. Uygulama yeniden başladığında oturumlar, agent tanımları ve çalıştırma geçmişi yerinde durmalıdır. **Sonuç:** hedefe ulaşıldı. Örnek API durdurulup yeniden başlatıldığında çalıştırma geçmişi, oturum ve sohbet geçmişi yerinde kaldı (aşağıdaki DoD tablosunda gerçek çıktı var). ---

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
