-- AgentPrism SQL Server semasi (Faz 23).
--
-- Bu dosya PostgreSQL'in 0001-0013 migration'larinin BIRIKMIS sonucudur, adim
-- adim tekrari degil. SQL Server destegi Faz 23'te aciliyor; yukseltilecek bir
-- kurulum yoktur, bu yuzden gecmisi oynatmak yalnizca okunmasi zor bir dosya
-- uretirdi. Bundan sonraki degisiklikler 0002, 0003 ... olarak eklenir.
--
-- 🚨 Numaralandirma PostgreSQL ile ESLESMEZ ve eslesmesi gerekmez. Eslestirmeye
-- calismak, ileride bir saglayiciya ozel duzeltme gerektiginde kilitlenme
-- uretirdi. Gerekce: docs/KARARLAR.md, karar K-178.
--
-- Kurallar:
--   * Tuketicinin `dbo` semasina DOKUNULMAZ (karar K-013).
--   * `{schema}` yer tutucusu calisma aninda AgentPrismSqlServerOptions.SchemaName
--     ile degistirilir. Ad, SqlIdentifier.RequireSchemaName ile dogrulanir.
--   * Zaman alanlari `datetimeoffset(7)`, her zaman UTC yazilir.
--   * Birincil anahtarlar `uniqueidentifier` (uuid v7); uygulama uretir.
--   * Enum degerleri `smallint` olarak saklanir ve PostgreSQL ile AYNI sayilardir.
--
-- Tip esleme (PostgreSQL -> SQL Server):
--   uuid           -> uniqueidentifier
--   text (anahtar) -> nvarchar(200)      -- nvarchar(max) indekslenemez
--   text (serbest) -> nvarchar(max)
--   jsonb / json   -> nvarchar(max)      -- ISJSON kisiti ile
--   timestamptz    -> datetimeoffset(7)
--   boolean        -> bit
--   bytea          -> varbinary(max)
--   numeric(20,10) -> decimal(20,10)
--   text[]         -> nvarchar(max)      -- JSON dizi, OPENJSON ile acilir
--
-- 🚨 KUMELENMIS INDEKS STRATEJISI. SQL Server'in `uniqueidentifier` siralamasi
-- bayt sirasina gore DEGILDIR (son alti bayt once karsilastirilir); bu yuzden
-- zaman sirali uuid v7 anahtarlar SQL Server'da zaman sirali GORUNMEZ ve
-- kumelenmis bir birincil anahtar sayfa bolunmesi uretir. Yogun yazilan
-- tablolarda birincil anahtar NONCLUSTERED yapilir ve kumelenmis indeks zaman
-- sutununa kurulur. Dusuk hacimli yapilandirma tablolarinda varsayilan
-- (kumelenmis PK) korunur. Gerekce: docs/KARARLAR.md, karar K-180.

-- ---------------------------------------------------------------------------
-- Kiracilar
-- ---------------------------------------------------------------------------
-- Diger tablolardaki `tenant_id`, bu tablonun `slug` degeriyle ayni metindir
-- ancak YABANCI ANAHTAR ile baglanmaz; kaydi olmayan bir kiraci icin calisma
-- aninda beklenmedik hata uretirdi.

IF OBJECT_ID(N'{schema}.tenants', N'U') IS NULL
CREATE TABLE {schema}.tenants (
    id           uniqueidentifier  NOT NULL CONSTRAINT tenants_pk PRIMARY KEY,
    slug         nvarchar(200)     NOT NULL CONSTRAINT tenants_slug_uq UNIQUE,
    display_name nvarchar(200)     NOT NULL,
    created_at   datetimeoffset(7) NOT NULL
);

-- ---------------------------------------------------------------------------
-- Agent tanimlari
-- ---------------------------------------------------------------------------
-- `definition` yalnizca tanimin ICERIGINI tasir. Ad, surum, kiraci ve guncelleme
-- zamani sutunlardadir ve tek dogru kaynak orasidir (bkz. AgentDefinitionPayload).
--
-- PostgreSQL'de bu sutunda bir GIN indeksi vardir. SQL Server'in JSON yollarina
-- karsiligi hesaplanmis sutun + indekstir; su an hicbir sorgu tanimin ICINDE
-- arama yapmadigi icin indeks acilmaz. Ihtiyac dogarsa yeni bir migration ekler.

IF OBJECT_ID(N'{schema}.agent_definitions', N'U') IS NULL
CREATE TABLE {schema}.agent_definitions (
    id         uniqueidentifier  NOT NULL CONSTRAINT agent_definitions_pk PRIMARY KEY,
    tenant_id  nvarchar(200)     NOT NULL,
    name       nvarchar(200)     NOT NULL,
    version    int               NOT NULL,
    definition nvarchar(max)     NOT NULL CONSTRAINT agent_definitions_definition_json CHECK (ISJSON(definition) = 1),
    created_at datetimeoffset(7) NOT NULL,
    updated_at datetimeoffset(7) NOT NULL,
    CONSTRAINT agent_definitions_tenant_name_uq UNIQUE (tenant_id, name)
);

-- Degismez surum gecmisi. Geri alma eski surumu SILMEZ; icerigini yeni surum olarak yazar.
IF OBJECT_ID(N'{schema}.agent_definition_versions', N'U') IS NULL
CREATE TABLE {schema}.agent_definition_versions (
    id         uniqueidentifier  NOT NULL CONSTRAINT agent_definition_versions_pk PRIMARY KEY,
    agent_id   uniqueidentifier  NOT NULL
        CONSTRAINT agent_definition_versions_agent_fk REFERENCES {schema}.agent_definitions (id) ON DELETE CASCADE,
    version    int               NOT NULL,
    definition nvarchar(max)     NOT NULL CONSTRAINT agent_definition_versions_definition_json CHECK (ISJSON(definition) = 1),
    created_by nvarchar(200)     NULL,
    created_at datetimeoffset(7) NOT NULL,
    CONSTRAINT agent_definition_versions_agent_version_uq UNIQUE (agent_id, version)
);

-- ---------------------------------------------------------------------------
-- Oturumlar
-- ---------------------------------------------------------------------------
-- `state`, Microsoft Agent Framework'un SerializeSessionAsync ciktisidir ve OPAKTIR.
--
-- K-027 BURADA GECERLI DEGILDIR: PostgreSQL'de bu sutunun `json` (jsonb degil)
-- olmasinin sebebi jsonb'nin nesne anahtarlarini yeniden siralamasi ve
-- System.Text.Json'un polimorfik `$type` ayracini ilk ozellik olmaktan
-- cikarmasidir. SQL Server'da JSON metin olarak saklanir ve sira zaten korunur;
-- sorun kendiliginden yoktur. Bu, kararin yanlis oldugu anlamina GELMEZ --
-- PostgreSQL'de hala gecerlidir.

