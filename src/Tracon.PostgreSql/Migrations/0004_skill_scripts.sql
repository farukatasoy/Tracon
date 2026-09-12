-- Phase 11 -- skill scripts and execution grants.

CREATE TABLE IF NOT EXISTS {schema}.agent_skill_scripts (
    id                uuid        NOT NULL PRIMARY KEY,
    skill_id          uuid        NOT NULL REFERENCES {schema}.agent_skills (id) ON DELETE CASCADE,
    name              text        NOT NULL,
    description       text,
    extension         text        NOT NULL,
    content           text        NOT NULL,
    parameters_schema jsonb,
    created_at        timestamptz NOT NULL,
    CONSTRAINT agent_skill_scripts_skill_name_uq UNIQUE (skill_id, name)
);

CREATE TABLE IF NOT EXISTS {schema}.skill_script_grants (
    id          uuid        NOT NULL PRIMARY KEY,
    tenant_id   text        NOT NULL,
    skill_name  text        NOT NULL,
    -- NULL = ALL scripts of the skill.
    script_name text,
    granted_by  text,
    granted_at  timestamptz NOT NULL,
    -- NULL = no expiry.
    expires_at  timestamptz,
    revoked_at  timestamptz
);

-- 🚨 A plain UNIQUE (tenant_id, skill_name, script_name) IS NOT ENOUGH: in
-- PostgreSQL no NULL is equal to any NULL, so an endless number of "all
-- scripts" records could exist for the same skill. The same lesson was learned
-- in phase 6 on the tool_approval_rules table. The fix is a COALESCE expression index.
CREATE UNIQUE INDEX IF NOT EXISTS skill_script_grants_uq
    ON {schema}.skill_script_grants (tenant_id, skill_name, COALESCE(script_name, ''));

CREATE INDEX IF NOT EXISTS skill_script_grants_lookup_idx
    ON {schema}.skill_script_grants (tenant_id, skill_name)
    WHERE revoked_at IS NULL;
