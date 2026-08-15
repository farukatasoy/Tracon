-- Phase 25 -- data retention policy and archiving.
--
-- NO default policy IS ADDED: an empty policy table = nothing is deleted.
--
-- 🚨 Index names also carry the TABLE PREFIX (K-193): in SQLite object names
-- share a single database wide namespace, they are NOT scoped by schema or
-- table.

CREATE TABLE IF NOT EXISTS {schema}retention_policies (
    id           TEXT    NOT NULL PRIMARY KEY,
    tenant_id    TEXT    NOT NULL,          -- '*' = all tenants
    target       TEXT    NOT NULL,          -- 'run_events', 'spans', ...
    max_age_days INTEGER NULL,
    max_rows     INTEGER NULL,
    archive      INTEGER NOT NULL DEFAULT 0,
    enabled      INTEGER NOT NULL DEFAULT 1,
    created_at   TEXT    NOT NULL,
    updated_at   TEXT    NOT NULL,
    UNIQUE (tenant_id, target)
);

-- tenant_id WAS NOT in the first draft of the doc; the tenant based policy
-- (K-198) needs the runs to be filterable by tenant as well.
CREATE TABLE IF NOT EXISTS {schema}retention_runs (
    id            TEXT    NOT NULL PRIMARY KEY,
    tenant_id     TEXT    NOT NULL,
    target        TEXT    NOT NULL,
    deleted_rows  INTEGER NOT NULL DEFAULT 0,
    archived_rows INTEGER NOT NULL DEFAULT 0,
    started_at    TEXT    NOT NULL,
    completed_at  TEXT    NULL,
    error         TEXT    NULL
);

CREATE INDEX IF NOT EXISTS {schema}retention_runs_tenant_started_idx ON {schema}retention_runs (tenant_id, started_at DESC);