IF OBJECT_ID(N'{schema}.sessions', N'U') IS NULL
CREATE TABLE {schema}.sessions (
    id             nvarchar(200)     NOT NULL CONSTRAINT sessions_pk PRIMARY KEY,
    tenant_id      nvarchar(200)     NOT NULL,
    agent_name     nvarchar(200)     NOT NULL,
    state          nvarchar(max)     NOT NULL CONSTRAINT sessions_state_json CHECK (ISJSON(state) = 1),
    schema_version int               NOT NULL,
    created_at     datetimeoffset(7) NOT NULL,
    updated_at     datetimeoffset(7) NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'sessions_tenant_updated_idx' AND object_id = OBJECT_ID(N'{schema}.sessions'))
CREATE INDEX sessions_tenant_updated_idx ON {schema}.sessions (tenant_id, updated_at DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'sessions_tenant_agent_updated_idx' AND object_id = OBJECT_ID(N'{schema}.sessions'))
CREATE INDEX sessions_tenant_agent_updated_idx ON {schema}.sessions (tenant_id, agent_name, updated_at DESC);

-- ---------------------------------------------------------------------------
-- Konusmalar
-- ---------------------------------------------------------------------------

IF OBJECT_ID(N'{schema}.conversations', N'U') IS NULL
CREATE TABLE {schema}.conversations (
    id         uniqueidentifier  NOT NULL CONSTRAINT conversations_pk PRIMARY KEY,
    tenant_id  nvarchar(200)     NOT NULL,
    agent_name nvarchar(200)     NOT NULL,
    metadata   nvarchar(max)     NOT NULL CONSTRAINT conversations_metadata_default DEFAULT N'{}'
                                 CONSTRAINT conversations_metadata_json CHECK (ISJSON(metadata) = 1),
    created_at datetimeoffset(7) NOT NULL,
    updated_at datetimeoffset(7) NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'conversations_tenant_updated_idx' AND object_id = OBJECT_ID(N'{schema}.conversations'))
CREATE INDEX conversations_tenant_updated_idx ON {schema}.conversations (tenant_id, updated_at DESC);

-- Kumelenmis indeks (conversation_id, seq) uzerindedir: bir konusmanin
-- mesajlari her zaman birlikte okunur ve ekleme o konusma icinde sirali gider.
IF OBJECT_ID(N'{schema}.conversation_items', N'U') IS NULL
CREATE TABLE {schema}.conversation_items (
    id              uniqueidentifier  NOT NULL CONSTRAINT conversation_items_pk PRIMARY KEY NONCLUSTERED,
    conversation_id uniqueidentifier  NOT NULL
        CONSTRAINT conversation_items_conversation_fk REFERENCES {schema}.conversations (id) ON DELETE CASCADE,
    seq             bigint            NOT NULL,
    item            nvarchar(max)     NOT NULL CONSTRAINT conversation_items_item_json CHECK (ISJSON(item) = 1),
    created_at      datetimeoffset(7) NOT NULL,
    CONSTRAINT conversation_items_conversation_seq_uq UNIQUE CLUSTERED (conversation_id, seq)
);

IF OBJECT_ID(N'{schema}.responses', N'U') IS NULL
CREATE TABLE {schema}.responses (
    id              uniqueidentifier  NOT NULL CONSTRAINT responses_pk PRIMARY KEY,
    conversation_id uniqueidentifier  NULL
        CONSTRAINT responses_conversation_fk REFERENCES {schema}.conversations (id) ON DELETE CASCADE,
    session_id      nvarchar(200)     NULL,
    payload         nvarchar(max)     NOT NULL CONSTRAINT responses_payload_json CHECK (ISJSON(payload) = 1),
    created_at      datetimeoffset(7) NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'responses_conversation_idx' AND object_id = OBJECT_ID(N'{schema}.responses'))
CREATE INDEX responses_conversation_idx ON {schema}.responses (conversation_id, created_at DESC);

-- ---------------------------------------------------------------------------
-- Calistirmalar
-- ---------------------------------------------------------------------------
-- Sutunlar PostgreSQL'in 0001 + 0002 + 0005 + 0007 + 0010 + 0011 birikimidir.
--
-- `error_message`, `variant` disindaki metin sutunlari indeksli oldugu icin
-- boyutludur. `pricing_source`: 0=Catalog 1=Configuration 2=Unknown.

IF OBJECT_ID(N'{schema}.runs', N'U') IS NULL
CREATE TABLE {schema}.runs (
    id             uniqueidentifier  NOT NULL CONSTRAINT runs_pk PRIMARY KEY NONCLUSTERED,
    tenant_id      nvarchar(200)     NOT NULL,
    agent_name     nvarchar(200)     NOT NULL,
    session_id     nvarchar(200)     NULL,
    status         smallint          NOT NULL,
    started_at     datetimeoffset(7) NOT NULL,
    completed_at   datetimeoffset(7) NULL,
    is_streaming   bit               NOT NULL,
    input_tokens   bigint            NULL,
    output_tokens  bigint            NULL,
    total_tokens   bigint            NULL,
    event_count    bigint            NOT NULL CONSTRAINT runs_event_count_default DEFAULT 0,
    error_type     nvarchar(200)     NULL,
    error_message  nvarchar(max)     NULL,
    model_id       nvarchar(200)     NULL,
    parent_run_id  uniqueidentifier  NULL,
    root_run_id    uniqueidentifier  NULL,
    depth          smallint          NOT NULL CONSTRAINT runs_depth_default DEFAULT 0,
    kind           smallint          NOT NULL CONSTRAINT runs_kind_default DEFAULT 0,
    workflow_name  nvarchar(200)     NULL,
    agent_version  int               NULL,
    experiment_id  uniqueidentifier  NULL,
    variant        nvarchar(200)     NULL,
    input_cost     decimal(20,10)    NULL,
    output_cost    decimal(20,10)    NULL,
    cost_currency  nvarchar(200)     NULL,
    pricing_source smallint          NULL
);

