-- AgentPrism ilk sema.
--
-- Kurallar:
--   * Tuketicinin `public` semasina DOKUNULMAZ (karar K-013).
--   * `{schema}` yer tutucusu calisma aninda AgentPrismPostgreSqlOptions.SchemaName ile degistirilir.
--     Ad, SqlIdentifier.RequireSchemaName ile dogrulanir.
--   * Zaman alanlari `timestamptz`, her zaman UTC.
--   * Birincil anahtarlar `uuid` v7; uygulama uretir. `gen_random_uuid()` KULLANILMAZ (v4 uretir).
--   * `RunStatus` ve `RunEventType` `smallint` olarak saklanir; enum degerleri kararlidir.

CREATE SCHEMA IF NOT EXISTS {schema};

-- ---------------------------------------------------------------------------
-- Kiracilar
-- ---------------------------------------------------------------------------
-- Diger tablolardaki `tenant_id`, bu tablonun `slug` degeriyle ayni metindir
-- ancak YABANCI ANAHTAR ile baglanmaz. Sebep: kiraci kaydi Faz 6'da yonetilecek;
-- kisiti simdiden koymak, kaydi olmayan bir kiraci icin calisma aninda beklenmedik
-- hata uretirdi. Kisit, kiraci yonetimi geldiginde eklenir.

CREATE TABLE IF NOT EXISTS {schema}.tenants (
    id           uuid        NOT NULL PRIMARY KEY,
    slug         text        NOT NULL UNIQUE,
    display_name text        NOT NULL,
    created_at   timestamptz NOT NULL
);

-- ---------------------------------------------------------------------------
-- Agent tanimlari
-- ---------------------------------------------------------------------------
-- `definition` yalnizca tanimin ICERIGINI tasir. Ad, surum, kiraci ve guncelleme
-- zamani sutunlardadir ve tek dogru kaynak orasidir (bkz. AgentDefinitionPayload).

CREATE TABLE IF NOT EXISTS {schema}.agent_definitions (
    id         uuid        NOT NULL PRIMARY KEY,
    tenant_id  text        NOT NULL,
    name       text        NOT NULL,
    version    integer     NOT NULL,
    definition jsonb       NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT agent_definitions_tenant_name_uq UNIQUE (tenant_id, name)
);

CREATE INDEX IF NOT EXISTS agent_definitions_definition_gin
    ON {schema}.agent_definitions USING gin (definition jsonb_path_ops);

-- Degismez surum gecmisi. Geri alma eski surumu SILMEZ; icerigini yeni surum olarak yazar.
CREATE TABLE IF NOT EXISTS {schema}.agent_definition_versions (
    id         uuid        NOT NULL PRIMARY KEY,
    agent_id   uuid        NOT NULL REFERENCES {schema}.agent_definitions (id) ON DELETE CASCADE,
    version    integer     NOT NULL,
    definition jsonb       NOT NULL,
    created_by text,
    created_at timestamptz NOT NULL,
    CONSTRAINT agent_definition_versions_agent_version_uq UNIQUE (agent_id, version)
);

-- ---------------------------------------------------------------------------
-- Oturumlar
-- ---------------------------------------------------------------------------
-- `state`, Microsoft Agent Framework'un SerializeSessionAsync ciktisidir ve OPAKTIR.
-- `schema_version` MAF serilestirme bicimi degisirse anlasilir hata verebilmek icindir.
--
-- `state` bilerek `json`, `jsonb` DEGIL. PostgreSQL `jsonb` nesne anahtarlarini
-- yeniden siralar (once uzunluga, sonra bayt sirasina gore). System.Text.Json'un
-- polimorfik ayraci `$type` ise nesnenin ILK ozelligi olmak zorundadir; jsonb bu
-- garantiyi bozar ve geri okuma JsonException ile basarisiz olur. Opak durum ayrica
-- sorgulanmaz, bu yuzden jsonb'nin index destegine ihtiyac yoktur.
-- Gerekce: docs/KARARLAR.md, karar K-027.

