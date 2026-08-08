-- Faz 53 -- kiraci bazli API anahtarlari ve kapsamlar.
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0025_api_keys.sql'e bakin.
-- scopes bir JSON dizisidir (K-182): OPENJSON ile acilir.

IF OBJECT_ID(N'{schema}.api_keys', N'U') IS NULL
CREATE TABLE {schema}.api_keys (
    id           uniqueidentifier NOT NULL CONSTRAINT api_keys_pk PRIMARY KEY,
    tenant_id    nvarchar(200)    NOT NULL,
    name         nvarchar(200)    NOT NULL,
    -- SHA-256 sabit 32 bayttir; varbinary(32) indekslenebilir (nvarchar(max)
    -- veya varbinary(max) INDEKSLENEMEZ).
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