-- Kumelenmis anahtar dar ve zaman siralidir; boylece ekleme her zaman sonuna
-- gider ve sayfa bolunmesi olusmaz. `id` yalnizca benzersizligi saglar.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_cx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE UNIQUE CLUSTERED INDEX runs_cx ON {schema}.runs (started_at, id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_tenant_started_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_tenant_started_idx ON {schema}.runs (tenant_id, started_at DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_tenant_agent_started_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_tenant_agent_started_idx ON {schema}.runs (tenant_id, agent_name, started_at DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_tenant_status_started_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_tenant_status_started_idx ON {schema}.runs (tenant_id, status, started_at DESC);

-- Filtrelenmis indeks PostgreSQL'in kismi indeksinin birebir karsiligidir.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_session_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_session_idx ON {schema}.runs (tenant_id, session_id, started_at DESC) WHERE session_id IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_tenant_model_started_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_tenant_model_started_idx ON {schema}.runs (tenant_id, model_id, started_at DESC) WHERE model_id IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_parent_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_parent_idx ON {schema}.runs (parent_run_id) WHERE parent_run_id IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_root_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_root_idx ON {schema}.runs (tenant_id, root_run_id, started_at) WHERE root_run_id IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_roots_only_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_roots_only_idx ON {schema}.runs (tenant_id, started_at DESC) WHERE parent_run_id IS NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_workflow_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_workflow_idx ON {schema}.runs (tenant_id, workflow_name, started_at DESC) WHERE kind = 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_agent_version_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_agent_version_idx ON {schema}.runs (tenant_id, agent_name, agent_version, started_at DESC) WHERE agent_version IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_experiment_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_experiment_idx ON {schema}.runs (experiment_id, variant) WHERE experiment_id IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_tenant_cost_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_tenant_cost_idx ON {schema}.runs (tenant_id, started_at DESC) WHERE input_cost IS NOT NULL;

-- Append-only olay akisi (karar K-014). Olaylar GUNCELLENMEZ, yalnizca eklenir.
--
-- `payload` bilerek serbest metindir ve ISJSON kisiti TASIMAZ: RunEventWriter
-- tool argumanlarini AOT uyumlu kalmak icin elle bicimlendirir ve cikti gecerli
-- JSON olmayabilir. Kisit bu durumda calistirmayi kesen bir hata uretirdi.
IF OBJECT_ID(N'{schema}.run_events', N'U') IS NULL
CREATE TABLE {schema}.run_events (
    run_id       uniqueidentifier  NOT NULL
        CONSTRAINT run_events_run_fk REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    seq          bigint            NOT NULL,
    type         smallint          NOT NULL,
    text         nvarchar(max)     NULL,
    tool_name    nvarchar(200)     NULL,
    tool_call_id nvarchar(200)     NULL,
    payload      nvarchar(max)     NULL,
    created_at   datetimeoffset(7) NOT NULL,
    CONSTRAINT run_events_pk PRIMARY KEY CLUSTERED (run_id, seq)
);

-- `arguments` ve `result` serbest metindir (PostgreSQL 0002 ile ayni gerekce).
IF OBJECT_ID(N'{schema}.tool_invocations', N'U') IS NULL
CREATE TABLE {schema}.tool_invocations (
    id           uniqueidentifier  NOT NULL CONSTRAINT tool_invocations_pk PRIMARY KEY NONCLUSTERED,
    run_id       uniqueidentifier  NOT NULL
        CONSTRAINT tool_invocations_run_fk REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    tool_name    nvarchar(200)     NOT NULL,
    tool_call_id nvarchar(200)     NULL,
    source       nvarchar(200)     NULL,
    arguments    nvarchar(max)     NULL,
    result       nvarchar(max)     NULL,
    duration_ms  int               NULL,
    error        nvarchar(max)     NULL,
    created_at   datetimeoffset(7) NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'tool_invocations_cx' AND object_id = OBJECT_ID(N'{schema}.tool_invocations'))
CREATE UNIQUE CLUSTERED INDEX tool_invocations_cx ON {schema}.tool_invocations (created_at, id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'tool_invocations_run_idx' AND object_id = OBJECT_ID(N'{schema}.tool_invocations'))
CREATE INDEX tool_invocations_run_idx ON {schema}.tool_invocations (run_id, created_at);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'tool_invocations_tool_created_idx' AND object_id = OBJECT_ID(N'{schema}.tool_invocations'))
CREATE INDEX tool_invocations_tool_created_idx ON {schema}.tool_invocations (tool_name, created_at DESC);

-- ---------------------------------------------------------------------------
-- Gozlemlenebilirlik ve denetim
-- ---------------------------------------------------------------------------

IF OBJECT_ID(N'{schema}.traces', N'U') IS NULL
CREATE TABLE {schema}.traces (
    id         uniqueidentifier  NOT NULL CONSTRAINT traces_pk PRIMARY KEY,
    tenant_id  nvarchar(200)     NOT NULL,
    trace_id   nvarchar(64)      NOT NULL,
    run_id     uniqueidentifier  NULL
        CONSTRAINT traces_run_fk REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    started_at datetimeoffset(7) NOT NULL,
    ended_at   datetimeoffset(7) NULL,
    CONSTRAINT traces_tenant_trace_uq UNIQUE (tenant_id, trace_id)
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'traces_run_idx' AND object_id = OBJECT_ID(N'{schema}.traces'))
CREATE INDEX traces_run_idx ON {schema}.traces (run_id) WHERE run_id IS NOT NULL;

IF OBJECT_ID(N'{schema}.spans', N'U') IS NULL
CREATE TABLE {schema}.spans (
    id             uniqueidentifier  NOT NULL CONSTRAINT spans_pk PRIMARY KEY NONCLUSTERED,
    trace_id       uniqueidentifier  NOT NULL
        CONSTRAINT spans_trace_fk REFERENCES {schema}.traces (id) ON DELETE CASCADE,
    parent_span_id uniqueidentifier  NULL,
    name           nvarchar(200)     NOT NULL,
    kind           smallint          NOT NULL,
    started_at     datetimeoffset(7) NOT NULL,
    ended_at       datetimeoffset(7) NULL,
    attributes     nvarchar(max)     NULL CONSTRAINT spans_attributes_json CHECK (attributes IS NULL OR ISJSON(attributes) = 1),
    status         smallint          NULL,
    span_id        nvarchar(64)      NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'spans_cx' AND object_id = OBJECT_ID(N'{schema}.spans'))
CREATE UNIQUE CLUSTERED INDEX spans_cx ON {schema}.spans (started_at, id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'spans_trace_started_idx' AND object_id = OBJECT_ID(N'{schema}.spans'))
CREATE INDEX spans_trace_started_idx ON {schema}.spans (trace_id, started_at);

-- `before` / `after` ISJSON kisiti TASIMAZ. Gozlemlenebilirlik islevselligi
-- bozmaz: denetim izi yazimi bir kisit ihlaliyle basarisiz olursa asil islem de
-- basarisiz olurdu. Kisitin degeri, tasidigi riski karsilamiyor.
IF OBJECT_ID(N'{schema}.audit_log', N'U') IS NULL
CREATE TABLE {schema}.audit_log (
    id         uniqueidentifier  NOT NULL CONSTRAINT audit_log_pk PRIMARY KEY NONCLUSTERED,
    tenant_id  nvarchar(200)     NOT NULL,
    actor      nvarchar(200)     NULL,
    action     nvarchar(200)     NOT NULL,
    entity     nvarchar(200)     NOT NULL,
    before     nvarchar(max)     NULL,
    after      nvarchar(max)     NULL,
    created_at datetimeoffset(7) NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'audit_log_cx' AND object_id = OBJECT_ID(N'{schema}.audit_log'))
CREATE UNIQUE CLUSTERED INDEX audit_log_cx ON {schema}.audit_log (created_at, id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'audit_log_tenant_created_idx' AND object_id = OBJECT_ID(N'{schema}.audit_log'))
CREATE INDEX audit_log_tenant_created_idx ON {schema}.audit_log (tenant_id, created_at DESC);

-- ---------------------------------------------------------------------------
-- Tool onay kurallari
-- ---------------------------------------------------------------------------
-- 🚨 PostgreSQL burada COALESCE'li bir ifade indeksi kullanir cunku PostgreSQL'de
-- NULL hicbir NULL'a esit degildir ve duz bir UNIQUE ayni kuralin sinirsiz kez
-- eklenmesine izin verirdi. SQL SERVER'DA BU GEREKMEZ: benzersiz indeks NULL'lari
-- birbirine ESIT sayar ve tek bir NULL satirina izin verir -- istenen davranis
-- zaten budur. Gerekce: docs/KARARLAR.md, karar K-184.

IF OBJECT_ID(N'{schema}.tool_approval_rules', N'U') IS NULL
CREATE TABLE {schema}.tool_approval_rules (
    id             uniqueidentifier  NOT NULL CONSTRAINT tool_approval_rules_pk PRIMARY KEY,
    tenant_id      nvarchar(200)     NOT NULL,
    agent_name     nvarchar(200)     NULL,
    tool_name      nvarchar(200)     NOT NULL,
    arguments_hash nvarchar(128)     NULL,
    created_by     nvarchar(200)     NULL,
    created_at     datetimeoffset(7) NOT NULL,
    CONSTRAINT tool_approval_rules_scope_uq UNIQUE (tenant_id, agent_name, tool_name, arguments_hash)
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'tool_approval_rules_tenant_created_idx' AND object_id = OBJECT_ID(N'{schema}.tool_approval_rules'))
CREATE INDEX tool_approval_rules_tenant_created_idx ON {schema}.tool_approval_rules (tenant_id, created_at DESC);

-- ---------------------------------------------------------------------------
-- MCP sunuculari
-- ---------------------------------------------------------------------------
-- 🚨 SIR TASIMAZ. Kimlik dogrulama basliginin ve OAuth istemci sirrinin DEGERI
-- burada saklanmaz; yalnizca degerin okunacagi yapilandirma anahtarinin ADI
-- saklanir. Deger calisma aninda IConfiguration uzerinden cozulur.
-- Gerekce: docs/KARARLAR.md, karar K-059.
--
-- `transport` smallint: 0 = StreamableHttp, 1 = SSE. Stdio BILEREK YOKTUR (K-058).

IF OBJECT_ID(N'{schema}.mcp_servers', N'U') IS NULL
CREATE TABLE {schema}.mcp_servers (
    id                                    uniqueidentifier  NOT NULL CONSTRAINT mcp_servers_pk PRIMARY KEY,
    tenant_id                             nvarchar(200)     NOT NULL,
    name                                  nvarchar(200)     NOT NULL,
    description                           nvarchar(max)     NULL,
    endpoint                              nvarchar(2000)    NOT NULL,
    transport                             smallint          NOT NULL CONSTRAINT mcp_servers_transport_default DEFAULT 0,
    authorization_configuration_key       nvarchar(200)     NULL,
    headers                               nvarchar(max)     NOT NULL
        CONSTRAINT mcp_servers_headers_default DEFAULT N'{}'
        CONSTRAINT mcp_servers_headers_json CHECK (ISJSON(headers) = 1),
    enabled                               bit               NOT NULL CONSTRAINT mcp_servers_enabled_default DEFAULT 1,
    requires_approval                     bit               NOT NULL CONSTRAINT mcp_servers_requires_approval_default DEFAULT 1,
    created_at                            datetimeoffset(7) NOT NULL,
    updated_at                            datetimeoffset(7) NOT NULL,
    oauth_enabled                         bit               NOT NULL CONSTRAINT mcp_servers_oauth_enabled_default DEFAULT 0,
    oauth_client_id                       nvarchar(200)     NULL,
    oauth_client_secret_configuration_key nvarchar(200)     NULL,
    oauth_scopes                          nvarchar(max)     NULL,
    oauth_authorization_mode              smallint          NOT NULL CONSTRAINT mcp_servers_oauth_mode_default DEFAULT 0,
    CONSTRAINT mcp_servers_tenant_name_uq UNIQUE (tenant_id, name)
);

-- ---------------------------------------------------------------------------
-- Agent skill'leri
-- ---------------------------------------------------------------------------

IF OBJECT_ID(N'{schema}.agent_skills', N'U') IS NULL
CREATE TABLE {schema}.agent_skills (
    id            uniqueidentifier  NOT NULL CONSTRAINT agent_skills_pk PRIMARY KEY,
    tenant_id     nvarchar(200)     NOT NULL,
    name          nvarchar(200)     NOT NULL,
    description   nvarchar(max)     NOT NULL,
    instructions  nvarchar(max)     NOT NULL,
    compatibility nvarchar(max)     NULL,
    license       nvarchar(max)     NULL,
    allowed_tools nvarchar(max)     NULL,
    metadata      nvarchar(max)     NOT NULL
        CONSTRAINT agent_skills_metadata_default DEFAULT N'{}'
        CONSTRAINT agent_skills_metadata_json CHECK (ISJSON(metadata) = 1),
    enabled       bit               NOT NULL CONSTRAINT agent_skills_enabled_default DEFAULT 1,
    version       int               NOT NULL CONSTRAINT agent_skills_version_default DEFAULT 1,
    created_at    datetimeoffset(7) NOT NULL,
    updated_at    datetimeoffset(7) NOT NULL,
    CONSTRAINT agent_skills_tenant_name_uq UNIQUE (tenant_id, name)
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'agent_skills_tenant_enabled_updated_idx' AND object_id = OBJECT_ID(N'{schema}.agent_skills'))
CREATE INDEX agent_skills_tenant_enabled_updated_idx ON {schema}.agent_skills (tenant_id, enabled, updated_at DESC);

IF OBJECT_ID(N'{schema}.agent_skill_resources', N'U') IS NULL
CREATE TABLE {schema}.agent_skill_resources (
    id          uniqueidentifier  NOT NULL CONSTRAINT agent_skill_resources_pk PRIMARY KEY,
    skill_id    uniqueidentifier  NOT NULL
        CONSTRAINT agent_skill_resources_skill_fk REFERENCES {schema}.agent_skills (id) ON DELETE CASCADE,
    name        nvarchar(200)     NOT NULL,
    description nvarchar(max)     NULL,
    media_type  nvarchar(200)     NOT NULL CONSTRAINT agent_skill_resources_media_default DEFAULT N'text/plain',
    content     nvarchar(max)     NOT NULL,
    created_at  datetimeoffset(7) NOT NULL,
    CONSTRAINT agent_skill_resources_skill_name_uq UNIQUE (skill_id, name)
);

IF OBJECT_ID(N'{schema}.agent_skill_scripts', N'U') IS NULL
CREATE TABLE {schema}.agent_skill_scripts (
    id                uniqueidentifier  NOT NULL CONSTRAINT agent_skill_scripts_pk PRIMARY KEY,
    skill_id          uniqueidentifier  NOT NULL
        CONSTRAINT agent_skill_scripts_skill_fk REFERENCES {schema}.agent_skills (id) ON DELETE CASCADE,
    name              nvarchar(200)     NOT NULL,
    description       nvarchar(max)     NULL,
    extension         nvarchar(200)     NOT NULL,
    content           nvarchar(max)     NOT NULL,
    parameters_schema nvarchar(max)     NULL
        CONSTRAINT agent_skill_scripts_parameters_json CHECK (parameters_schema IS NULL OR ISJSON(parameters_schema) = 1),
    created_at        datetimeoffset(7) NOT NULL,
    CONSTRAINT agent_skill_scripts_skill_name_uq UNIQUE (skill_id, name)
);

-- SQL Server benzersiz indekste NULL'lari esit sayar; COALESCE'li ifade indeksi
-- gerekmez (K-184).
IF OBJECT_ID(N'{schema}.skill_script_grants', N'U') IS NULL
CREATE TABLE {schema}.skill_script_grants (
    id          uniqueidentifier  NOT NULL CONSTRAINT skill_script_grants_pk PRIMARY KEY,
    tenant_id   nvarchar(200)     NOT NULL,
    skill_name  nvarchar(200)     NOT NULL,
    script_name nvarchar(200)     NULL,
    granted_by  nvarchar(200)     NULL,
    granted_at  datetimeoffset(7) NOT NULL,
    expires_at  datetimeoffset(7) NULL,
    revoked_at  datetimeoffset(7) NULL,
    CONSTRAINT skill_script_grants_uq UNIQUE (tenant_id, skill_name, script_name)
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'skill_script_grants_lookup_idx' AND object_id = OBJECT_ID(N'{schema}.skill_script_grants'))
CREATE INDEX skill_script_grants_lookup_idx ON {schema}.skill_script_grants (tenant_id, skill_name) WHERE revoked_at IS NULL;

-- ---------------------------------------------------------------------------
-- Ekler ve kalici agent dosya bellegi
-- ---------------------------------------------------------------------------
-- `session_id` KASITLI OLARAK yabanci anahtar DEGILDIR: bir ek, kendi oturumu
-- hic acilmadan once yuklenebilir (K-112).

IF OBJECT_ID(N'{schema}.attachments', N'U') IS NULL
CREATE TABLE {schema}.attachments (
    id           uniqueidentifier  NOT NULL CONSTRAINT attachments_pk PRIMARY KEY NONCLUSTERED,
    tenant_id    nvarchar(200)     NOT NULL,
    session_id   nvarchar(200)     NULL,
    run_id       uniqueidentifier  NULL,
    file_name    nvarchar(400)     NOT NULL,
    media_type   nvarchar(200)     NOT NULL,
    byte_size    bigint            NOT NULL,
    sha256       nvarchar(64)      NOT NULL,
    content      varbinary(max)    NULL,
    external_uri nvarchar(2000)    NULL,
    created_by   nvarchar(200)     NULL,
    created_at   datetimeoffset(7) NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'attachments_cx' AND object_id = OBJECT_ID(N'{schema}.attachments'))
CREATE UNIQUE CLUSTERED INDEX attachments_cx ON {schema}.attachments (created_at, id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'attachments_tenant_created_idx' AND object_id = OBJECT_ID(N'{schema}.attachments'))
CREATE INDEX attachments_tenant_created_idx ON {schema}.attachments (tenant_id, created_at DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'attachments_session_idx' AND object_id = OBJECT_ID(N'{schema}.attachments'))
CREATE INDEX attachments_session_idx ON {schema}.attachments (tenant_id, session_id) WHERE session_id IS NOT NULL;

IF OBJECT_ID(N'{schema}.agent_files', N'U') IS NULL
CREATE TABLE {schema}.agent_files (
    id         uniqueidentifier  NOT NULL CONSTRAINT agent_files_pk PRIMARY KEY,
    tenant_id  nvarchar(200)     NOT NULL,
    agent_name nvarchar(200)     NOT NULL,
    path       nvarchar(400)     NOT NULL,
    content    nvarchar(max)     NOT NULL,
    created_at datetimeoffset(7) NOT NULL,
    updated_at datetimeoffset(7) NOT NULL,
    CONSTRAINT agent_files_tenant_agent_path_uq UNIQUE (tenant_id, agent_name, path)
);

-- ---------------------------------------------------------------------------
-- Workflow'lar
-- ---------------------------------------------------------------------------

IF OBJECT_ID(N'{schema}.workflows', N'U') IS NULL
CREATE TABLE {schema}.workflows (
    id         uniqueidentifier  NOT NULL CONSTRAINT workflows_pk PRIMARY KEY,
    tenant_id  nvarchar(200)     NOT NULL,
    name       nvarchar(200)     NOT NULL,
    version    int               NOT NULL,
    definition nvarchar(max)     NOT NULL CONSTRAINT workflows_definition_json CHECK (ISJSON(definition) = 1),
    created_at datetimeoffset(7) NOT NULL,
    updated_at datetimeoffset(7) NOT NULL,
    CONSTRAINT workflows_tenant_name_uq UNIQUE (tenant_id, name)
);

-- K-027 burada gecerli degildir (bkz. sessions.state notu): SQL Server JSON'u
-- metin olarak saklar ve `$type` ayraci ilk ozellik olmaya devam eder.
IF OBJECT_ID(N'{schema}.workflow_checkpoints', N'U') IS NULL
CREATE TABLE {schema}.workflow_checkpoints (
    id            uniqueidentifier  NOT NULL CONSTRAINT workflow_checkpoints_pk PRIMARY KEY,
    tenant_id     nvarchar(200)     NOT NULL,
    session_id    nvarchar(200)     NOT NULL,
    checkpoint_id nvarchar(200)     NOT NULL,
    parent_id     nvarchar(200)     NULL,
    run_id        uniqueidentifier  NULL,
    state         nvarchar(max)     NOT NULL CONSTRAINT workflow_checkpoints_state_json CHECK (ISJSON(state) = 1),
    created_at    datetimeoffset(7) NOT NULL,
    CONSTRAINT workflow_checkpoints_uq UNIQUE (tenant_id, session_id, checkpoint_id)
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'workflow_checkpoints_session_idx' AND object_id = OBJECT_ID(N'{schema}.workflow_checkpoints'))
CREATE INDEX workflow_checkpoints_session_idx ON {schema}.workflow_checkpoints (tenant_id, session_id, created_at);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'workflow_checkpoints_run_idx' AND object_id = OBJECT_ID(N'{schema}.workflow_checkpoints'))
CREATE INDEX workflow_checkpoints_run_idx ON {schema}.workflow_checkpoints (tenant_id, run_id, created_at) WHERE run_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- Is kuyrugu ve zamanlama
-- ---------------------------------------------------------------------------

IF OBJECT_ID(N'{schema}.job_schedules', N'U') IS NULL
CREATE TABLE {schema}.job_schedules (
    id          uniqueidentifier  NOT NULL CONSTRAINT job_schedules_pk PRIMARY KEY,
    tenant_id   nvarchar(200)     NOT NULL,
    name        nvarchar(200)     NOT NULL,
    kind        smallint          NOT NULL,
    target_name nvarchar(200)     NOT NULL,
    cron        nvarchar(200)     NULL,
    time_zone   nvarchar(200)     NOT NULL CONSTRAINT job_schedules_time_zone_default DEFAULT N'UTC',
    payload     nvarchar(max)     NOT NULL CONSTRAINT job_schedules_payload_json CHECK (ISJSON(payload) = 1),
    enabled     bit               NOT NULL CONSTRAINT job_schedules_enabled_default DEFAULT 1,
    next_run_at datetimeoffset(7) NULL,
    last_run_at datetimeoffset(7) NULL,
    created_by  nvarchar(200)     NULL,
    created_at  datetimeoffset(7) NOT NULL,
    updated_at  datetimeoffset(7) NOT NULL,
    CONSTRAINT job_schedules_tenant_name_uq UNIQUE (tenant_id, name)
);

-- `schedule_id` uzerinde ON DELETE SET NULL kullanilir; jobs -> job_items zinciri
-- CASCADE tasidigi icin cok yollu bir kaskad olusmaz (SQL Server hata 1785).
IF OBJECT_ID(N'{schema}.jobs', N'U') IS NULL
CREATE TABLE {schema}.jobs (
    id            uniqueidentifier  NOT NULL CONSTRAINT jobs_pk PRIMARY KEY,
    tenant_id     nvarchar(200)     NOT NULL,
    schedule_id   uniqueidentifier  NULL
        CONSTRAINT jobs_schedule_fk REFERENCES {schema}.job_schedules (id) ON DELETE SET NULL,
    kind          smallint          NOT NULL,
    target_name   nvarchar(200)     NOT NULL,
    status        smallint          NOT NULL,
    payload       nvarchar(max)     NOT NULL CONSTRAINT jobs_payload_json CHECK (ISJSON(payload) = 1),
    total_items   int               NOT NULL CONSTRAINT jobs_total_items_default DEFAULT 0,
    done_items    int               NOT NULL CONSTRAINT jobs_done_items_default DEFAULT 0,
    failed_items  int               NOT NULL CONSTRAINT jobs_failed_items_default DEFAULT 0,
    attempt       smallint          NOT NULL CONSTRAINT jobs_attempt_default DEFAULT 0,
    lease_owner   nvarchar(200)     NULL,
    lease_until   datetimeoffset(7) NULL,
    scheduled_for datetimeoffset(7) NOT NULL,
    started_at    datetimeoffset(7) NULL,
    completed_at  datetimeoffset(7) NULL,
    error_message nvarchar(max)     NULL,
    created_at    datetimeoffset(7) NOT NULL,
    max_attempts  smallint          NULL
);

-- 🚨 PostgreSQL'de NULL schedule_id'ler birbirinden ayirt edilir ve elle
-- olusturulan (zamanlamasiz) isler UNIQUE (schedule_id, scheduled_for) kisitiyla
-- cakismaz. SQL Server NULL'lari ESIT sayar; duz bir benzersiz kisit ikinci bir
-- zamanlamasiz isin eklenmesini engellerdi. Bu yuzden kisit FILTRELENMIS bir
-- benzersiz indekstir ve yalnizca zamanlamaya bagli isleri kapsar.
-- Gerekce: docs/KARARLAR.md, karar K-184.

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'jobs_schedule_scheduled_uq' AND object_id = OBJECT_ID(N'{schema}.jobs'))
CREATE UNIQUE INDEX jobs_schedule_scheduled_uq ON {schema}.jobs (schedule_id, scheduled_for) WHERE schedule_id IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'jobs_claim_idx' AND object_id = OBJECT_ID(N'{schema}.jobs'))
CREATE INDEX jobs_claim_idx ON {schema}.jobs (status, scheduled_for) WHERE status IN (0, 1);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'jobs_tenant_created_idx' AND object_id = OBJECT_ID(N'{schema}.jobs'))
CREATE INDEX jobs_tenant_created_idx ON {schema}.jobs (tenant_id, created_at DESC);

