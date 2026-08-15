-- Phase 43 -- Idempotency-Key support.
--
-- For the rationale and the column meanings see PostgreSQL 0020_idempotency_keys.sql.
--
-- 🚨 Table and index names carry the PREFIX (K-193). `key` is a RESERVED word in
-- SQLite; it is put in double quotes as "key" everywhere.

CREATE TABLE IF NOT EXISTS {schema}idempotency_keys (
    tenant_id    TEXT    NOT NULL,
    "key"        TEXT    NOT NULL,
    fingerprint  TEXT    NOT NULL,
    state        INTEGER NOT NULL,
    status_code  INTEGER,
    content_type TEXT,
    body         TEXT,
    run_id       TEXT,
    created_at   TEXT    NOT NULL,
    completed_at TEXT,
    PRIMARY KEY (tenant_id, "key")
);

CREATE INDEX IF NOT EXISTS {schema}idempotency_keys_created_idx
    ON {schema}idempotency_keys (tenant_id, created_at DESC);
