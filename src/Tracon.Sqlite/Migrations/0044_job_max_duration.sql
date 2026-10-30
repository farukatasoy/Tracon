-- A job's own time limit.
--
-- For the rationale see PostgreSQL 0057_job_max_duration.sql. NULL means the job
-- sets none and the handler key's mapped limit, if any, applies; existing rows stay
-- NULL on purpose. Whole milliseconds; SQLite's INTEGER holds 64 bits.
--
-- 🚨 SQLite has no `ADD COLUMN IF NOT EXISTS`. The guard is the migration
-- ledger: a numbered file runs exactly once, so a plain ADD COLUMN is correct.

ALTER TABLE {schema}jobs ADD COLUMN max_duration_ms INTEGER;
