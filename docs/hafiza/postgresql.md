# PostgreSQL Tuzaklari

> SQL, migration, jsonb, sabit sutun indeksi.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.
>
> **Faz 23'ten sonra:** `store` uygulamalari `Tracon.Sql.Shared` altinda
> PAYLASILIR ve `Npgsql` tipi gormez. PostgreSQL'e ozgu her sey
> `PostgresQueries` + `PostgresDialect` icindedir. Paylasilan katman ve SQL
> Server icin: [`sql-saglayicilari.md`](sql-saglayicilari.md).

- **🚨 `jsonb` nesne anahtarlarını yeniden sıralar** (2026-08-02): PostgreSQL `jsonb` anahtarları önce uzunluğa, sonra bayta göre sıralar. System.Text.Json'ın polimorfik `$type` ayracı ilk özellik olmak zorundadır → okuma `JsonException: The metadata property ... is not the first property` verir. Opak veya polimorfik yükler **`json`** sütununda saklanır (`sessions.state`, `conversation_items.item`). Karar K-027.
- **Migration checksum'ı satır sonu farkına duyarlı olmamalı** (2026-08-02): `MigrationDescriptor.ComputeChecksum` CRLF'i LF'e normalleştirir. Aksi halde farklı `core.autocrlf` ayarıyla klonlanan repo "migration değişmiş" hatası verir.
- **🚨 Uygulanmış migration'ın HER baytı Git tabanına sabittir** (2026-08-25, Faz 99, K-612): SQL etkilenmese bile yorum değişikliği checksum'u bozar ve eski veritabanının açılışını durdurur. `scripts/kapi.py tarama`, migration metnini `scripts/applied-migrations.json` içindeki Git kaynak commit'inden okur; değiştirilebilir checksum manifesti bu kontrolü onaylayamaz. Yeni migration eklerken kendi kaynak commit'ini manifest'e yaz; uygulanmış dosyada yorum, boşluk veya bağlantı bile değiştirme.
- **Postgres span `store`'u kiracı bağlamıyla okur** (2026-08-02): sözleşme testi span'leri `"test"` kiracısına yazıp varsayılan kiracıyla okuyunca trace hiç bulunamadı. Yazma ve okuma aynı kiracıya düşmelidir.
- **🚨 PostgreSQL'de NULL sütun içeren benzersizlik `COALESCE` ile kurulur** (2026-08-02, Faz 11): `UNIQUE (tenant_id, skill_name, script_name)` NULL'ları farklı sayar ve kopya satırlar birikir. Doğrusu: `CREATE UNIQUE INDEX ... (tenant_id, skill_name, COALESCE(script_name, ''))`. `ON CONFLICT` yan tümcesi de aynı ifadeyi kullanmalıdır.
- **🚨 Bir sutunu FK yapmadan once gercek yukleme sirasini dusun** (2026-08-02, Faz 14): `attachments.session_id` FK ile denendi, gercek akista (ek run baslamadan/oturum satiri dogmadan once yuklenir) `INSERT` "violates foreign key constraint" ile bozuldu. Oturum silme kaskadi FK yerine uygulama katmaninda (`IAttachmentStore.DeleteBySessionAsync`) yapildi (K-112).
- **🚨 `AgentDefinitionPayload` (`Tracon.PostgreSql`) `AgentDefinition`'ı AYRI bir JSON şemasıyla serileştirir** (2026-08-02, Faz 13): `TraconCoreJsonContext`'in kaynak ürettiği tip değil; elle yazılmış bir DTO. Yeni bir `AgentDefinition` alanı eklendiğinde `Internal/AgentDefinitionPayload.cs`'e de elle eklenmezse alan PostgreSQL'e **sessizce** yazılmaz/okunmaz — build ve test kırılmaz, yalnız round-trip testi yakalar. Aynı dosyada Faz 12'den kalma bağımsız bir hata (`CallableAgentNames` hiç yoktu) bulunup düzeltildi.
- **`runColumns`/`ReadRun` (PostgresRunStore) sabit sutun indeksi tasir** (2026-08-03, Faz 19): `runs` tablosuna sondan sutun eklerken (`agent_version`/`experiment_id`/`variant`) `runColumns` sabitindeki sira ile `ReadRun`'daki `reader.GetX(N)` indeksleri BIRLIKTE guncellenmeli — biri unutulursa derleme/test hatasi vermez, yanlis sutunu okur. Yeni sutunlar her zaman **sona** eklenir, var olan indeksleri kaydirmamak icin.
- **`audit_log` şeması Faz 9 için zaten yeterliydi** (0001, satır 229): `actor`, `entity`, `before`, `after` sütunları var, migration gerekmedi. Faz 64 bunu **değiştirdi**: `prev_hash`/`hash`/`chain_seq` eklendi ve `before`/`after` `jsonb`'den `json`'a çevrildi (0031_audit_chain.sql) — aşağıdaki iki madde.
- **🚨 `jsonb` hash zinciri gibi "yazılan metnin AYNEN geri okunmasını" gerektiren bir alanda KULLANILAMAZ** (2026-08-18, Faz 64, K-460): K-027'nin bilinen "anahtar sırası" sorunu yetmiyor — `jsonb` boşluğu da değiştiriyor, yani `Convert.ToHexString(SHA256(...))` yazma anında hesaplanan hash, `jsonb` sütunundan okunan metinle **asla eşleşmiyor**. `audit_log.before`/`after` bu yüzden `json`'a çevrildi (`ALTER COLUMN ... TYPE json USING ...::json`, kayıpsız) — K-027'nin (polimorfik yük → `json`, `jsonb` DEĞİL) BEŞİNCİ uygulaması.
- **🚨 Bir "zincirin son halkası" sorgusu `ORDER BY created_at, id` ile YAZILAMAZ** (2026-08-18, Faz 64, K-459): uuid v7'nin (`id`) alt bitleri **rastgele**dir, aynı (truncated) mikrosaniyeye düşen satırlar arasında ekleme sırasını YANSITMAZ. 20 eşzamanlı yazıcı testinde bir satır, rastgele büyük `id`'si yüzünden "son satır" olarak SONSUZA KADAR raporlandı — yeni satırlar eklense bile onu geçemediler, yazıcılar hiç yakınsamadı. Gerçek bir ekleme sırası gerekiyorsa `GENERATED ... AS IDENTITY`/`SERIAL` gibi veritabanının kendi atadığı bir sayaç kullanılır (tek deyimlik autocommit `INSERT`'lerde sıra ataması commit sırasıyla birebir örtüşür).
- **🚨 `(@p IS NULL OR col = @p)` deseninde parametre ACIKCA tiplenmelidir** (2026-08-03, Faz 21): `AddWithValue("p", DBNull.Value)` ile gonderilen istege bagli suzgec parametresinde PostgreSQL tipi cikaramaz ve `42P08: could not determine data type of parameter $N` verir. Sorgu C# tarafinda derlenir, bu yuzden hata yalnizca **calisma aninda** gorunur. Cozum: `new NpgsqlParameter("p", NpgsqlDbType.Text) { Value = ... }`. `PostgresQuotaStore.GetUsageAsync` ve `PostgresWebhookStore.QueryDeliveriesAsync` bu deseni izler.
- **`ON CONFLICT` hedefi ifade olabilir, sutun listesi olmak zorunda degil** (2026-08-03, Faz 21): `quotas` tablosunda benzersizlik `COALESCE(agent_name, '')` uzerine kuruludur; `ON CONFLICT` yan tumcesi **ayni ifadeyi** yazmalidir (`ON CONFLICT (tenant_id, COALESCE(agent_name, ''), period)`), aksi halde PostgreSQL eslesen bir kisit bulamaz. Bkz. `SqlQueries.UpsertQuota`.
- **Migration tablo sayisi testi BILEREK sabittir** (2026-08-03, Faz 21): `MigrationRunnerTests.Ilk_kosuda_sema_ve_tablolar_olusur` icindeki `tableCount.ShouldBe(N)` yeni bir tablo eklenince kirilir — bu bir hata degil, kasitli bir kapidir. Migration eklerken sayiyi ve ustundeki aciklama listesini birlikte guncelleyin (0012 ile 32 → 36).

- **`run_inputs.messages` `json`, `jsonb` DEĞİL** (2026-08-07, Faz 47, K-308): K-027'nin dördüncü uygulaması. `ChatMessage` içerikleri polimorfiktir; `jsonb` anahtarları yeniden sıralar ve `$type` ayracı ilk özellik olmaktan çıkar. Girdi ayrıca `runs`'a sütun olarak EKLENMEZ — `runs` en sıcak tablodur ve her liste/istatistik sorgusu onu okur.
- **`runs.replay_of_run_id` 39. sütun indeksidir** (2026-08-07, Faz 47): `SqlRunStore.ReadRun` sabit indeksle okur; yeni sütunlar her zaman SONA eklenir. Kısmi indeks (`WHERE replay_of_run_id IS NOT NULL`) kullanıldı: satırların büyük çoğunluğu NULL'dur.
- **🚨 `conversations.parent_conversation_id` yabancı anahtar TAŞIMAZ** (2026-08-07, Faz 47, K-312): plan `ON DELETE SET NULL` öngörüyordu ama SQL Server kendine referans veren bir FK'de `SET NULL` kabul etmez (hata 1785). Kısıtı yalnız PostgreSQL/SQLite'a koymak aynı silmeyi üç sağlayıcıda üç farklı sonuca çevirirdi. Şema farkı davranış farkına DÖNÜŞTÜRÜLMEZ (K-184'ün dersi).

- **🚨 PostgreSQL entegrasyon test imajı `postgres:*-alpine` DEĞİL, `pgvector/pgvector:pgNN`dir** (2026-08-08, Faz 51, K-346): migration 0024 `CREATE EXTENSION IF NOT EXISTS vector;` çalıştırır ve bu HER PostgreSQL testinde (yalnız vektör testlerinde değil) uygulanır — düz `postgres` imajı uzantıyı taşımadığı için TÜM migration seti (dolayısıyla tüm test paketi) patlardı. `tests/Tracon.PostgreSql.IntegrationTests/Infrastructure/PostgresFixture.cs`. `pgvector/pgvector` imajı resmi `postgres` imajının üstüne yalnız bu uzantıyı ekler, başka davranış farkı yaratmaz (862 test ölçüldü, hepsi yeşil).
- **Kurulum anında bilinen, sağlayıcıya özgü migration değerleri (şema DIŞINDA) `SqlStoreContext.MigrationTemplateValues` ile geçer** (2026-08-08, Faz 51, K-346): `{schema}` yer tutucusu `SqlQueriesBase.ApplySchema`'dan geçer; başka bir `{anahtar}` yer tutucusu (örn. `document_embeddings.embedding` sütununun `vector({dimension})` boyutu) `MigrationRunner.ApplyTemplate` ile şema değiştirmesinden SONRA uygulanır. Checksum HAM (değiştirilmemiş) metin üzerinden hesaplanır — `SchemaName` gibi, bu değer de değiştirilse checksum uyuşmazlığı vermez.
- **PostgreSQL `LIKE 'onek%'` sorgusu locale'e göre bir index range scan'e dönüşebilir** (2026-08-08, Faz 51): test container'ında ölçüldü — 10 000 ilgisiz satır arasında hedef dizin araması `EXPLAIN ANALYZE`'da yalnız **1 satır** okudu (composite `(tenant_id, agent_name, path)` üzerinde). Bu optimizasyon `COLLATE "C"` veya eşdeğerine bağlıdır; farklı locale'lerde tam sayı değişebilir, kanıt satır sayısının toplam depo büyüklüğünden KAT KAT küçük kalmasıdır, mutlak "1" değil.
- **`pgvector`'ın `vector` sütunu metin (`'[...]'::vector`) ile YA DA ikili protokolle yazılsa da diskte AYNI kanonik ikili gösterimi saklar** (2026-08-08, Faz 51, K-341): ölçüldü — 1536 boyutlu bir gömünün metin gösterimi 14 416 bayt, `pg_column_size` ise 6 148 bayt (`1536×4+4`, tam olarak ikili biçimin kendisi). Metin/ikili farkı YALNIZ yazma sırasındaki istemci→sunucu TELİNDEDİR; disk saklama ve okuma maliyeti özdeştir. Bir vektör paketi (`Pgvector`, `SK.Connectors.PgVector`) yalnızca "daha az tel trafiği" için alınıyorsa bu ölçüm kazancı sorgulatır.

- **🚨 YALNIZ YAZMA tarafindaki bir alan `[JsonIgnore]` ISTER.** Alan bir sutuna
  yazilmaz, yalnizca `WHERE` muhafizidir; geri okundugunda her zaman `null`
  olurdu. Isaretlenmezse OpenAPI belgesi **hicbir zaman dolmayan** bir alan ilan
  eder — `OpenApiSnapshotTests` bunu yakaladi ve `[JsonIgnore]` sonrasi
  `docs/openapi/tracon.json` degismedi.
- **Düz bir `string→string` haritası için `jsonb` DOĞRUDUR; K-027'nin yasağı POLİMORFİK yükler içindir** (2026-08-19, Faz 68, K-479): `runs.labels` `$type` ayracı taşımaz, dolayısıyla `jsonb`'nin anahtar yeniden sıralaması zararsızdır ve GIN indeksi kazançtır (K-345'in `document_embeddings.metadata` ile aynı gerekçe). Süzgeç `jsonb_exists(labels, @key)` + `labels @> jsonb_build_object(...)` ile yazılır — ikisi de `gin (labels)`'tan yararlanır; `?` operatörü yerine FONKSİYON biçimi seçildi ki hiçbir sürücü onu parametre yer tutucusu sanmasın. SQL Server/SQLite'ta harita JSON metnidir (`OPENJSON`/`json_each`) ve indeks YOKTUR: serbest bir etiket kümesi, hesaplanmış sütun indeksinin isteyeceği önceden bilinen anahtar listesini veremez.
- **🚨 Exact timestamp round-trip testine 100 ns hassasiyetli `UtcNow` verme**
  (2026-08-28): .NET tick'i 100 ns, PostgreSQL timestamp'i mikrosaniye
  hassasiyetindedir. Test çıktısı saniyeyi gösterdiği için iki değer aynı görünür,
  fakat exact assertion düşer. Sağlayıcılar arası contract testinde değeri yazmadan
  önce ortak güvenli hassasiyete indir; burada Unix millisecond kullanılır.
- **🚨 Npgsql havuzu `NpgsqlDataSource` ÖRNEĞİNE aittir, connection string'e DEĞİL** (2026-08-26, Faz 110, K-625): aynı connection string'le kurulan İKİ ayrı `NpgsqlDataSource` bağlantı havuzunu PAYLAŞMAZ — her biri kendi havuzunu açar. Ölçüldü: `ConnectionPoolSharingTests` — iki data source'tan 5'er eşzamanlı bağlantı tutuldu, `pg_stat_activity` tam **10** backend gördü (5 değil). `TraconPostgreSqlOptions.DataSource` seçeneği bu yüzden var: gerçek paylaşım, YALNIZ aynı örneği iki tarafa da vermekle olur (`NpgsqlDataSourceBuilder(...).Build()` bir kez, sonra hem `UseNpgsql(dataSource)` hem `UsePostgreSql(o => o.DataSource = dataSource)`).
- **🚨 Dış verilen bir `DbDataSource`'u Tracon ASLA dispose etmez** (Faz 110, K-625): `SqlStoreContext.OwnsDataSource` bu sahiplik bayrağını taşır — `ConnectionString`'den kendi kurduğunda `true`, `Options.DataSource` verildiğinde `false`. Yanlış yön tüketicinin kendi `DbContext`'ini host kapanışında sessizce öldürür. `Dispose`/`DisposeAsync` bu bayrağa koşulludur; kanıt bir spy `DbDataSource` ile izole edildi (`SqlStoreContextDisposalTests`, Docker gerekmez).
- **🚨 `agent_definitions.definition` TÜM `AgentDefinition` içeriğini tek `jsonb` blob'unda taşır — yeni bir alan eklemek migration İSTEMEZ** (2026-08-19, Faz 72, K-499): `AgentDefinitionPayload` (`Tracon.Sql.Shared`) sadece `Name`/`Version`/`TenantId`/`UpdatedAt` sütun; geri kalan HER `AgentDefinition` alanı payload'ın kendisidir ve üç sağlayıcının `TraconJsonContext`'i zaten kayıtlı tipleri (ör. `Dictionary<string,string>`) paylaşır. Faz 72'nin planı "üç migration seti gerekli" diyordu — ölçülmeden yazılmış yanlış bir yapısal iddiaydı (`faz-uygulama` Adım 1'in tam örneği). Yeni alan eklerken kontrol et: alan zaten `AgentDefinitionPayload`'a mı giriyor (migration YOK), yoksa kendi SÜTUNU mu gerekiyor (migration VAR, K-308'deki `run_inputs` gibi ayrı tablo deseni)?

- **Kısıtlı bir uygulama rolüyle koşmanın yordamı** (2026-09-19, manuel kapanış
  `MT-SEC-190…192`): `REVOKE` süperkullanıcıyı etkilemez, o yüzden izin
  senaryoları `postgres` ile ölçülemez. Sıra önemlidir — migration'lar önce
  `postgres` ile koşulur, sonra uygulama kısıtlı role çevrilir:
  ```sql
  CREATE ROLE tracon_app LOGIN PASSWORD '…';          -- rolsuper = f
  GRANT USAGE ON SCHEMA <şema> TO tracon_app;
  GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA <şema> TO tracon_app;
  ```
  Uygulama `AutoApplyMigrations=false` ile o role bağlanır. Doğrulama:
  `SELECT has_table_privilege('tracon_app','<şema>.audit_log','INSERT')` → `f`.
- **🚨 Denetim yazımının İKİ ayrı yolu vardır ve aynı veritabanı hatası iki
  farklı sonuç verir** (aynı vaka): onay **kararı** `AuditRecorder.WriteOrThrowAsync`
  ile yazar — yazamazsa `LogError` + `500` ve karar **uygulanmaz**
  (`ApprovalEndpoints.DecideAsync`). Agent **oluşturma** ise
  `AuditingAgentDefinitionStore` ile yazar — yazamazsa `LogWarning` + `201` ve
  işlem **tamamlanır**. Ayrım keyfi değil: biri denetim izi olmadan
  yapılmaması gereken bir **karar**, diğeri izsiz de doğru olan bir **yazım**.
  Bir izin senaryosu ölçerken hangi yolda olduğunu önce belirle.
