-- Parameterized agent input surface.
--
-- For the rationale see PostgreSQL 0036_eval_case_parameters.sql.
ALTER TABLE {schema}eval_cases ADD COLUMN parameters TEXT NULL;
