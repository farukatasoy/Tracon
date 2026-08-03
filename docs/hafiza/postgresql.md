# PostgreSQL Tuzaklari

> SQL, migration, jsonb, sabit sutun indeksi.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.

- **🚨 `jsonb` nesne anahtarlarını yeniden sıralar** (2026-08-02): PostgreSQL `jsonb` anahtarları önce uzunluğa, sonra bayta göre sıralar. System.Text.Json'ın polimorfik `$type` ayracı ilk özellik olmak zorundadır → okuma `JsonException: The metadata property ... is not the first property` verir. Opak veya polimorfik yükler **`json`** sütununda saklanır (`sessions.state`, `conversation_items.item`). Karar K-027.
- **Migration checksum'ı satır sonu farkına duyarlı olmamalı** (2026-08-02): `MigrationDescriptor.ComputeChecksum` CRLF'i LF'e normalleştirir. Aksi halde farklı `core.autocrlf` ayarıyla klonlanan depo "migration değişmiş" hatası verir.
- **Postgres span deposu kiracı bağlamıyla okur** (2026-08-02): sözleşme testi span'leri `"test"` kiracısına yazıp varsayılan kiracıyla okuyunca trace hiç bulunamadı. Yazma ve okuma aynı kiracıya düşmelidir.
- **🚨 PostgreSQL'de NULL sütun içeren benzersizlik `COALESCE` ile kurulur** (2026-08-02, Faz 11): `UNIQUE (tenant_id, skill_name, script_name)` NULL'ları farklı sayar ve kopya satırlar birikir. Doğrusu: `CREATE UNIQUE INDEX ... (tenant_id, skill_name, COALESCE(script_name, ''))`. `ON CONFLICT` yan tümcesi de aynı ifadeyi kullanmalıdır.
- **🚨 Bir sutunu FK yapmadan once gercek yukleme sirasini dusun** (2026-08-02, Faz 14): `attachments.session_id` FK ile denendi, gercek akista (ek run baslamadan/oturum satiri dogmadan once yuklenir) `INSERT` "violates foreign key constraint" ile bozuldu. Oturum silme kaskadi FK yerine uygulama katmaninda (`IAttachmentStore.DeleteBySessionAsync`) yapildi (K-112).
- **🚨 `AgentDefinitionPayload` (`AgentPrism.PostgreSql`) `AgentDefinition`'ı AYRI bir JSON şemasıyla serileştirir** (2026-08-02, Faz 13): `AgentPrismCoreJsonContext`'in kaynak ürettiği tip değil; elle yazılmış bir DTO. Yeni bir `AgentDefinition` alanı eklendiğinde `Internal/AgentDefinitionPayload.cs`'e de elle eklenmezse alan PostgreSQL'e **sessizce** yazılmaz/okunmaz — build ve test kırılmaz, yalnız round-trip testi yakalar. Aynı dosyada Faz 12'den kalma bağımsız bir hata (`CallableAgentNames` hiç yoktu) bulunup düzeltildi.
- **`runColumns`/`ReadRun` (PostgresRunStore) sabit sutun indeksi tasir** (2026-08-03, Faz 19): `runs` tablosuna sondan sutun eklerken (`agent_version`/`experiment_id`/`variant`) `runColumns` sabitindeki sira ile `ReadRun`'daki `reader.GetX(N)` indeksleri BIRLIKTE guncellenmeli — biri unutulursa derleme/test hatasi vermez, yanlis sutunu okur. Yeni sutunlar her zaman **sona** eklenir, var olan indeksleri kaydirmamak icin.
- **`audit_log` şeması zaten yeterli** (0001, satır 229): `actor`, `entity`, `before`, `after` sütunları var. Faz 9 için migration **gerekmiyor**, yalnız yazan kod eksik.
