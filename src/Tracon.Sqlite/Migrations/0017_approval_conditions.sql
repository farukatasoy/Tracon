-- Phase 63 -- argument-level approval conditions.
--
-- For the column rationale see PostgreSQL 0030_approval_conditions.sql. SQLite
-- has no `IF NOT EXISTS` for `ALTER TABLE ... ADD COLUMN`; safety comes from the
-- migration runner (the same file does not run a second time).
--
-- 🚨 The index name carries the TABLE PREFIX (K-193): in SQLite index names
-- share a single database wide namespace.

ALTER TABLE {schema}tool_approval_rules ADD COLUMN argument_conditions TEXT;
ALTER TABLE {schema}tool_approval_rules ADD COLUMN conditions_hash      TEXT;

DROP INDEX IF EXISTS {schema}tool_approval_rules_scope_uq;

CREATE UNIQUE INDEX IF NOT EXISTS {schema}tool_approval_rules_scope_uq
    ON {schema}tool_approval_rules (
        tenant_id,
        COALESCE(agent_name, ''),
        tool_name,
        COALESCE(arguments_hash, ''),
        COALESCE(conditions_hash, '')
    );
