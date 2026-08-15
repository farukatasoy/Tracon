-- Phase 29 -- summary record of real time speech connections.
--
-- For the rationale and the column meanings see PostgreSQL 0016_voice_sessions.sql.
--
-- 🚨 Table and index names carry the PREFIX (K-193): in SQLite object names
-- share a single database wide namespace. Without the prefix two different
-- TablePrefix values that share the same `.db` file clash.
--
-- `numeric` needs no special handling: the driver writes TEXT and is culture
-- independent (the Precision/Scale duty of SQL Server IS ABSENT here).

CREATE TABLE IF NOT EXISTS {schema}voice_sessions (
    id            TEXT    NOT NULL PRIMARY KEY,
    tenant_id     TEXT    NOT NULL,
    session_id    TEXT    NOT NULL,
    agent_name    TEXT    NOT NULL,
    started_at    TEXT    NOT NULL,
    ended_at      TEXT    NULL,
    turns         INTEGER NOT NULL DEFAULT 0,
    input_seconds TEXT    NULL,
    output_chars  INTEGER NULL,
    end_reason    INTEGER NULL,
    created_by    TEXT    NULL
);

CREATE INDEX IF NOT EXISTS {schema}voice_sessions_tenant_started_idx
    ON {schema}voice_sessions (tenant_id, started_at DESC);
