-- ---------------------------------------------------------------------------
-- 0036 -- an index for the score summary query (phase 154)
--
-- For the rationale see PostgreSQL 0049_run_scores_created_at_index.sql: the
-- only index run_scores carried was (tenant_id, run_id); IRunScoreStore.
-- SummarizeAsync's time-range query needs a second one.
-- ---------------------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'run_scores_created_at_idx' AND object_id = OBJECT_ID(N'{schema}.run_scores'))
CREATE INDEX run_scores_created_at_idx
    ON {schema}.run_scores (tenant_id, created_at);
