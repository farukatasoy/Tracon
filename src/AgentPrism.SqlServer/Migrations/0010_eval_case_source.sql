-- Phase 45 -- promotion of a production run to an eval case (F-53).
--
-- For the rationale and the column meanings see PostgreSQL 0022_eval_case_source.sql.

IF COL_LENGTH(N'{schema}.eval_cases', N'source_run_id') IS NULL
ALTER TABLE {schema}.eval_cases ADD source_run_id uniqueidentifier NULL;

IF COL_LENGTH(N'{schema}.eval_cases', N'source_kind') IS NULL
ALTER TABLE {schema}.eval_cases ADD source_kind smallint NULL;

IF COL_LENGTH(N'{schema}.eval_cases', N'promoted_at') IS NULL
ALTER TABLE {schema}.eval_cases ADD promoted_at datetimeoffset NULL;

-- Partial (filtered) unique index: rows where source_run_id is NULL (written by
-- hand) stay outside the constraint. The syntax is the same as PostgreSQL (K-178).
--
-- 🚨 Wrapped in EXEC: source_run_id is added above with ALTER TABLE in the SAME
-- batch; without EXEC it gives "Invalid column name" (see 0003_tool_usage.sql).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'eval_cases_source_run_uq' AND object_id = OBJECT_ID(N'{schema}.eval_cases'))
EXEC(N'CREATE UNIQUE INDEX eval_cases_source_run_uq
    ON {schema}.eval_cases (suite_id, source_run_id)
    WHERE source_run_id IS NOT NULL;');
