-- Phase 6 — observability, tool approval, MCP and tenant management.
--
-- Tables whose schema was created in 0001 but were NOT FILLED become usable
-- with this migration: tool_invocations, traces, spans.
--
-- The rules are the same as 0001:
--   * The `{schema}` placeholder is replaced at run time.
--   * Time fields are `timestamptz`, always UTC.
--   * Primary keys are `uuid`; the application generates them.

-- ---------------------------------------------------------------------------
-- Runs: model name
-- ---------------------------------------------------------------------------
-- Cost and model breakdown reports need the model name. This column did not
-- exist in 0001 and /api/stats deliberately did not show the cost.
--
-- The column accepts NULL: the model binding of code defined agents can be
-- unknown in the catalog summary. An unknown model is not broken out but counts in totals.

ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS model_id text;

CREATE INDEX IF NOT EXISTS runs_tenant_model_started_idx
    ON {schema}.runs (tenant_id, model_id, started_at DESC)
    WHERE model_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- Tool calls
-- ---------------------------------------------------------------------------
-- The `arguments` and `result` columns were `jsonb`; they become `text`.
--
-- The reason is the same as run_events.payload: RunRecordingAgent formats tool
-- arguments BY HAND to stay AOT compatible (`name=value, name=value`) and the
-- output is not valid JSON. A `jsonb` column would then stop the run with an error.
-- The table was never filled up to this migration, so the conversion loses no data.

ALTER TABLE {schema}.tool_invocations
    ALTER COLUMN arguments TYPE text USING arguments::text,
    ALTER COLUMN result    TYPE text USING result::text;

-- Source of the tool. NULL for code defined tools; the server name for MCP tools.
ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS source text;

-- The tool usage summary (ToolUsageQuery) groups by tool name.
CREATE INDEX IF NOT EXISTS tool_invocations_tool_created_idx
    ON {schema}.tool_invocations (tool_name, created_at DESC);

-- ---------------------------------------------------------------------------
-- Spans
-- ---------------------------------------------------------------------------
-- The W3C span id is stored separately: the user must be able to find the same
-- span in their own APM system (Jaeger, Application Insights). `spans.id` is
-- DERIVED from that id (SHA-256), so the parent span id can be computed without
-- a map and a span written twice does not create a duplicate.
--
-- Not NOT NULL: there is no default for the (empty) rows left from the 0001 era.

ALTER TABLE {schema}.spans ADD COLUMN IF NOT EXISTS span_id text;

CREATE INDEX IF NOT EXISTS traces_run_idx
    ON {schema}.traces (run_id)
    WHERE run_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- Tool approval rules
-- ---------------------------------------------------------------------------
-- Persistent approvals where the user said "do not ask again".
--
-- A rule is bound to the tenant + agent + tool triple. If `agent_name` is NULL
-- the rule covers all agents of the tenant. If `arguments_hash` is NULL it covers
-- every call of the tool; if filled, only the call with the same arguments.
--
-- The uniqueness constraint contains columns that carry NULL and in PostgreSQL
-- NULLs are not equal to each other; therefore an expression index with COALESCE
-- is used instead of a constraint. Otherwise the same rule could be added endlessly.

CREATE TABLE IF NOT EXISTS {schema}.tool_approval_rules (
    id             uuid        NOT NULL PRIMARY KEY,
    tenant_id      text        NOT NULL,
    agent_name     text,
    tool_name      text        NOT NULL,
    arguments_hash text,
    created_by     text,
    created_at     timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS tool_approval_rules_scope_uq
    ON {schema}.tool_approval_rules (
        tenant_id,
        COALESCE(agent_name, ''),
        tool_name,
        COALESCE(arguments_hash, '')
    );

CREATE INDEX IF NOT EXISTS tool_approval_rules_tenant_created_idx
    ON {schema}.tool_approval_rules (tenant_id, created_at DESC);

-- ---------------------------------------------------------------------------
-- MCP servers
-- ---------------------------------------------------------------------------
-- Remote MCP servers. Their tools are discovered at connection time.
--
-- 🚨 CARRIES NO SECRET. The VALUE of the authentication header is not stored
-- here; only the NAME of the configuration key that the value is read from is
-- stored (`authorization_configuration_key`). The value is resolved at run time
-- through IConfiguration and stays in `dotnet user-secrets` or an environment
-- variable. So a database backup, an audit trail or a UI response never carries
-- a secret. Rationale: docs/KARARLAR.md, decision K-059.
--
-- `transport` smallint: 0 = StreamableHttp, 1 = SSE. Stdio IS DELIBERATELY ABSENT —
-- starting a process on the server breaks design rule K2 (decision K-058).

CREATE TABLE IF NOT EXISTS {schema}.mcp_servers (
    id                              uuid        NOT NULL PRIMARY KEY,
    tenant_id                       text        NOT NULL,
    name                            text        NOT NULL,
    description                     text,
    endpoint                        text        NOT NULL,
    transport                       smallint    NOT NULL DEFAULT 0,
    authorization_configuration_key text,
    headers                         jsonb       NOT NULL DEFAULT '{}'::jsonb,
    enabled                         boolean     NOT NULL DEFAULT true,
    requires_approval               boolean     NOT NULL DEFAULT true,
    created_at                      timestamptz NOT NULL,
    updated_at                      timestamptz NOT NULL,
    CONSTRAINT mcp_servers_tenant_name_uq UNIQUE (tenant_id, name)
);
