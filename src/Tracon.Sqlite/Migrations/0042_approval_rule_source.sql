-- A standing approval rule pins the source of the tool it approves.
--
-- For the rationale see PostgreSQL 0055_approval_rule_source.sql. Existing rows
-- stay NULL on purpose: a NULL rule does not approve an MCP tool.
--
-- 🚨 SQLite has no `ADD COLUMN IF NOT EXISTS`. The guard is the migration
-- ledger: a numbered file runs exactly once, so a plain ADD COLUMN is correct.
-- The index name carries the TABLE PREFIX.

ALTER TABLE {schema}tool_approval_rules ADD COLUMN source_fingerprint TEXT;

DROP INDEX IF EXISTS {schema}tool_approval_rules_scope_uq;

CREATE UNIQUE INDEX IF NOT EXISTS {schema}tool_approval_rules_scope_uq
    ON {schema}tool_approval_rules (
        tenant_id,
        COALESCE(agent_name, ''),
        tool_name,
        COALESCE(arguments_hash, ''),
        COALESCE(conditions_hash, ''),
        COALESCE(source_fingerprint, '')
    );
