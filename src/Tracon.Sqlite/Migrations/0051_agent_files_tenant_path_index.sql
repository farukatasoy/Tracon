-- ---------------------------------------------------------------------------
-- 0051 -- an index for the files under a path prefix, by tenant
--
-- For the rationale see PostgreSQL 0064_agent_files_tenant_path_index.sql: the
-- only index agent_files carried was (tenant_id, agent_name, path), and the
-- session workspace reads and deletes a directory with no agent in scope.
--
-- SQLite answers `LIKE 'prefix%'` from this index only when LIKE is case
-- sensitive and the column is compared with the BINARY collation; the data
-- source turns `case_sensitive_like` on for every connection, and the column
-- has no other collation.
-- ---------------------------------------------------------------------------

CREATE INDEX IF NOT EXISTS {schema}agent_files_tenant_path_idx
    ON {schema}agent_files (tenant_id, path);
