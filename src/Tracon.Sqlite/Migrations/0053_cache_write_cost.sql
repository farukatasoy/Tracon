-- Prompt-cache writes become the fourth term of a run's cost.
--
-- For the rationale and the column meanings see PostgreSQL 0066_cache_write_cost.sql.
-- SQLite has no `IF NOT EXISTS` for `ALTER TABLE ... ADD COLUMN`; safety comes from the
-- migration runner (the same file runs once).
--
-- The charge is TEXT like input_cost and output_cost: the driver writes a decimal as text,
-- which keeps the amount exact. The two unit prices are NUMERIC like the other
-- `*_price_per_mtok` columns.

ALTER TABLE {schema}runs ADD COLUMN cache_write_input_tokens         INTEGER NULL;
ALTER TABLE {schema}runs ADD COLUMN cache_write_long_input_tokens    INTEGER NULL;
ALTER TABLE {schema}runs ADD COLUMN cache_write_cost                 TEXT    NULL;
ALTER TABLE {schema}runs ADD COLUMN cache_write_price_per_mtok       NUMERIC NULL;
ALTER TABLE {schema}runs ADD COLUMN cache_write_long_price_per_mtok  NUMERIC NULL;
