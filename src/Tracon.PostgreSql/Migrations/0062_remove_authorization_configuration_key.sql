-- Phase 218 -- the MCP server's `authorization_configuration_key` column goes away (F-290, K-869).
--
-- Phase 190 moved the `Authorization` header into `header_configuration_keys` (a map
-- from a header name to the NAME of the configuration key its value is read from) and
-- deprecated the single-key column. This file moves the stored data and drops the column.
--
-- The rule is the one the connection code applied, so an upgraded row sends exactly the
-- header it sent before:
--   * an empty or NULL key has nothing to move;
--   * a row with OAuth on never read the column (OAuth manages `Authorization`), so the
--     value is dropped, not moved -- moving it would create the combination a save refuses;
--   * a row whose map already names `Authorization` (any letter case) keeps its map: the
--     map always won over the column;
--   * otherwise the key name becomes the map's `Authorization` entry.
-- Only the key NAME is ever stored (K-059); no secret value moves.
--
-- 🚨 The column is dropped in the same migration, so there is no way back: an application
-- build from before this migration cannot read the table.

UPDATE {schema}.mcp_servers
   SET header_configuration_keys = header_configuration_keys
       || jsonb_build_object('Authorization', authorization_configuration_key)
 WHERE authorization_configuration_key IS NOT NULL
   AND authorization_configuration_key <> ''
   AND NOT oauth_enabled
   AND NOT EXISTS (
        SELECT 1
          FROM jsonb_object_keys(header_configuration_keys) AS existing_key
         WHERE lower(existing_key) = 'authorization');

ALTER TABLE {schema}.mcp_servers DROP COLUMN IF EXISTS authorization_configuration_key;
