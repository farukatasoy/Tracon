# PostgreSQL Tuzaklari

> SQL, migration, jsonb, sabit sutun indeksi.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.
>
> **Faz 23'ten sonra:** `store` uygulamalari `AgentPrism.Sql.Shared` altinda
> PAYLASILIR ve `Npgsql` tipi gormez. PostgreSQL'e ozgu her sey
> `PostgresQueries` + `PostgresDialect` icindedir. Paylasilan katman ve SQL
> Server icin: [`sql-saglayicilari.md`](sql-saglayicilari.md).

- **🚨 `jsonb` nesne anahtarlarını yeniden sıralar** (2026-08-02): PostgreSQL `jsonb` anahtarları önce uzunluğa, sonra bayta göre sıralar. System.Text.Json'ın polimorfik `$type` ayracı ilk özellik olmak zorundadır → okuma `JsonException: The metadata property ... is not the first property` verir. Opak veya polimorfik yükler **`json`** sütununda saklanır (`sessions.state`, `conversation_items.item`). Karar K-027.
- **Migration checksum'ı satır sonu farkına duyarlı olmamalı** (2026-08-02): `MigrationDescriptor.ComputeChecksum` CRLF'i LF'e normalleştirir. Aksi halde farklı `core.autocrlf` ayarıyla klonlanan repo "migration değişmiş" hatası verir.
- **Postgres span `store`'u kiracı bağlamıyla okur** (2026-08-02): sözleşme testi span'leri `"test"` kiracısına yazıp varsayılan kiracıyla okuyunca trace hiç bulunamadı. Yazma ve okuma aynı kiracıya düşmelidir.
- **🚨 PostgreSQL'de NULL sütun içeren benzersizlik `COALESCE` ile kurulur** (2026-08-02, Faz 11): `UNIQUE (tenant_id, skill_name, script_name)` NULL'ları farklı sayar ve kopya satırlar birikir. Doğrusu: `CREATE UNIQUE INDEX ... (tenant_id, skill_name, COALESCE(script_name, ''))`. `ON CONFLICT` yan tümcesi de aynı ifadeyi kullanmalıdır.
- **🚨 Bir sutunu FK yapmadan once gercek yukleme sirasini dusun** (2026-08-02, Faz 14): `attachments.session_id` FK ile denendi, gercek akista (ek run baslamadan/oturum satiri dogmadan once yuklenir) `INSERT` "violates foreign key constraint" ile bozuldu. Oturum silme kaskadi FK yerine uygulama katmaninda (`IAttachmentStore.DeleteBySessionAsync`) yapildi (K-112).
- **🚨 `AgentDefinitionPayload` (`AgentPrism.PostgreSql`) `AgentDefinition`'ı AYRI bir JSON şemasıyla serileştirir** (2026-08-02, Faz 13): `AgentPrismCoreJsonContext`'in kaynak ürettiği tip değil; elle yazılmış bir DTO. Yeni bir `AgentDefinition` alanı eklendiğinde `Internal/AgentDefinitionPayload.cs`'e de elle eklenmezse alan PostgreSQL'e **sessizce** yazılmaz/okunmaz — build ve test kırılmaz, yalnız round-trip testi yakalar. Aynı dosyada Faz 12'den kalma bağımsız bir hata (`CallableAgentNames` hiç yoktu) bulunup düzeltildi.
- **`runColumns`/`ReadRun` (PostgresRunStore) sabit sutun indeksi tasir** (2026-08-03, Faz 19): `runs` tablosuna sondan sutun eklerken (`agent_version`/`experiment_id`/`variant`) `runColumns` sabitindeki sira ile `ReadRun`'daki `reader.GetX(N)` indeksleri BIRLIKTE guncellenmeli — biri unutulursa derleme/test hatasi vermez, yanlis sutunu okur. Yeni sutunlar her zaman **sona** eklenir, var olan indeksleri kaydirmamak icin.
- **`audit_log` şeması zaten yeterli** (0001, satır 229): `actor`, `entity`, `before`, `after` sütunları var. Faz 9 için migration **gerekmiyor**, yalnız yazan kod eksik.
- **🚨 `(@p IS NULL OR col = @p)` deseninde parametre ACIKCA tiplenmelidir** (2026-08-03, Faz 21): `AddWithValue("p", DBNull.Value)` ile gonderilen istege bagli suzgec parametresinde PostgreSQL tipi cikaramaz ve `42P08: could not determine data type of parameter $N` verir. Sorgu C# tarafinda derlenir, bu yuzden hata yalnizca **calisma aninda** gorunur. Cozum: `new NpgsqlParameter("p", NpgsqlDbType.Text) { Value = ... }`. `PostgresQuotaStore.GetUsageAsync` ve `PostgresWebhookStore.QueryDeliveriesAsync` bu deseni izler.
- **`ON CONFLICT` hedefi ifade olabilir, sutun listesi olmak zorunda degil** (2026-08-03, Faz 21): `quotas` tablosunda benzersizlik `COALESCE(agent_name, '')` uzerine kuruludur; `ON CONFLICT` yan tumcesi **ayni ifadeyi** yazmalidir (`ON CONFLICT (tenant_id, COALESCE(agent_name, ''), period)`), aksi halde PostgreSQL eslesen bir kisit bulamaz. Bkz. `SqlQueries.UpsertQuota`.
- **Migration tablo sayisi testi BILEREK sabittir** (2026-08-03, Faz 21): `MigrationRunnerTests.Ilk_kosuda_sema_ve_tablolar_olusur` icindeki `tableCount.ShouldBe(N)` yeni bir tablo eklenince kirilir — bu bir hata degil, kasitli bir kapidir. Migration eklerken sayiyi ve ustundeki aciklama listesini birlikte guncelleyin (0012 ile 32 → 36).

