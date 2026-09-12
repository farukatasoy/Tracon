-- Optimistic concurrency for sessions.
--
-- For the rationale and the failure it closes see PostgreSQL
-- 0039_session_version.sql.
--
-- SQLite has no `ADD COLUMN IF NOT EXISTS`; migrations here run exactly once,
-- tracked by the migration table, so the bare ADD COLUMN is correct.

ALTER TABLE {schema}sessions ADD COLUMN version INTEGER NOT NULL DEFAULT 1;
