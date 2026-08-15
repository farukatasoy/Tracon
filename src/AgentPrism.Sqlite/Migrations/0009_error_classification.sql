-- Phase 44 -- error classification and failure clustering.
--
-- For the rationale and the column meanings see PostgreSQL
-- 0021_error_classification.sql. SQLite has no `IF NOT EXISTS` for `ALTER TABLE
-- ... ADD COLUMN`; safety comes from the migration runner (same file runs once).
--
-- 🚨 The index name carries the TABLE PREFIX (K-193): in SQLite index names
-- share a single database wide namespace.

ALTER TABLE {schema}runs ADD COLUMN error_class       INTEGER NULL;
ALTER TABLE {schema}runs ADD COLUMN error_fingerprint TEXT    NULL;

CREATE INDEX IF NOT EXISTS {schema}runs_error_class_idx
    ON {schema}runs (tenant_id, error_class, started_at DESC)
    WHERE error_class IS NOT NULL;
