-- The inbox of workflow requests.
--
-- For the rationale and the column meanings see PostgreSQL 0061_workflow_requests.sql.
--
-- 🚨 Table and index names carry the PREFIX: SQLite has no schema and
-- index names share a single database wide namespace.

CREATE TABLE IF NOT EXISTS {schema}workflow_requests (
    run_id               TEXT    NOT NULL REFERENCES {schema}runs (id) ON DELETE CASCADE,
    request_id           TEXT    NOT NULL,
    tenant_id            TEXT    NOT NULL,
    session_id           TEXT    NOT NULL,
    workflow_name        TEXT    NOT NULL,
    port_id              TEXT    NOT NULL,
    event_seq            INTEGER NOT NULL,
    requested_at         TEXT    NOT NULL,
    expires_at           TEXT,
    status               INTEGER NOT NULL,
    answered_at          TEXT,
    last_response_run_id TEXT,
    closed_at            TEXT,
    PRIMARY KEY (run_id, request_id)
);

CREATE INDEX IF NOT EXISTS {schema}workflow_requests_inbox_idx
    ON {schema}workflow_requests (tenant_id, status, requested_at);

CREATE INDEX IF NOT EXISTS {schema}workflow_requests_logical_idx
    ON {schema}workflow_requests (tenant_id, session_id, request_id, requested_at);

CREATE INDEX IF NOT EXISTS {schema}workflow_requests_expiry_idx
    ON {schema}workflow_requests (expires_at)
    WHERE status = 0 AND expires_at IS NOT NULL;