CREATE TABLE IF NOT EXISTS {schema}.sessions (
    id             text        NOT NULL PRIMARY KEY,
    tenant_id      text        NOT NULL,
    agent_name     text        NOT NULL,
    state          json        NOT NULL,
    schema_version integer     NOT NULL,
    created_at     timestamptz NOT NULL,
    updated_at     timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS sessions_tenant_updated_idx
    ON {schema}.sessions (tenant_id, updated_at DESC);

CREATE INDEX IF NOT EXISTS sessions_tenant_agent_updated_idx
    ON {schema}.sessions (tenant_id, agent_name, updated_at DESC);

-- ---------------------------------------------------------------------------
-- Konusmalar
-- ---------------------------------------------------------------------------
-- PostgresChatHistoryProvider sohbet gecmisini buraya yazar. Faz 4'te OpenAI
-- uyumlu Conversations API'si de ayni tablolari kullanir.

CREATE TABLE IF NOT EXISTS {schema}.conversations (
    id         uuid        NOT NULL PRIMARY KEY,
    tenant_id  text        NOT NULL,
    agent_name text        NOT NULL,
    metadata   jsonb       NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS conversations_tenant_updated_idx
    ON {schema}.conversations (tenant_id, updated_at DESC);

-- `item` bilerek `json`, `jsonb` DEGIL: ChatMessage icerikleri polimorfiktir ve
-- `$type` ayraci nesnenin ilk ozelligi olmalidir. Ayrinti icin sessions.state notu.
CREATE TABLE IF NOT EXISTS {schema}.conversation_items (
    id              uuid        NOT NULL PRIMARY KEY,
    conversation_id uuid        NOT NULL REFERENCES {schema}.conversations (id) ON DELETE CASCADE,
    seq             bigint      NOT NULL,
    item            json        NOT NULL,
    created_at      timestamptz NOT NULL,
    CONSTRAINT conversation_items_conversation_seq_uq UNIQUE (conversation_id, seq)
);

-- Responses API kayitlari. Faz 4 doldurur.
CREATE TABLE IF NOT EXISTS {schema}.responses (
    id              uuid        NOT NULL PRIMARY KEY,
    conversation_id uuid        REFERENCES {schema}.conversations (id) ON DELETE CASCADE,
    session_id      text,
    payload         jsonb       NOT NULL,
    created_at      timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS responses_conversation_idx
    ON {schema}.responses (conversation_id, created_at DESC);

-- ---------------------------------------------------------------------------
-- Calistirmalar
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS {schema}.runs (
    id            uuid        NOT NULL PRIMARY KEY,
    tenant_id     text        NOT NULL,
    agent_name    text        NOT NULL,
    session_id    text,
    status        smallint    NOT NULL,
    started_at    timestamptz NOT NULL,
    completed_at  timestamptz,
    is_streaming  boolean     NOT NULL,
    input_tokens  bigint,
    output_tokens bigint,
    total_tokens  bigint,
    event_count   bigint      NOT NULL DEFAULT 0,
    error_type    text,
    error_message text
);

CREATE INDEX IF NOT EXISTS runs_tenant_started_idx
    ON {schema}.runs (tenant_id, started_at DESC);

CREATE INDEX IF NOT EXISTS runs_tenant_agent_started_idx
    ON {schema}.runs (tenant_id, agent_name, started_at DESC);

CREATE INDEX IF NOT EXISTS runs_tenant_status_started_idx
    ON {schema}.runs (tenant_id, status, started_at DESC);

CREATE INDEX IF NOT EXISTS runs_session_idx
    ON {schema}.runs (tenant_id, session_id, started_at DESC)
    WHERE session_id IS NOT NULL;

-- Append-only olay akisi (karar K-014). Olaylar GUNCELLENMEZ, yalnizca eklenir.
--
-- `payload` bilerek `text`, `jsonb` degil: RunEventWriter tool argumanlarini AOT
-- uyumlu kalmak icin elle bicimlendirir ve cikti gecerli JSON olmayabilir.
-- `jsonb` sutunu bu durumda calistirmayi kesen bir hata uretirdi.
--
-- `created_at` partition anahtari olmaya adaydir; partition Faz 6'da acilir ve
-- o zaman birincil anahtarin `created_at` sutununu da icermesi gerekecektir.
CREATE TABLE IF NOT EXISTS {schema}.run_events (
    run_id       uuid        NOT NULL REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    seq          bigint      NOT NULL,
    type         smallint    NOT NULL,
    text         text,
    tool_name    text,
    tool_call_id text,
    payload      text,
    created_at   timestamptz NOT NULL,
    CONSTRAINT run_events_pkey PRIMARY KEY (run_id, seq)
);

-- Tool cagrilarinin ozeti. Sema burada kurulur; Faz 6 doldurur.
CREATE TABLE IF NOT EXISTS {schema}.tool_invocations (
    id           uuid        NOT NULL PRIMARY KEY,
    run_id       uuid        NOT NULL REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    tool_name    text        NOT NULL,
    tool_call_id text,
    arguments    jsonb,
    result       jsonb,
    duration_ms  integer,
    error        text,
    created_at   timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS tool_invocations_run_idx
    ON {schema}.tool_invocations (run_id, created_at);

-- ---------------------------------------------------------------------------
-- Gozlemlenebilirlik ve denetim
-- ---------------------------------------------------------------------------
-- Sema burada kurulur; Faz 6 doldurur.

CREATE TABLE IF NOT EXISTS {schema}.traces (
    id         uuid        NOT NULL PRIMARY KEY,
    tenant_id  text        NOT NULL,
    trace_id   text        NOT NULL,
    run_id     uuid        REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    started_at timestamptz NOT NULL,
    ended_at   timestamptz,
    CONSTRAINT traces_tenant_trace_uq UNIQUE (tenant_id, trace_id)
);

CREATE TABLE IF NOT EXISTS {schema}.spans (
    id             uuid        NOT NULL PRIMARY KEY,
    trace_id       uuid        NOT NULL REFERENCES {schema}.traces (id) ON DELETE CASCADE,
    parent_span_id uuid,
    name           text        NOT NULL,
    kind           smallint    NOT NULL,
    started_at     timestamptz NOT NULL,
    ended_at       timestamptz,
    attributes     jsonb,
    status         smallint
);

CREATE INDEX IF NOT EXISTS spans_trace_started_idx
    ON {schema}.spans (trace_id, started_at);

CREATE TABLE IF NOT EXISTS {schema}.audit_log (
    id         uuid        NOT NULL PRIMARY KEY,
    tenant_id  text        NOT NULL,
    actor      text,
    action     text        NOT NULL,
    entity     text        NOT NULL,
    before     jsonb,
    after      jsonb,
    created_at timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS audit_log_tenant_created_idx
    ON {schema}.audit_log (tenant_id, created_at DESC);
