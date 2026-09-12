-- Phase 86 -- parameterized agent input surface (F-34).
--
-- A parameterized agent cannot be evaluated without a value for each
-- placeholder its instructions reference; a case now carries the values a
-- run needs, the same shape and JSON-text convention as runs.labels
-- (0034_run_attribution.sql).
ALTER TABLE {schema}.eval_cases ADD COLUMN IF NOT EXISTS parameters jsonb;