- **`run_inputs.messages` `json`, `jsonb` DEĞİL** (2026-08-07, Faz 47, K-308): K-027'nin dördüncü uygulaması. `ChatMessage` içerikleri polimorfiktir; `jsonb` anahtarları yeniden sıralar ve `$type` ayracı ilk özellik olmaktan çıkar. Girdi ayrıca `runs`'a sütun olarak EKLENMEZ — `runs` en sıcak tablodur ve her liste/istatistik sorgusu onu okur.
- **`runs.replay_of_run_id` 39. sütun indeksidir** (2026-08-07, Faz 47): `SqlRunStore.ReadRun` sabit indeksle okur; yeni sütunlar her zaman SONA eklenir. Kısmi indeks (`WHERE replay_of_run_id IS NOT NULL`) kullanıldı: satırların büyük çoğunluğu NULL'dur.
- **🚨 `conversations.parent_conversation_id` yabancı anahtar TAŞIMAZ** (2026-08-07, Faz 47, K-312): plan `ON DELETE SET NULL` öngörüyordu ama SQL Server kendine referans veren bir FK'de `SET NULL` kabul etmez (hata 1785). Kısıtı yalnız PostgreSQL/SQLite'a koymak aynı silmeyi üç sağlayıcıda üç farklı sonuca çevirirdi. Şema farkı davranış farkına DÖNÜŞTÜRÜLMEZ (K-184'ün dersi).

- **🚨 PostgreSQL entegrasyon test imajı `postgres:*-alpine` DEĞİL, `pgvector/pgvector:pgNN`dir** (2026-08-08, Faz 51, K-346): migration 0024 `CREATE EXTENSION IF NOT EXISTS vector;` çalıştırır ve bu HER PostgreSQL testinde (yalnız vektör testlerinde değil) uygulanır — düz `postgres` imajı uzantıyı taşımadığı için TÜM migration seti (dolayısıyla tüm test paketi) patlardı. `tests/AgentPrism.PostgreSql.IntegrationTests/Infrastructure/PostgresFixture.cs`. `pgvector/pgvector` imajı resmi `postgres` imajının üstüne yalnız bu uzantıyı ekler, başka davranış farkı yaratmaz (862 test ölçüldü, hepsi yeşil).
- **Kurulum anında bilinen, sağlayıcıya özgü migration değerleri (şema DIŞINDA) `SqlStoreContext.MigrationTemplateValues` ile geçer** (2026-08-08, Faz 51, K-346): `{schema}` yer tutucusu `SqlQueriesBase.ApplySchema`'dan geçer; başka bir `{anahtar}` yer tutucusu (örn. `document_embeddings.embedding` sütununun `vector({dimension})` boyutu) `MigrationRunner.ApplyTemplate` ile şema değiştirmesinden SONRA uygulanır. Checksum HAM (değiştirilmemiş) metin üzerinden hesaplanır — `SchemaName` gibi, bu değer de değiştirilse checksum uyuşmazlığı vermez.
- **PostgreSQL `LIKE 'onek%'` sorgusu locale'e göre bir index range scan'e dönüşebilir** (2026-08-08, Faz 51): test container'ında ölçüldü — 10 000 ilgisiz satır arasında hedef dizin araması `EXPLAIN ANALYZE`'da yalnız **1 satır** okudu (composite `(tenant_id, agent_name, path)` üzerinde). Bu optimizasyon `COLLATE "C"` veya eşdeğerine bağlıdır; farklı locale'lerde tam sayı değişebilir, kanıt satır sayısının toplam depo büyüklüğünden KAT KAT küçük kalmasıdır, mutlak "1" değil.
- **`pgvector`'ın `vector` sütunu metin (`'[...]'::vector`) ile YA DA ikili protokolle yazılsa da diskte AYNI kanonik ikili gösterimi saklar** (2026-08-08, Faz 51, K-341): ölçüldü — 1536 boyutlu bir gömünün metin gösterimi 14 416 bayt, `pg_column_size` ise 6 148 bayt (`1536×4+4`, tam olarak ikili biçimin kendisi). Metin/ikili farkı YALNIZ yazma sırasındaki istemci→sunucu TELİNDEDİR; disk saklama ve okuma maliyeti özdeştir. Bir vektör paketi (`Pgvector`, `SK.Connectors.PgVector`) yalnızca "daha az tel trafiği" için alınıyorsa bu ölçüm kazancı sorgulatır.

## Alt yazma yollarinda BEKLENEN kiraci (K-355)

- **🚨 Ambient kiraciyla suzmek MESRU yazmalari dusurur.** `RunStartInfo.TenantId`
  ambient kiraciyi **bilerek** ezer (workflow ve is kuyrugu boyle calisir);
  bir kez denenip GERI ALINDI (K-280). Dogru cozum cagrinin tasidigi **beklenen**
  kiracidir: `RunEvent`/`RunCompletion`/`ToolInvocationRecord`'da `TenantId`,
  `UpdateRunCostAsync`'te `string? tenantId`. Deger `RunEventWriter.StartAsync`
  icinde `RunStartInfo.TenantId`'den alinir. `null` → denetim yok (geriye donuk
  uyumlu).
- **SQL sekli:** UPDATE'lerde `AND (@tenant_id IS NULL OR tenant_id = @tenant_id)`;
  INSERT'lerde `VALUES` yerine
  `SELECT ... WHERE EXISTS (SELECT 1 FROM runs r WHERE r.id = @run_id AND (@tenant_id IS NULL OR r.tenant_id = @tenant_id))`.
  PostgreSQL parametre tiplerini hedef sutunlardan cozer; `INSERT ... SELECT`
  ek cast gerektirmedi. Uc lehcede de yesil kostu.
- **🚨 YALNIZ YAZMA tarafindaki bir alan `[JsonIgnore]` ISTER.** Alan bir sutuna
  yazilmaz, yalnizca `WHERE` muhafizidir; geri okundugunda her zaman `null`
  olurdu. Isaretlenmezse OpenAPI belgesi **hicbir zaman dolmayan** bir alan ilan
  eder — `OpenApiSnapshotTests` bunu yakaladi ve `[JsonIgnore]` sonrasi
  `docs/openapi/agentprism.json` degismedi.
