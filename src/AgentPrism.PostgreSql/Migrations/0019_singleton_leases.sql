-- ---------------------------------------------------------------------------
-- 0019 — Tek yurutucu secimi kira tablosu (Faz 42)
--
-- Kume genelinde adlandirilmis bir isin (MCP kesfi, model saglik yoklamasi)
-- yalnizca bir replikada kosmasini saglar. Oturum kilidi (pg_try_advisory_lock)
-- yerine bir tablo secildi: SQLite'in oturum kilidi karsiligi yoktur ve bir
-- tablo baglanti havuzuna bagimli degildir. Gerekce: docs/42-TEK-YURUTUCU-SECIMI.md.
--
-- Kiraci sutunu YOKTUR ve bu bilinclidir: tek yurutucu secimi kurulum
-- genelinde bir isletim kavramidir, kiraci basina degil.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS {schema}.singleton_leases (
    name       text        NOT NULL PRIMARY KEY,
    owner_id   text        NOT NULL,
    expires_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);
