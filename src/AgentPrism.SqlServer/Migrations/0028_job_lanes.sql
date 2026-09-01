-- Job queue lanes (Phase 129).
--
-- For the rationale see PostgreSQL 0041_job_lanes.sql. SQL Server has no
-- `ADD COLUMN IF NOT EXISTS`; the guard is an explicit catalog check, the
-- same shape the other SQL Server migrations use. lane is nvarchar(64):
-- JobLanes.IsValidName caps a lane name at 64 characters.

IF COL_LENGTH(N'{schema}.jobs', N'lane') IS NULL
ALTER TABLE {schema}.jobs ADD lane nvarchar(64) NOT NULL CONSTRAINT DF_jobs_lane DEFAULT 'default';

IF COL_LENGTH(N'{schema}.job_schedules', N'lane') IS NULL
ALTER TABLE {schema}.job_schedules ADD lane nvarchar(64) NOT NULL CONSTRAINT DF_job_schedules_lane DEFAULT 'default';

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'jobs_claim_idx' AND object_id = OBJECT_ID(N'{schema}.jobs'))
DROP INDEX jobs_claim_idx ON {schema}.jobs;

-- 🚨 Wrapped in EXEC: `lane` is added above with ALTER TABLE in the SAME
-- batch; SQL Server compiles the WHOLE batch before running it and without
-- EXEC it gives "Invalid column name 'lane'" (see 0017_approval_conditions.sql).
--
-- 🚨 status IN (0, 1, 2), not (0, 1): a PRE-EXISTING gap found while proving
-- this index is used. LeaseJob's WHERE also reclaims status = 2 (Running,
-- an owning worker that died mid-execution) -- see the matching PostgreSQL
-- migration (0041_job_lanes.sql) for the measured planner behavior this fixes.
EXEC(N'CREATE INDEX jobs_claim_idx ON {schema}.jobs (lane, status, scheduled_for) WHERE status IN (0, 1, 2);');
