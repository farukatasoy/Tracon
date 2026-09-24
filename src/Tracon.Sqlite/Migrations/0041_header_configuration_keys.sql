-- Credential headers declared by the NAME of their configuration key (phase 190).
--
-- For the rationale see PostgreSQL 0054_header_configuration_keys.sql. Same shape
-- as `headers`: TEXT, `{}` default, json_valid check.
--
-- 🚨 SQLite has no `ADD COLUMN IF NOT EXISTS`. The guard is the migration
-- ledger: a numbered file runs exactly once, so a plain ADD COLUMN is correct.
-- A NOT NULL column needs a non-NULL default to be added to a table with rows.

ALTER TABLE {schema}mcp_servers
    ADD COLUMN header_configuration_keys TEXT NOT NULL DEFAULT '{}' CHECK (json_valid(header_configuration_keys));

ALTER TABLE {schema}webhook_subscriptions
    ADD COLUMN header_configuration_keys TEXT NOT NULL DEFAULT '{}' CHECK (json_valid(header_configuration_keys));
