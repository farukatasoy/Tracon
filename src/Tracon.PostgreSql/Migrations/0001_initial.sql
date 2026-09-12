-- Tracon initial schema.
--
-- Rules:
--   * The consumer `public` schema is NOT TOUCHED (decision K-013).
--   * The `{schema}` placeholder is replaced at run time with TraconPostgreSqlOptions.SchemaName.
--     The name is validated by SqlIdentifier.RequireSchemaName.
--   * Time fields are `timestamptz`, always UTC.
--   * Primary keys are `uuid` v7; the application generates them. `gen_random_uuid()` IS NOT USED (it makes v4).
--   * `RunStatus` and `RunEventType` are stored as `smallint`; enum values are stable.

CREATE SCHEMA IF NOT EXISTS {schema};

-- ---------------------------------------------------------------------------
-- Tenants
-- ---------------------------------------------------------------------------
-- `tenant_id` in other tables is the same text as this table `slug` value but
-- it is NOT linked with a FOREIGN KEY. Reason: tenant records are managed in
-- phase 6; adding the constraint now would give an unexpected run time error
-- for a tenant that has no record. It is added when tenant management arrives.

CREATE TABLE IF NOT EXISTS {schema}.tenants (
    id           uuid        NOT NULL PRIMARY KEY,
    slug         text        NOT NULL UNIQUE,
    display_name text        NOT NULL,
    created_at   timestamptz NOT NULL
);

-- ---------------------------------------------------------------------------
-- Agent definitions
-- ---------------------------------------------------------------------------
-- `definition` carries ONLY THE CONTENT of the definition. Name, version, tenant
-- and update time live in columns; that is the source of truth (AgentDefinitionPayload).

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

-- Immutable version history. Rollback DOES NOT DELETE the old version; writes it as a new version.
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
-- Sessions
-- ---------------------------------------------------------------------------
-- `state` is the SerializeSessionAsync output of Microsoft Agent Framework and it is OPAQUE.
-- `schema_version` exists to give a clear error if the MAF serialization format changes.
--
-- `state` is deliberately `json`, NOT `jsonb`. PostgreSQL `jsonb` reorders object
-- keys (first by length, then by byte order). The polymorphic `$type` discriminator
-- of System.Text.Json must be the FIRST property of the object; jsonb breaks that
-- guarantee and the read back fails with JsonException. The opaque state is also
-- not queried, so the index support of jsonb is not needed.
-- Rationale: docs/KARARLAR.md, decision K-027.

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
-- Conversations
-- ---------------------------------------------------------------------------
-- PostgresChatHistoryProvider writes the chat history here. In phase 4 the OpenAI
-- compatible Conversations API uses the same tables.

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

-- `item` is deliberately `json`, NOT `jsonb`: ChatMessage contents are polymorphic
-- and the `$type` discriminator must be the first property. See the sessions.state note.
CREATE TABLE IF NOT EXISTS {schema}.conversation_items (
    id              uuid        NOT NULL PRIMARY KEY,
    conversation_id uuid        NOT NULL REFERENCES {schema}.conversations (id) ON DELETE CASCADE,
    seq             bigint      NOT NULL,
    item            json        NOT NULL,
    created_at      timestamptz NOT NULL,
    CONSTRAINT conversation_items_conversation_seq_uq UNIQUE (conversation_id, seq)
);

-- Responses API records. Phase 4 fills them.
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
-- Runs
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

-- Append-only event stream (decision K-014). Events are NOT UPDATED, only appended.
--
-- `payload` is deliberately `text`, not `jsonb`: RunEventWriter formats tool
-- arguments by hand to stay AOT compatible and the output can be invalid JSON.
-- A `jsonb` column would then produce an error that stops the run.
--
-- `created_at` is a candidate partition key; partitioning opens in phase 6 and
-- the primary key must then also contain the `created_at` column.
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

-- Summary of tool calls. The schema is created here; phase 6 fills it.
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
-- Observability and audit
-- ---------------------------------------------------------------------------
-- The schema is created here; phase 6 fills it.

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
