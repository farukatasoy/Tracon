-- Phase 65 -- per-tenant model provider bindings (BYOK) and egress policy.
--
-- For the rationale and column meanings see PostgreSQL 0032_tenant_provider_bindings.sql.
--
-- 🚨 Table names carry the PREFIX (K-193): SQLite has no schema and object
-- names share a single database-wide namespace.

CREATE TABLE IF NOT EXISTS {schema}tenant_provider_bindings (
    tenant_id                   TEXT NOT NULL,
    provider_name                TEXT NOT NULL,
    api_key_configuration_name   TEXT NOT NULL,
    endpoint                     TEXT,
    updated_at                   TEXT NOT NULL,
    PRIMARY KEY (tenant_id, provider_name)
);

CREATE TABLE IF NOT EXISTS {schema}tenant_egress_policies (
    tenant_id          TEXT NOT NULL PRIMARY KEY,
    allowed_providers  TEXT NOT NULL,
    updated_at         TEXT NOT NULL
);
