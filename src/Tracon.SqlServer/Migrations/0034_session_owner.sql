-- Per-user session ownership.
--
-- For the rationale, the null semantics and the "no foreign key" decision see
-- PostgreSQL 0047_session_owner.sql.
--
-- 🚨 nvarchar(200), not nvarchar(max): the column is INDEXED below and SQL
-- Server cannot index nvarchar(max). RunLabels.MaxUserIdLength bounds a legal
-- identity at exactly 200, so nothing writable can be truncated here. This is
-- the same pair of constraints runs.user_id already carries.

IF COL_LENGTH(N'{schema}.sessions', N'owner_id') IS NULL
ALTER TABLE {schema}.sessions ADD owner_id nvarchar(200) NULL;

-- 🚨 Wrapped in EXEC: owner_id is added by the ALTER above in the SAME batch,
-- and SQL Server compiles the WHOLE batch before running it -- without EXEC
-- this fails with "Invalid column name 'owner_id'" (the same trap
-- 0021_run_attribution.sql records).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'sessions_tenant_owner_updated_idx' AND object_id = OBJECT_ID(N'{schema}.sessions'))
EXEC(N'CREATE INDEX sessions_tenant_owner_updated_idx
    ON {schema}.sessions (tenant_id, owner_id, updated_at DESC)
    WHERE owner_id IS NOT NULL;');
