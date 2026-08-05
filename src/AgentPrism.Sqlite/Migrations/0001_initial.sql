-- AgentPrism SQLite semasi (Faz 24).
--
-- Bu dosya PostgreSQL'in 0001-0013 migration'larinin BIRIKMIS sonucudur (SQL
-- Server'in 0001_initial.sql'iyle ayni desen, K-178). SQLite destegi Faz 24'te
-- aciliyor; yukseltilecek bir kurulum yoktur. Bundan sonraki degisiklikler
-- 0002, 0003 ... olarak eklenir.
--
-- 🚨 Numaralandirma diger saglayicilarla ESLESMEZ ve eslesmesi gerekmez.
-- Gerekce: docs/KARARLAR.md, karar K-178.
--
-- Kurallar:
--   * SQLite'ta sema kavrami yoktur. `{schema}` yer tutucusu calisma aninda
--     AgentPrismSqliteOptions.TablePrefix ile degistirilir (varsayilan
--     `agentprism_`); tuketicinin kendi tablolarina dokunulmaz (K-013'un
--     SQLite karsiligi).
--   * Zaman alanlari TEXT, ISO-8601 UTC (`yyyy-MM-ddTHH:mm:ss.fffffffZ`),
--     SqliteDialect.AddTimestamp tarafindan yazilir.
--   * Birincil anahtarlar TEXT (uuid v7, buyuk harfli — SqliteDialect'teki
--     "uuid buyuk harfle yazilir" notuna bakin); uygulama uretir.
--   * Enum degerleri INTEGER olarak saklanir ve diger saglayicilarla AYNI
--     sayilardir.
--   * `numeric`/`decimal` sutunlari TEXT'tir; REAL KULLANILMAZ (para
--     hesabinda kayan nokta yasak, docs/24-SQLITE.md bolum 24.2).
--
-- Tip esleme (PostgreSQL -> SQLite):
--   uuid           -> TEXT
--   text           -> TEXT (SQLite'ta uzunluk siniri islevsizdir)
--   jsonb / json    -> TEXT (json_valid() kisitiyla)
--   timestamptz    -> TEXT (ISO-8601 UTC)
--   boolean        -> INTEGER (0/1)
--   bytea          -> BLOB
--   numeric(20,10) -> TEXT
--   text[]         -> TEXT (JSON dizi, json_each ile acilir)
--   date           -> TEXT (yyyy-MM-dd)
--
-- Yabanci anahtar zorlamasi baglanti acilisinda `Foreign Keys=True` ile
-- ETKINDIR (SqliteDataSource); aksi halde SQLite REFERENCES yan tumcelerini
-- sessizce YOK SAYAR.

-- ---------------------------------------------------------------------------
-- Kiracilar
-- ---------------------------------------------------------------------------
-- Diger tablolardaki `tenant_id`, bu tablonun `slug` degeriyle ayni metindir
-- ancak YABANCI ANAHTAR ile baglanmaz; kaydi olmayan bir kiraci icin calisma
-- aninda beklenmedik hata uretirdi.

CREATE TABLE IF NOT EXISTS {schema}tenants (
    id           TEXT NOT NULL PRIMARY KEY,
    slug         TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    created_at   TEXT NOT NULL
);

-- ---------------------------------------------------------------------------
-- Agent tanimlari
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS {schema}agent_definitions (
    id         TEXT    NOT NULL PRIMARY KEY,
    tenant_id  TEXT    NOT NULL,
    name       TEXT    NOT NULL,
    version    INTEGER NOT NULL,
    definition TEXT    NOT NULL CHECK (json_valid(definition)),
    created_at TEXT    NOT NULL,
    updated_at TEXT    NOT NULL,
    UNIQUE (tenant_id, name)
);

-- Degismez surum gecmisi. Geri alma eski surumu SILMEZ; icerigini yeni surum olarak yazar.
CREATE TABLE IF NOT EXISTS {schema}agent_definition_versions (
    id         TEXT    NOT NULL PRIMARY KEY,
    agent_id   TEXT    NOT NULL REFERENCES {schema}agent_definitions (id) ON DELETE CASCADE,
    version    INTEGER NOT NULL,
    definition TEXT    NOT NULL CHECK (json_valid(definition)),
    created_by TEXT    NULL,
    created_at TEXT    NOT NULL,
    UNIQUE (agent_id, version)
);

-- ---------------------------------------------------------------------------
-- Oturumlar
-- ---------------------------------------------------------------------------
-- `state`, Microsoft Agent Framework'un SerializeSessionAsync ciktisidir ve
-- OPAKTIR. K-027 burada gecerli degildir (bkz. SQL Server DDL'deki ayni not):
-- SQLite JSON'u metin olarak saklar ve sira zaten korunur.

CREATE TABLE IF NOT EXISTS {schema}sessions (
    id             TEXT    NOT NULL PRIMARY KEY,
    tenant_id      TEXT    NOT NULL,
    agent_name     TEXT    NOT NULL,
    state          TEXT    NOT NULL CHECK (json_valid(state)),
    schema_version INTEGER NOT NULL,
    created_at     TEXT    NOT NULL,
    updated_at     TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS {schema}sessions_tenant_updated_idx ON {schema}sessions (tenant_id, updated_at DESC);
CREATE INDEX IF NOT EXISTS {schema}sessions_tenant_agent_updated_idx ON {schema}sessions (tenant_id, agent_name, updated_at DESC);

-- ---------------------------------------------------------------------------
-- Konusmalar
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS {schema}conversations (
    id         TEXT NOT NULL PRIMARY KEY,
    tenant_id  TEXT NOT NULL,
    agent_name TEXT NOT NULL,
    metadata   TEXT NOT NULL DEFAULT '{}' CHECK (json_valid(metadata)),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS {schema}conversations_tenant_updated_idx ON {schema}conversations (tenant_id, updated_at DESC);

CREATE TABLE IF NOT EXISTS {schema}conversation_items (
    id              TEXT   NOT NULL PRIMARY KEY,
    conversation_id TEXT   NOT NULL REFERENCES {schema}conversations (id) ON DELETE CASCADE,
    seq             INTEGER NOT NULL,
    item            TEXT   NOT NULL CHECK (json_valid(item)),
    created_at      TEXT   NOT NULL,
    UNIQUE (conversation_id, seq)
);

CREATE TABLE IF NOT EXISTS {schema}responses (
    id              TEXT NOT NULL PRIMARY KEY,
    conversation_id TEXT NULL REFERENCES {schema}conversations (id) ON DELETE CASCADE,
    session_id      TEXT NULL,
    payload         TEXT NOT NULL CHECK (json_valid(payload)),
    created_at      TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS {schema}responses_conversation_idx ON {schema}responses (conversation_id, created_at DESC);

-- ---------------------------------------------------------------------------
-- Calistirmalar
-- ---------------------------------------------------------------------------
-- `pricing_source`: 0=Catalog 1=Configuration 2=Unknown.

CREATE TABLE IF NOT EXISTS {schema}runs (
    id             TEXT    NOT NULL PRIMARY KEY,
    tenant_id      TEXT    NOT NULL,
    agent_name     TEXT    NOT NULL,
    session_id     TEXT    NULL,
    status         INTEGER NOT NULL,
    started_at     TEXT    NOT NULL,
    completed_at   TEXT    NULL,
    is_streaming   INTEGER NOT NULL,
    input_tokens   INTEGER NULL,
    output_tokens  INTEGER NULL,
    total_tokens   INTEGER NULL,
    event_count    INTEGER NOT NULL DEFAULT 0,
    error_type     TEXT    NULL,
    error_message  TEXT    NULL,
    model_id       TEXT    NULL,
    parent_run_id  TEXT    NULL,
    root_run_id    TEXT    NULL,
    depth          INTEGER NOT NULL DEFAULT 0,
    kind           INTEGER NOT NULL DEFAULT 0,
    workflow_name  TEXT    NULL,
    agent_version  INTEGER NULL,
    experiment_id  TEXT    NULL,
    variant        TEXT    NULL,
    input_cost     TEXT    NULL,
    output_cost    TEXT    NULL,
    cost_currency  TEXT    NULL,
    pricing_source INTEGER NULL
);

CREATE INDEX IF NOT EXISTS {schema}runs_tenant_started_idx ON {schema}runs (tenant_id, started_at DESC);
CREATE INDEX IF NOT EXISTS {schema}runs_tenant_agent_started_idx ON {schema}runs (tenant_id, agent_name, started_at DESC);
CREATE INDEX IF NOT EXISTS {schema}runs_tenant_status_started_idx ON {schema}runs (tenant_id, status, started_at DESC);
CREATE INDEX IF NOT EXISTS {schema}runs_session_idx ON {schema}runs (tenant_id, session_id, started_at DESC) WHERE session_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS {schema}runs_tenant_model_started_idx ON {schema}runs (tenant_id, model_id, started_at DESC) WHERE model_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS {schema}runs_parent_idx ON {schema}runs (parent_run_id) WHERE parent_run_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS {schema}runs_root_idx ON {schema}runs (tenant_id, root_run_id, started_at) WHERE root_run_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS {schema}runs_roots_only_idx ON {schema}runs (tenant_id, started_at DESC) WHERE parent_run_id IS NULL;
CREATE INDEX IF NOT EXISTS {schema}runs_workflow_idx ON {schema}runs (tenant_id, workflow_name, started_at DESC) WHERE kind = 1;
CREATE INDEX IF NOT EXISTS {schema}runs_agent_version_idx ON {schema}runs (tenant_id, agent_name, agent_version, started_at DESC) WHERE agent_version IS NOT NULL;
CREATE INDEX IF NOT EXISTS {schema}runs_experiment_idx ON {schema}runs (experiment_id, variant) WHERE experiment_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS {schema}runs_tenant_cost_idx ON {schema}runs (tenant_id, started_at DESC) WHERE input_cost IS NOT NULL;

-- Append-only olay akisi (karar K-014). Olaylar GUNCELLENMEZ, yalnizca eklenir.
-- `payload` bilerek serbest metindir ve json_valid kisiti TASIMAZ: RunEventWriter
-- tool argumanlarini elle bicimlendirir ve cikti gecerli JSON olmayabilir.
CREATE TABLE IF NOT EXISTS {schema}run_events (
    run_id       TEXT    NOT NULL REFERENCES {schema}runs (id) ON DELETE CASCADE,
    seq          INTEGER NOT NULL,
    type         INTEGER NOT NULL,
    text         TEXT    NULL,
    tool_name    TEXT    NULL,
    tool_call_id TEXT    NULL,
    payload      TEXT    NULL,
    created_at   TEXT    NOT NULL,
    PRIMARY KEY (run_id, seq)
);

CREATE TABLE IF NOT EXISTS {schema}tool_invocations (
    id           TEXT    NOT NULL PRIMARY KEY,
    run_id       TEXT    NOT NULL REFERENCES {schema}runs (id) ON DELETE CASCADE,
    tool_name    TEXT    NOT NULL,
    tool_call_id TEXT    NULL,
    source       TEXT    NULL,
    arguments    TEXT    NULL,
    result       TEXT    NULL,
    duration_ms  INTEGER NULL,
    error        TEXT    NULL,
    created_at   TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS {schema}tool_invocations_run_idx ON {schema}tool_invocations (run_id, created_at);
CREATE INDEX IF NOT EXISTS {schema}tool_invocations_tool_created_idx ON {schema}tool_invocations (tool_name, created_at DESC);

-- ---------------------------------------------------------------------------
-- Gozlemlenebilirlik ve denetim
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS {schema}traces (
    id         TEXT NOT NULL PRIMARY KEY,
    tenant_id  TEXT NOT NULL,
    trace_id   TEXT NOT NULL,
    run_id     TEXT NULL REFERENCES {schema}runs (id) ON DELETE CASCADE,
    started_at TEXT NOT NULL,
    ended_at   TEXT NULL,
    UNIQUE (tenant_id, trace_id)
);

CREATE INDEX IF NOT EXISTS {schema}traces_run_idx ON {schema}traces (run_id) WHERE run_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS {schema}spans (
    id             TEXT    NOT NULL PRIMARY KEY,
    trace_id       TEXT    NOT NULL REFERENCES {schema}traces (id) ON DELETE CASCADE,
    parent_span_id TEXT    NULL,
    name           TEXT    NOT NULL,
    kind           INTEGER NOT NULL,
    started_at     TEXT    NOT NULL,
    ended_at       TEXT    NULL,
    attributes     TEXT    NULL CHECK (attributes IS NULL OR json_valid(attributes)),
    status         INTEGER NULL,
    span_id        TEXT    NULL
);

CREATE INDEX IF NOT EXISTS {schema}spans_trace_started_idx ON {schema}spans (trace_id, started_at);

-- `before` / `after` json_valid kisiti TASIMAZ. Gozlemlenebilirlik islevselligi
-- bozmaz: denetim izi yazimi bir kisit ihlaliyle basarisiz olursa asil islem de
-- basarisiz olurdu.
CREATE TABLE IF NOT EXISTS {schema}audit_log (
    id         TEXT NOT NULL PRIMARY KEY,
    tenant_id  TEXT NOT NULL,
    actor      TEXT NULL,
    action     TEXT NOT NULL,
    entity     TEXT NOT NULL,
    before     TEXT NULL,
    after      TEXT NULL,
    created_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS {schema}audit_log_tenant_created_idx ON {schema}audit_log (tenant_id, created_at DESC);

-- ---------------------------------------------------------------------------
-- Tool onay kurallari
-- ---------------------------------------------------------------------------
-- SQLite benzersiz kisitta NULL'lari PostgreSQL gibi birbirinden AYIRT EDER
-- (K-184 SQL Server'a ozgudur, burada gecerli DEGILDIR); duz bir UNIQUE
-- ayni kuralin (agent_name NULL iken) sinirsiz kez eklenmesine izin verirdi.
-- Bu yuzden kisit COALESCE'li bir ifade UZERINDE kurulur.

CREATE TABLE IF NOT EXISTS {schema}tool_approval_rules (
    id             TEXT NOT NULL PRIMARY KEY,
    tenant_id      TEXT NOT NULL,
    agent_name     TEXT NULL,
    tool_name      TEXT NOT NULL,
    arguments_hash TEXT NULL,
    created_by     TEXT NULL,
    created_at     TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS {schema}tool_approval_rules_scope_uq
    ON {schema}tool_approval_rules (tenant_id, COALESCE(agent_name, ''), tool_name, COALESCE(arguments_hash, ''));
CREATE INDEX IF NOT EXISTS {schema}tool_approval_rules_tenant_created_idx ON {schema}tool_approval_rules (tenant_id, created_at DESC);

-- ---------------------------------------------------------------------------
-- MCP sunuculari
-- ---------------------------------------------------------------------------
-- 🚨 SIR TASIMAZ. Yalnizca degerin okunacagi yapilandirma anahtarinin ADI
-- saklanir (K-059). `transport` INTEGER: 0 = StreamableHttp, 1 = SSE.

CREATE TABLE IF NOT EXISTS {schema}mcp_servers (
    id                                    TEXT    NOT NULL PRIMARY KEY,
    tenant_id                             TEXT    NOT NULL,
    name                                  TEXT    NOT NULL,
    description                           TEXT    NULL,
    endpoint                              TEXT    NOT NULL,
    transport                             INTEGER NOT NULL DEFAULT 0,
    authorization_configuration_key       TEXT    NULL,
    headers                               TEXT    NOT NULL DEFAULT '{}' CHECK (json_valid(headers)),
    enabled                               INTEGER NOT NULL DEFAULT 1,
    requires_approval                     INTEGER NOT NULL DEFAULT 1,
    created_at                            TEXT    NOT NULL,
    updated_at                            TEXT    NOT NULL,
    oauth_enabled                         INTEGER NOT NULL DEFAULT 0,
    oauth_client_id                       TEXT    NULL,
    oauth_client_secret_configuration_key TEXT    NULL,
    oauth_scopes                          TEXT    NULL,
    oauth_authorization_mode              INTEGER NOT NULL DEFAULT 0,
    UNIQUE (tenant_id, name)
);

-- ---------------------------------------------------------------------------
-- Agent skill'leri
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS {schema}agent_skills (
    id            TEXT    NOT NULL PRIMARY KEY,
    tenant_id     TEXT    NOT NULL,
    name          TEXT    NOT NULL,
    description   TEXT    NOT NULL,
    instructions  TEXT    NOT NULL,
    compatibility TEXT    NULL,
    license       TEXT    NULL,
    allowed_tools TEXT    NULL,
    metadata      TEXT    NOT NULL DEFAULT '{}' CHECK (json_valid(metadata)),
    enabled       INTEGER NOT NULL DEFAULT 1,
    version       INTEGER NOT NULL DEFAULT 1,
    created_at    TEXT    NOT NULL,
    updated_at    TEXT    NOT NULL,
    UNIQUE (tenant_id, name)
);

CREATE INDEX IF NOT EXISTS {schema}agent_skills_tenant_enabled_updated_idx ON {schema}agent_skills (tenant_id, enabled, updated_at DESC);

CREATE TABLE IF NOT EXISTS {schema}agent_skill_resources (
    id          TEXT NOT NULL PRIMARY KEY,
    skill_id    TEXT NOT NULL REFERENCES {schema}agent_skills (id) ON DELETE CASCADE,
    name        TEXT NOT NULL,
    description TEXT NULL,
    media_type  TEXT NOT NULL DEFAULT 'text/plain',
    content     TEXT NOT NULL,
    created_at  TEXT NOT NULL,
    UNIQUE (skill_id, name)
);

CREATE TABLE IF NOT EXISTS {schema}agent_skill_scripts (
    id                TEXT NOT NULL PRIMARY KEY,
    skill_id          TEXT NOT NULL REFERENCES {schema}agent_skills (id) ON DELETE CASCADE,
    name              TEXT NOT NULL,
    description       TEXT NULL,
    extension         TEXT NOT NULL,
    content           TEXT NOT NULL,
    parameters_schema TEXT NULL CHECK (parameters_schema IS NULL OR json_valid(parameters_schema)),
    created_at        TEXT NOT NULL,
    UNIQUE (skill_id, name)
);

-- SQLite benzersiz kisitta NULL'lari PostgreSQL gibi birbirinden AYIRT EDER;
-- duz UNIQUE (tenant_id, skill_name, script_name) burada dogru davranistir
-- (script_name NULL olan birden fazla kayit -- her biri farkli skill'e ait
-- olmadikca -- zaten skill_name ile ayrisir).
CREATE TABLE IF NOT EXISTS {schema}skill_script_grants (
    id          TEXT NOT NULL PRIMARY KEY,
    tenant_id   TEXT NOT NULL,
    skill_name  TEXT NOT NULL,
    script_name TEXT NULL,
    granted_by  TEXT NULL,
    granted_at  TEXT NOT NULL,
    expires_at  TEXT NULL,
    revoked_at  TEXT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS {schema}skill_script_grants_uq
    ON {schema}skill_script_grants (tenant_id, skill_name, COALESCE(script_name, ''));
CREATE INDEX IF NOT EXISTS {schema}skill_script_grants_lookup_idx ON {schema}skill_script_grants (tenant_id, skill_name) WHERE revoked_at IS NULL;

-- ---------------------------------------------------------------------------
-- Ekler ve kalici agent dosya bellegi
-- ---------------------------------------------------------------------------
-- `session_id` KASITLI OLARAK yabanci anahtar DEGILDIR: bir ek, kendi oturumu
-- hic acilmadan once yuklenebilir (K-112).

CREATE TABLE IF NOT EXISTS {schema}attachments (
    id           TEXT    NOT NULL PRIMARY KEY,
    tenant_id    TEXT    NOT NULL,
    session_id   TEXT    NULL,
    run_id       TEXT    NULL,
    file_name    TEXT    NOT NULL,
    media_type   TEXT    NOT NULL,
    byte_size    INTEGER NOT NULL,
    sha256       TEXT    NOT NULL,
    content      BLOB    NULL,
    external_uri TEXT    NULL,
    created_by   TEXT    NULL,
    created_at   TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS {schema}attachments_tenant_created_idx ON {schema}attachments (tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS {schema}attachments_session_idx ON {schema}attachments (tenant_id, session_id) WHERE session_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS {schema}agent_files (
    id         TEXT NOT NULL PRIMARY KEY,
    tenant_id  TEXT NOT NULL,
    agent_name TEXT NOT NULL,
    path       TEXT NOT NULL,
    content    TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    UNIQUE (tenant_id, agent_name, path)
);

-- ---------------------------------------------------------------------------
-- Workflow'lar
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS {schema}workflows (
    id         TEXT    NOT NULL PRIMARY KEY,
    tenant_id  TEXT    NOT NULL,
    name       TEXT    NOT NULL,
    version    INTEGER NOT NULL,
    definition TEXT    NOT NULL CHECK (json_valid(definition)),
    created_at TEXT    NOT NULL,
    updated_at TEXT    NOT NULL,
    UNIQUE (tenant_id, name)
);

CREATE TABLE IF NOT EXISTS {schema}workflow_checkpoints (
    id            TEXT NOT NULL PRIMARY KEY,
    tenant_id     TEXT NOT NULL,
    session_id    TEXT NOT NULL,
    checkpoint_id TEXT NOT NULL,
    parent_id     TEXT NULL,
    run_id        TEXT NULL,
    state         TEXT NOT NULL CHECK (json_valid(state)),
    created_at    TEXT NOT NULL,
    UNIQUE (tenant_id, session_id, checkpoint_id)
);

CREATE INDEX IF NOT EXISTS {schema}workflow_checkpoints_session_idx ON {schema}workflow_checkpoints (tenant_id, session_id, created_at);
CREATE INDEX IF NOT EXISTS {schema}workflow_checkpoints_run_idx ON {schema}workflow_checkpoints (tenant_id, run_id, created_at) WHERE run_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- Is kuyrugu ve zamanlama
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS {schema}job_schedules (
    id          TEXT    NOT NULL PRIMARY KEY,
    tenant_id   TEXT    NOT NULL,
    name        TEXT    NOT NULL,
    kind        INTEGER NOT NULL,
    target_name TEXT    NOT NULL,
    cron        TEXT    NULL,
    time_zone   TEXT    NOT NULL DEFAULT 'UTC',
    payload     TEXT    NOT NULL CHECK (json_valid(payload)),
    enabled     INTEGER NOT NULL DEFAULT 1,
    next_run_at TEXT    NULL,
    last_run_at TEXT    NULL,
    created_by  TEXT    NULL,
    created_at  TEXT    NOT NULL,
    updated_at  TEXT    NOT NULL,
    UNIQUE (tenant_id, name)
);

CREATE TABLE IF NOT EXISTS {schema}jobs (
    id            TEXT    NOT NULL PRIMARY KEY,
    tenant_id     TEXT    NOT NULL,
    schedule_id   TEXT    NULL REFERENCES {schema}job_schedules (id) ON DELETE SET NULL,
    kind          INTEGER NOT NULL,
    target_name   TEXT    NOT NULL,
    status        INTEGER NOT NULL,
    payload       TEXT    NOT NULL CHECK (json_valid(payload)),
    total_items   INTEGER NOT NULL DEFAULT 0,
    done_items    INTEGER NOT NULL DEFAULT 0,
    failed_items  INTEGER NOT NULL DEFAULT 0,
    attempt       INTEGER NOT NULL DEFAULT 0,
    lease_owner   TEXT    NULL,
    lease_until   TEXT    NULL,
    scheduled_for TEXT    NOT NULL,
    started_at    TEXT    NULL,
    completed_at  TEXT    NULL,
    error_message TEXT    NULL,
    created_at    TEXT    NOT NULL,
    max_attempts  INTEGER NULL
);

-- SQLite benzersiz kisitta NULL'lari PostgreSQL gibi birbirinden AYIRT EDER;
-- elle olusturulan (zamanlamasiz, NULL schedule_id) isler duz bir
-- UNIQUE (schedule_id, scheduled_for) kisitiyla CAKISMAZ.
CREATE UNIQUE INDEX IF NOT EXISTS {schema}jobs_schedule_scheduled_uq ON {schema}jobs (schedule_id, scheduled_for);
CREATE INDEX IF NOT EXISTS {schema}jobs_claim_idx ON {schema}jobs (status, scheduled_for) WHERE status IN (0, 1);
CREATE INDEX IF NOT EXISTS {schema}jobs_tenant_created_idx ON {schema}jobs (tenant_id, created_at DESC);

CREATE TABLE IF NOT EXISTS {schema}job_items (
    id     TEXT    NOT NULL PRIMARY KEY,
    job_id TEXT    NOT NULL REFERENCES {schema}jobs (id) ON DELETE CASCADE,
    seq    INTEGER NOT NULL,
    input  TEXT    NOT NULL,
    run_id TEXT    NULL,
    status INTEGER NOT NULL,
    error  TEXT    NULL,
    UNIQUE (job_id, seq)
);

-- ---------------------------------------------------------------------------
-- Degerlendirme (eval)
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS {schema}eval_suites (
    id          TEXT NOT NULL PRIMARY KEY,
    tenant_id   TEXT NOT NULL,
    name        TEXT NOT NULL,
    description TEXT NULL,
    agent_name  TEXT NOT NULL,
    checks      TEXT NOT NULL DEFAULT '[]' CHECK (json_valid(checks)),
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL,
    UNIQUE (tenant_id, name)
);

CREATE TABLE IF NOT EXISTS {schema}eval_cases (
    id              TEXT    NOT NULL PRIMARY KEY,
    suite_id        TEXT    NOT NULL REFERENCES {schema}eval_suites (id) ON DELETE CASCADE,
    seq             INTEGER NOT NULL,
    query           TEXT    NOT NULL,
    expected_output TEXT    NULL,
    expected_tools  TEXT    NULL,
    context         TEXT    NULL,
    UNIQUE (suite_id, seq)
);

CREATE TABLE IF NOT EXISTS {schema}eval_runs (
    id            TEXT    NOT NULL PRIMARY KEY,
    tenant_id     TEXT    NOT NULL,
    suite_id      TEXT    NOT NULL REFERENCES {schema}eval_suites (id) ON DELETE CASCADE,
    job_id        TEXT    NULL,
    agent_version INTEGER NULL,
    model_id      TEXT    NULL,
    status        INTEGER NOT NULL,
    total         INTEGER NOT NULL DEFAULT 0,
    passed        INTEGER NOT NULL DEFAULT 0,
    failed        INTEGER NOT NULL DEFAULT 0,
    input_tokens  INTEGER NULL,
    output_tokens INTEGER NULL,
    started_at    TEXT    NOT NULL,
    completed_at  TEXT    NULL
);

CREATE INDEX IF NOT EXISTS {schema}eval_runs_suite_started_idx ON {schema}eval_runs (tenant_id, suite_id, started_at DESC);
CREATE INDEX IF NOT EXISTS {schema}eval_runs_job_idx ON {schema}eval_runs (tenant_id, job_id);

-- `case_id` KASITLI OLARAK yabanci anahtar tasimaz: bir vaka sonradan
-- degistirilse veya silinse bile gecmis sonuc kaydi anlasilir kalir.
CREATE TABLE IF NOT EXISTS {schema}eval_case_results (
    id             TEXT    NOT NULL PRIMARY KEY,
    eval_run_id    TEXT    NOT NULL REFERENCES {schema}eval_runs (id) ON DELETE CASCADE,
    case_id        TEXT    NOT NULL,
    run_id         TEXT    NULL,
    passed         INTEGER NOT NULL,
    output         TEXT    NULL,
    scores         TEXT    NOT NULL DEFAULT '[]' CHECK (json_valid(scores)),
    failure_reason TEXT    NULL
);

CREATE INDEX IF NOT EXISTS {schema}eval_case_results_run_idx ON {schema}eval_case_results (eval_run_id, id);

-- ---------------------------------------------------------------------------
-- A/B deneyleri
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS {schema}experiments (
    id             TEXT    NOT NULL PRIMARY KEY,
    tenant_id      TEXT    NOT NULL,
    name           TEXT    NOT NULL,
    agent_name     TEXT    NOT NULL,
    variants       TEXT    NOT NULL CHECK (json_valid(variants)),
    status         INTEGER NOT NULL DEFAULT 0,
    assignment_key TEXT    NULL,
    started_at     TEXT    NULL,
    ended_at       TEXT    NULL,
    updated_at     TEXT    NOT NULL,
    UNIQUE (tenant_id, name)
);

-- Ayni agent icin ayni anda TEK Running deney olabilir.
CREATE UNIQUE INDEX IF NOT EXISTS {schema}experiments_running_agent_uq ON {schema}experiments (tenant_id, agent_name) WHERE status = 1;
CREATE INDEX IF NOT EXISTS {schema}experiments_tenant_agent_idx ON {schema}experiments (tenant_id, agent_name);

-- ---------------------------------------------------------------------------
-- Kota ve olay yayini
-- ---------------------------------------------------------------------------
-- SQLite benzersiz kisitta NULL'lari PostgreSQL gibi birbirinden AYIRT EDER;
-- `agent_name` NULL olan kuralin sinirsiz kez eklenmesini onlemek icin kisit
-- COALESCE'li bir ifade uzerindedir.

CREATE TABLE IF NOT EXISTS {schema}quotas (
    id         TEXT    NOT NULL PRIMARY KEY,
    tenant_id  TEXT    NOT NULL,
    agent_name TEXT    NULL,
    period     INTEGER NOT NULL,
    max_runs   INTEGER NULL,
    max_tokens INTEGER NULL,
    max_cost   TEXT    NULL,
    enabled    INTEGER NOT NULL DEFAULT 1,
    created_at TEXT    NOT NULL,
    updated_at TEXT    NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS {schema}quotas_scope_uq ON {schema}quotas (tenant_id, COALESCE(agent_name, ''), period);

CREATE TABLE IF NOT EXISTS {schema}quota_usage (
    tenant_id    TEXT    NOT NULL,
    agent_name   TEXT    NOT NULL DEFAULT '',
    period       INTEGER NOT NULL,
    period_start TEXT    NOT NULL,
    runs         INTEGER NOT NULL DEFAULT 0,
    tokens       INTEGER NOT NULL DEFAULT 0,
    cost         TEXT    NOT NULL DEFAULT '0',
    updated_at   TEXT    NOT NULL,
    PRIMARY KEY (tenant_id, agent_name, period, period_start)
);

-- 🚨 `events` bir JSON DIZISIDIR. SQLite'ta dizi tipi yoktur; PostgreSQL'in
-- `text[]` sutunu burada JSON metnine donusur ve sorgularda json_each ile acilir.
CREATE TABLE IF NOT EXISTS {schema}webhook_subscriptions (
    id                       TEXT    NOT NULL PRIMARY KEY,
    tenant_id                TEXT    NOT NULL,
    name                     TEXT    NOT NULL,
    url                      TEXT    NOT NULL,
    events                   TEXT    NOT NULL CHECK (json_valid(events)),
    secret_configuration_key TEXT    NULL,
    headers                  TEXT    NOT NULL DEFAULT '{}' CHECK (json_valid(headers)),
    enabled                  INTEGER NOT NULL DEFAULT 1,
    consecutive_failures     INTEGER NOT NULL DEFAULT 0,
    created_at               TEXT    NOT NULL,
    updated_at               TEXT    NOT NULL,
    UNIQUE (tenant_id, name)
);

CREATE TABLE IF NOT EXISTS {schema}webhook_deliveries (
    id              TEXT    NOT NULL PRIMARY KEY,
    subscription_id TEXT    NOT NULL REFERENCES {schema}webhook_subscriptions (id) ON DELETE CASCADE,
    tenant_id       TEXT    NOT NULL,
    event_type      TEXT    NOT NULL,
    payload         TEXT    NOT NULL,
    status          INTEGER NOT NULL,
    attempt         INTEGER NOT NULL DEFAULT 0,
    response_code   INTEGER NULL,
    error           TEXT    NULL,
    created_at      TEXT    NOT NULL,
    delivered_at    TEXT    NULL
);

CREATE INDEX IF NOT EXISTS {schema}webhook_deliveries_subscription_idx ON {schema}webhook_deliveries (subscription_id, created_at DESC);
CREATE INDEX IF NOT EXISTS {schema}webhook_deliveries_tenant_created_idx ON {schema}webhook_deliveries (tenant_id, created_at DESC);
