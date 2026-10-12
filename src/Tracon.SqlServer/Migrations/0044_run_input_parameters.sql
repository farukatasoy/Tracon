-- The parameter values a run was started with (F-294).
--
-- For the rationale see PostgreSQL 0056_run_input_parameters.sql. NULL means
-- "nothing was recorded" and '{}' means "recorded, no values"; existing rows
-- stay NULL on purpose.
--
-- The column is added AT THE END: the reader uses fixed column ordinals.

IF COL_LENGTH(N'{schema}.run_inputs', N'parameters') IS NULL
ALTER TABLE {schema}.run_inputs ADD parameters nvarchar(max) NULL;
