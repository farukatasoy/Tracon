-- ---------------------------------------------------------------------------
-- 0006 — The session primary key also covers the tenant (phase 41)
--
-- 🚨 SECURITY FIX. The rationale is the same as PostgreSQL 0018: `sessions.id`
-- is given by the caller and while it was the primary key on its own, one
-- tenant could overwrite the session of another tenant.
--
-- SQLite CANNOT CHANGE a primary key; the table is rebuilt and the data is
-- moved. Indexes are dropped with the table, so they are recreated (the index
-- namespace is database wide in SQLite, which is why the names carry the
-- prefix).
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
