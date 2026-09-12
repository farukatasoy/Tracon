-- Phase 68 -- run attribution (who, and for which job) and the token breakdown.
--
-- Two questions land in the same row, so they land in the same migration: a
-- second ALTER over the hottest table in the schema is not worth splitting them.
--
-- 🚨 user_id is an OPAQUE string. Tracon neither resolves nor validates its
-- meaning and stores no personal detail of its own; the consumer decides what it
-- identifies (the same stance IDataSubjectResolver takes in phase 64, whose
-- erasure flow covers this column too). It is never read from a request body --
-- that would let any client write spend against another user's name.
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS user_id text;

-- Labels answer "which job was this run made for". A flat string->string map, so
-- jsonb is correct and json is not: K-027's ban applies to POLYMORPHIC payloads
-- whose `$type` discriminator must stay the first property, and this map carries
-- no discriminator. jsonb's key reordering is harmless here and its GIN index is
-- the whole reason the column is queryable (the same reasoning as K-345 for
-- document_embeddings.metadata).
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS labels jsonb;

-- Token breakdown. 🚨 Every one of these is counted INSIDE input_tokens or
-- output_tokens (the Microsoft.Extensions.AI contract), never beside them:
-- SUM(input_tokens) + SUM(cached_input_tokens) double counts.
--
-- They stay NULL when the provider does not report them. Zero is the claim
-- "measured, and it was none" -- writing it would report a 0% cache hit rate as
-- an observation on every provider that reports nothing.
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS cached_input_tokens bigint;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS reasoning_tokens    bigint;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS audio_input_tokens  bigint;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS audio_output_tokens bigint;

-- The cache read charge. numeric(20,10) like every other money column -- binary
-- floating point is not used in money arithmetic.
--
-- 🚨 This is a THIRD ADDEND of a run's total, not a subset of input_cost: the
-- pricing resolver SUBTRACTS the cached tokens out of input_cost and charges
-- them here. Every total that reads input_cost + output_cost must add this too,
-- or it under-reports every run that hit the prompt cache.
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS cached_input_cost numeric(20,10);

-- Partial index: rows with no user (every row written before this migration, and
-- every run of an application that registers no IRunAttributionContext) take no
-- space in it.
CREATE INDEX IF NOT EXISTS runs_tenant_user_idx
    ON {schema}.runs (tenant_id, user_id, started_at DESC)
    WHERE user_id IS NOT NULL;

-- GIN over the label map. jsonb_ops supports BOTH shapes the run list filters
-- with: `labels ? key` for "carries this key" and `labels @> {"k":"v"}` for
-- "carries this exact pair".
CREATE INDEX IF NOT EXISTS runs_labels_idx
    ON {schema}.runs USING gin (labels)
    WHERE labels IS NOT NULL;
