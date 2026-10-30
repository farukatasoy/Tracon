-- Prompt-cache writes become the fourth term of a run's cost.
--
-- A provider that bills a cache write apart from plain input (Anthropic: 1.25 times the
-- input price for a five-minute entry, 2 times for a one-hour entry) charged more for it
-- than Tracon recorded: the written tokens are INSIDE input_tokens and were priced at the
-- plain input rate. Five additions carry what a run needs to price them:
--
--   * `cache_write_input_tokens`, `cache_write_long_input_tokens` -- the written tokens by
--     time to live (short, long), counted INSIDE `input_tokens` like `cached_input_tokens`.
--   * `cache_write_cost` -- the charge, a FOURTH addend of the run's cost, not a subset of
--     `input_cost`: the resolver subtracts the priced written tokens out of the input
--     charge and bills them here (SqlQueriesBase.CostAddends lists it).
--   * `cache_write_price_per_mtok`, `cache_write_long_price_per_mtok` -- the unit prices that
--     produced the charge, a snapshot like the other `*_price_per_mtok` columns. RATES, not
--     amounts: they are never summed into a total.
--
-- 🚨 NULL stays NULL on every existing row and means "no write was priced separately". There
-- is no backfill: a past row's charge was computed without the rate, and recomputing it now
-- would rewrite history (a priced run is a snapshot).
--
-- The new `runs` columns are added AT THE END: the reader uses fixed column ordinals
--numeric(20,10) like every other money column here.

ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS cache_write_input_tokens      bigint;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS cache_write_long_input_tokens bigint;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS cache_write_cost              numeric(20,10);
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS cache_write_price_per_mtok      numeric(20,10);
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS cache_write_long_price_per_mtok numeric(20,10);
