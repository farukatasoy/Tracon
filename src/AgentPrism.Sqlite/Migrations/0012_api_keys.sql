-- Faz 53 -- kiraci bazli API anahtarlari ve kapsamlar.
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0025_api_keys.sql'e bakin.
--
-- 🚨 Tablo ve indeks adlari ONEK tasir (K-193): SQLite'ta sema yoktur ve
-- indeks adlari veritabani genelinde tek ad alanini paylasir.

CREATE TABLE IF NOT EXISTS {schema}api_keys (
    id           TEXT NOT NULL PRIMARY KEY,
    tenant_id    TEXT NOT NULL,
    name         TEXT NOT NULL,
    key_hash     BLOB NOT NULL,
    key_prefix   TEXT NOT NULL,
    scopes       TEXT NOT NULL,
    expires_at   TEXT,
    revoked_at   TEXT,
    last_used_at TEXT,
    created_at   TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS {schema}api_keys_hash_uq ON {schema}api_keys (key_hash);

CREATE INDEX IF NOT EXISTS {schema}api_keys_tenant_idx ON {schema}api_keys (tenant_id, created_at DESC);
