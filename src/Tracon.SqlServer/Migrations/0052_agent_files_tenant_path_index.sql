-- ---------------------------------------------------------------------------
-- 0052 -- an index for the files under a path prefix, by tenant (phase 234)
--
-- For the rationale see PostgreSQL 0064_agent_files_tenant_path_index.sql: the
-- only index agent_files carried was (tenant_id, agent_name, path), and the
-- session workspace reads and deletes a directory with no agent in scope.
--
-- The key is 600 characters (1 200 bytes) wide, inside the 1 700 byte limit of
-- a non-clustered index.
-- ---------------------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'agent_files_tenant_path_idx' AND object_id = OBJECT_ID(N'{schema}.agent_files'))
CREATE INDEX agent_files_tenant_path_idx
    ON {schema}.agent_files (tenant_id, path);
