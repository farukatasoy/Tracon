-- Phase 218 -- the MCP server's `authorization_configuration_key` column goes away (F-290, K-869).
--
-- The PostgreSQL twin is 0062; migration numbers are independent per provider (K-178). The
-- rule, the four cases and the "no way back" warning are identical -- see that file's header.
--
-- 🚨 T-SQL compiles a batch UP FRONT, and MigrationRunner sends each file as ONE batch. The
-- whole move runs through EXEC(N'...') behind a COL_LENGTH guard: a batch that already
-- dropped the column and failed before the ledger write is re-run from the top, and its
-- UPDATE would not compile any more (the K-475 class).
--
-- JSON_MODIFY takes the new value as a plain string and escapes it. OPENJSON's `key` column
-- has a binary collation, so the case-insensitive match lowers it first.

IF COL_LENGTH(N'{schema}.mcp_servers', N'authorization_configuration_key') IS NOT NULL
BEGIN
    EXEC(N'
    UPDATE {schema}.mcp_servers
       SET header_configuration_keys = JSON_MODIFY(header_configuration_keys, N''$.Authorization'', authorization_configuration_key)
     WHERE authorization_configuration_key IS NOT NULL
       AND authorization_configuration_key <> N''''
       AND oauth_enabled = 0
       AND NOT EXISTS (
            SELECT 1
              FROM OPENJSON(header_configuration_keys) AS existing_key
             WHERE LOWER(existing_key.[key]) = N''authorization'');');

    EXEC(N'ALTER TABLE {schema}.mcp_servers DROP COLUMN authorization_configuration_key;');
END
