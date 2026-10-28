-- Phase 261 (F-759) -- per-tenant tool policy.
--
-- The closed sets of tool names and permissions a tenant may run. Absence of a
-- row (not an empty array) means "no policy": the tenant is unrestricted unless
-- Tracon:Tools:RequireTenantPolicy is on; see ITenantToolPolicyStore. Two empty
-- arrays are a policy that allows no tool.
--
-- Neither column holds a secret: a tool name is a name the code registered and a
-- permission is an opaque string the tool declared (K-059 does not apply).

CREATE TABLE IF NOT EXISTS {schema}.tenant_tool_policies (
    tenant_id            text        NOT NULL PRIMARY KEY,
    allowed_tools        text[]      NOT NULL,
    granted_permissions  text[]      NOT NULL,
    updated_at           timestamptz NOT NULL
);
