-- Phase 28 -- NON token usage and cost per tool call.
--
-- For the rationale and the column meanings see PostgreSQL 0015_tool_usage.sql.
--
-- SQLite has no `IF NOT EXISTS` for `ALTER TABLE ... ADD COLUMN`. Safety comes
-- from the migration runner: applied migrations are recorded and the same file
-- is not run a second time.
--
-- `decimal` needs no special handling: the driver always writes TEXT and is
-- culture independent (the Precision/Scale duty of SQL Server IS ABSENT here).
--
-- 🚨 The index name carries the TABLE PREFIX (K-193): in SQLite index names
-- share a single database wide namespace. Without the prefix two different
-- TablePrefix values that share the same `.db` file clash and the second index
-- is SILENTLY skipped.

ALTER TABLE {schema}tool_invocations ADD COLUMN usage_unit      TEXT    NULL;
ALTER TABLE {schema}tool_invocations ADD COLUMN usage_quantity  TEXT    NULL;
ALTER TABLE {schema}tool_invocations ADD COLUMN usage_estimated INTEGER NULL;
ALTER TABLE {schema}tool_invocations ADD COLUMN cost            TEXT    NULL;
ALTER TABLE {schema}tool_invocations ADD COLUMN cost_currency   TEXT    NULL;

CREATE INDEX IF NOT EXISTS {schema}tool_invocations_usage_idx
    ON {schema}tool_invocations (usage_unit, created_at DESC)
    WHERE usage_unit IS NOT NULL;
