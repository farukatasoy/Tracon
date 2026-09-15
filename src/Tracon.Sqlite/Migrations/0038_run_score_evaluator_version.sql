-- The version that produced a score (phase 176).
--
-- For the rationale — why the column exists, why NULL is not "version zero"
-- and why there is no backfill — see PostgreSQL 0051_run_score_evaluator_version.sql.
--
-- 🚨 The column is added at the END of the table on purpose: SqlRunScoreStore
-- reads by bare ordinal, so inserting a column in the middle would shift every
-- reader silently.
--
-- 🚨 SQLite has no `ADD COLUMN IF NOT EXISTS`. The guard is the migration
-- ledger: a numbered file runs exactly once, so a plain ADD COLUMN is correct.
--
-- The length bound is not spelled here: SQLite ignores the declared width and
-- RunScoreRules enforces it before the write, which is where the bound lives
-- for all three providers.

ALTER TABLE {schema}run_scores ADD COLUMN evaluator_version TEXT NULL;
