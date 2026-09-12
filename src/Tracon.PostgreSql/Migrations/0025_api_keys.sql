-- Phase 53 -- per tenant API keys and scopes.
--
-- 🚨 key_hash IS NOT THE RAW VALUE -- it is an irreversible SHA-256 digest
-- (docs/53-KIRACI-API-ANAHTARLARI.md, section 53.2). The raw value is returned
-- once in the creation response only and is written nowhere.

CREATE TABLE IF NOT EXISTS {schema}.api_keys (
    id           uuid        NOT NULL PRIMARY KEY,
    tenant_id    text        NOT NULL,
    name         text        NOT NULL,
    key_hash     bytea       NOT NULL,
    key_prefix   text        NOT NULL,
    scopes       text[]      NOT NULL,
    expires_at   timestamptz,
    revoked_at   timestamptz,
    last_used_at timestamptz,
    created_at   timestamptz NOT NULL
);

-- The SEARCH runs on the digest (FindByHashAsync); uniqueness is also a guard
-- against a collision (impossible in practice but cheap).
CREATE UNIQUE INDEX IF NOT EXISTS api_keys_hash_uq ON {schema}.api_keys (key_hash);

CREATE INDEX IF NOT EXISTS api_keys_tenant_idx ON {schema}.api_keys (tenant_id, created_at DESC);
