-- Phase 218 -- the MCP server's `authorization_configuration_key` column goes away (F-290, K-869).
--
-- The PostgreSQL twin is 0062; migration numbers are independent per provider (K-178). The
-- rule, the four cases and the "no way back" warning are identical -- see that file's header.
--
-- 🚨 `json_each()` returns its own `key` and `value` columns, so the match aliases the table
-- function and qualifies the column (docs/hafiza/sqlite.md). The outer table is named with
-- its prefix (K-193, K-259): a bare name never matches here.
--
-- The column is dropped in place with ALTER TABLE ... DROP COLUMN (SQLite 3.35+), not by
-- rebuilding the table: it is in no index, and a rebuild under `foreign_keys = ON` would
-- fire the cascades of the tables that point at this one (K-666).

UPDATE {schema}mcp_servers
   SET header_configuration_keys = json_set(header_configuration_keys, '$.Authorization', authorization_configuration_key)
 WHERE authorization_configuration_key IS NOT NULL
   AND authorization_configuration_key <> ''
   AND oauth_enabled = 0
   AND NOT EXISTS (
        SELECT 1
          FROM json_each({schema}mcp_servers.header_configuration_keys) AS existing_key
         WHERE lower(existing_key.key) = 'authorization');

ALTER TABLE {schema}mcp_servers DROP COLUMN authorization_configuration_key;
