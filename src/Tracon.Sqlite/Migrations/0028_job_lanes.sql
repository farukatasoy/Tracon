-- Job queue lanes (Phase 129).
--
-- For the rationale see PostgreSQL 0041_job_lanes.sql. SQLite has no
-- `ADD COLUMN IF NOT EXISTS`; migrations here run exactly once, tracked by
-- the migration table, so the bare ADD COLUMN is correct.
--
-- 🚨 The index name carries the TABLE PREFIX (K-193): in SQLite index names
-- share a single database wide namespace.

ALTER TABLE {schema}jobs ADD COLUMN lane TEXT NOT NULL DEFAULT 'default';
ALTER TABLE {schema}job_schedules ADD COLUMN lane TEXT NOT NULL DEFAULT 'default';

-- 🚨 status IN (0, 1, 2), not (0, 1): a PRE-EXISTING gap found while proving
-- this index is used. LeaseJob's WHERE also reclaims status = 2 (Running, an
-- owning worker that died mid-execution) -- see the matching PostgreSQL
-- migration (0041_job_lanes.sql) for the measured planner behavior this fixes.
DROP INDEX IF EXISTS {schema}jobs_claim_idx;

CREATE INDEX IF NOT EXISTS {schema}jobs_claim_idx
    ON {schema}jobs (lane, status, scheduled_for)
    WHERE status IN (0, 1, 2);
