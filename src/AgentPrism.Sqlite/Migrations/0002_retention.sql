-- Faz 25 -- veri saklama politikasi ve arsivleme.
--
-- Varsayilan politika EKLENMEZ: bos politika tablosu = hicbir sey silinmez.
--
-- 🚨 Indeks adlari da TABLO ONEKINI tasir (K-193): SQLite'ta nesne adlari
-- veritabani genelinde tek ad alanini paylasir, sema veya tabloya gore
-- kapsamli DEGILDIR.

CREATE TABLE IF NOT EXISTS {schema}retention_policies (
    id           TEXT    NOT NULL PRIMARY KEY,
    tenant_id    TEXT    NOT NULL,          -- '*' = tum kiracilar
    target       TEXT    NOT NULL,          -- 'run_events', 'spans', ...
    max_age_days INTEGER NULL,
    max_rows     INTEGER NULL,
    archive      INTEGER NOT NULL DEFAULT 0,
    enabled      INTEGER NOT NULL DEFAULT 1,
    created_at   TEXT    NOT NULL,
    updated_at   TEXT    NOT NULL,
    UNIQUE (tenant_id, target)
);

-- tenant_id doc'un ilk taslaginda YOKTU; kiraci bazli politika (K-198)
-- kosularin da kiraciya gore filtrelenebilmesini gerektirir.
CREATE TABLE IF NOT EXISTS {schema}retention_runs (
    id            TEXT    NOT NULL PRIMARY KEY,
    tenant_id     TEXT    NOT NULL,
    target        TEXT    NOT NULL,
    deleted_rows  INTEGER NOT NULL DEFAULT 0,
    archived_rows INTEGER NOT NULL DEFAULT 0,
    started_at    TEXT    NOT NULL,
    completed_at  TEXT    NULL,
    error         TEXT    NULL
);

CREATE INDEX IF NOT EXISTS {schema}retention_runs_tenant_started_idx ON {schema}retention_runs (tenant_id, started_at DESC);
