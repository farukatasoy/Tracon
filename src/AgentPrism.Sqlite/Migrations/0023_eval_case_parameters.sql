-- Phase 86 -- parameterized agent input surface (F-34).
--
-- For the rationale see PostgreSQL 0036_eval_case_parameters.sql.
ALTER TABLE {schema}eval_cases ADD COLUMN parameters TEXT NULL;
