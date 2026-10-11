-- A standing approval rule pins the source of the tool it approves.
--
-- A rule named an agent, a tool and optionally the arguments -- never the MCP
-- server behind the tool. An MCP tool is named {server}_{tool}, and the server
-- record, its endpoint included, is written by another role. Pointing the same
-- server name at another endpoint kept every tool name, so the rule approved a
-- tool of an endpoint it was never given for. source_fingerprint is the SHA-256
-- of the endpoint the tool came from when the rule was written (64 upper-case
-- hexadecimal characters); NULL for a tool defined in code.
--
-- 🚨 Existing rows stay NULL on purpose, like skill_script_grants.content_hash
-- (0053): a NULL rule does not approve an MCP tool, so a standing MCP rule asks
-- once more after the upgrade. Backfilling today's endpoint would approve an
-- endpoint that may already have changed since the rule was given.
--
-- The column joins the uniqueness key (the COALESCE lesson from
-- 0030_approval_conditions.sql): a rule for the same tool of a server that moved
-- to another endpoint is a new rule, never the old one returned in its place.

ALTER TABLE {schema}.tool_approval_rules ADD COLUMN IF NOT EXISTS source_fingerprint text;

DROP INDEX IF EXISTS {schema}.tool_approval_rules_scope_uq;

CREATE UNIQUE INDEX IF NOT EXISTS tool_approval_rules_scope_uq
    ON {schema}.tool_approval_rules (
        tenant_id,
        COALESCE(agent_name, ''),
        tool_name,
        COALESCE(arguments_hash, ''),
        COALESCE(conditions_hash, ''),
        COALESCE(source_fingerprint, '')
    );
