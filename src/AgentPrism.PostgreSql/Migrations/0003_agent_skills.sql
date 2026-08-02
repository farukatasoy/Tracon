-- Faz 10 -- markdown tabanli agent skill'leri ve kaynaklari.

CREATE TABLE IF NOT EXISTS {schema}.agent_skills (
    id            uuid        NOT NULL PRIMARY KEY,
    tenant_id     text        NOT NULL,
    name          text        NOT NULL,
    description   text        NOT NULL,
    instructions  text        NOT NULL,
    compatibility text,
    license       text,
    allowed_tools text,
    metadata      jsonb       NOT NULL DEFAULT '{}'::jsonb,
    enabled       boolean     NOT NULL DEFAULT true,
    version       integer     NOT NULL DEFAULT 1,
    created_at    timestamptz NOT NULL,
    updated_at    timestamptz NOT NULL,
    CONSTRAINT agent_skills_tenant_name_uq UNIQUE (tenant_id, name)
);

CREATE TABLE IF NOT EXISTS {schema}.agent_skill_resources (
    id          uuid        NOT NULL PRIMARY KEY,
    skill_id    uuid        NOT NULL REFERENCES {schema}.agent_skills (id) ON DELETE CASCADE,
    name        text        NOT NULL,
    description text,
    media_type  text        NOT NULL DEFAULT 'text/plain',
    content     text        NOT NULL,
    created_at  timestamptz NOT NULL,
    CONSTRAINT agent_skill_resources_skill_name_uq UNIQUE (skill_id, name)
);

CREATE INDEX IF NOT EXISTS agent_skills_tenant_enabled_updated_idx
    ON {schema}.agent_skills (tenant_id, enabled, updated_at DESC);