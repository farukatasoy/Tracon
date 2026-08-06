-- ---------------------------------------------------------------------------
-- 0006 — Oturum birincil anahtari kiraciyi da kapsar (Faz 41)
--
-- 🚨 GUVENLIK DUZELTMESI. Gerekce PostgreSQL 0018 ile aynidir: `sessions.id`
-- cagiran tarafindan verilir ve tek basina birincil anahtar oldugunda bir
-- kiraci baska bir kiracinin oturumunu uzerine yazabiliyordu.
--
-- SQLite birincil anahtari DEGISTIREMEZ; tablo yeniden kurulur ve veri
-- tasinir. Indeksler tabloyla birlikte dusuruldugu icin yeniden olusturulur
-- (indeks ad alani SQLite'ta veritabani genelindedir, bu yuzden adlar onek
-- tasir).
-- ---------------------------------------------------------------------------

CREATE TABLE {schema}sessions_new (
    id             TEXT    NOT NULL,
    tenant_id      TEXT    NOT NULL,
    agent_name     TEXT    NOT NULL,
    state          TEXT    NOT NULL CHECK (json_valid(state)),
    schema_version INTEGER NOT NULL,
    created_at     TEXT    NOT NULL,
    updated_at     TEXT    NOT NULL,
    PRIMARY KEY (tenant_id, id)
);

INSERT INTO {schema}sessions_new (id, tenant_id, agent_name, state, schema_version, created_at, updated_at)
SELECT id, tenant_id, agent_name, state, schema_version, created_at, updated_at FROM {schema}sessions;

DROP TABLE {schema}sessions;

ALTER TABLE {schema}sessions_new RENAME TO {schema}sessions;

CREATE INDEX IF NOT EXISTS {schema}sessions_tenant_updated_idx ON {schema}sessions (tenant_id, updated_at DESC);
CREATE INDEX IF NOT EXISTS {schema}sessions_tenant_agent_updated_idx ON {schema}sessions (tenant_id, agent_name, updated_at DESC);
