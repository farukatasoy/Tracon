-- ---------------------------------------------------------------------------
-- 0064 -- an index for the files under a path prefix, by tenant
--
-- The session workspace keeps the files of one session under one directory,
-- `/{tenant}/~workspace/{key}/...` (see SessionWorkspacePaths). Three queries
-- read or delete that directory with no agent in scope, because an erasure of
-- a session runs outside any run: the size of a session (checked on every
-- write), the size of all the workspaces of a tenant (the same), and the delete
-- of a session's files (with the session). The only index agent_files carried
-- was the uniqueness one, (tenant_id, agent_name, path); a query that names no
-- agent cannot use it for the path.
--
-- Measured on PostgreSQL 18, 200 000 rows of 2 KB (20 000 of them workspace
-- files in 2 000 sessions), warm cache:
--   size of one session ........ 14.1 ms -> 0.5 ms
--   delete of one session ...... 13.9 ms -> 0.03 ms
--   size of a tenant's workspaces  12.9 ms ->  7.2 ms (the sum itself dominates)
-- Without the index the first two grow with the whole table, and the delete
-- runs once per expired session in a retention batch.
--
-- `text_pattern_ops` because the database collation (en_US.utf8) does not let
-- a b-tree answer `LIKE 'prefix%'`; the operator class does. The queries
-- compare with `LIKE ... ESCAPE '\'`, which the class serves.
-- ---------------------------------------------------------------------------

CREATE INDEX IF NOT EXISTS agent_files_tenant_path_idx
    ON {schema}.agent_files (tenant_id, path text_pattern_ops);
