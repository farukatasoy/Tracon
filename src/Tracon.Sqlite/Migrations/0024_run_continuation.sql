-- Phase 87 -- interrupted-run continuation.
--
-- For the rationale and the column meaning see PostgreSQL
-- 0037_run_continuation.sql.
--
-- 🚨 There is no `IF NOT EXISTS` for `ALTER TABLE ... ADD COLUMN`; safety comes
-- from the migration runner (the same file does not run a second time).
-- Table and index names carry the PREFIX (K-193): SQLite has no schema and
-- index names share a single database wide namespace.

ALTER TABLE {schema}runs ADD COLUMN continued_from_run_id TEXT NULL;

CREATE INDEX IF NOT EXISTS {schema}runs_continued_from_idx
    ON {schema}runs (tenant_id, continued_from_run_id)
    WHERE continued_from_run_id IS NOT NULL;
