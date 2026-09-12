-- Phase 68 -- run attribution and the token breakdown.
--
-- For the rationale and the column meanings see PostgreSQL
-- 0034_run_attribution.sql. SQLite has no `IF NOT EXISTS` for `ALTER TABLE ...
-- ADD COLUMN`; safety comes from the migration runner (the same file runs once).
--
-- 🚨 The index name carries the TABLE PREFIX (K-193): SQLite index names share
-- one database-wide namespace.

ALTER TABLE {schema}runs ADD COLUMN user_id             TEXT    NULL;
ALTER TABLE {schema}runs ADD COLUMN labels              TEXT    NULL;
ALTER TABLE {schema}runs ADD COLUMN cached_input_tokens INTEGER NULL;
ALTER TABLE {schema}runs ADD COLUMN reasoning_tokens    INTEGER NULL;
ALTER TABLE {schema}runs ADD COLUMN audio_input_tokens  INTEGER NULL;
ALTER TABLE {schema}runs ADD COLUMN audio_output_tokens INTEGER NULL;
ALTER TABLE {schema}runs ADD COLUMN cached_input_cost   NUMERIC NULL;

CREATE INDEX IF NOT EXISTS {schema}runs_tenant_user_idx
    ON {schema}runs (tenant_id, user_id, started_at DESC)
    WHERE user_id IS NOT NULL;

-- The label map is JSON text and is expanded with json_each. No index: the same
-- reasoning as SQL Server, and SQLite's json_each is a table-valued function
-- that no index can serve anyway.
