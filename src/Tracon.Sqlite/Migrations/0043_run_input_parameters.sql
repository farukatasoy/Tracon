-- The parameter values a run was started with.
--
-- For the rationale see PostgreSQL 0056_run_input_parameters.sql. NULL means
-- "nothing was recorded" and '{}' means "recorded, no values"; existing rows
-- stay NULL on purpose.
--
-- 🚨 SQLite has no `ADD COLUMN IF NOT EXISTS`. The guard is the migration
-- ledger: a numbered file runs exactly once, so a plain ADD COLUMN is correct.

ALTER TABLE {schema}run_inputs ADD COLUMN parameters TEXT;
