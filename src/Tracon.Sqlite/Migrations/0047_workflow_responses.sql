-- The answers to workflow requests.
--
-- For the rationale and the column meanings see PostgreSQL 0060_workflow_responses.sql.
-- The claim index is a partial unique index (measured on SQLite 3.54).
--
-- 🚨 Table and index names carry the PREFIX: SQLite has no schema and
-- index names share a single database wide namespace.

CREATE TABLE IF NOT EXISTS {schema}workflow_responses (
    response_run_id TEXT    NOT NULL PRIMARY KEY,
    tenant_id       TEXT    NOT NULL,
    source_run_id   TEXT    NOT NULL REFERENCES {schema}runs (id) ON DELETE CASCADE,
    session_id      TEXT    NOT NULL,
    request_id      TEXT    NOT NULL,
    claim_scope     TEXT,
    answer_hash     TEXT    NOT NULL,
    status          INTEGER NOT NULL,
    responded_by    TEXT,
    responded_at    TEXT    NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS {schema}workflow_responses_claim_idx
    ON {schema}workflow_responses (tenant_id, claim_scope, request_id)
    WHERE claim_scope IS NOT NULL AND status <> 2;

CREATE INDEX IF NOT EXISTS {schema}workflow_responses_source_idx
    ON {schema}workflow_responses (tenant_id, source_run_id);