IF OBJECT_ID(N'{schema}.job_items', N'U') IS NULL
CREATE TABLE {schema}.job_items (
    id     uniqueidentifier NOT NULL CONSTRAINT job_items_pk PRIMARY KEY NONCLUSTERED,
    job_id uniqueidentifier NOT NULL
        CONSTRAINT job_items_job_fk REFERENCES {schema}.jobs (id) ON DELETE CASCADE,
    seq    int              NOT NULL,
    input  nvarchar(max)    NOT NULL,
    run_id uniqueidentifier NULL,
    status smallint         NOT NULL,
    error  nvarchar(max)    NULL,
    CONSTRAINT job_items_job_seq_uq UNIQUE CLUSTERED (job_id, seq)
);

-- ---------------------------------------------------------------------------
-- Degerlendirme (eval)
-- ---------------------------------------------------------------------------

IF OBJECT_ID(N'{schema}.eval_suites', N'U') IS NULL
CREATE TABLE {schema}.eval_suites (
    id          uniqueidentifier  NOT NULL CONSTRAINT eval_suites_pk PRIMARY KEY,
    tenant_id   nvarchar(200)     NOT NULL,
    name        nvarchar(200)     NOT NULL,
    description nvarchar(max)     NULL,
    agent_name  nvarchar(200)     NOT NULL,
    checks      nvarchar(max)     NOT NULL
        CONSTRAINT eval_suites_checks_default DEFAULT N'[]'
        CONSTRAINT eval_suites_checks_json CHECK (ISJSON(checks) = 1),
    created_at  datetimeoffset(7) NOT NULL,
    updated_at  datetimeoffset(7) NOT NULL,
    CONSTRAINT eval_suites_tenant_name_uq UNIQUE (tenant_id, name)
);

