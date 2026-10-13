-- A job's own time limit (F-323).
--
-- For the rationale see PostgreSQL 0057_job_max_duration.sql. NULL means the job
-- sets none and the handler key's mapped limit, if any, applies; existing rows stay
-- NULL on purpose. Whole milliseconds as `bigint`.
--
-- The column is added AT THE END: the reader uses fixed column ordinals.

IF COL_LENGTH(N'{schema}.jobs', N'max_duration_ms') IS NULL
ALTER TABLE {schema}.jobs ADD max_duration_ms bigint NULL;
