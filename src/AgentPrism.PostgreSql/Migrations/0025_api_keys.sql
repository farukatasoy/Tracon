-- Faz 53 -- kiraci bazli API anahtarlari ve kapsamlar.
--
-- 🚨 key_hash HAM DEGER DEGILDIR -- geri donduruleyemez bir SHA-256 ozetidir
-- (docs/53-KIRACI-API-ANAHTARLARI.md, bolum 53.2). Ham deger yalnizca
-- olusturma yanitinda bir kez doner ve hicbir yere yazilmaz.

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

-- Ozet uzerinde ARAMA yapilir (FindByHashAsync); benzersizlik ayrica bir
-- carpisma (pratikte imkansiz ama ucuz) korumasidir.
CREATE UNIQUE INDEX IF NOT EXISTS api_keys_hash_uq ON {schema}.api_keys (key_hash);

CREATE INDEX IF NOT EXISTS api_keys_tenant_idx ON {schema}.api_keys (tenant_id, created_at DESC);
