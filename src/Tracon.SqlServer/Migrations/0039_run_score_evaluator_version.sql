-- The version that produced a score (phase 176).
--
-- For the rationale — why the column exists, why NULL is not "version zero"
-- and why there is no backfill — see PostgreSQL 0051_run_score_evaluator_version.sql.
--
-- 🚨 The column is added at the END of the table on purpose: SqlRunScoreStore
-- reads by bare ordinal, so inserting a column in the middle would shift every
-- reader silently.
--
-- nvarchar(128), not nvarchar(max): the bound is the one RunScoreRules enforces
-- before the write, so nothing writable is truncated.

IF COL_LENGTH(N'{schema}.run_scores', N'evaluator_version') IS NULL
ALTER TABLE {schema}.run_scores ADD evaluator_version nvarchar(128) NULL;
