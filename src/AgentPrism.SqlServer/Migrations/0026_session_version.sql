-- Optimistic concurrency for sessions.
--
-- For the rationale and the failure it closes see PostgreSQL
-- 0039_session_version.sql.
--
-- SQL Server has no `ADD COLUMN IF NOT EXISTS`; the guard is an explicit
-- catalog check, the same shape the other SQL Server migrations use.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'{schema}.sessions') AND name = N'version')
BEGIN
    ALTER TABLE {schema}.sessions
        ADD version bigint NOT NULL CONSTRAINT DF_sessions_version DEFAULT 1;
END;
