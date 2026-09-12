-- Phase 132 -- the price snapshot gains the unit prices it applied, and the
-- run gains the provider that actually answered.
--
-- For the rationale and the column meanings see PostgreSQL
-- 0042_price_snapshot.sql. SQLite has no `IF NOT EXISTS` for `ALTER TABLE ...
-- ADD COLUMN`; safety comes from the migration runner (the same file runs once).

ALTER TABLE {schema}runs ADD COLUMN model_provider               TEXT    NULL;
ALTER TABLE {schema}runs ADD COLUMN input_price_per_mtok         NUMERIC NULL;
ALTER TABLE {schema}runs ADD COLUMN output_price_per_mtok        NUMERIC NULL;
ALTER TABLE {schema}runs ADD COLUMN cached_input_price_per_mtok  NUMERIC NULL;
