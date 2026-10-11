-- A standing approval rule pins the source of the tool it approves.
--
-- For the rationale see PostgreSQL 0055_approval_rule_source.sql. Existing rows
-- stay NULL on purpose: a NULL rule does not approve an MCP tool.
--
-- SQL Server's UNIQUE CONSTRAINT already treats NULL as equal to NULL, so the
-- column is simply added to the constraint's column list (0017 pattern).

IF COL_LENGTH(N'{schema}.tool_approval_rules', N'source_fingerprint') IS NULL
ALTER TABLE {schema}.tool_approval_rules ADD source_fingerprint nvarchar(128) NULL;

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'tool_approval_rules_scope_uq' AND parent_object_id = OBJECT_ID(N'{schema}.tool_approval_rules'))
ALTER TABLE {schema}.tool_approval_rules DROP CONSTRAINT tool_approval_rules_scope_uq;

-- 🚨 Wrapped in EXEC: source_fingerprint is added above with ALTER TABLE in the
-- SAME batch; SQL Server compiles the WHOLE batch before running it and without
-- EXEC it gives "Invalid column name 'source_fingerprint'" (see 0017).
EXEC(N'ALTER TABLE {schema}.tool_approval_rules ADD CONSTRAINT tool_approval_rules_scope_uq
    UNIQUE (tenant_id, agent_name, tool_name, arguments_hash, conditions_hash, source_fingerprint);');