IF OBJECT_ID(N'{schema}.eval_cases', N'U') IS NULL
CREATE TABLE {schema}.eval_cases (
    id              uniqueidentifier NOT NULL CONSTRAINT eval_cases_pk PRIMARY KEY,
    suite_id        uniqueidentifier NOT NULL
        CONSTRAINT eval_cases_suite_fk REFERENCES {schema}.eval_suites (id) ON DELETE CASCADE,
    seq             int              NOT NULL,
    query           nvarchar(max)    NOT NULL,
    expected_output nvarchar(max)    NULL,
    expected_tools  nvarchar(max)    NULL,
    context         nvarchar(max)    NULL,
    CONSTRAINT eval_cases_suite_seq_uq UNIQUE (suite_id, seq)
);

IF OBJECT_ID(N'{schema}.eval_runs', N'U') IS NULL
CREATE TABLE {schema}.eval_runs (
    id            uniqueidentifier  NOT NULL CONSTRAINT eval_runs_pk PRIMARY KEY,
    tenant_id     nvarchar(200)     NOT NULL,
    suite_id      uniqueidentifier  NOT NULL
        CONSTRAINT eval_runs_suite_fk REFERENCES {schema}.eval_suites (id) ON DELETE CASCADE,
    job_id        uniqueidentifier  NULL,
    agent_version int               NULL,
    model_id      nvarchar(200)     NULL,
    status        smallint          NOT NULL,
    total         int               NOT NULL CONSTRAINT eval_runs_total_default DEFAULT 0,
    passed        int               NOT NULL CONSTRAINT eval_runs_passed_default DEFAULT 0,
    failed        int               NOT NULL CONSTRAINT eval_runs_failed_default DEFAULT 0,
    input_tokens  bigint            NULL,
    output_tokens bigint            NULL,
    started_at    datetimeoffset(7) NOT NULL,
    completed_at  datetimeoffset(7) NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'eval_runs_suite_started_idx' AND object_id = OBJECT_ID(N'{schema}.eval_runs'))
