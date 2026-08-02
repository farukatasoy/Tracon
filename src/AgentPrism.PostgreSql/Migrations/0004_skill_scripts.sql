-- Faz 11 -- skill script'leri ve calistirma izinleri.

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
    -- NULL = skill'in TUM script'leri.
    script_name text,
    granted_by  text,
    granted_at  timestamptz NOT NULL,
    -- NULL = suresiz.
    expires_at  timestamptz,
    revoked_at  timestamptz
);

-- 🚨 Duz bir UNIQUE (tenant_id, skill_name, script_name) YETMEZ: PostgreSQL'de
-- NULL hicbir NULL'a esit degildir, bu yuzden ayni skill icin sinirsiz sayida
-- "tum script'ler" kaydi olusabilirdi. Ayni ders Faz 6'da tool_approval_rules
-- tablosunda ogrenildi. Cozum COALESCE'li ifade indeksidir.
CREATE UNIQUE INDEX IF NOT EXISTS skill_script_grants_uq
    ON {schema}.skill_script_grants (tenant_id, skill_name, COALESCE(script_name, ''));

CREATE INDEX IF NOT EXISTS skill_script_grants_lookup_idx
    ON {schema}.skill_script_grants (tenant_id, skill_name)
    WHERE revoked_at IS NULL;
