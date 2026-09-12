-- Phase 54 -- orphaned run reconciliation.
--
-- For the rationale and the column meaning see PostgreSQL 0026_run_heartbeat.sql.

IF COL_LENGTH(N'{schema}.runs', N'heartbeat_at') IS NULL
ALTER TABLE {schema}.runs ADD heartbeat_at datetimeoffset NULL;

-- 🚨 Wrapped in EXEC: heartbeat_at is added above with ALTER TABLE in the SAME
-- batch; SQL Server compiles the WHOLE batch before running it and without EXEC
-- it gives "Invalid column name 'heartbeat_at'" (see 0003_tool_usage.sql).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_running_heartbeat_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
EXEC(N'CREATE INDEX runs_running_heartbeat_idx
    ON {schema}.runs (heartbeat_at)
    WHERE status = 0;');