CREATE INDEX eval_runs_suite_started_idx ON {schema}.eval_runs (tenant_id, suite_id, started_at DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'eval_runs_job_idx' AND object_id = OBJECT_ID(N'{schema}.eval_runs'))
CREATE INDEX eval_runs_job_idx ON {schema}.eval_runs (tenant_id, job_id);

-- `case_id` KASITLI OLARAK yabanci anahtar tasimaz: bir vaka sonradan
-- degistirilse veya silinse bile gecmis sonuc kaydi anlasilir kalir.
IF OBJECT_ID(N'{schema}.eval_case_results', N'U') IS NULL
CREATE TABLE {schema}.eval_case_results (
    id             uniqueidentifier NOT NULL CONSTRAINT eval_case_results_pk PRIMARY KEY NONCLUSTERED,
    eval_run_id    uniqueidentifier NOT NULL
        CONSTRAINT eval_case_results_run_fk REFERENCES {schema}.eval_runs (id) ON DELETE CASCADE,
    case_id        uniqueidentifier NOT NULL,
    run_id         uniqueidentifier NULL,
    passed         bit              NOT NULL,
    output         nvarchar(max)    NULL,
    scores         nvarchar(max)    NOT NULL
        CONSTRAINT eval_case_results_scores_default DEFAULT N'[]'
        CONSTRAINT eval_case_results_scores_json CHECK (ISJSON(scores) = 1),
    failure_reason nvarchar(max)    NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'eval_case_results_cx' AND object_id = OBJECT_ID(N'{schema}.eval_case_results'))
