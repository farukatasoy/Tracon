-- Phase 132 -- the price snapshot gains the unit prices it applied, and the
-- run gains the provider that actually answered.
--
-- 🚨 model_provider is NOT a cost addend and carries no rate: it is who
-- answered, the same role model_id already plays for what answered. The three
-- price columns are RATES (per million tokens), not amounts -- they are never
-- summed into a run's total cost (RunCost.Total() sums input_cost/output_cost/
-- cached_input_cost only, K-483's rule; SqlQueriesBase.CostAddends does not
-- list these three).
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS model_provider text;

-- numeric(20,10) like every other money column here -- binary floating point
-- is not used in money arithmetic. NULL when the price was unknown at the
-- time the run completed (PricingSource.Unknown): a run's price is a
-- snapshot, so this stays NULL forever for that run, never backfilled to 0.
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS input_price_per_mtok        numeric(20,10);
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS output_price_per_mtok       numeric(20,10);
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS cached_input_price_per_mtok numeric(20,10);
