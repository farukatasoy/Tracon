-- ---------------------------------------------------------------------------
-- 0036 -- an index for the score summary query (phase 154)
--
-- For the rationale see PostgreSQL 0049_run_scores_created_at_index.sql: the
-- only index run_scores carried was (tenant_id, run_id); IRunScoreStore.
-- SummarizeAsync's time-range query needs a second one.
-- ---------------------------------------------------------------------------

CREATE INDEX IF NOT EXISTS {schema}run_scores_created_at_idx
    ON {schema}run_scores (tenant_id, created_at);
