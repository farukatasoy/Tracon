-- Phase 63 -- argument-level approval conditions.
--
-- For the column rationale see PostgreSQL 0030_approval_conditions.sql.
-- argument_conditions is nvarchar(max) (K-182 pattern, no ISJSON constraint --
-- only the application writes it).
--
-- Unlike PostgreSQL/SQLite, SQL Server's UNIQUE CONSTRAINT already treats NULL
-- as equal to NULL for uniqueness, so no COALESCE expression index is needed --
-- conditions_hash is simply added to the existing constraint's column list.

IF COL_LENGTH(N'{schema}.tool_approval_rules', N'argument_conditions') IS NULL
ALTER TABLE {schema}.tool_approval_rules ADD argument_conditions nvarchar(max) NULL;

IF COL_LENGTH(N'{schema}.tool_approval_rules', N'conditions_hash') IS NULL
ALTER TABLE {schema}.tool_approval_rules ADD conditions_hash nvarchar(128) NULL;

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'tool_approval_rules_scope_uq' AND parent_object_id = OBJECT_ID(N'{schema}.tool_approval_rules'))
ALTER TABLE {schema}.tool_approval_rules DROP CONSTRAINT tool_approval_rules_scope_uq;

-- 🚨 Wrapped in EXEC: conditions_hash is added above with ALTER TABLE in the SAME
-- batch; SQL Server compiles the WHOLE batch before running it and without EXEC
-- it gives "Invalid column name 'conditions_hash'" (see 0009_error_classification.sql).
EXEC(N'ALTER TABLE {schema}.tool_approval_rules ADD CONSTRAINT tool_approval_rules_scope_uq
    UNIQUE (tenant_id, agent_name, tool_name, arguments_hash, conditions_hash);');
