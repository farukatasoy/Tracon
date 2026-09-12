-- Tracon SQLite schema (phase 24).
--
-- This file is the ACCUMULATED result of the PostgreSQL 0001-0013 migrations (the
-- same pattern as the SQL Server 0001_initial.sql, K-178). SQLite support opens
-- in phase 24; there is no installation to upgrade. Later changes are added as
-- 0002, 0003 ...
--
-- 🚨 The numbering DOES NOT MATCH the other providers and it does not need to.
-- Rationale: docs/KARARLAR.md, decision K-178.
--
-- Rules:
--   * SQLite has no schema concept. The `{schema}` placeholder is replaced at
--     run time with TraconSqliteOptions.TablePrefix (the default is
--     `tracon_`); the consumer own tables are not touched (the SQLite
--     counterpart of K-013).
--   * Time fields are TEXT, ISO-8601 UTC (`yyyy-MM-ddTHH:mm:ss.fffffffZ`),
--     written by SqliteDialect.AddTimestamp.
--   * Primary keys are TEXT (uuid v7, upper case — see the "uuid is written in
--     upper case" note in SqliteDialect); the application generates them.
--   * Enum values are stored as INTEGER and are the SAME numbers as on the
--     other providers.
--   * `numeric`/`decimal` columns are TEXT; REAL IS NOT USED (floating point is
--     banned in money arithmetic, docs/24-SQLITE.md section 24.2).
--
-- Type mapping (PostgreSQL -> SQLite):
--   uuid           -> TEXT
--   text           -> TEXT (a length limit has no effect in SQLite)
--   jsonb / json    -> TEXT (with a json_valid() constraint)
--   timestamptz    -> TEXT (ISO-8601 UTC)
--   boolean        -> INTEGER (0/1)
--   bytea          -> BLOB
--   numeric(20,10) -> TEXT
--   text[]         -> TEXT (JSON array, opened with json_each)
--   date           -> TEXT (yyyy-MM-dd)
--
-- Foreign key enforcement is ENABLED at connection open with `Foreign Keys=True`
-- (SqliteDataSource); otherwise SQLite silently IGNORES the REFERENCES
-- clauses.

-- ---------------------------------------------------------------------------
-- Tenants
-- ---------------------------------------------------------------------------
-- `tenant_id` in the other tables is the same text as the `slug` value of this
-- table but it is NOT linked with a FOREIGN KEY; it would produce an unexpected
-- run time error for a tenant that has no record.

CREATE TABLE IF NOT EXISTS {schema}tenants (
    id           TEXT NOT NULL PRIMARY KEY,
    slug         TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    created_at   TEXT NOT NULL
);

-- ---------------------------------------------------------------------------
-- Agent definitions
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

-- Immutable version history. Rollback DOES NOT DELETE the old version; writes it as a new version.
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
-- Sessions
-- ---------------------------------------------------------------------------
-- `state` is the SerializeSessionAsync output of Microsoft Agent Framework and
-- it is OPAQUE. K-027 does not hold here (see the same note in the SQL Server
-- DDL): SQLite stores JSON as text and the order is already kept.

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
-- Conversations
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
-- Runs
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

-- Append-only event stream (decision K-014). Events are NOT UPDATED, only appended.
-- `payload` is deliberately free text and CARRIES NO json_valid constraint:
-- RunEventWriter formats tool arguments by hand and the output can be invalid JSON.
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
-- Observability and audit
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

-- `before` / `after` CARRY NO json_valid constraint. Observability must not break
-- functionality: if the audit trail write failed with a constraint violation the
-- real operation would fail too.
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
-- Tool approval rules
-- ---------------------------------------------------------------------------
-- Like PostgreSQL, SQLite TELLS NULLs APART in a unique constraint (K-184 is
-- specific to SQL Server and DOES NOT HOLD here); a plain UNIQUE would let the
-- same rule (while agent_name is NULL) be added endlessly. The constraint is
-- therefore built ON a COALESCE expression.

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
-- MCP servers
-- ---------------------------------------------------------------------------
-- 🚨 CARRIES NO SECRET. Only the NAME of the configuration key that the value is
-- read from is stored (K-059). `transport` INTEGER: 0 = StreamableHttp, 1 = SSE.

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
-- Agent skills
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

-- Like PostgreSQL, SQLite TELLS NULLs APART in a unique constraint; a plain
-- UNIQUE (tenant_id, skill_name, script_name) is the right behaviour here (more
-- than one record with a NULL script_name -- unless each belongs to a different
-- skill -- is already separated by skill_name).
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
-- Attachments and persistent agent file memory
-- ---------------------------------------------------------------------------
-- `session_id` is DELIBERATELY NOT a foreign key: an attachment can be uploaded
-- before its own session is ever opened (K-112).

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
-- Workflows
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
-- Job queue and scheduling
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

-- Like PostgreSQL, SQLite TELLS NULLs APART in a unique constraint; jobs created
-- by hand (with no schedule, NULL schedule_id) DO NOT CLASH with a plain
-- UNIQUE (schedule_id, scheduled_for) constraint.
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
-- Evaluation (eval)
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

-- `case_id` DELIBERATELY carries no foreign key: even if a case is later changed
-- or deleted, the past result record stays understandable.
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
-- A/B experiments
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

-- Only ONE Running experiment can exist for the same agent at a time.
CREATE UNIQUE INDEX IF NOT EXISTS {schema}experiments_running_agent_uq ON {schema}experiments (tenant_id, agent_name) WHERE status = 1;
CREATE INDEX IF NOT EXISTS {schema}experiments_tenant_agent_idx ON {schema}experiments (tenant_id, agent_name);

-- ---------------------------------------------------------------------------
-- Quota and event publishing
-- ---------------------------------------------------------------------------
-- Like PostgreSQL, SQLite TELLS NULLs APART in a unique constraint; to stop a
-- rule with a NULL `agent_name` from being added endlessly the constraint is on
-- a COALESCE expression.

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

-- 🚨 `events` IS A JSON ARRAY. SQLite has no array type; the PostgreSQL `text[]`
-- column becomes JSON text here and queries open it with json_each.
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
