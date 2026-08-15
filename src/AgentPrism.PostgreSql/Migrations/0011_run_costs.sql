-- Phase 20 -- run cost: the price snapshot (the continuation of K-032).

-- input_cost/output_cost are `numeric(20,10)` -- binary floating point is not
-- used in money arithmetic. Both can stay NULL: if the price is undefined the
-- value written is NULL, not ZERO (see RunPricingResolver,
-- docs/20-MALIYET-VE-GOSTERGE-PANELI.md section 20.1).
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS input_cost     numeric(20,10);
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS output_cost    numeric(20,10);
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS cost_currency  text;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS pricing_source smallint; -- 0=Catalog 1=Configuration 2=Unknown

-- Partial index to find rows that carry a cost quickly: rows where input_cost is
-- NULL (the price was never computed) take no space in the index.
CREATE INDEX IF NOT EXISTS runs_tenant_cost_idx
    ON {schema}.runs (tenant_id, started_at DESC)
    WHERE input_cost IS NOT NULL;