CREATE UNIQUE CLUSTERED INDEX eval_case_results_cx ON {schema}.eval_case_results (eval_run_id, id);

-- ---------------------------------------------------------------------------
-- A/B deneyleri
-- ---------------------------------------------------------------------------

IF OBJECT_ID(N'{schema}.experiments', N'U') IS NULL
CREATE TABLE {schema}.experiments (
    id             uniqueidentifier  NOT NULL CONSTRAINT experiments_pk PRIMARY KEY,
    tenant_id      nvarchar(200)     NOT NULL,
    name           nvarchar(200)     NOT NULL,
    agent_name     nvarchar(200)     NOT NULL,
    variants       nvarchar(max)     NOT NULL CONSTRAINT experiments_variants_json CHECK (ISJSON(variants) = 1),
    status         smallint          NOT NULL CONSTRAINT experiments_status_default DEFAULT 0,
    assignment_key nvarchar(200)     NULL,
    started_at     datetimeoffset(7) NULL,
    ended_at       datetimeoffset(7) NULL,
    updated_at     datetimeoffset(7) NOT NULL,
    CONSTRAINT experiments_tenant_name_uq UNIQUE (tenant_id, name)
);

-- Ayni agent icin ayni anda TEK Running deney olabilir. Filtrelenmis benzersiz
-- indeks bu kurali uygulama kodu race'ine birakmadan veritabaninda zorlar.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'experiments_running_agent_uq' AND object_id = OBJECT_ID(N'{schema}.experiments'))
CREATE UNIQUE INDEX experiments_running_agent_uq ON {schema}.experiments (tenant_id, agent_name) WHERE status = 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'experiments_tenant_agent_idx' AND object_id = OBJECT_ID(N'{schema}.experiments'))
CREATE INDEX experiments_tenant_agent_idx ON {schema}.experiments (tenant_id, agent_name);

