-- Phase 28 -- NON token usage and cost per tool call.
--
-- The cost model of phase 20 assumes tokens and writes to the `runs` table. Voice
-- tools spend no tokens but do create a charge: text to speech is billed by
-- CHARACTER, speech to text by SECOND. Until now `tool_invocations` carried no
-- usage column at all.
--
-- The two usage kinds are not summed: different units cannot be added. Reports
-- show the voice cost as a SEPARATE item from the token cost.
-- Rationale: docs/28-SES-TOOLLARI.md, section 28.5.

-- The unit name is free text; the own tools of Tracon use `characters` and
-- `seconds` (ToolUsageUnits). NULL = the call reported no usage, which is true
-- for the great majority of the calls.
ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS usage_unit     text;

-- The billed quantity. `numeric(20,10)` -- binary floating point is not used in
-- money and usage arithmetic (the same reason as the cost columns in 0011).
ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS usage_quantity numeric(20,10);

-- Whether the quantity came from the provider or is an estimate. Showing an
-- estimate as a measurement is inventing a price (K-032); the UI separates them.
ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS usage_estimated boolean;

-- The computed amount. If the price is undefined it stays NULL, not ZERO.
ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS cost           numeric(20,10);
ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS cost_currency  text;

-- The `pricing_source` column is DELIBERATELY ABSENT: the only source of a voice
-- price is configuration (the model catalog carries no voice price), so there is
-- no source to tell apart. Whether the usage is an estimate is separate
-- information and it stays in the `usage_estimated` column.

-- Partial index to find calls that carry usage quickly: rows without usage
-- (the majority) take no space in the index.
CREATE INDEX IF NOT EXISTS tool_invocations_usage_idx
    ON {schema}.tool_invocations (usage_unit, created_at DESC)
    WHERE usage_unit IS NOT NULL;
