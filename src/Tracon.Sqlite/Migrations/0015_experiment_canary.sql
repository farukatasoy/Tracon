-- Phase 56 -- canary release and automatic rollback.
--
-- For the rationale and the column meanings see PostgreSQL
-- 0028_experiment_canary.sql. SQLite has no `IF NOT EXISTS` for
-- `ALTER TABLE ... ADD COLUMN`; safety comes from the migration runner.
--
-- 🚨 The index name carries the TABLE PREFIX (K-193): in SQLite index names
-- share a single database wide namespace.

ALTER TABLE {schema}experiments ADD COLUMN canary_policy   TEXT;
ALTER TABLE {schema}experiments ADD COLUMN rollback_reason TEXT;

CREATE INDEX IF NOT EXISTS {schema}experiments_running_canary_idx
    ON {schema}experiments (status)
    WHERE status = 1 AND canary_policy IS NOT NULL;
