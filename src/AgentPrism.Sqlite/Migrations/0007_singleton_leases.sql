-- Faz 42 -- tek yurutucu secimi kira tablosu.
--
-- Gerekce icin PostgreSQL 0019_singleton_leases.sql'e bakin. Kiraci sutunu
-- YOKTUR: tek yurutucu secimi kurulum genelinde bir isletim kavramidir.
--
-- 🚨 Tablo adi ONEK tasir (K-193): SQLite'ta nesne adlari veritabani
-- genelinde tek ad alanini paylasir. Bu tabloda indeks yoktur.

CREATE TABLE IF NOT EXISTS {schema}singleton_leases (
    name       TEXT NOT NULL PRIMARY KEY,
    owner_id   TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
