-- What a run spends that is not tokens.
--
-- A workflow function node reports the characters it synthesized, the seconds it
-- rendered, the GPU minutes it used; a tool already measured its own (0015). Until
-- now none of it reached the run's total, its budget or the tenant's quota. Three
-- additions carry it:
--
--   * `run_non_token_usage` -- one row per item a function node reported, written the
--     moment the node reports it (a cancelled run keeps what it had spent).
--   * four `runs` columns -- the SNAPSHOT of the run's reported total, written once
--     when the run closes (RunCompletion.NonTokenCost), exactly as the token cost is.
--   * `usage_provider` and `usage_model` on `tool_invocations` -- a tool's measurement
--     now names who did the work.
--
-- 🚨 The snapshot is NOT a term of the token cost. input_cost/output_cost/
-- cached_input_cost stay the price of one model's tokens; a caller that needs "what did
-- this run spend" adds the two through RunSpend, never through the SQL total
-- (SqlQueriesBase.CostAddends does not list these columns on purpose).
--
-- 🚨 NULL and zero stay different. `non_token_unsummed IS NULL` means the run reported
-- nothing (every row from before this migration, and every run that reported none);
-- it is the PRESENCE marker of the snapshot. `non_token_cost IS NULL` with a non-NULL
-- `non_token_unsummed` means items were reported and none carried an amount in the
-- run's currency: unknown, not free. Existing rows stay NULL on purpose; nobody
-- reported a number for them.
--
-- The new `runs` columns are added AT THE END: the reader uses fixed column ordinals

ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS non_token_cost      numeric(20,10);
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS non_token_currency  text;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS non_token_estimated boolean;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS non_token_unsummed  integer;

ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS usage_provider text;
ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS usage_model    text;

-- One row per reported item. No tenant column: the owning run carries the tenant (the
-- `tool_invocations` pattern), and ON DELETE CASCADE removes the rows with the run --
-- retention and a data subject's erasure reach them through `runs`. `quantity` and
-- `cost` are numeric(20,10) like the tool columns in 0015: binary floating point is not
-- used in money arithmetic.
CREATE TABLE IF NOT EXISTS {schema}.run_non_token_usage (
    id         uuid          NOT NULL PRIMARY KEY,
    run_id     uuid          NOT NULL REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    node_name  text,
    unit       text          NOT NULL,
    quantity   numeric(20,10) NOT NULL,
    cost       numeric(20,10),
    currency   text,
    estimated  boolean       NOT NULL,
    provider   text,
    model      text,
    created_at timestamptz   NOT NULL
);

CREATE INDEX IF NOT EXISTS run_non_token_usage_run_idx
    ON {schema}.run_non_token_usage (run_id, created_at);
