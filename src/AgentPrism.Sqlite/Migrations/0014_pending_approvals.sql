-- Phase 55 -- asynchronous approval inbox.
--
-- For the rationale and the column meanings see PostgreSQL 0027_pending_approvals.sql.
--
-- 🚨 Table and index names carry the PREFIX (K-193): SQLite has no schema and
-- index names share a single database wide namespace.

CREATE TABLE IF NOT EXISTS {schema}pending_approvals (
    id          TEXT NOT NULL PRIMARY KEY,
    tenant_id   TEXT NOT NULL,
    run_id      TEXT NOT NULL REFERENCES {schema}runs (id) ON DELETE CASCADE,
    session_id  TEXT NOT NULL,
    request_id  TEXT NOT NULL,
    tool_name   TEXT NOT NULL,
    arguments   TEXT,
    status      INTEGER NOT NULL,
    decided_by  TEXT,
    decided_at  TEXT,
    expires_at  TEXT NOT NULL,
    created_at  TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS {schema}pending_approvals_tenant_status_idx
    ON {schema}pending_approvals (tenant_id, status, created_at);

CREATE INDEX IF NOT EXISTS {schema}pending_approvals_expiry_idx
    ON {schema}pending_approvals (status, expires_at);
