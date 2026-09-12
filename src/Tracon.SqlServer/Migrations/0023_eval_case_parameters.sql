-- Phase 86 -- parameterized agent input surface (F-34).
--
-- For the rationale see PostgreSQL 0036_eval_case_parameters.sql.
IF COL_LENGTH(N'{schema}.eval_cases', N'parameters') IS NULL
ALTER TABLE {schema}.eval_cases ADD parameters nvarchar(max) NULL;
