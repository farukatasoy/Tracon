-- Phase 56 -- canary release and automatic rollback.
--
-- For the rationale and the column meanings see PostgreSQL
-- 0028_experiment_canary.sql. canary_policy is nvarchar(max) (as in the K-182
-- pattern NO ISJSON CONSTRAINT is added -- the same reason as
-- audit_log.before/after: only the application writes it, no validation needed).

IF COL_LENGTH(N'{schema}.experiments', N'canary_policy') IS NULL
ALTER TABLE {schema}.experiments ADD canary_policy nvarchar(max) NULL;

IF COL_LENGTH(N'{schema}.experiments', N'rollback_reason') IS NULL
ALTER TABLE {schema}.experiments ADD rollback_reason nvarchar(max) NULL;

-- 🚨 Wrapped in EXEC: canary_policy is added above with ALTER TABLE in the SAME
-- batch; SQL Server compiles the WHOLE batch before running it and without EXEC
-- it gives "Invalid column name 'canary_policy'" (see 0003_tool_usage.sql).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'experiments_running_canary_idx' AND object_id = OBJECT_ID(N'{schema}.experiments'))
EXEC(N'CREATE INDEX experiments_running_canary_idx
    ON {schema}.experiments (status)
    WHERE status = 1 AND canary_policy IS NOT NULL;');