-- ---------------------------------------------------------------------------
-- Kota ve olay yayini
-- ---------------------------------------------------------------------------
-- SQL Server benzersiz indekste NULL'lari esit sayar; `agent_name` NULL olan
-- kural yalnizca bir kez eklenebilir (K-184).

IF OBJECT_ID(N'{schema}.quotas', N'U') IS NULL
CREATE TABLE {schema}.quotas (
    id         uniqueidentifier  NOT NULL CONSTRAINT quotas_pk PRIMARY KEY,
    tenant_id  nvarchar(200)     NOT NULL,
    agent_name nvarchar(200)     NULL,
    period     smallint          NOT NULL,
    max_runs   bigint            NULL,
    max_tokens bigint            NULL,
    max_cost   decimal(20,10)    NULL,
    enabled    bit               NOT NULL CONSTRAINT quotas_enabled_default DEFAULT 1,
    created_at datetimeoffset(7) NOT NULL,
    updated_at datetimeoffset(7) NOT NULL,
    CONSTRAINT quotas_scope_uq UNIQUE (tenant_id, agent_name, period)
);

IF OBJECT_ID(N'{schema}.quota_usage', N'U') IS NULL
CREATE TABLE {schema}.quota_usage (
    tenant_id    nvarchar(200)     NOT NULL,
    agent_name   nvarchar(200)     NOT NULL CONSTRAINT quota_usage_agent_default DEFAULT N'',
    period       smallint          NOT NULL,
    period_start date              NOT NULL,
    runs         bigint            NOT NULL CONSTRAINT quota_usage_runs_default DEFAULT 0,
    tokens       bigint            NOT NULL CONSTRAINT quota_usage_tokens_default DEFAULT 0,
    cost         decimal(20,10)    NOT NULL CONSTRAINT quota_usage_cost_default DEFAULT 0,
    updated_at   datetimeoffset(7) NOT NULL,
    CONSTRAINT quota_usage_pk PRIMARY KEY (tenant_id, agent_name, period, period_start)
);

-- 🚨 `events` bir JSON DIZISIDIR. SQL Server'da dizi tipi yoktur; PostgreSQL'in
-- `text[]` sutunu burada JSON metnine donusur ve sorgularda OPENJSON ile acilir.
-- Gerekce: docs/KARARLAR.md, karar K-182.
IF OBJECT_ID(N'{schema}.webhook_subscriptions', N'U') IS NULL
CREATE TABLE {schema}.webhook_subscriptions (
    id                       uniqueidentifier  NOT NULL CONSTRAINT webhook_subscriptions_pk PRIMARY KEY,
    tenant_id                nvarchar(200)     NOT NULL,
    name                     nvarchar(200)     NOT NULL,
    url                      nvarchar(2000)    NOT NULL,
    events                   nvarchar(max)     NOT NULL CONSTRAINT webhook_subscriptions_events_json CHECK (ISJSON(events) = 1),
    secret_configuration_key nvarchar(200)     NULL,
    headers                  nvarchar(max)     NOT NULL
        CONSTRAINT webhook_subscriptions_headers_default DEFAULT N'{}'
        CONSTRAINT webhook_subscriptions_headers_json CHECK (ISJSON(headers) = 1),
    enabled                  bit               NOT NULL CONSTRAINT webhook_subscriptions_enabled_default DEFAULT 1,
    consecutive_failures     int               NOT NULL CONSTRAINT webhook_subscriptions_failures_default DEFAULT 0,
    created_at               datetimeoffset(7) NOT NULL,
    updated_at               datetimeoffset(7) NOT NULL,
    CONSTRAINT webhook_subscriptions_tenant_name_uq UNIQUE (tenant_id, name)
);

IF OBJECT_ID(N'{schema}.webhook_deliveries', N'U') IS NULL
CREATE TABLE {schema}.webhook_deliveries (
    id              uniqueidentifier  NOT NULL CONSTRAINT webhook_deliveries_pk PRIMARY KEY NONCLUSTERED,
    subscription_id uniqueidentifier  NOT NULL
        CONSTRAINT webhook_deliveries_subscription_fk REFERENCES {schema}.webhook_subscriptions (id) ON DELETE CASCADE,
    tenant_id       nvarchar(200)     NOT NULL,
    event_type      nvarchar(200)     NOT NULL,
    payload         nvarchar(max)     NOT NULL,
    status          smallint          NOT NULL,
    attempt         smallint          NOT NULL CONSTRAINT webhook_deliveries_attempt_default DEFAULT 0,
    response_code   int               NULL,
    error           nvarchar(max)     NULL,
    created_at      datetimeoffset(7) NOT NULL,
    delivered_at    datetimeoffset(7) NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'webhook_deliveries_cx' AND object_id = OBJECT_ID(N'{schema}.webhook_deliveries'))
CREATE UNIQUE CLUSTERED INDEX webhook_deliveries_cx ON {schema}.webhook_deliveries (created_at, id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'webhook_deliveries_subscription_idx' AND object_id = OBJECT_ID(N'{schema}.webhook_deliveries'))
CREATE INDEX webhook_deliveries_subscription_idx ON {schema}.webhook_deliveries (subscription_id, created_at DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'webhook_deliveries_tenant_created_idx' AND object_id = OBJECT_ID(N'{schema}.webhook_deliveries'))
CREATE INDEX webhook_deliveries_tenant_created_idx ON {schema}.webhook_deliveries (tenant_id, created_at DESC);
