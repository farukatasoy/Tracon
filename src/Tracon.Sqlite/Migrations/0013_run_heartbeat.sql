-- Phase 54 -- orphaned run reconciliation.
--
-- For the rationale and the column meaning see PostgreSQL 0026_run_heartbeat.sql.
-- SQLite has no `IF NOT EXISTS` for `ALTER TABLE ... ADD COLUMN`; safety comes
-- from the migration runner (the same file does not run a second time). The
-- index name carries the TABLE PREFIX (K-193): in SQLite index names share a
-- single database wide namespace.

ALTER TABLE {schema}runs ADD COLUMN heartbeat_at TEXT NULL;

CREATE INDEX IF NOT EXISTS {schema}runs_running_heartbeat_idx
    ON {schema}runs (heartbeat_at)
    WHERE status = 0;
