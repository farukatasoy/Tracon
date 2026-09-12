-- Phase 31 -- human (or judge) score per run and per message.
--
-- For the rationale and the column meanings see PostgreSQL 0017_run_scores.sql.
--
-- 🚨 Table and index names carry the PREFIX (K-193): in SQLite object names
-- share a single database wide namespace.
--
-- The upsert is exactly the same as PostgreSQL (K-194): message_id is levelled
-- with COALESCE(…, ''), author IS DELIBERATELY NOT COALESCED -- like PostgreSQL,
-- SQLite DOES NOT treat NULLs as equal, so when author is empty (an
-- installation without identity) every call opens a new row.

CREATE TABLE IF NOT EXISTS {schema}run_scores (
    id          TEXT    NOT NULL PRIMARY KEY,
    tenant_id   TEXT    NOT NULL,
    run_id      TEXT    NOT NULL,
    message_id  TEXT    NULL,
    kind        INTEGER NOT NULL,
    value       INTEGER NOT NULL,
    comment     TEXT    NULL,
    source      TEXT    NOT NULL,
    author      TEXT    NULL,
    created_at  TEXT    NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS {schema}run_scores_target_author_idx
    ON {schema}run_scores (tenant_id, run_id, COALESCE(message_id, ''), author);

CREATE INDEX IF NOT EXISTS {schema}run_scores_run_idx
    ON {schema}run_scores (tenant_id, run_id);
