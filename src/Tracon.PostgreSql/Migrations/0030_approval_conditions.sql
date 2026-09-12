-- Phase 63 -- argument-level approval conditions.
--
-- argument_conditions: the jsonb serialization of a ToolArgumentCondition[]. NULL
-- (or an empty array) means the rule matches every call, same as a NULL
-- arguments_hash. Mutually exclusive with arguments_hash -- validated by the
-- endpoint, not by the database.
--
-- conditions_hash: the deterministic fingerprint of the (sorted) condition set,
-- computed in SqlToolApprovalRuleStore. It exists ONLY to widen the uniqueness
-- key: without it, a second rule with the SAME conditions could be added
-- endlessly, since arguments_hash stays NULL for a condition-based rule (the
-- COALESCE lesson from 0004_skill_scripts.sql -- NULLs are not equal to each
-- other in PostgreSQL).

ALTER TABLE {schema}.tool_approval_rules ADD COLUMN IF NOT EXISTS argument_conditions jsonb;
ALTER TABLE {schema}.tool_approval_rules ADD COLUMN IF NOT EXISTS conditions_hash      text;

DROP INDEX IF EXISTS {schema}.tool_approval_rules_scope_uq;

CREATE UNIQUE INDEX IF NOT EXISTS tool_approval_rules_scope_uq
    ON {schema}.tool_approval_rules (
        tenant_id,
        COALESCE(agent_name, ''),
        tool_name,
        COALESCE(arguments_hash, ''),
        COALESCE(conditions_hash, '')
    );
