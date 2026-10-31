-- ---------------------------------------------------------------------------
-- 0049 -- an index for the score summary query
--
-- The only index run_scores carried until now was (tenant_id, run_id) --
-- 0017's own comment says "Listing the scores of a run -- that is the only
-- access pattern." IRunScoreStore.SummarizeAsync adds a second one: a time
-- range scoped to a tenant. Without this index that query is a sequential
-- scan of the whole table.
--
-- Column order is (tenant_id, created_at): every query is scoped to a
-- tenant first, so the tenant column leads.
--
-- Migration 0048 rewrites the OTHER index on this table (the uniqueness one);
-- the two migrations are independent and are ordered this way only because the
-- score breakdown reads the `name` column that 0048 introduces.
--
-- ---------------------------------------------------------------------------

CREATE INDEX IF NOT EXISTS run_scores_created_at_idx
    ON {schema}.run_scores (tenant_id, created_at);
