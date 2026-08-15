-- Phase 45 -- promotion of a production run to an eval case (F-53).
--
-- For the rationale and the column meanings see PostgreSQL 0022_eval_case_source.sql.
-- SQLite has no `IF NOT EXISTS` for `ALTER TABLE ... ADD COLUMN`; safety comes
-- from the migration runner (the same file does not run a second time).
--
-- 🚨 The index name carries the TABLE PREFIX (K-193): in SQLite index names
-- share a single database wide namespace.

ALTER TABLE {schema}eval_cases ADD COLUMN source_run_id TEXT;
ALTER TABLE {schema}eval_cases ADD COLUMN source_kind   INTEGER;
ALTER TABLE {schema}eval_cases ADD COLUMN promoted_at   TEXT;

CREATE UNIQUE INDEX IF NOT EXISTS {schema}eval_cases_source_run_uq
    ON {schema}eval_cases (suite_id, source_run_id)
    WHERE source_run_id IS NOT NULL;
