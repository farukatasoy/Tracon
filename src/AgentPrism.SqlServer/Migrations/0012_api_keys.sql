-- Phase 53 -- per tenant API keys and scopes.
--
-- For the rationale and the column meanings see PostgreSQL 0025_api_keys.sql.
-- scopes is a JSON array (K-182): it is opened with OPENJSON.

IF OBJECT_ID(N'{schema}.api_keys', N'U') IS NULL
CREATE TABLE {schema}.api_keys (
    id           uniqueidentifier NOT NULL CONSTRAINT api_keys_pk PRIMARY KEY,
    tenant_id    nvarchar(200)    NOT NULL,
    name         nvarchar(200)    NOT NULL,
    -- SHA-256 is a fixed 32 bytes; varbinary(32) can be indexed (nvarchar(max)
    -- or varbinary(max) CANNOT BE INDEXED).
    key_hash     varbinary(32)    NOT NULL,
    key_prefix   nvarchar(32)     NOT NULL,
    scopes       nvarchar(max)    NOT NULL,
    expires_at   datetimeoffset,
    revoked_at   datetimeoffset,
    last_used_at datetimeoffset,
    created_at   datetimeoffset   NOT NULL,
    CONSTRAINT api_keys_hash_uq UNIQUE (key_hash)
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'api_keys_tenant_idx' AND object_id = OBJECT_ID(N'{schema}.api_keys'))
CREATE INDEX api_keys_tenant_idx ON {schema}.api_keys (tenant_id, created_at DESC);
